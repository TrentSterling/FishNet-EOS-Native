using System;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using FishNet.Transporting;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Handles client-side P2P connections for EOS transport.
    /// </summary>
    public class EOSClient
    {
        #region Private Fields

        private readonly EOSNativeTransport _transport;
        private Connection _connection;

        private string _socketName;
        private ProductUserId _remoteUserId;

        private NotifyEventHandle _connectionEstablishedHandle;
        private NotifyEventHandle _connectionClosedHandle;

        private readonly PacketFragmenter _fragmenter = new();

        // Bandwidth tracking
        private long _totalBytesSent;
        private long _totalBytesReceived;

        private P2PInterface P2P => EOSManager.Instance?.P2PInterface;
        private ProductUserId LocalUserId => EOSManager.Instance?.LocalProductUserId;

        #endregion

        #region Public Properties

        /// <summary>
        /// Total bytes sent since client started.
        /// </summary>
        public long TotalBytesSent => _totalBytesSent;

        /// <summary>
        /// Total bytes received since client started.
        /// </summary>
        public long TotalBytesReceived => _totalBytesReceived;

        #endregion

        #region Constructor

        public EOSClient(EOSNativeTransport transport)
        {
            _transport = transport;
        }

        #endregion

        #region Start / Stop

        public bool Start(string socketName, string remoteProductUserId)
        {
            if (P2P == null || LocalUserId == null)
            {
                Debug.LogError("[EOSClient] P2P interface or LocalUserId not available.");
                return false;
            }

            // Parse remote ProductUserId
            _remoteUserId = ProductUserId.FromString(remoteProductUserId);
            if (_remoteUserId == null || !_remoteUserId.IsValid())
            {
                EOSDebugLogger.LogError("EOSClient", $" Invalid RemoteProductUserId: {remoteProductUserId}");
                return false;
            }

            _socketName = socketName;

            // Register for connection established
            var establishedOptions = new AddNotifyPeerConnectionEstablishedOptions
            {
                LocalUserId = LocalUserId,
                SocketId = new SocketId { SocketName = _socketName }
            };
            ulong establishedHandle = P2P.AddNotifyPeerConnectionEstablished(ref establishedOptions, null, OnConnectionEstablished);
            _connectionEstablishedHandle = new NotifyEventHandle(establishedHandle, h => P2P.RemoveNotifyPeerConnectionEstablished(h));

            // Register for connection closed
            var closedOptions = new AddNotifyPeerConnectionClosedOptions
            {
                LocalUserId = LocalUserId,
                SocketId = new SocketId { SocketName = _socketName }
            };
            ulong closedHandle = P2P.AddNotifyPeerConnectionClosed(ref closedOptions, null, OnConnectionClosed);
            _connectionClosedHandle = new NotifyEventHandle(closedHandle, h => P2P.RemoveNotifyPeerConnectionClosed(h));

            // Create connection object
            var socketId = new SocketId { SocketName = _socketName };
            _connection = new Connection(0, LocalUserId, _remoteUserId, socketId)
            {
                OpenedOutgoing = true // We're initiating
            };

            // CRITICAL: Accept connection from server BEFORE sending packets
            // This enables bidirectional P2P communication
            // Without this, client can send but server's response won't be received
            var acceptOptions = new AcceptConnectionOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = _remoteUserId,
                SocketId = socketId
            };
            Result acceptResult = P2P.AcceptConnection(ref acceptOptions);
            if (acceptResult != Result.Success)
            {
                EOSDebugLogger.LogError("EOSClient", $" AcceptConnection failed: {acceptResult}");
                _transport.SetClientState(LocalConnectionState.Stopped);
                return false;
            }
            Log($" AcceptConnection succeeded for {_remoteUserId}");

            // Send initial packet to trigger connection request on server
            // EOS P2P requires sending a packet to establish connection
            SendConnectionRequest();

            Log($" Connecting to {remoteProductUserId} on socket '{_socketName}'");
            return true;
        }

        private void SendConnectionRequest()
        {
            // Send an empty reliable packet to trigger connection
            var sendOptions = new SendPacketOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = _remoteUserId,
                SocketId = new SocketId { SocketName = _socketName },
                Channel = 0,
                Data = new ArraySegment<byte>(Array.Empty<byte>()),
                Reliability = PacketReliability.ReliableOrdered,
                AllowDelayedDelivery = true
            };

            Result result = P2P.SendPacket(ref sendOptions);
            if (result != Result.Success)
            {
                EOSDebugLogger.LogError("EOSClient", $" Failed to send connection request: {result}");
                _transport.SetClientState(LocalConnectionState.Stopped);
            }
        }

        public void Stop()
        {
            if (_connection != null)
            {
                CloseConnection();
                _connection = null;
            }

            _fragmenter.ClearAll();

            // Dispose notification handles
            _connectionEstablishedHandle?.Dispose();
            _connectionClosedHandle?.Dispose();

            _connectionEstablishedHandle = null;
            _connectionClosedHandle = null;

            Log("Stopped.");
        }

        #endregion

        #region Connection Callbacks

        private void OnConnectionEstablished(ref OnPeerConnectionEstablishedInfo data)
        {
            if (data.SocketId?.SocketName != _socketName) return;
            if (_connection == null) return;

            // Verify this is for our target server
            if (data.RemoteUserId != _remoteUserId)
            {
                LogWarning($" Connection established for unexpected user: {data.RemoteUserId}");
                return;
            }

            _connection.OpenedIncoming = true;

            if (!_connection.ConnectionOpenedHandled && _connection.IsFullyOpened)
            {
                _connection.ConnectionOpenedHandled = true;
                _transport.SetClientState(LocalConnectionState.Started);
                Log($" Connection established with server {data.RemoteUserId}");
            }
        }

        private void OnConnectionClosed(ref OnRemoteConnectionClosedInfo data)
        {
            if (data.SocketId?.SocketName != _socketName) return;
            if (_connection == null) return;

            if (data.RemoteUserId != _remoteUserId) return;

            if (!_connection.ConnectionClosedHandled)
            {
                _connection.ConnectionClosedHandled = true;
                _transport.SetClientState(LocalConnectionState.Stopped);
                Log($" Connection closed. Reason: {data.Reason}");
            }
        }

        #endregion

        #region Send / Receive

        public void Send(ArraySegment<byte> data, Channel channel)
        {
            if (_connection == null || P2P == null) return;

            // Fragment if needed (EOS max packet = 1170 bytes)
            // Use EOS channel 0 for Reliable, channel 1 for Unreliable
            // This allows the receiver to determine the original FishNet channel
            byte eosChannel = channel == Channel.Reliable ? (byte)0 : (byte)1;

            foreach (var packet in _fragmenter.Fragment(data))
            {
                var sendOptions = new SendPacketOptions
                {
                    LocalUserId = LocalUserId,
                    RemoteUserId = _remoteUserId,
                    SocketId = new SocketId { SocketName = _socketName },
                    Channel = eosChannel,
                    Data = packet,
                    Reliability = channel == Channel.Reliable ? PacketReliability.ReliableOrdered : PacketReliability.UnreliableUnordered,
                    AllowDelayedDelivery = true,
                    DisableAutoAcceptConnection = false
                };

                Result result = P2P.SendPacket(ref sendOptions);
                if (result != Result.Success)
                {
                    LogWarning($" SendPacket failed: {result}");
                    return;
                }
                _totalBytesSent += packet.Count;
            }
        }

        // Pre-allocated receive buffer (MAX_PACKET_SIZE = 1170 bytes)
        private readonly byte[] _receiveBuffer = new byte[1170];

        public void IterateIncoming()
        {
            if (P2P == null || LocalUserId == null) return;

            // Use ReceivePacket directly with pre-allocated buffer (like Mirror transport)
            var receiveOptions = new ReceivePacketOptions
            {
                LocalUserId = LocalUserId,
                MaxDataSizeBytes = (uint)_receiveBuffer.Length,
                RequestedChannel = null
            };

            while (true)
            {
                ProductUserId remoteUserId = null;
                SocketId socketId = new SocketId();
                var dataSegment = new ArraySegment<byte>(_receiveBuffer);
                Result receiveResult = P2P.ReceivePacket(ref receiveOptions, ref remoteUserId, ref socketId, out byte outChannel, dataSegment, out uint bytesWritten);

                // NotFound means no more packets - this is normal
                if (receiveResult == Result.NotFound)
                {
                    break;
                }

                if (receiveResult != Result.Success)
                {
                    LogWarning($" ReceivePacket failed: {receiveResult}");
                    break;
                }

                // Verify socket name
                if (socketId.SocketName != _socketName)
                {
                    LogWarning($" Received packet on wrong socket: '{socketId.SocketName}' (expected '{_socketName}')");
                    continue;
                }

                // Verify sender is our server
                if (remoteUserId != _remoteUserId)
                {
                    LogWarning($" Received packet from unexpected user: {remoteUserId}");
                    continue;
                }

                // Skip empty packets (connection establishment)
                if (bytesWritten == 0)
                {
                    continue;
                }

                // Track bandwidth
                _totalBytesReceived += bytesWritten;

                // Process through fragmenter for reassembly (use 0 as sender ID since only one server)
                var rawData = new ArraySegment<byte>(_receiveBuffer, 0, (int)bytesWritten);
                byte[] reassembled = _fragmenter.ProcessIncoming(0, rawData, outChannel);

                if (reassembled == null)
                {
                    // Still waiting for more fragments
                    continue;
                }

                Channel channel = outChannel == 0 ? Channel.Reliable : Channel.Unreliable;
                _transport.InvokeClientReceivedData(new ArraySegment<byte>(reassembled), channel);
            }
        }

        #endregion

        #region Connection Management

        private void CloseConnection()
        {
            if (P2P == null || _remoteUserId == null) return;

            var closeOptions = new CloseConnectionOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = _remoteUserId,
                SocketId = new SocketId { SocketName = _socketName }
            };

            P2P.CloseConnection(ref closeOptions);
        }

        #endregion

        #region Logging

        private static void Log(string message)
        {
            EOSDebugLogger.Log(DebugCategory.Client, "EOSClient", message);
        }

        private static void LogWarning(string message)
        {
            EOSDebugLogger.LogWarning(DebugCategory.Client, "EOSClient", message);
        }

        #endregion
    }
}
