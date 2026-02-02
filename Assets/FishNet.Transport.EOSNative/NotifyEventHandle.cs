using System;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Auto-disposing wrapper for EOS notification handles.
    /// When disposed, automatically calls the remove delegate to unsubscribe from notifications.
    /// Pattern borrowed from PlayEveryWare EOS Plugin.
    /// </summary>
    public class NotifyEventHandle : IDisposable
    {
        private ulong _handle;
        private readonly Action<ulong> _removeDelegate;
        private bool _disposed;

        /// <summary>
        /// Whether this handle is valid (non-zero and not disposed).
        /// </summary>
        public bool IsValid => _handle != 0 && !_disposed;

        /// <summary>
        /// The raw notification handle value.
        /// </summary>
        public ulong Handle => _handle;

        /// <summary>
        /// Creates a new NotifyEventHandle.
        /// </summary>
        /// <param name="handle">The notification handle returned by EOS AddNotify* methods.</param>
        /// <param name="removeDelegate">The delegate to call when disposing (e.g., RemoveNotify*).</param>
        public NotifyEventHandle(ulong handle, Action<ulong> removeDelegate)
        {
            _handle = handle;
            _removeDelegate = removeDelegate;
            _disposed = false;
        }

        /// <summary>
        /// Disposes the handle and unsubscribes from the notification.
        /// Safe to call even if EOS SDK has already shut down.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle != 0 && _removeDelegate != null)
            {
                try
                {
                    _removeDelegate(_handle);
                }
                catch (Exception)
                {
                    // Ignore - EOS SDK may have already shut down
                }
                _handle = 0;
            }

            GC.SuppressFinalize(this);
        }

        ~NotifyEventHandle()
        {
            Dispose();
        }

        /// <summary>
        /// Implicit conversion to ulong for use with EOS APIs that expect the raw handle.
        /// </summary>
        public static implicit operator ulong(NotifyEventHandle handle) => handle?._handle ?? 0;
    }
}
