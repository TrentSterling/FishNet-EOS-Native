using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.TitleStorage;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Storage
{
    /// <summary>
    /// Manages EOS Title Storage - server-side game configuration files.
    /// Read-only from client. Files uploaded via Developer Portal.
    /// No authentication required.
    /// </summary>
    public class EOSTitleStorage : MonoBehaviour
    {
        #region Singleton

        private static EOSTitleStorage _instance;
        public static EOSTitleStorage Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSTitleStorage>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSTitleStorage");
                        _instance = go.AddComponent<EOSTitleStorage>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when file list is refreshed.</summary>
        public event Action<List<FileMetadata>> OnFileListUpdated;

        /// <summary>Fired when a file is loaded.</summary>
        public event Action<string, byte[]> OnFileLoaded; // filename, data

        #endregion

        #region Private Fields

        private TitleStorageInterface _tsInterface;
        private ProductUserId _localUserId;
        private List<FileMetadata> _cachedFiles = new();
        private Dictionary<string, byte[]> _fileCache = new(); // In-memory cache of loaded files
        private Dictionary<string, TitleStorageFileTransferRequest> _activeTransfers = new();

        #endregion

        #region Public Properties

        /// <summary>Cached list of available files.</summary>
        public IReadOnlyList<FileMetadata> Files => _cachedFiles;

        /// <summary>Whether storage is ready to use.</summary>
        public bool IsReady => _tsInterface != null;

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
            // Title Storage can work even before login, but we wait for EOSManager
            while (EOSManager.Instance == null || EOSManager.Instance.TitleStorageInterface == null)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _tsInterface = EOSManager.Instance.TitleStorageInterface;
            _localUserId = EOSManager.Instance.LocalProductUserId;

            EOSDebugLogger.Log(DebugCategory.TitleStorage, "EOSTitleStorage", "Initialized");
            _ = QueryFileListAsync();
        }

        private void OnDestroy()
        {
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
        /// Query the list of available title storage files.
        /// </summary>
        /// <param name="tags">Optional tags to filter by.</param>
        public async Task<(Result result, List<FileMetadata> files)> QueryFileListAsync(string[] tags = null)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            var options = new QueryFileListOptions
            {
                LocalUserId = _localUserId,
                ListOfTags = tags?.Select(t => new Utf8String(t)).ToArray()
            };

            var tcs = new TaskCompletionSource<QueryFileListCallbackInfo>();
            _tsInterface.QueryFileList(ref options, null, (ref QueryFileListCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSTitleStorage] QueryFileList failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            // Cache file list
            _cachedFiles.Clear();
            uint count = result.FileCount;

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyFileMetadataAtIndexOptions
                {
                    LocalUserId = _localUserId,
                    Index = i
                };

                if (_tsInterface.CopyFileMetadataAtIndex(ref copyOptions, out var metadata) == Result.Success && metadata.HasValue)
                {
                    _cachedFiles.Add(metadata.Value);
                }
            }

            EOSDebugLogger.Log(DebugCategory.TitleStorage, "EOSTitleStorage", $" Found {_cachedFiles.Count} files");
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

            if (_tsInterface.CopyFileMetadataByFilename(ref options, out var metadata) == Result.Success)
            {
                return metadata;
            }
            return null;
        }

        #endregion

        #region Public API - Read

        /// <summary>
        /// Read a file from title storage.
        /// </summary>
        /// <param name="filename">File to read.</param>
        /// <param name="useCache">If true, return cached version if available.</param>
        public async Task<(Result result, byte[] data)> ReadFileAsync(string filename, bool useCache = true)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            if (string.IsNullOrEmpty(filename))
                return (Result.InvalidParameters, null);

            // Check cache first
            if (useCache && _fileCache.TryGetValue(filename, out var cached))
            {
                EOSDebugLogger.Log(DebugCategory.TitleStorage, "EOSTitleStorage", $" Returning cached {filename}");
                return (Result.Success, cached);
            }

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
                    return ReadResult.RrContinueReading;
                },
                FileTransferProgressCallback = (ref FileTransferProgressCallbackInfo info) =>
                {
                    // Progress tracking if needed
                }
            };

            var tcs = new TaskCompletionSource<ReadFileCallbackInfo>();
            var handle = _tsInterface.ReadFile(ref options, null, (ref ReadFileCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            if (handle == null)
            {
                Debug.LogWarning($"[EOSTitleStorage] Failed to start read for {filename}");
                return (Result.UnexpectedError, null);
            }

            _activeTransfers[transferId] = handle;
            var result = await tcs.Task;
            _activeTransfers.Remove(transferId);

            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSTitleStorage] ReadFile failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            var data = buffer.ToArray();
            _fileCache[filename] = data; // Cache it

            EOSDebugLogger.Log(DebugCategory.TitleStorage, "EOSTitleStorage", $" Read {data.Length} bytes from {filename}");
            OnFileLoaded?.Invoke(filename, data);
            return (Result.Success, data);
        }

        /// <summary>
        /// Read a file as string (UTF-8).
        /// </summary>
        public async Task<(Result result, string content)> ReadFileAsStringAsync(string filename, bool useCache = true)
        {
            var (result, data) = await ReadFileAsync(filename, useCache);
            if (result != Result.Success || data == null)
                return (result, null);

            return (Result.Success, System.Text.Encoding.UTF8.GetString(data));
        }

        /// <summary>
        /// Read a file as JSON and deserialize.
        /// </summary>
        public async Task<(Result result, T data)> ReadFileAsJsonAsync<T>(string filename, bool useCache = true)
        {
            var (result, content) = await ReadFileAsStringAsync(filename, useCache);
            if (result != Result.Success || string.IsNullOrEmpty(content))
                return (result, default);

            try
            {
                var data = JsonUtility.FromJson<T>(content);
                return (Result.Success, data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSTitleStorage] JSON parse failed: {e.Message}");
                return (Result.InvalidParameters, default);
            }
        }

        #endregion

        #region Public API - Utility

        /// <summary>
        /// Check if a file exists in title storage.
        /// </summary>
        public bool FileExists(string filename)
        {
            return GetFileMetadata(filename).HasValue;
        }

        /// <summary>
        /// Check if a file is cached in memory.
        /// </summary>
        public bool IsFileCached(string filename)
        {
            return _fileCache.ContainsKey(filename);
        }

        /// <summary>
        /// Clear the in-memory file cache.
        /// </summary>
        public void ClearCache()
        {
            _fileCache.Clear();
            EOSDebugLogger.Log(DebugCategory.TitleStorage, "EOSTitleStorage", "Cache cleared");
        }

        /// <summary>
        /// Get cached file data without async read.
        /// </summary>
        public byte[] GetCachedFile(string filename)
        {
            return _fileCache.TryGetValue(filename, out var data) ? data : null;
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSTitleStorage))]
    public class EOSTitleStorageEditor : Editor
    {
        private Vector2 _scrollPos;
        private bool _showFiles = true;

        public override void OnInspectorGUI()
        {
            var storage = (EOSTitleStorage)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Title Storage (Read-Only)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", storage.IsReady);
                EditorGUILayout.IntField("Files", storage.Files.Count);
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
                    _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(100));

                    foreach (var file in storage.Files)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(file.Filename, GUILayout.Width(150));
                        bool cached = storage.IsFileCached(file.Filename);
                        EditorGUILayout.LabelField(cached ? "[Cached]" : "", EditorStyles.miniLabel, GUILayout.Width(50));
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }

                EditorGUILayout.Space(5);
                if (GUILayout.Button("Clear Cache"))
                {
                    storage.ClearCache();
                }

                EditorUtility.SetDirty(target);
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode. Files are uploaded via Developer Portal.", MessageType.Info);
            }
        }
    }
#endif
}
