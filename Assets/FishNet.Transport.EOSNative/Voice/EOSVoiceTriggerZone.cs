using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Voice
{
    /// <summary>
    /// A trigger-based voice zone. Players in the same zone can hear each other.
    /// Attach to a GameObject with a Collider set to isTrigger=true.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EOSVoiceTriggerZone : MonoBehaviour
    {
        [Header("Zone Settings")]
        [Tooltip("Unique name for this zone")]
        [SerializeField] private string _zoneName = "Zone1";

        [Tooltip("Color for gizmo visualization")]
        [SerializeField] private Color _gizmoColor = new Color(0f, 1f, 0.5f, 0.3f);

        [Header("Options")]
        [Tooltip("Set this as the default zone for players not in any trigger")]
        [SerializeField] private bool _isDefaultZone = false;

        [Tooltip("Tag to filter which objects are tracked as players")]
        [SerializeField] private string _playerTag = "Player";

        /// <summary>Zone name for this trigger.</summary>
        public string ZoneName => _zoneName;

        /// <summary>Whether this is the default zone.</summary>
        public bool IsDefaultZone => _isDefaultZone;

        private void Awake()
        {
            // Ensure collider is a trigger
            var collider = GetComponent<Collider>();
            if (collider != null && !collider.isTrigger)
            {
                collider.isTrigger = true;
                Debug.LogWarning($"[EOSVoiceTriggerZone] Collider on '{gameObject.name}' set to trigger mode.");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsValidPlayer(other)) return;

            var zoneManager = EOSVoiceZoneManager.Instance;
            if (zoneManager == null) return;

            // Check if this is the local player
            if (IsLocalPlayer(other))
            {
                zoneManager.SetLocalZone(_zoneName);
                Debug.Log($"[EOSVoiceTriggerZone] Local player entered zone: {_zoneName}");
            }
            else
            {
                // Get PUID for remote player
                string puid = GetPlayerPuid(other);
                if (!string.IsNullOrEmpty(puid))
                {
                    zoneManager.SetPlayerZone(puid, _zoneName);
                    Debug.Log($"[EOSVoiceTriggerZone] Player {puid.Substring(0, 8)}... entered zone: {_zoneName}");
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsValidPlayer(other)) return;

            var zoneManager = EOSVoiceZoneManager.Instance;
            if (zoneManager == null) return;

            // When exiting, set to default zone
            string defaultZone = GetDefaultZoneName();

            if (IsLocalPlayer(other))
            {
                zoneManager.SetLocalZone(defaultZone);
                Debug.Log($"[EOSVoiceTriggerZone] Local player exited to zone: {defaultZone}");
            }
            else
            {
                string puid = GetPlayerPuid(other);
                if (!string.IsNullOrEmpty(puid))
                {
                    zoneManager.SetPlayerZone(puid, defaultZone);
                    Debug.Log($"[EOSVoiceTriggerZone] Player {puid.Substring(0, 8)}... exited to zone: {defaultZone}");
                }
            }
        }

        private bool IsValidPlayer(Collider other)
        {
            if (string.IsNullOrEmpty(_playerTag)) return true;
            return other.CompareTag(_playerTag);
        }

        private bool IsLocalPlayer(Collider other)
        {
            // Check via FishNet NetworkObject ownership
            var nob = other.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null)
            {
                return nob.IsOwner;
            }

            // Fallback: check if it has a component marked as local
            // Users can customize this check
            return false;
        }

        private string GetPlayerPuid(Collider other)
        {
            var nob = other.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob == null || nob.Owner == null || !nob.Owner.IsValid) return null;

            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return null;

            return registry.GetPuid(nob.OwnerId);
        }

        private string GetDefaultZoneName()
        {
            // Find the default zone if one exists
#if UNITY_2023_1_OR_NEWER
            var zones = FindObjectsByType<EOSVoiceTriggerZone>(FindObjectsSortMode.None);
#else
            var zones = FindObjectsOfType<EOSVoiceTriggerZone>();
#endif
            foreach (var zone in zones)
            {
                if (zone._isDefaultZone)
                    return zone._zoneName;
            }
            return "default";
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            Gizmos.color = _gizmoColor;

            if (collider is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (collider is SphereCollider sphere)
            {
                Gizmos.DrawSphere(transform.position + sphere.center, sphere.radius);
                Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius);
            }
            else if (collider is CapsuleCollider capsule)
            {
                // Simplified capsule visualization
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(capsule.center + Vector3.up * (capsule.height / 2 - capsule.radius), capsule.radius);
                Gizmos.DrawWireSphere(capsule.center - Vector3.up * (capsule.height / 2 - capsule.radius), capsule.radius);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw label
            Handles.Label(transform.position + Vector3.up * 2f, $"Voice Zone: {_zoneName}");
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSVoiceTriggerZone))]
    public class EOSVoiceTriggerZoneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var zone = (EOSVoiceTriggerZone)target;

            EditorGUILayout.Space(10);

            // Validation
            var collider = zone.GetComponent<Collider>();
            if (collider == null)
            {
                EditorGUILayout.HelpBox("Add a Collider component (Box, Sphere, etc.) to define the zone area.", MessageType.Warning);
            }
            else if (!collider.isTrigger)
            {
                EditorGUILayout.HelpBox("Collider should be set to 'Is Trigger'. It will be set automatically at runtime.", MessageType.Info);
                if (GUILayout.Button("Set as Trigger"))
                {
                    collider.isTrigger = true;
                    EditorUtility.SetDirty(collider);
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "Voice Zones work with Custom mode.\n" +
                "1. Set EOSVoiceZoneManager to Custom mode\n" +
                "2. Players in the same zone can hear each other\n" +
                "3. Players in different zones are muted",
                MessageType.Info);
        }
    }
#endif
}
