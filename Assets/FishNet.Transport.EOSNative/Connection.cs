using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Represents a P2P connection with state tracking.
    /// Tracks both outgoing and incoming connection states for proper handshake completion.
    /// </summary>
    public class Connection
    {
        /// <summary>
        /// The unique connection ID assigned by the server.
        /// </summary>
        public int ConnectionId { get; }

        /// <summary>
        /// The local user's ProductUserId.
        /// </summary>
        public ProductUserId LocalUserId { get; }

        /// <summary>
        /// The remote user's ProductUserId.
        /// </summary>
        public ProductUserId RemoteUserId { get; }

        /// <summary>
        /// The socket ID used for this connection.
        /// </summary>
        public SocketId SocketId { get; }

        /// <summary>
        /// Whether we initiated the connection (called AcceptConnection).
        /// </summary>
        public bool OpenedOutgoing { get; set; }

        /// <summary>
        /// Whether the remote side has connected to us.
        /// </summary>
        public bool OpenedIncoming { get; set; }

        /// <summary>
        /// Whether the connection is pending (we initiated but remote hasn't connected yet).
        /// </summary>
        public bool IsPendingOutgoing => OpenedOutgoing && !OpenedIncoming;

        /// <summary>
        /// Whether the connection is pending (remote initiated but we haven't accepted yet).
        /// </summary>
        public bool IsPendingIncoming => !OpenedOutgoing && OpenedIncoming;

        /// <summary>
        /// Whether the connection is fully established (both sides have connected).
        /// </summary>
        public bool IsFullyOpened => OpenedOutgoing && OpenedIncoming;

        /// <summary>
        /// Whether the OnConnectionEstablished event has been handled for this connection.
        /// </summary>
        public bool ConnectionOpenedHandled { get; set; }

        /// <summary>
        /// Whether the OnConnectionClosed event has been handled for this connection.
        /// </summary>
        public bool ConnectionClosedHandled { get; set; }

        public Connection(int connectionId, ProductUserId localUserId, ProductUserId remoteUserId, SocketId socketId)
        {
            ConnectionId = connectionId;
            LocalUserId = localUserId;
            RemoteUserId = remoteUserId;
            SocketId = socketId;
            OpenedOutgoing = false;
            OpenedIncoming = false;
            ConnectionOpenedHandled = false;
            ConnectionClosedHandled = false;
        }

        public override string ToString()
        {
            return $"Connection[Id={ConnectionId}, Remote={RemoteUserId}, Outgoing={OpenedOutgoing}, Incoming={OpenedIncoming}]";
        }
    }
}
