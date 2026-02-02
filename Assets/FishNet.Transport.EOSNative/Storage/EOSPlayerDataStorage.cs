using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.PlayerDataStorage;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Storage
{
    /// <summary>
    /// Manages EOS Player Data Storage - cloud saves for player-specific files.
    /// 400MB max per player. Works with DeviceID (no Epic Account needed).
    /// </summary>
    public class EOSPlayerDataStorage : MonoBehaviour
    {
        #region Singleton

        private static EOSPlayerDataStorage _instance;
        public static EOSPlayerDataStorage Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSPlayerDataStorage>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSPlayerDataStorage");
                        _instance = go.AddComponent<EOSPlayerDataStorage>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when a file operation completes.</summary>
        public event Action<string, bool> OnFileOperationComplete; // filename, success

        /// <summary>Fired when file list is refreshed.</summary>
        public event Action<List<FileMetadata>> OnFileListUpdated;

        #endregion

        #region Private Fields

        private PlayerDataStorageInterface _pdsInterface;
        private ProductUserId _localUserId;
        private List<FileMetadata> _cachedFiles = new();
        private Dictionary<string, byte[]> _transferBuffers = new();
        private Dictionary<string, PlayerDataStorageFileTransferRequest> _activeTransfers = new();

        #endregion

        #region Public Properties

        /// <summary>Cached list of files.</summary>
        public IReadOnlyList<FileMetadata> Files => _cachedFiles;

        /// <summary>Whether storage is ready to use.</summary>
        public bool IsReady => _pdsInterface != null && _localUserId != null;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            StartCoroutine(InitializeCoroutine());
        }

        private System.Collections.IEnumerator InitializeCoroutine()
        {
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _pdsInterface = EOSManager.Instance.PlayerDataStorageInterface;
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_pdsInterface != null)
            {
                EOSDebugLogger.Log(DebugCategory.PlayerDataStorage, "EOSPlayerDataStorage", "Initialized");
                _ = QueryFileListAsync();
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.PlayerDataStorage, "EOSPlayerDataStorage", "PlayerDataStorageInterface not available");
            }
        }

        private void OnDestroy()
        {
            // Cancel any active transfers
            foreach (var transfer in _activeTransfers.Values)
            {
                transfer?.CancelRequest();
            }
            _activeTransfers.Clear();

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Query

        /// <summary>
        /// Query the list of files in cloud storage.
        /// </summary>
        public async Task<(Result result, List<FileMetadata> files)> QueryFileListAsync()
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            var options = new QueryFileListOptions
            {
                LocalUserId = _localUserId
            };

            var tcs = new TaskCompletionSource<QueryFileListCallbackInfo>();
            _pdsInterface.QueryFileList(ref options, null, (ref QueryFileListCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSPlayerDataStorage] QueryFileList failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            // Cache file list
            _cachedFiles.Clear();
            var countOptions = new GetFileMetadataCountOptions { LocalUserId = _localUserId };
            _pdsInterface.GetFileMetadataCount(ref countOptions, out int countInt);
            uint count = (uint)countInt;

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyFileMetadataAtIndexOptions
                {
                    LocalUserId = _localUserId,
                    Index = i
                };

                if (_pdsInterface.CopyFileMetadataAtIndex(ref copyOptions, out var metadata) == Result.Success && metadata.HasValue)
                {
                    _cachedFiles.Add(metadata.Value);
                }
            }

            EOSDebugLogger.Log(DebugCategory.PlayerDataStorage, "EOSPlayerDataStorage", $" Found {_cachedFiles.Count} files");
            OnFileListUpdated?.Invoke(_cachedFiles);
            return (Result.Success, _cachedFiles);
        }

        /// <summary>
        /// Get metadata for a specific file.
        /// </summary>
        public FileMetadata? GetFileMetadata(string filename)
        {
            var options = new CopyFileMetadataByFilenameOptions
            {
                LocalUserId = _localUserId,
                Filename = filename
            };

            if (_pdsInterface.CopyFileMetadataByFilename(ref options, out var metadata) == Result.Success)
            {
                return metadata;
            }
            return null;
        }

        #endregion

        #region Public API - Read

        /// <summary>
        /// Read a file from cloud storage.
        /// </summary>
        public async Task<(Result result, byte[] data)> ReadFileAsync(string filename)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            if (string.IsNullOrEmpty(filename))
                return (Result.InvalidParameters, null);

            // Prepare buffer
            var buffer = new List<byte>();
            string transferId = Guid.NewGuid().ToString();

            var options = new ReadFileOptions
            {
                LocalUserId = _localUserId,
                Filename = filename,
                ReadChunkLengthBytes = 4096,
                ReadFileDataCallback = (ref ReadFileDataCallbackInfo info) =>
                {
                    if (info.DataChunk != null)
                    {
                        buffer.AddRange(info.DataChunk);
                    }
                    return ReadResult.ContinueReading;
                },
                FileTransferProgressCallback = (ref FileTransferProgressCallbackInfo info) =>
                {
                    // Progress tracking if needed
                }
            };

            var tcs = new TaskCompletionSource<ReadFileCallbackInfo>();
            var handle = _pdsInterface.ReadFile(ref options, null, (ref ReadFileCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            if (handle == null)
            {
                Debug.LogWarning($"[EOSPlayerDataStorage] Failed to start read for {filename}");
                return (Result.UnexpectedError, null);
            }

            _activeTransfers[transferId] = handle;
            var result = await tcs.Task;
            _activeTransfers.Remove(transferId);

            if (result.ResultCode != Result.Success)
            {
                // NotFound is expected for files that don't exist yet - don't log as warning
                if (result.ResultCode != Result.NotFound)
                {
                    Debug.LogWarning($"[EOSPlayerDataStorage] ReadFile failed: {result.ResultCode}");
                }
                OnFileOperationComplete?.Invoke(filename, false);
                return (result.ResultCode, null);
            }

            EOSDebugLogger.Log(DebugCategory.PlayerDataStorage, "EOSPlayerDataStorage", $" Read {buffer.Count} bytes from {filename}");
            OnFileOperationComplete?.Invoke(filename, true);
            return (Result.Success, buffer.ToArray());
        }

        /// <summary>
        /// Read a file as string (UTF-8).
        /// </summary>
        public async Task<(Result result, string content)> ReadFileAsStringAsync(string filename)
        {
            var (result, data) = await ReadFileAsync(filename);
            if (result != Result.Success || data == null)
                return (result, null);

            return (Result.Success, System.Text.Encoding.UTF8.GetString(data));
        }

        /// <summary>
        /// Read a file as JSON and deserialize.
        /// </summary>
        public async Task<(Result result, T data)> ReadFileAsJsonAsync<T>(string filename)
        {
            var (result, content) = await ReadFileAsStringAsync(filename);
            if (result != Result.Success || string.IsNullOrEmpty(content))
                return (result, default);

            try
            {
                var data = JsonUtility.FromJson<T>(content);
                return (Result.Success, data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSPlayerDataStorage] JSON parse failed: {e.Message}");
                return (Result.InvalidParameters, default);
            }
        }

        #endregion

        #region Public API - Write

        /// <summary>
        /// Write data to cloud storage.
        /// </summary>
        public async Task<Result> WriteFileAsync(string filename, byte[] data)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (string.IsNullOrEmpty(filename) || data == null)
                return Result.InvalidParameters;

            int offset = 0;
            string transferId = Guid.NewGuid().ToString();

            var options = new WriteFileOptions
            {
                LocalUserId = _localUserId,
                Filename = filename,
                ChunkLengthBytes = (uint)data.Length,
                WriteFileDataCallback = (ref WriteFileDataCallbackInfo info, out ArraySegment<byte> outData) =>
                {
                    int remaining = data.Length - offset;
                    int toWrite = (int)Math.Min(remaining, info.DataBufferLengthBytes);

                    if (toWrite > 0)
                    {
                        outData = new ArraySegment<byte>(data, offset, toWrite);
                        offset += toWrite;
                        return remaining > toWrite ? WriteResult.ContinueWriting : WriteResult.CompleteRequest;
                    }

                    outData = new ArraySegment<byte>();
                    return WriteResult.CompleteRequest;
                },
                FileTransferProgressCallback = (ref FileTransferProgressCallbackInfo info) =>
                {
                    // Progress tracking if needed
                }
            };

            var tcs = new TaskCompletionSource<WriteFileCallbackInfo>();
            var handle = _pdsInterface.WriteFile(ref options, null, (ref WriteFileCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            if (handle == null)
            {
                Debug.LogWarning($"[EOSPlayerDataStorage] Failed to start write for {filename}");
                return Result.UnexpectedError;
            }

            _activeTransfers[transferId] = handle;
            var result = await tcs.Task;
            _activeTransfers.Remove(transferId);

            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSPlayerDataStorage] WriteFile failed: {result.ResultCode}");
                OnFileOperationComplete?.Invoke(filename, false);
                return result.ResultCode;
            }

            EOSDebugLogger.Log(DebugCategory.PlayerDataStorage, "EOSPlayerDataStorage", $" Wrote {data.Length} bytes to {filename}");
            OnFileOperationComplete?.Invoke(filename, true);

            // Refresh file list
            _ = QueryFileListAsync();
            return Result.Success;
        }

        /// <summary>
        /// Write string content to cloud storage (UTF-8).
        /// </summary>
        public async Task<Result> WriteFileAsync(string filename, string content)
        {
            if (string.IsNullOrEmpty(content))
                return Result.InvalidParameters;

            return await WriteFileAsync(filename, System.Text.Encoding.UTF8.GetBytes(content));
        }

        /// <summary>
        /// Write object as JSON to cloud storage.
        /// </summary>
        public async Task<Result> WriteFileAsJsonAsync<T>(string filename, T data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                return await WriteFileAsync(filename, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSPlayerDataStorage] JSON serialize failed: {e.Message}");
                return Result.InvalidParameters;
            }
        }

        #endregion

        #region Public API - Delete

        /// <summary>
        /// Delete a file from cloud storage.
        /// </summary>
        public async Task<Result> DeleteFileAsync(string filename)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (string.IsNullOrEmpty(filename))
                return Result.InvalidParameters;

            var options = new DeleteFileOptions
            {
                LocalUserId = _localUserId,
                Filename = filename
            };

            var tcs = new TaskCompletionSource<DeleteFileCallbackInfo>();
            _pdsInterface.DeleteFile(ref options, null, (ref DeleteFileCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSPlayerDataStorage] DeleteFile failed: {result.ResultCode}");
                OnFileOperationComplete?.Invoke(filename, false);
                return result.ResultCode;
            }

            EOSDebugLogger.Log(DebugCategory.PlayerDataStorage, "EOSPlayerDataStorage", $" Deleted {filename}");
            OnFileOperationComplete?.Invoke(filename, true);

            // Refresh file list
            _ = QueryFileListAsync();
            return Result.Success;
        }

        #endregion

        #region Public API - Duplicate

        /// <summary>
        /// Duplicate a file in cloud storage.
        /// </summary>
        public async Task<Result> DuplicateFileAsync(string sourceFilename, string destFilename)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (string.IsNullOrEmpty(sourceFilename) || string.IsNullOrEmpty(destFilename))
                return Result.InvalidParameters;

            var options = new DuplicateFileOptions
            {
                LocalUserId = _localUserId,
                SourceFilename = sourceFilename,
                DestinationFilename = destFilename
            };

            var tcs = new TaskCompletionSource<DuplicateFileCallbackInfo>();
            _pdsInterface.DuplicateFile(ref options, null, (ref DuplicateFileCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSPlayerDataStorage] DuplicateFile failed: {result.ResultCode}");
                return result.ResultCode;
            }

            EOSDebugLogger.Log(DebugCategory.PlayerDataStorage, "EOSPlayerDataStorage", $" Duplicated {sourceFilename} to {destFilename}");
            _ = QueryFileListAsync();
            return Result.Success;
        }

        #endregion

        #region Public API - Convenience

        /// <summary>
        /// Check if a file exists.
        /// </summary>
        public bool FileExists(string filename)
        {
            return GetFileMetadata(filename).HasValue;
        }

        /// <summary>
        /// Get total storage used (bytes).
        /// </summary>
        public long GetTotalStorageUsed()
        {
            long total = 0;
            foreach (var file in _cachedFiles)
            {
                total += (long)file.FileSizeBytes;
            }
            return total;
        }

        /// <summary>
        /// Format bytes as human-readable string.
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSPlayerDataStorage))]
    public class EOSPlayerDataStorageEditor : Editor
    {
        private Vector2 _scrollPos;
        private bool _showFiles = true;

        public override void OnInspectorGUI()
        {
            var storage = (EOSPlayerDataStorage)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Player Data Storage", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", storage.IsReady);
                EditorGUILayout.IntField("Files", storage.Files.Count);
                EditorGUILayout.TextField("Storage Used", EOSPlayerDataStorage.FormatBytes(storage.GetTotalStorageUsed()));
            }

            if (Application.isPlaying && storage.IsReady)
            {
                EditorGUILayout.Space(5);

                if (GUILayout.Button("Refresh File List"))
                {
                    _ = storage.QueryFileListAsync();
                }

                _showFiles = EditorGUILayout.Foldout(_showFiles, $"Files ({storage.Files.Count})", true);
                if (_showFiles && storage.Files.Count > 0)
                {
                    _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(120));

                    foreach (var file in storage.Files)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(file.Filename, GUILayout.Width(150));
                        EditorGUILayout.LabelField(EOSPlayerDataStorage.FormatBytes((long)file.FileSizeBytes), EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }

                EditorUtility.SetDirty(target);
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use storage.", MessageType.Info);
            }
        }
    }
#endif
}
