using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Visual representation of an object during replay playback.
    /// Shows a name label and optional trail effect.
    /// </summary>
    public class ReplayGhost : MonoBehaviour
    {
        /// <summary>
        /// Color palette for player ghosts. Each player gets a unique color.
        /// </summary>
        public static readonly Color[] PlayerColors = new Color[]
        {
            new Color(0.2f, 0.6f, 1.0f),  // Blue
            new Color(1.0f, 0.4f, 0.4f),  // Red
            new Color(0.4f, 1.0f, 0.4f),  // Green
            new Color(1.0f, 1.0f, 0.4f),  // Yellow
            new Color(1.0f, 0.6f, 0.2f),  // Orange
            new Color(0.8f, 0.4f, 1.0f),  // Purple
            new Color(0.4f, 1.0f, 1.0f),  // Cyan
            new Color(1.0f, 0.6f, 0.8f),  // Pink
        };

        private static int _nextColorIndex = 0;

        /// <summary>
        /// Get the next color from the palette (cycles).
        /// </summary>
        public static Color GetNextPlayerColor()
        {
            var color = PlayerColors[_nextColorIndex % PlayerColors.Length];
            _nextColorIndex++;
            return color;
        }

        /// <summary>
        /// Reset color index (call when starting a new replay).
        /// </summary>
        public static void ResetColorIndex()
        {
            _nextColorIndex = 0;
        }

        [Header("Appearance")]
        [SerializeField] private Color _color = Color.white;
        [SerializeField] private bool _showLabel = true;
        [SerializeField] private bool _showTrail = false;

        [Header("Label Settings")]
        [SerializeField] private float _labelHeight = 1.5f;
        [SerializeField] private float _labelScale = 0.02f;

        private TextMesh _labelText;
        private TrailRenderer _trail;
        private Renderer _renderer;
        private Material _material;

        /// <summary>Display name shown above the ghost.</summary>
        public string DisplayName
        {
            get => _labelText != null ? _labelText.text : "";
            set { if (_labelText != null) _labelText.text = value; }
        }

        /// <summary>Ghost color.</summary>
        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                ApplyColor();
            }
        }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            SetupLabel();
            SetupTrail();
            ApplyColor();
        }

        private void SetupLabel()
        {
            if (!_showLabel) return;

            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(transform);
            labelObj.transform.localPosition = Vector3.up * _labelHeight;
            labelObj.transform.localScale = Vector3.one * _labelScale;

            _labelText = labelObj.AddComponent<TextMesh>();
            _labelText.alignment = TextAlignment.Center;
            _labelText.anchor = TextAnchor.MiddleCenter;
            _labelText.fontSize = 100;
            _labelText.characterSize = 1f;
            _labelText.color = Color.white;

            // Add billboard behavior
            labelObj.AddComponent<ReplayLabelBillboard>();
        }

        private void SetupTrail()
        {
            if (!_showTrail) return;

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.5f;
            _trail.startWidth = 0.2f;
            _trail.endWidth = 0.05f;
            _trail.material = new Material(Shader.Find("Sprites/Default"));
            _trail.startColor = new Color(_color.r, _color.g, _color.b, 0.5f);
            _trail.endColor = new Color(_color.r, _color.g, _color.b, 0f);
        }

        private void ApplyColor()
        {
            if (_renderer != null)
            {
                if (_material == null)
                {
                    _material = new Material(Shader.Find("Standard"));
                    _renderer.material = _material;
                }
                _material.color = _color;
            }

            if (_trail != null)
            {
                _trail.startColor = new Color(_color.r, _color.g, _color.b, 0.5f);
                _trail.endColor = new Color(_color.r, _color.g, _color.b, 0f);
            }
        }

        /// <summary>
        /// Create a player ghost with capsule shape.
        /// </summary>
        public static ReplayGhost CreatePlayerGhost(Transform parent, string name, Color color)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            obj.name = $"PlayerGhost_{name}";
            obj.transform.SetParent(parent);
            obj.transform.localScale = new Vector3(0.5f, 1f, 0.5f);

            // Remove collider
            var collider = obj.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            var ghost = obj.AddComponent<ReplayGhost>();
            ghost._showLabel = true;
            ghost._showTrail = true;
            ghost._color = color;
            ghost.DisplayName = name;

            return ghost;
        }

        /// <summary>
        /// Create an object ghost with cube shape.
        /// </summary>
        public static ReplayGhost CreateObjectGhost(Transform parent, Color color)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = "ObjectGhost";
            obj.transform.SetParent(parent);
            obj.transform.localScale = Vector3.one * 0.5f;

            // Remove collider
            var collider = obj.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            var ghost = obj.AddComponent<ReplayGhost>();
            ghost._showLabel = false;
            ghost._showTrail = false;
            ghost._color = color;

            return ghost;
        }

        /// <summary>
        /// Create a sphere ghost (default).
        /// </summary>
        public static ReplayGhost CreateSphereGhost(Transform parent, Color color, bool isPlayer)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.name = isPlayer ? "PlayerGhost" : "ObjectGhost";
            obj.transform.SetParent(parent);
            obj.transform.localScale = Vector3.one * (isPlayer ? 0.6f : 0.4f);

            // Remove collider
            var collider = obj.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            var ghost = obj.AddComponent<ReplayGhost>();
            ghost._showLabel = isPlayer;
            ghost._showTrail = isPlayer;
            ghost._color = color;

            return ghost;
        }
    }

    /// <summary>
    /// Makes the label always face the camera.
    /// </summary>
    public class ReplayLabelBillboard : MonoBehaviour
    {
        private Camera _camera;

        private void Start()
        {
            _camera = Camera.main;
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null) return;
            }

            transform.rotation = Quaternion.LookRotation(transform.position - _camera.transform.position);
        }
    }
}
