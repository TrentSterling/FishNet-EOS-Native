using FishNet.Transporting;
using System;
using System.Collections.Generic;

namespace FishNet.Transport.EOSNative.Offline
{
    /// <summary>
    /// Local client socket for offline/singleplayer mode.
    /// Routes packets directly to the local server without any network.
    /// </summary>
    internal class EOSOfflineClient
    {
        #region Private
        private EOSNativeTransport _transport;
        private EOSOfflineServer _server;
        private Queue<OfflinePacket> _incoming = new Queue<OfflinePacket>();
        private LocalConnectionState _connectionState = LocalConnectionState.Stopped;
        #endregion

        /// <summary>
        /// Initializes the offline client with references.
        /// </summary>
        internal void Initialize(EOSNativeTransport transport, EOSOfflineServer server)
        {
            _transport = transport;
            _server = server;
        }

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        internal LocalConnectionState GetLocalConnectionState() => _connectionState;

        /// <summary>
        /// Starts the offline client connection.
        /// </summary>
        internal bool StartConnection()
        {
            if (_connectionState != LocalConnectionState.Stopped)
                return false;

            SetConnectionState(LocalConnectionState.Starting);

            // If server is already started, immediately connect
            LocalConnectionState serverState = _server.GetLocalConnectionState();
            if (serverState == LocalConnectionState.Started)
            {
                SetConnectionState(LocalConnectionState.Started);
            }
            else if (serverState == LocalConnectionState.Stopping || serverState == LocalConnectionState.Stopped)
            {
                // Server not available, fail connection
                SetConnectionState(LocalConnectionState.Stopping);
                SetConnectionState(LocalConnectionState.Stopped);
            }
            // Otherwise wait for server to start (OnServerConnectionStateChanged will be called)

            return true;
        }

        /// <summary>
        /// Stops the offline client connection.
        /// </summary>
        internal bool StopConnection()
        {
            if (_connectionState == LocalConnectionState.Stopped || _connectionState == LocalConnectionState.Stopping)
                return false;

            ClearIncoming();
            SetConnectionState(LocalConnectionState.Stopping);
            SetConnectionState(LocalConnectionState.Stopped);
            return true;
        }

        /// <summary>
        /// Processes incoming packets from the local server.
        /// </summary>
        internal void IterateIncoming()
        {
            if (_connectionState != LocalConnectionState.Started)
                return;

            while (_incoming.Count > 0)
            {
                OfflinePacket packet = _incoming.Dequeue();
                ArraySegment<byte> segment = new ArraySegment<byte>(packet.Data, 0, packet.Length);
                ClientReceivedDataArgs args = new ClientReceivedDataArgs(
                    segment,
                    (Channel)packet.Channel,
                    _transport.Index
                );
                _transport.HandleClientReceivedDataArgs(args);
                packet.Dispose();
            }
        }

        /// <summary>
        /// Sends data to the local server.
        /// </summary>
        internal void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (_connectionState != LocalConnectionState.Started)
                return;
            if (_server.GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            OfflinePacket packet = new OfflinePacket(segment, channelId);
            _server.ReceivedFromClient(packet);
        }

        /// <summary>
        /// Called when the local server sends data to this client.
        /// </summary>
        internal void ReceivedFromServer(OfflinePacket packet)
        {
            _incoming.Enqueue(packet);
        }

        /// <summary>
        /// Called when the local server's connection state changes.
        /// </summary>
        internal void OnServerConnectionStateChanged(LocalConnectionState state)
        {
            if (state == LocalConnectionState.Started && _connectionState == LocalConnectionState.Starting)
            {
                // Server started while we were waiting
                SetConnectionState(LocalConnectionState.Started);
            }
            else if ((state == LocalConnectionState.Stopping || state == LocalConnectionState.Stopped) &&
                     (_connectionState == LocalConnectionState.Started || _connectionState == LocalConnectionState.Starting))
            {
                // Server stopped, disconnect client
                ClearIncoming();
                SetConnectionState(LocalConnectionState.Stopping);
                SetConnectionState(LocalConnectionState.Stopped);
            }
        }

        private void SetConnectionState(LocalConnectionState state)
        {
            if (state == _connectionState)
                return;

            _connectionState = state;
            _transport.HandleClientConnectionState(new ClientConnectionStateArgs(state, _transport.Index));

            if (state == LocalConnectionState.Started || state == LocalConnectionState.Stopped)
            {
                _server.OnClientConnectionStateChanged(state);
            }
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
