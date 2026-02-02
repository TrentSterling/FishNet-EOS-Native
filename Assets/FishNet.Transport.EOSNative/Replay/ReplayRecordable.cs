using UnityEngine;
using FishNet.Object;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Marker component that identifies objects to be recorded in replays.
    /// Add this to any NetworkObject that should appear in replay recordings.
    ///
    /// By default, all objects with NetworkObject are recorded. Add this component
    /// to customize recording behavior or to explicitly mark objects for recording.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class ReplayRecordable : MonoBehaviour
    {
        [Header("Recording Settings")]
        [Tooltip("If true, this object will be recorded in replays. If false, it will be excluded.")]
        [SerializeField] private bool _recordEnabled = true;

        [Tooltip("Custom prefab name for replay spawning. If empty, uses the NetworkObject's prefab name.")]
        [SerializeField] private string _customPrefabName = "";

        [Tooltip("Minimum position change to trigger recording (optimization).")]
        [SerializeField] private float _positionThreshold = 0.001f;

        [Tooltip("Minimum rotation change in degrees to trigger recording (optimization).")]
        [SerializeField] private float _rotationThreshold = 0.1f;

        /// <summary>Whether this object should be recorded.</summary>
        public bool RecordEnabled => _recordEnabled;

        /// <summary>Custom prefab name or empty for default.</summary>
        public string CustomPrefabName => _customPrefabName;

        /// <summary>Minimum position change threshold.</summary>
        public float PositionThreshold => _positionThreshold;

        /// <summary>Minimum rotation change threshold.</summary>
        public float RotationThreshold => _rotationThreshold;

        // Cached last recorded state for delta compression
        private Vector3 _lastRecordedPosition;
        private Quaternion _lastRecordedRotation;
        private bool _hasBeenRecorded;

        /// <summary>
        /// Check if the object has moved enough to warrant recording.
        /// </summary>
        public bool HasChangedSinceLastRecord(Vector3 currentPosition, Quaternion currentRotation)
        {
            if (!_hasBeenRecorded) return true;

            bool positionChanged = Vector3.SqrMagnitude(currentPosition - _lastRecordedPosition) > _positionThreshold * _positionThreshold;
            bool rotationChanged = Quaternion.Angle(currentRotation, _lastRecordedRotation) > _rotationThreshold;

            return positionChanged || rotationChanged;
        }

        /// <summary>
        /// Update the last recorded state.
        /// </summary>
        public void MarkRecorded(Vector3 position, Quaternion rotation)
        {
            _lastRecordedPosition = position;
            _lastRecordedRotation = rotation;
            _hasBeenRecorded = true;
        }

        /// <summary>
        /// Reset recorded state (called when recording starts).
        /// </summary>
        public void ResetRecordedState()
        {
            _hasBeenRecorded = false;
        }

        private void OnValidate()
        {
            _positionThreshold = Mathf.Max(0.0001f, _positionThreshold);
            _rotationThreshold = Mathf.Max(0.01f, _rotationThreshold);
        }
    }
}
