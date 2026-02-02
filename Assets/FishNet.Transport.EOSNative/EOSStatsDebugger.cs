using FishNet.Managing;
using FishNet.Transport.EOSNative.Lobbies;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// DEPRECATED: Stats are now integrated into EOSNativeUI (F1).
    /// This component is kept for backwards compatibility but is disabled by default.
    /// If you want a separate stats overlay, enable this component and press F2.
    /// </summary>
    [AddComponentMenu("FishNet/Transport/EOS Stats Debugger (Legacy)")]
    public class EOSStatsDebugger : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Enable this overlay. Stats are now shown in the main EOSNativeUI (F1).")]
        private bool _enabled = false;

        [SerializeField]
        [Tooltip("Show the debug overlay on start.")]
        private bool _showDebugger = false;

        [SerializeField]
        [Tooltip("Key to toggle the debug overlay.")]
        private Key _toggleKey = Key.F2;

        private void Update()
        {
            if (!_enabled) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame)
            {
                _showDebugger = !_showDebugger;
            }
        }

        private void OnGUI()
        {
            if (!_enabled || !_showDebugger) return;

            // Simple fallback overlay - main stats are in EOSNativeUI
            float width = 200f;
            float height = 60f;
            float x = Screen.width - width - 10f;
            float y = 10f;

            GUI.Box(new Rect(x, y, width, height), "");
            GUILayout.BeginArea(new Rect(x + 5, y + 5, width - 10, height - 10));

            GUILayout.Label("Stats overlay moved to F1 menu", new GUIStyle(GUI.skin.label) { fontSize = 10 });
            GUILayout.Label("Enable EOSNativeUI for full stats", new GUIStyle(GUI.skin.label) { fontSize = 10 });

            GUILayout.EndArea();
        }
    }
}
