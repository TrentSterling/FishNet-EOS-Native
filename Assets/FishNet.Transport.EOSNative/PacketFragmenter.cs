using System;
using System.Collections.Generic;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Handles packet fragmentation and reassembly for EOS P2P transport.
    /// EOS has a max packet size of 1170 bytes, so larger packets must be split.
    ///
    /// Header format (7 bytes):
    /// - packetId (uint, 4 bytes): Identifies the original packet
    /// - fragmentId (ushort, 2 bytes): Fragment index (0, 1, 2...)
    /// - lastFragment (byte, 1 byte): 1 if this is the last fragment, 0 otherwise
    /// </summary>
    public class PacketFragmenter
    {
        public const int HeaderSize = sizeof(uint) + sizeof(ushort) + sizeof(byte); // 7 bytes
        public const int MaxPacketSize = 1170; // EOS P2P max packet size
        public const int MaxPayloadSize = MaxPacketSize - HeaderSize; // 1163 bytes

        private uint _nextPacketId;
        private readonly Dictionary<FragmentKey, List<FragmentData>> _pendingFragments = new();

        // Reusable buffer for building outgoing packets
        private readonly byte[] _sendBuffer = new byte[MaxPacketSize];

        #region Fragmentation (Send)

        /// <summary>
        /// Fragments data if needed and returns packets ready to send.
        /// Each packet includes the 7-byte header.
        /// </summary>
        public IEnumerable<ArraySegment<byte>> Fragment(ArraySegment<byte> data)
        {
            uint packetId = _nextPacketId++;

            // Handle empty data - still send a packet with just the header
            if (data.Count == 0)
            {
                byte[] packet = new byte[HeaderSize];
                WriteHeader(packet, packetId, 0, true);
                yield return new ArraySegment<byte>(packet);
                yield break;
            }

            int totalFragments = (data.Count + MaxPayloadSize - 1) / MaxPayloadSize; // Ceiling division

            for (int i = 0; i < totalFragments; i++)
            {
                int offset = i * MaxPayloadSize;
                int remaining = data.Count - offset;
                int payloadLength = Math.Min(MaxPayloadSize, remaining);
                bool isLast = (i == totalFragments - 1);

                // Write header
                WriteHeader(_sendBuffer, packetId, (ushort)i, isLast);

                // Write payload
                Array.Copy(data.Array, data.Offset + offset, _sendBuffer, HeaderSize, payloadLength);

                // Return a copy (we reuse _sendBuffer)
                byte[] packet = new byte[HeaderSize + payloadLength];
                Array.Copy(_sendBuffer, packet, packet.Length);

                yield return new ArraySegment<byte>(packet);
            }
        }

        /// <summary>
        /// Check if data needs fragmentation.
        /// </summary>
        public static bool NeedsFragmentation(int dataLength)
        {
            return dataLength > MaxPayloadSize;
        }

        /// <summary>
        /// For small packets that don't need fragmentation, create a single packet with header.
        /// </summary>
        public ArraySegment<byte> CreateSinglePacket(ArraySegment<byte> data)
        {
            uint packetId = _nextPacketId++;

            byte[] packet = new byte[HeaderSize + data.Count];
            WriteHeader(packet, packetId, 0, true);
            Array.Copy(data.Array, data.Offset, packet, HeaderSize, data.Count);

            return new ArraySegment<byte>(packet);
        }

        private static void WriteHeader(byte[] buffer, uint packetId, ushort fragmentId, bool lastFragment)
        {
            // packetId (little-endian)
            buffer[0] = (byte)packetId;
            buffer[1] = (byte)(packetId >> 8);
            buffer[2] = (byte)(packetId >> 16);
            buffer[3] = (byte)(packetId >> 24);

            // fragmentId (little-endian)
            buffer[4] = (byte)fragmentId;
            buffer[5] = (byte)(fragmentId >> 8);

            // lastFragment
            buffer[6] = lastFragment ? (byte)1 : (byte)0;
        }

        #endregion

        #region Reassembly (Receive)

        /// <summary>
        /// Process an incoming packet. Returns the reassembled data if complete, null otherwise.
        /// </summary>
        /// <param name="senderId">Unique identifier for the sender (e.g., connection ID)</param>
        /// <param name="data">Raw packet data including header</param>
        /// <param name="channel">Channel the packet was received on</param>
        /// <returns>Complete reassembled data, or null if still waiting for fragments</returns>
        public byte[] ProcessIncoming(int senderId, ArraySegment<byte> data, byte channel)
        {
            if (data.Count < HeaderSize)
            {
                EOSDebugLogger.LogWarning(DebugCategory.PacketFragmenter, "PacketFragmenter", $"Received packet too small for header: {data.Count} bytes (need {HeaderSize})");
                return null;
            }

            // Parse header
            uint packetId = BitConverter.ToUInt32(data.Array, data.Offset);
            ushort fragmentId = BitConverter.ToUInt16(data.Array, data.Offset + 4);
            bool lastFragment = data.Array[data.Offset + 6] == 1;

            // Extract payload
            int payloadLength = data.Count - HeaderSize;
            byte[] payload = new byte[payloadLength];
            if (payloadLength > 0)
            {
                Array.Copy(data.Array, data.Offset + HeaderSize, payload, 0, payloadLength);
            }

            // Single fragment packet (most common case)
            if (fragmentId == 0 && lastFragment)
            {
                return payload;
            }

            // Multi-fragment packet - store and check for completion
            var key = new FragmentKey(senderId, packetId, channel);

            if (!_pendingFragments.TryGetValue(key, out var fragments))
            {
                fragments = new List<FragmentData>();
                _pendingFragments[key] = fragments;
            }

            fragments.Add(new FragmentData(fragmentId, lastFragment, payload));

            // Check if we have all fragments
            return TryReassemble(key, fragments);
        }

        private byte[] TryReassemble(FragmentKey key, List<FragmentData> fragments)
        {
            // Find the expected count (from the lastFragment marker)
            int expectedCount = -1;
            foreach (var frag in fragments)
            {
                if (frag.IsLast)
                {
                    expectedCount = frag.Id + 1;
                    break;
                }
            }

            if (expectedCount == -1)
            {
                // Haven't received the last fragment yet
                return null;
            }

            if (fragments.Count != expectedCount)
            {
                // Still missing some fragments
                return null;
            }

            // Verify we have contiguous fragments 0 to expectedCount-1
            bool[] seen = new bool[expectedCount];
            foreach (var frag in fragments)
            {
                if (frag.Id >= expectedCount)
                {
                    // Invalid fragment ID
                    _pendingFragments.Remove(key);
                    return null;
                }
                seen[frag.Id] = true;
            }

            for (int i = 0; i < expectedCount; i++)
            {
                if (!seen[i])
                {
                    // Missing fragment
                    return null;
                }
            }

            // Sort by fragment ID
            fragments.Sort((a, b) => a.Id.CompareTo(b.Id));

            // Calculate total size
            int totalSize = 0;
            foreach (var frag in fragments)
            {
                totalSize += frag.Payload.Length;
            }

            // Reassemble
            byte[] result = new byte[totalSize];
            int offset = 0;
            foreach (var frag in fragments)
            {
                Array.Copy(frag.Payload, 0, result, offset, frag.Payload.Length);
                offset += frag.Payload.Length;
            }

            // Clean up
            _pendingFragments.Remove(key);

            return result;
        }

        /// <summary>
        /// Clear pending fragments for a specific sender (call when connection closes).
        /// </summary>
        public void ClearPendingForSender(int senderId)
        {
            var keysToRemove = new List<FragmentKey>();
            foreach (var key in _pendingFragments.Keys)
            {
                if (key.SenderId == senderId)
                {
                    keysToRemove.Add(key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _pendingFragments.Remove(key);
            }
        }

        /// <summary>
        /// Clear all pending fragments.
        /// </summary>
        public void ClearAll()
        {
            _pendingFragments.Clear();
        }

        #endregion

        #region Helper Types

        private readonly struct FragmentKey : IEquatable<FragmentKey>
        {
            public readonly int SenderId;
            public readonly uint PacketId;
            public readonly byte Channel;

            public FragmentKey(int senderId, uint packetId, byte channel)
            {
                SenderId = senderId;
                PacketId = packetId;
                Channel = channel;
            }

            public bool Equals(FragmentKey other)
            {
                return SenderId == other.SenderId && PacketId == other.PacketId && Channel == other.Channel;
            }

            public override bool Equals(object obj)
            {
                return obj is FragmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(SenderId, PacketId, Channel);
            }
        }

        private readonly struct FragmentData
        {
            public readonly ushort Id;
            public readonly bool IsLast;
            public readonly byte[] Payload;

            public FragmentData(ushort id, bool isLast, byte[] payload)
            {
                Id = id;
                IsLast = isLast;
                Payload = payload;
            }
        }

        #endregion
    }
}
