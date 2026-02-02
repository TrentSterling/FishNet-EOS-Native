using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Half-precision Vector3 for bandwidth-efficient position storage (6 bytes vs 12).
    /// </summary>
    [Serializable]
    public struct Vector3Half
    {
        public ushort x, y, z;

        public Vector3Half(Vector3 v)
        {
            x = Mathf.FloatToHalf(v.x);
            y = Mathf.FloatToHalf(v.y);
            z = Mathf.FloatToHalf(v.z);
        }

        public Vector3 ToVector3()
        {
            return new Vector3(
                Mathf.HalfToFloat(x),
                Mathf.HalfToFloat(y),
                Mathf.HalfToFloat(z)
            );
        }

        public static Vector3Half FromVector3(Vector3 v) => new Vector3Half(v);
    }

    /// <summary>
    /// Single object snapshot (~13 bytes with serialization overhead).
    /// </summary>
    [Serializable]
    public struct ReplayObjectSnapshot
    {
        public int ObjectId;              // NetworkObject.ObjectId
        public Vector3Half Position;      // 6 bytes
        public uint CompressedRotation;   // 4 bytes (smallest-three encoding)
        public byte Flags;                // Active, etc.

        public const byte FLAG_ACTIVE = 1;
    }

    /// <summary>
    /// Single frame of replay data.
    /// </summary>
    [Serializable]
    public struct ReplayFrame
    {
        public float Timestamp;           // Seconds since recording start
        public bool IsKeyframe;           // Full snapshot vs delta
        public ReplayObjectSnapshot[] Objects;
        public ReplayEvent[] Events;
    }

    /// <summary>
    /// Events that occur during replay (spawns, despawns, game events).
    /// </summary>
    [Serializable]
    public struct ReplayEvent
    {
        public float Timestamp;
        public ReplayEventType Type;
        public int ObjectId;
        public string PrefabName;
        public Vector3Half Position;
        public uint Rotation;
        public string OwnerPuid;
        public string Data;               // JSON for custom events
    }

    /// <summary>
    /// Types of events that can occur during a replay.
    /// </summary>
    public enum ReplayEventType
    {
        ObjectSpawned,
        ObjectDespawned,
        PlayerJoined,
        PlayerLeft,
        GameEvent
    }

    /// <summary>
    /// Warning levels for recording quality issues.
    /// </summary>
    public enum RecordingQualityWarning
    {
        /// <summary>No quality issues detected.</summary>
        None,
        /// <summary>Elevated ping (150-250ms) - may affect replay accuracy.</summary>
        HighPing,
        /// <summary>Very high ping (250ms+) - replay may have gaps.</summary>
        VeryHighPing,
        /// <summary>High jitter detected - object positions may appear jumpy.</summary>
        HighJitter,
        /// <summary>Packet loss detected - some frames may be missing data.</summary>
        PacketLoss,
        /// <summary>Frame rate dropped significantly below target.</summary>
        LowFrameRate
    }

    /// <summary>
    /// Complete replay file structure.
    /// </summary>
    [Serializable]
    public struct ReplayFile
    {
        public int Version;
        public ReplayHeader Header;
        public byte[] CompressedFrames;   // GZip compressed frame data
    }

    /// <summary>
    /// Replay metadata header.
    /// </summary>
    [Serializable]
    public struct ReplayHeader
    {
        public string ReplayId;
        public string MatchId;            // Links to EOSMatchHistory
        public long RecordedAt;           // Unix timestamp ms
        public float Duration;            // Total seconds
        public int FrameCount;
        public float FrameRate;
        public string LobbyCode;
        public string GameMode;
        public string MapName;
        public ReplayParticipant[] Participants;

        public DateTime RecordedDateTime => DateTimeOffset.FromUnixTimeMilliseconds(RecordedAt).LocalDateTime;
    }

    /// <summary>
    /// Participant in a replay.
    /// </summary>
    [Serializable]
    public struct ReplayParticipant
    {
        public string Puid;
        public string DisplayName;
        public int Team;
        public string Platform;
    }

    /// <summary>
    /// Internal container for uncompressed frames (used during recording).
    /// </summary>
    [Serializable]
    internal struct ReplayFrameData
    {
        public List<ReplayFrame> Frames;
    }

    /// <summary>
    /// Compression utilities for replay data.
    /// </summary>
    public static class ReplayCompression
    {
        /// <summary>
        /// Smallest-three quaternion compression to 32 bits.
        /// </summary>
        public static uint CompressRotation(Quaternion rot)
        {
            float absX = Mathf.Abs(rot.x);
            float absY = Mathf.Abs(rot.y);
            float absZ = Mathf.Abs(rot.z);
            float absW = Mathf.Abs(rot.w);

            int largestIndex = 0;
            float largestValue = absX;

            if (absY > largestValue) { largestIndex = 1; largestValue = absY; }
            if (absZ > largestValue) { largestIndex = 2; largestValue = absZ; }
            if (absW > largestValue) { largestIndex = 3; }

            float a, b, c;
            switch (largestIndex)
            {
                case 0: a = rot.y; b = rot.z; c = rot.w; if (rot.x < 0) { a = -a; b = -b; c = -c; } break;
                case 1: a = rot.x; b = rot.z; c = rot.w; if (rot.y < 0) { a = -a; b = -b; c = -c; } break;
                case 2: a = rot.x; b = rot.y; c = rot.w; if (rot.z < 0) { a = -a; b = -b; c = -c; } break;
                default: a = rot.x; b = rot.y; c = rot.z; if (rot.w < 0) { a = -a; b = -b; c = -c; } break;
            }

            const float scale = 1023f / 1.41421356f;
            uint ua = (uint)Mathf.Clamp((int)((a + 0.707106781f) * scale), 0, 1023);
            uint ub = (uint)Mathf.Clamp((int)((b + 0.707106781f) * scale), 0, 1023);
            uint uc = (uint)Mathf.Clamp((int)((c + 0.707106781f) * scale), 0, 1023);

            return ((uint)largestIndex << 30) | (ua << 20) | (ub << 10) | uc;
        }

        /// <summary>
        /// Decompress smallest-three quaternion from 32 bits.
        /// </summary>
        public static Quaternion DecompressRotation(uint compressed)
        {
            int largestIndex = (int)(compressed >> 30);
            const float scale = 1.41421356f / 1023f;

            float a = ((compressed >> 20) & 1023) * scale - 0.707106781f;
            float b = ((compressed >> 10) & 1023) * scale - 0.707106781f;
            float c = (compressed & 1023) * scale - 0.707106781f;

            float largest = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));

            switch (largestIndex)
            {
                case 0: return new Quaternion(largest, a, b, c);
                case 1: return new Quaternion(a, largest, b, c);
                case 2: return new Quaternion(a, b, largest, c);
                default: return new Quaternion(a, b, c, largest);
            }
        }

        /// <summary>
        /// GZip compress byte array.
        /// </summary>
        public static byte[] Compress(byte[] data)
        {
            using (var output = new System.IO.MemoryStream())
            {
                using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                {
                    gzip.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }

        /// <summary>
        /// GZip decompress byte array.
        /// </summary>
        public static byte[] Decompress(byte[] data)
        {
            using (var input = new System.IO.MemoryStream(data))
            using (var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (var output = new System.IO.MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }

        /// <summary>
        /// Serialize frames to JSON bytes.
        /// </summary>
        public static byte[] SerializeFrames(List<ReplayFrame> frames)
        {
            var data = new ReplayFrameData { Frames = frames };
            string json = JsonUtility.ToJson(data);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Deserialize frames from JSON bytes.
        /// </summary>
        public static List<ReplayFrame> DeserializeFrames(byte[] data)
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            var frameData = JsonUtility.FromJson<ReplayFrameData>(json);
            return frameData.Frames ?? new List<ReplayFrame>();
        }
    }
}
