using System;
using FishNet.Transporting;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Packet structure for local IPC between ClientHost and Server.
    /// Used when the host is acting as both server and client simultaneously.
    /// </summary>
    public readonly struct LocalPacket
    {
        /// <summary>
        /// The packet data.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// The length of the data.
        /// </summary>
        public int Length => Data?.Length ?? 0;

        /// <summary>
        /// The channel this packet was sent on.
        /// </summary>
        public Channel Channel { get; }

        /// <summary>
        /// Creates a new LocalPacket.
        /// </summary>
        /// <param name="data">The packet data (will be copied).</param>
        /// <param name="channel">The channel for this packet.</param>
        public LocalPacket(ArraySegment<byte> data, Channel channel)
        {
            Data = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, Data, 0, data.Count);
            Channel = channel;
        }

        /// <summary>
        /// Creates a new LocalPacket from a byte array.
        /// </summary>
        /// <param name="data">The packet data (will be copied).</param>
        /// <param name="channel">The channel for this packet.</param>
        public LocalPacket(byte[] data, Channel channel)
        {
            Data = new byte[data.Length];
            Buffer.BlockCopy(data, 0, Data, 0, data.Length);
            Channel = channel;
        }

        /// <summary>
        /// Gets the data as an ArraySegment.
        /// </summary>
        public ArraySegment<byte> GetSegment()
        {
            return new ArraySegment<byte>(Data, 0, Length);
        }
    }
}
