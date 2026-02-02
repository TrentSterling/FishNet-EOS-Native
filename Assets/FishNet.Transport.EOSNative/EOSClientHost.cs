using System;
using System.Collections.Generic;
using FishNet.Transporting;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Handles local client connection when the host is acting as both server and client.
    /// Uses direct memory queues instead of P2P for zero-latency local communication.
    /// </summary>
    public class EOSClientHost
    {
        #region Private Fields

        private readonly EOSNativeTransport _transport;
        private readonly EOSServer _server;

        // Queues for local IPC
        private readonly Queue<LocalPacket> _clientToServerQueue = new();
        private readonly Queue<LocalPacket> _serverToClientQueue = new();

        private bool _isConnected;

        #endregion

        #region Constructor

        public EOSClientHost(EOSNativeTransport transport, EOSServer server)
        {
            _transport = transport;
            _server = server;
        }

        #endregion

        #region Start / Stop

        public void Start()
        {
            _clientToServerQueue.Clear();
            _serverToClientQueue.Clear();
            _isConnected = true;

            // Notify server of ClientHost connection
            _transport.InvokeRemoteConnectionState(RemoteConnectionState.Started, EOSNativeTransport.CLIENT_HOST_ID);

            Log(" Started as local client.");
        }

        public void Stop()
        {
            if (!_isConnected) return;

            _isConnected = false;

            // Notify server of ClientHost disconnection
            _transport.InvokeRemoteConnectionState(RemoteConnectionState.Stopped, EOSNativeTransport.CLIENT_HOST_ID);

            _clientToServerQueue.Clear();
            _serverToClientQueue.Clear();

            Log(" Stopped.");
        }

        #endregion

        #region Send Methods

        /// <summary>
        /// Client sends data to server (queued for server to process).
        /// </summary>
        public void SendToServer(ArraySegment<byte> data, Channel channel)
        {
            if (!_isConnected) return;

            var packet = new LocalPacket(data, channel);
            _clientToServerQueue.Enqueue(packet);
        }

        /// <summary>
        /// Server sends data to ClientHost client (queued for client to process).
        /// </summary>
        public void SendFromServer(ArraySegment<byte> data, Channel channel)
        {
            if (!_isConnected) return;

            var packet = new LocalPacket(data, channel);
            _serverToClientQueue.Enqueue(packet);
        }

        #endregion

        #region Iterate Methods

        /// <summary>
        /// Called during server's IterateIncoming - processes packets from client to server.
        /// </summary>
        public void IterateIncoming()
        {
            if (!_isConnected) return;

            while (_clientToServerQueue.Count > 0)
            {
                var packet = _clientToServerQueue.Dequeue();
                _transport.InvokeServerReceivedData(packet.GetSegment(), packet.Channel, EOSNativeTransport.CLIENT_HOST_ID);
            }
        }

        /// <summary>
        /// Called during client's IterateIncoming - processes packets from server to client.
        /// </summary>
        public void IterateOutgoing()
        {
            if (!_isConnected) return;

            while (_serverToClientQueue.Count > 0)
            {
                var packet = _serverToClientQueue.Dequeue();
                _transport.InvokeClientReceivedData(packet.GetSegment(), packet.Channel);
            }
        }

        #endregion

        #region Properties

        public bool IsConnected => _isConnected;

        #endregion

        #region Logging

        private static void Log(string message)
        {
            EOSDebugLogger.Log(DebugCategory.ClientHost, "EOSClientHost", message);
        }

        #endregion
    }
}
