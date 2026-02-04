using FishNet.Utility.Performance;
using System;

namespace FishNet.Transport.EOSNative.Offline
{
    /// <summary>
    /// A packet for local/offline communication.
    /// Uses FishNet's ByteArrayPool for efficient memory reuse.
    /// </summary>
    internal struct OfflinePacket
    {
        public byte[] Data;
        public int Length;
        public byte Channel;

        public OfflinePacket(ArraySegment<byte> data, byte channel)
        {
            Data = ByteArrayPool.Retrieve(data.Count);
            Length = data.Count;
            Buffer.BlockCopy(data.Array, data.Offset, Data, 0, Length);
            Channel = channel;
        }

        public void Dispose()
        {
            if (Data != null)
            {
                ByteArrayPool.Store(Data);
                Data = null;
            }
        }
    }
}
