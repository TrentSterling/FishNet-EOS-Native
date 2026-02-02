using FishNet.Transport.EOSNative.Migration;
using UnityEditor;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Editor
{
    /// <summary>
    /// Custom inspector for HostMigratable components.
    /// Shows network state, PUID, pending repossession status, and migration info.
    /// </summary>
    [CustomEditor(typeof(HostMigratable))]
    public class HostMigratableEditor : UnityEditor.Editor
    {
        private bool _loadStateFoldout;

        public override void OnInspectorGUI()
        {
            var migratable = (HostMigratable)target;

            // Static debug toggle
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Debug Logging:", GUILayout.Width(100));
            HostMigratable.EnableDebugLogs = EditorGUILayout.Toggle(HostMigratable.EnableDebugLogs);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Network State Section
            EditorGUILayout.LabelField("Network State", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see network state.", MessageType.Info);
            }
            else
            {
                DrawNetworkState(migratable);
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // Owner PUID Section
            EditorGUILayout.LabelField("Owner Information", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            DrawOwnerInfo(migratable);

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // Pending Repossession Section
            EditorGUILayout.LabelField("Repossession Status", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            DrawRepossessionStatus(migratable);

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // LoadState Section (if present)
            if (migratable.LoadState.HasValue)
            {
                EditorGUILayout.LabelField("Pending Load State", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                DrawLoadState(migratable.LoadState.Value);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(10);
            }

            // Transform Section
            EditorGUILayout.LabelField("Transform", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.Vector3Field("Position", migratable.transform.position);
            EditorGUILayout.Vector3Field("Rotation", migratable.transform.rotation.eulerAngles);

            EditorGUI.indentLevel--;

            // Force repaint during play mode
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawNetworkState(HostMigratable migratable)
        {
            bool isSpawned = migratable.IsSpawned;
            bool isServer = migratable.IsServerInitialized;
            bool isOwner = migratable.IsOwner;
            int ownerId = migratable.OwnerId;

            EditorGUILayout.BeginHorizontal();
            DrawStatusLabel("Spawned", isSpawned);
            DrawStatusLabel("Server", isServer);
            DrawStatusLabel("Owner", isOwner);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Owner ID", ownerId >= 0 ? ownerId.ToString() : "None (-1)");
        }

        private void DrawOwnerInfo(HostMigratable migratable)
        {
            string puid = migratable.OwnerPuidSyncVar?.Value ?? "";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("PUID:");

            if (string.IsNullOrEmpty(puid))
            {
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
            }
            else
            {
                // Show truncated PUID
                string truncated = puid.Length > 16 ? puid.Substring(0, 8) + "..." + puid.Substring(puid.Length - 4) : puid;
                EditorGUILayout.SelectableLabel(truncated, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    GUIUtility.systemCopyBuffer = puid;
                    Debug.Log($"[HostMigratableEditor] Copied PUID: {puid}");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRepossessionStatus(HostMigratable migratable)
        {
            string puid = migratable.OwnerPuidSyncVar?.Value ?? "";
            bool isPending = false;

            if (!string.IsNullOrEmpty(puid) && HostMigratable.PendingRepossessions.TryGetValue(puid, out var list))
            {
                isPending = list.Contains(migratable);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Pending Repossession:");
            DrawStatusLabel(isPending ? "YES" : "No", isPending, isPending ? Color.yellow : Color.gray);
            EditorGUILayout.EndHorizontal();

            // Show total pending count
            int totalPending = 0;
            foreach (var kvp in HostMigratable.PendingRepossessions)
            {
                totalPending += kvp.Value.Count;
            }

            EditorGUILayout.LabelField("Total Pending (all owners)", totalPending.ToString());
        }

        private void DrawLoadState(MigratableObjectState state)
        {
            _loadStateFoldout = EditorGUILayout.Foldout(_loadStateFoldout, "State Preview", true);

            if (_loadStateFoldout)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("Prefab Name", state.PrefabName ?? "(null)");
                EditorGUILayout.Vector3Field("Position", state.Position);
                EditorGUILayout.Vector3Field("Rotation", state.Rotation.eulerAngles);

                if (!string.IsNullOrEmpty(state.OwnerPuid))
                {
                    string truncated = state.OwnerPuid.Length > 16
                        ? state.OwnerPuid.Substring(0, 8) + "..."
                        : state.OwnerPuid;
                    EditorGUILayout.LabelField("Owner PUID", truncated);
                }

                if (state.SyncVarData != null)
                {
                    EditorGUILayout.LabelField("SyncVar Data", $"{state.SyncVarData.Count} components");
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawStatusLabel(string label, bool isActive, Color? activeColor = null)
        {
            Color color = isActive ? (activeColor ?? new Color(0.3f, 0.8f, 0.3f)) : new Color(0.5f, 0.5f, 0.5f);
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = color },
                fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal
            };
            EditorGUILayout.LabelField(label, style, GUILayout.Width(60));
        }
    }

    /// <summary>
    /// Property drawer for MigratableObjectState struct.
    /// Shows a foldout with all state fields.
    /// </summary>
    [CustomPropertyDrawer(typeof(MigratableObjectState))]
    public class MigratableObjectStateDrawer : PropertyDrawer
    {
        private bool _foldout;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            _foldout = EditorGUI.Foldout(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight), _foldout, label, true);

            if (_foldout)
            {
                EditorGUI.indentLevel++;
                float y = position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                float lineHeight = EditorGUIUtility.singleLineHeight;
                float spacing = EditorGUIUtility.standardVerticalSpacing;

                var prefabNameProp = property.FindPropertyRelative("PrefabName");
                var positionProp = property.FindPropertyRelative("Position");
                var rotationProp = property.FindPropertyRelative("Rotation");
                var ownerPuidProp = property.FindPropertyRelative("OwnerPuid");

                if (prefabNameProp != null)
                {
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), prefabNameProp);
                    y += lineHeight + spacing;
                }

                if (positionProp != null)
                {
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), positionProp);
                    y += lineHeight + spacing;
                }

                if (rotationProp != null)
                {
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), rotationProp);
                    y += lineHeight + spacing;
                }

                if (ownerPuidProp != null)
                {
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), ownerPuidProp);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!_foldout) return EditorGUIUtility.singleLineHeight;

            float height = EditorGUIUtility.singleLineHeight; // Foldout
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            // PrefabName, Position, Rotation, OwnerPuid
            height += 4 * (lineHeight + spacing);

            return height;
        }
    }
}
