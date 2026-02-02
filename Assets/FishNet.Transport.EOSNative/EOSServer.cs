using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using FishNet.Transporting;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Handles server-side P2P connections for EOS transport.
    /// </summary>
    public class EOSServer
    {
        #region Private Fields

        private readonly EOSNativeTransport _transport;
        private readonly Dictionary<int, Connection> _connections = new();
        private readonly Dictionary<ProductUserId, int> _userIdToConnectionId = new();
        private readonly Dictionary<int, float> _lastPacketTime = new();

        private string _socketName;
        private int _maxClients;
        private int _nextConnectionId = 1;
        private float _heartbeatTimeout = 5f;
        private bool _checkSanctionsBeforeAccept = false;

        // Pending connections waiting for sanctions check
        private readonly HashSet<string> _pendingSanctionChecks = new();

        private NotifyEventHandle _connectionRequestHandle;
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
        /// Total bytes sent since server started.
        /// </summary>
        public long TotalBytesSent => _totalBytesSent;

        /// <summary>
        /// Total bytes received since server started.
        /// </summary>
        public long TotalBytesReceived => _totalBytesReceived;

        /// <summary>
        /// Number of active P2P connections (excludes ClientHost).
        /// </summary>
        public int ConnectionCount => _connections.Count;

        /// <summary>
        /// Gets all active connection info for monitoring/debugging.
        /// Returns a list of (connectionId, puid, lastPacketTime) tuples.
        /// </summary>
        public List<(int connectionId, string puid, float lastPacketAge)> GetAllConnectionInfo()
        {
            var result = new List<(int, string, float)>();
            float now = Time.realtimeSinceStartup;

            foreach (var kvp in _connections)
            {
                int connId = kvp.Key;
                string puid = kvp.Value.RemoteUserId?.ToString() ?? "unknown";
                float lastPacket = _lastPacketTime.TryGetValue(connId, out float t) ? t : now;
                float age = now - lastPacket;
                result.Add((connId, puid, age));
            }

            return result;
        }

        #endregion

        #region Constructor

        public EOSServer(EOSNativeTransport transport)
        {
            _transport = transport;
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Sets the heartbeat timeout in seconds. Connections without packets for this duration will be disconnected.
        /// </summary>
        public void SetHeartbeatTimeout(float timeout)
        {
            _heartbeatTimeout = timeout;
        }

        /// <summary>
        /// When enabled, checks EOS Sanctions before accepting connections.
        /// Banned players will be rejected. Requires EOSSanctions to be ready.
        /// </summary>
        public bool CheckSanctionsBeforeAccept
        {
            get => _checkSanctionsBeforeAccept;
            set => _checkSanctionsBeforeAccept = value;
        }

        /// <summary>
        /// Gets the ProductUserId (PUID) string for a given connection ID.
        /// Used for voice chat to map FishNet connections to EOS users.
        /// </summary>
        public string GetPuidForConnection(int connectionId)
        {
            if (_connections.TryGetValue(connectionId, out Connection connection))
            {
                return connection.RemoteUserId?.ToString();
            }
            return null;
        }

        #endregion

        #region Start / Stop

        public bool Start(string socketName, int maxClients)
        {
            if (P2P == null || LocalUserId == null)
            {
                EOSDebugLogger.LogError("EOSServer", "P2P interface or LocalUserId not available.");
                return false;
            }

            _socketName = socketName;
            _maxClients = maxClients;
            _connections.Clear();
            _userIdToConnectionId.Clear();
            _lastPacketTime.Clear();
            _nextConnectionId = 1;
            _totalBytesSent = 0;
            _totalBytesReceived = 0;

            // Register for connection requests
            var requestOptions = new AddNotifyPeerConnectionRequestOptions
            {
                LocalUserId = LocalUserId,
                SocketId = new SocketId { SocketName = _socketName }
            };
            ulong requestHandle = P2P.AddNotifyPeerConnectionRequest(ref requestOptions, null, OnConnectionRequest);
            _connectionRequestHandle = new NotifyEventHandle(requestHandle, h => P2P.RemoveNotifyPeerConnectionRequest(h));

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

            Log($" Started listening on socket '{_socketName}'");
            return true;
        }

        public void Stop()
        {
            // Close all connections - iterate over a copy of values to avoid modification during enumeration
            var connectionsToClose = new List<Connection>(_connections.Values);
            foreach (var conn in connectionsToClose)
            {
                CloseConnection(conn);
            }
            _connections.Clear();
            _userIdToConnectionId.Clear();
            _lastPacketTime.Clear();
            _fragmenter.ClearAll();

            // Dispose notification handles
            _connectionRequestHandle?.Dispose();
            _connectionEstablishedHandle?.Dispose();
            _connectionClosedHandle?.Dispose();

            _connectionRequestHandle = null;
            _connectionEstablishedHandle = null;
            _connectionClosedHandle = null;

            Log("Stopped.");
        }

        #endregion

        #region Connection Callbacks

        private void OnConnectionRequest(ref OnIncomingConnectionRequestInfo data)
        {
            if (data.SocketId?.SocketName != _socketName)
            {
                LogWarning($" Ignoring connection request on wrong socket: {data.SocketId?.SocketName}");
                return;
            }

            // Check if we've reached max clients
            if (_connections.Count >= _maxClients)
            {
                LogWarning($" Rejecting connection - max clients ({_maxClients}) reached.");
                return;
            }

            // Check sanctions if enabled
            if (_checkSanctionsBeforeAccept)
            {
                string remotePuid = data.RemoteUserId.ToString();
                if (_pendingSanctionChecks.Contains(remotePuid))
                {
                    // Already checking, ignore duplicate request
                    return;
                }

                // Capture data for async callback
                var remoteUserId = data.RemoteUserId;
                var socketId = data.SocketId;

                _pendingSanctionChecks.Add(remotePuid);
                CheckSanctionsAndAcceptAsync(remoteUserId, socketId.Value);
                return;
            }

            // No sanctions check - accept immediately
            AcceptConnectionInternal(data.RemoteUserId, data.SocketId.Value);
        }

        private async void CheckSanctionsAndAcceptAsync(ProductUserId remoteUserId, SocketId socketId)
        {
            string remotePuid = remoteUserId.ToString();

            try
            {
                var sanctions = EOSSanctions.Instance;
                if (sanctions != null && sanctions.IsReady)
                {
                    bool isBanned = await sanctions.IsPlayerBannedAsync(remotePuid);
                    if (isBanned)
                    {
                        LogWarning($" Rejecting banned player: {remotePuid}");
                        _pendingSanctionChecks.Remove(remotePuid);
                        return;
                    }
                }
                else
                {
                    Debug.LogWarning("[EOSServer] Sanctions check enabled but EOSSanctions not ready - allowing connection");
                }

                // Not banned (or couldn't check) - accept the connection
                AcceptConnectionInternal(remoteUserId, socketId);
            }
            catch (Exception e)
            {
                LogWarning($" Sanctions check failed, allowing connection: {e.Message}");
                AcceptConnectionInternal(remoteUserId, socketId);
            }
            finally
            {
                _pendingSanctionChecks.Remove(remotePuid);
            }
        }

        private void AcceptConnectionInternal(ProductUserId remoteUserId, SocketId socketId)
        {
            // Accept the connection
            var acceptOptions = new AcceptConnectionOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = remoteUserId,
                SocketId = socketId
            };

            Result result = P2P.AcceptConnection(ref acceptOptions);
            if (result != Result.Success)
            {
                Debug.LogError($"[EOSServer] Failed to accept connection: {result}");
                return;
            }

            // Create connection with incoming state
            int connectionId = _nextConnectionId++;
            var connection = new Connection(connectionId, LocalUserId, remoteUserId, socketId)
            {
                OpenedIncoming = true
            };

            _connections[connectionId] = connection;
            _userIdToConnectionId[remoteUserId] = connectionId;

            Log($" Accepted connection request from {remoteUserId}, assigned ID {connectionId}, socket '{socketId.SocketName}'");
        }

        private void OnConnectionEstablished(ref OnPeerConnectionEstablishedInfo data)
        {
            if (data.SocketId?.SocketName != _socketName) return;

            if (!_userIdToConnectionId.TryGetValue(data.RemoteUserId, out int connectionId))
            {
                LogWarning($" Connection established for unknown user: {data.RemoteUserId}");
                return;
            }

            if (!_connections.TryGetValue(connectionId, out Connection connection))
            {
                return;
            }

            connection.OpenedOutgoing = true;

            if (!connection.ConnectionOpenedHandled && connection.IsFullyOpened)
            {
                connection.ConnectionOpenedHandled = true;
                _lastPacketTime[connectionId] = Time.realtimeSinceStartup;
                _transport.InvokeRemoteConnectionState(RemoteConnectionState.Started, connectionId);
                Log($" Connection {connectionId} fully established with {data.RemoteUserId}");
            }
        }

        private void OnConnectionClosed(ref OnRemoteConnectionClosedInfo data)
        {
            if (data.SocketId?.SocketName != _socketName) return;

            if (!_userIdToConnectionId.TryGetValue(data.RemoteUserId, out int connectionId))
            {
                return;
            }

            if (!_connections.TryGetValue(connectionId, out Connection connection))
            {
                return;
            }

            if (!connection.ConnectionClosedHandled)
            {
                connection.ConnectionClosedHandled = true;
                _transport.InvokeRemoteConnectionState(RemoteConnectionState.Stopped, connectionId);
                Log($" Connection {connectionId} closed. Reason: {data.Reason}");
            }

            _connections.Remove(connectionId);
            _userIdToConnectionId.Remove(data.RemoteUserId);
            _lastPacketTime.Remove(connectionId);
        }

        #endregion

        #region Send / Receive

        public void Send(int connectionId, ArraySegment<byte> data, Channel channel)
        {
            if (!_connections.TryGetValue(connectionId, out Connection connection))
            {
                LogWarning($" Cannot send - connection {connectionId} not found.");
                return;
            }

            // Use EOS channel 0 for Reliable, channel 1 for Unreliable
            // This allows the receiver to determine the original FishNet channel
            byte eosChannel = channel == Channel.Reliable ? (byte)0 : (byte)1;

            // Fragment if needed (EOS max packet = 1170 bytes)
            foreach (var packet in _fragmenter.Fragment(data))
            {
                var sendOptions = new SendPacketOptions
                {
                    LocalUserId = LocalUserId,
                    RemoteUserId = connection.RemoteUserId,
                    SocketId = connection.SocketId,
                    Channel = eosChannel,
                    Data = packet,
                    Reliability = channel == Channel.Reliable ? PacketReliability.ReliableOrdered : PacketReliability.UnreliableUnordered,
                    AllowDelayedDelivery = true
                };

                Result result = P2P.SendPacket(ref sendOptions);
                if (result != Result.Success)
                {
                    LogWarning($" SendPacket failed for connection {connectionId}: {result}");
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
            // This avoids issues with GetNextReceivedPacketSize not finding packets
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

                // Find connection ID for this remote user
                if (!_userIdToConnectionId.TryGetValue(remoteUserId, out int connectionId))
                {
                    LogWarning($" Received packet from unknown user: {remoteUserId}");
                    continue;
                }

                // Skip empty packets (connection establishment)
                if (bytesWritten == 0)
                {
                    continue;
                }

                // Track bandwidth
                _totalBytesReceived += bytesWritten;

                // Update last packet time for heartbeat tracking
                _lastPacketTime[connectionId] = Time.realtimeSinceStartup;

                // Process through fragmenter for reassembly
                var rawData = new ArraySegment<byte>(_receiveBuffer, 0, (int)bytesWritten);
                byte[] reassembled = _fragmenter.ProcessIncoming(connectionId, rawData, outChannel);

                if (reassembled == null)
                {
                    // Still waiting for more fragments
                    continue;
                }

                Channel channel = outChannel == 0 ? Channel.Reliable : Channel.Unreliable;
                _transport.InvokeServerReceivedData(new ArraySegment<byte>(reassembled), channel, connectionId);
            }
        }

        public void IterateOutgoing()
        {
            // Outgoing is handled in Send methods
        }

        #endregion

        #region Connection Management

        public bool StopConnection(int connectionId, bool immediately)
        {
            if (!_connections.TryGetValue(connectionId, out Connection connection))
            {
                return false;
            }

            CloseConnection(connection);

            if (!connection.ConnectionClosedHandled)
            {
                connection.ConnectionClosedHandled = true;
                _transport.InvokeRemoteConnectionState(RemoteConnectionState.Stopped, connectionId);
            }

            _connections.Remove(connectionId);
            _userIdToConnectionId.Remove(connection.RemoteUserId);
            _lastPacketTime.Remove(connectionId);
            _fragmenter.ClearPendingForSender(connectionId);

            Log($" Stopped connection {connectionId}");
            return true;
        }

        /// <summary>
        /// Checks for stale connections and disconnects them.
        /// Called from transport's Update loop.
        /// </summary>
        public void CheckHeartbeats()
        {
            if (_connections.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            var staleConnections = new List<int>();

            foreach (var kvp in _lastPacketTime)
            {
                if (now - kvp.Value > _heartbeatTimeout)
                {
                    staleConnections.Add(kvp.Key);
                }
            }

            foreach (int connectionId in staleConnections)
            {
                Log($" Connection {connectionId} timed out (no packets for {_heartbeatTimeout}s)");
                _transport.StopConnection(connectionId, immediately: true);
            }
        }

        private void CloseConnection(Connection connection)
        {
            if (P2P == null) return;

            var closeOptions = new CloseConnectionOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = connection.RemoteUserId,
                SocketId = connection.SocketId
            };

            P2P.CloseConnection(ref closeOptions);
        }

        public RemoteConnectionState GetConnectionState(int connectionId)
        {
            if (_connections.TryGetValue(connectionId, out Connection connection))
            {
                return connection.IsFullyOpened ? RemoteConnectionState.Started : RemoteConnectionState.Stopped;
            }
            return RemoteConnectionState.Stopped;
        }

        public string GetConnectionAddress(int connectionId)
        {
            if (_connections.TryGetValue(connectionId, out Connection connection))
            {
                return connection.RemoteUserId?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets the connection ID for a given ProductUserId string (PUID).
        /// Used by lobby notifications to map member disconnects to FishNet connections.
        /// </summary>
        /// <param name="puid">The ProductUserId string to look up.</param>
        /// <returns>The connection ID, or -1 if not found.</returns>
        public int GetConnectionIdByPuid(string puid)
        {
            if (string.IsNullOrEmpty(puid))
                return -1;

            foreach (var kvp in _userIdToConnectionId)
            {
                if (kvp.Key?.ToString() == puid)
                    return kvp.Value;
            }
            return -1;
        }

        #endregion

        #region Logging

        private static void Log(string message)
        {
            EOSDebugLogger.Log(DebugCategory.Server, "EOSServer", message);
        }

        private static void LogWarning(string message)
        {
            EOSDebugLogger.LogWarning(DebugCategory.Server, "EOSServer", message);
        }

        #endregion
    }
}
