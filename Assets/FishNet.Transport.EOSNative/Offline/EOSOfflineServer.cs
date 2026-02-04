using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;

namespace FishNet.Transport.EOSNative.Offline
{
    /// <summary>
    /// Local server socket for offline/singleplayer mode.
    /// Routes packets directly to the local client without any network.
    /// </summary>
    internal class EOSOfflineServer
    {
        #region Private
        private EOSNativeTransport _transport;
        private EOSOfflineClient _client;
        private Queue<OfflinePacket> _incoming = new Queue<OfflinePacket>();
        private LocalConnectionState _connectionState = LocalConnectionState.Stopped;
        #endregion

        /// <summary>
        /// Initializes the offline server with references.
        /// </summary>
        internal void Initialize(EOSNativeTransport transport, EOSOfflineClient client)
        {
            _transport = transport;
            _client = client;
        }

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        internal LocalConnectionState GetLocalConnectionState() => _connectionState;

        /// <summary>
        /// Gets the connection state of a remote client (always the local client in offline mode).
        /// </summary>
        internal RemoteConnectionState GetConnectionState(int connectionId)
        {
            if (connectionId != NetworkConnection.SIMULATED_CLIENTID_VALUE)
                return RemoteConnectionState.Stopped;

            return _client.GetLocalConnectionState() == LocalConnectionState.Started
                ? RemoteConnectionState.Started
                : RemoteConnectionState.Stopped;
        }

        /// <summary>
        /// Starts the offline server.
        /// </summary>
        internal bool StartConnection()
        {
            if (_connectionState != LocalConnectionState.Stopped)
                return false;

            SetConnectionState(LocalConnectionState.Starting);
            SetConnectionState(LocalConnectionState.Started);
            return true;
        }

        /// <summary>
        /// Stops the offline server.
        /// </summary>
        internal bool StopConnection()
        {
            if (_connectionState == LocalConnectionState.Stopped)
                return false;

            ClearIncoming();
            SetConnectionState(LocalConnectionState.Stopping);
            SetConnectionState(LocalConnectionState.Stopped);
            return true;
        }

        /// <summary>
        /// Stops a specific client connection.
        /// </summary>
        internal bool StopConnection(int connectionId)
        {
            if (connectionId != NetworkConnection.SIMULATED_CLIENTID_VALUE)
                return false;

            _client.StopConnection();
            return true;
        }

        /// <summary>
        /// Processes incoming packets from the local client.
        /// </summary>
        internal void IterateIncoming()
        {
            if (_connectionState != LocalConnectionState.Started)
                return;

            while (_incoming.Count > 0)
            {
                OfflinePacket packet = _incoming.Dequeue();
                ArraySegment<byte> segment = new ArraySegment<byte>(packet.Data, 0, packet.Length);
                ServerReceivedDataArgs args = new ServerReceivedDataArgs(
                    segment,
                    (Channel)packet.Channel,
                    NetworkConnection.SIMULATED_CLIENTID_VALUE,
                    _transport.Index
                );
                _transport.HandleServerReceivedDataArgs(args);
                packet.Dispose();
            }
        }

        /// <summary>
        /// Sends data to the local client.
        /// </summary>
        internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (_connectionState != LocalConnectionState.Started)
                return;
            if (connectionId != NetworkConnection.SIMULATED_CLIENTID_VALUE)
                return;

            OfflinePacket packet = new OfflinePacket(segment, channelId);
            _client.ReceivedFromServer(packet);
        }

        /// <summary>
        /// Called when the local client sends data to the server.
        /// </summary>
        internal void ReceivedFromClient(OfflinePacket packet)
        {
            if (_client.GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            _incoming.Enqueue(packet);
        }

        /// <summary>
        /// Called when the local client's connection state changes.
        /// </summary>
        internal void OnClientConnectionStateChanged(LocalConnectionState state)
        {
            if (state != LocalConnectionState.Started)
            {
                ClearIncoming();

                if (state == LocalConnectionState.Stopped)
                {
                    _transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(
                        RemoteConnectionState.Stopped,
                        NetworkConnection.SIMULATED_CLIENTID_VALUE,
                        _transport.Index
                    ));
                }
            }
            else
            {
                _transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(
                    RemoteConnectionState.Started,
                    NetworkConnection.SIMULATED_CLIENTID_VALUE,
                    _transport.Index
                ));
            }
        }

        private void SetConnectionState(LocalConnectionState state)
        {
            if (state == _connectionState)
                return;

            _connectionState = state;
            _transport.HandleServerConnectionState(new ServerConnectionStateArgs(state, _transport.Index));
            _client.OnServerConnectionStateChanged(state);
        }

        private void ClearIncoming()
        {
            while (_incoming.Count > 0)
            {
                OfflinePacket packet = _incoming.Dequeue();
                packet.Dispose();
            }
        }
    }
}
