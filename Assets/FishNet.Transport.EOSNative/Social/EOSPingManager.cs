using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Object.Synchronizing;
using FishNet.Managing;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Type of ping callout
    /// </summary>
    public enum PingType
    {
        Default,        // Generic ping
        Location,       // "Going here"
        Enemy,          // "Enemy spotted"
        Danger,         // "Danger!"
        Item,           // "Item here"
        Help,           // "Need help"
        Attack,         // "Attack here"
        Defend,         // "Defend here"
        Custom          // Custom ping type
    }

    /// <summary>
    /// Visibility settings for pings
    /// </summary>
    public enum PingVisibility
    {
        All,            // Everyone can see
        Team,           // Only teammates
        Self            // Only the sender
    }

    /// <summary>
    /// Data for a world ping
    /// </summary>
    [Serializable]
    public class PingData
    {
        public string PingId;
        public string SenderPuid;
        public string SenderName;
        public int SenderTeam;
        public PingType Type;
        public Vector3 WorldPosition;
        public string TargetObjectId;       // NetworkObject ID if pinged on object
        public string CustomLabel;          // Custom text label
        public float CreatedTime;
        public float Duration;
        public PingVisibility Visibility;
        public Dictionary<string, string> CustomData;

        public PingData()
        {
            PingId = Guid.NewGuid().ToString("N")[..8];
            CustomData = new Dictionary<string, string>();
            CreatedTime = Time.time;
            Duration = 5f;
            Visibility = PingVisibility.Team;
        }

        public bool IsExpired => Time.time - CreatedTime > Duration;

        public float TimeRemaining => Mathf.Max(0, Duration - (Time.time - CreatedTime));
    }

    /// <summary>
    /// Manages world pings and callouts for team communication
    /// </summary>
    public class EOSPingManager : NetworkBehaviour
    {
        public static EOSPingManager Instance { get; private set; }

        [Header("Ping Settings")]
        [SerializeField] private float _defaultPingDuration = 5f;
        [SerializeField] private float _pingCooldown = 0.5f;
        [SerializeField] private int _maxActivePings = 3;
        [SerializeField] private float _maxPingDistance = 100f;
        [SerializeField] private bool _allowEnemyPings = true;
        [SerializeField] private bool _allowCustomLabels = true;

        [Header("Visual Settings")]
        [SerializeField] private bool _showPingIndicators = true;
        [SerializeField] private bool _showPingOnMinimap = true;
        [SerializeField] private bool _playSoundOnPing = true;
        [SerializeField] private Color _defaultPingColor = Color.white;
        [SerializeField] private Color _enemyPingColor = Color.red;
        [SerializeField] private Color _dangerPingColor = new Color(1f, 0.5f, 0f); // Orange
        [SerializeField] private Color _itemPingColor = Color.yellow;
        [SerializeField] private Color _helpPingColor = Color.cyan;

        [Header("Team Settings")]
        [SerializeField] private bool _teamPingsOnly = true;
        [SerializeField] private bool _showSenderName = true;

        // Runtime state
        private readonly List<PingData> _activePings = new();
        private readonly Dictionary<string, float> _playerCooldowns = new();
        private readonly Dictionary<string, List<string>> _playerPingIds = new();
        private int _localTeam = 0;

        // Events
        public event Action<PingData> OnPingCreated;
        public event Action<PingData> OnPingExpired;
        public event Action<PingData> OnPingRemoved;
        public event Action<string, float> OnCooldownStarted;

        // Properties
        public float DefaultPingDuration { get => _defaultPingDuration; set => _defaultPingDuration = value; }
        public float PingCooldown { get => _pingCooldown; set => _pingCooldown = value; }
        public int MaxActivePings { get => _maxActivePings; set => _maxActivePings = value; }
        public float MaxPingDistance { get => _maxPingDistance; set => _maxPingDistance = value; }
        public bool AllowEnemyPings { get => _allowEnemyPings; set => _allowEnemyPings = value; }
        public bool AllowCustomLabels { get => _allowCustomLabels; set => _allowCustomLabels = value; }
        public bool ShowPingIndicators { get => _showPingIndicators; set => _showPingIndicators = value; }
        public bool ShowPingOnMinimap { get => _showPingOnMinimap; set => _showPingOnMinimap = value; }
        public bool PlaySoundOnPing { get => _playSoundOnPing; set => _playSoundOnPing = value; }
        public bool TeamPingsOnly { get => _teamPingsOnly; set => _teamPingsOnly = value; }
        public bool ShowSenderName { get => _showSenderName; set => _showSenderName = value; }
        public int LocalTeam { get => _localTeam; set => _localTeam = value; }

        public IReadOnlyList<PingData> ActivePings => _activePings;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            // Clean up expired pings
            CleanupExpiredPings();
        }

        /// <summary>
        /// Set the local player's team
        /// </summary>
        public void SetTeam(int team)
        {
            _localTeam = team;
        }

        /// <summary>
        /// Ping a world position
        /// </summary>
        public bool PingPosition(Vector3 worldPosition, PingType type = PingType.Default, string customLabel = null)
        {
            if (!CanPing()) return false;

            var localPuid = GetLocalPuid();
            if (string.IsNullOrEmpty(localPuid)) return false;

            var ping = new PingData
            {
                SenderPuid = localPuid,
                SenderName = GetLocalName(),
                SenderTeam = _localTeam,
                Type = type,
                WorldPosition = worldPosition,
                Duration = _defaultPingDuration,
                CustomLabel = _allowCustomLabels ? customLabel : null,
                Visibility = _teamPingsOnly ? PingVisibility.Team : PingVisibility.All
            };

            StartCooldown(localPuid);
            SendPingToNetwork(ping);
            return true;
        }

        /// <summary>
        /// Ping an object in the world
        /// </summary>
        public bool PingObject(NetworkObject targetObject, PingType type = PingType.Default, string customLabel = null)
        {
            if (targetObject == null) return false;
            if (!CanPing()) return false;

            var localPuid = GetLocalPuid();
            if (string.IsNullOrEmpty(localPuid)) return false;

            var ping = new PingData
            {
                SenderPuid = localPuid,
                SenderName = GetLocalName(),
                SenderTeam = _localTeam,
                Type = type,
                WorldPosition = targetObject.transform.position,
                TargetObjectId = targetObject.ObjectId.ToString(),
                Duration = _defaultPingDuration,
                CustomLabel = _allowCustomLabels ? customLabel : null,
                Visibility = _teamPingsOnly ? PingVisibility.Team : PingVisibility.All
            };

            StartCooldown(localPuid);
            SendPingToNetwork(ping);
            return true;
        }

        /// <summary>
        /// Ping an enemy (convenience method)
        /// </summary>
        public bool PingEnemy(Vector3 position, string enemyName = null)
        {
            if (!_allowEnemyPings) return false;
            return PingPosition(position, PingType.Enemy, enemyName ?? "Enemy spotted!");
        }

        /// <summary>
        /// Ping for help (convenience method)
        /// </summary>
        public bool PingHelp(Vector3 position)
        {
            return PingPosition(position, PingType.Help, "Need help!");
        }

        /// <summary>
        /// Ping danger (convenience method)
        /// </summary>
        public bool PingDanger(Vector3 position, string dangerType = null)
        {
            return PingPosition(position, PingType.Danger, dangerType ?? "Danger!");
        }

        /// <summary>
        /// Ping an item (convenience method)
        /// </summary>
        public bool PingItem(Vector3 position, string itemName = null)
        {
            return PingPosition(position, PingType.Item, itemName ?? "Item here");
        }

        /// <summary>
        /// Quick ping at crosshair position using raycast
        /// </summary>
        public bool PingAtCrosshair(Camera camera, float maxDistance = 100f, PingType type = PingType.Default)
        {
            if (camera == null) camera = Camera.main;
            if (camera == null) return false;

            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                // Check if we hit a NetworkObject
                var netObj = hit.collider.GetComponentInParent<NetworkObject>();
                if (netObj != null)
                {
                    return PingObject(netObj, type);
                }
                else
                {
                    return PingPosition(hit.point, type);
                }
            }

            return false;
        }

        /// <summary>
        /// Remove a ping by ID
        /// </summary>
        public void RemovePing(string pingId)
        {
            var ping = _activePings.Find(p => p.PingId == pingId);
            if (ping != null)
            {
                _activePings.Remove(ping);

                if (_playerPingIds.TryGetValue(ping.SenderPuid, out var pings))
                {
                    pings.Remove(pingId);
                }

                OnPingRemoved?.Invoke(ping);

                // Notify other clients
                if (IsServerInitialized)
                {
                    RpcRemovePing(pingId);
                }
            }
        }

        /// <summary>
        /// Remove all pings from a player
        /// </summary>
        public void RemovePlayerPings(string puid)
        {
            var pingsToRemove = _activePings.Where(p => p.SenderPuid == puid).ToList();
            foreach (var ping in pingsToRemove)
            {
                RemovePing(ping.PingId);
            }
        }

        /// <summary>
        /// Clear all active pings
        /// </summary>
        public void ClearAllPings()
        {
            foreach (var ping in _activePings.ToList())
            {
                RemovePing(ping.PingId);
            }
        }

        /// <summary>
        /// Check if local player can ping
        /// </summary>
        public bool CanPing()
        {
            var localPuid = GetLocalPuid();
            if (string.IsNullOrEmpty(localPuid)) return false;

            // Check cooldown
            if (_playerCooldowns.TryGetValue(localPuid, out float cooldownEnd))
            {
                if (Time.time < cooldownEnd) return false;
            }

            // Check max active pings
            if (_playerPingIds.TryGetValue(localPuid, out var pings))
            {
                if (pings.Count >= _maxActivePings) return false;
            }

            return true;
        }

        /// <summary>
        /// Get remaining cooldown time
        /// </summary>
        public float GetCooldownRemaining()
        {
            var localPuid = GetLocalPuid();
            if (string.IsNullOrEmpty(localPuid)) return 0;

            if (_playerCooldowns.TryGetValue(localPuid, out float cooldownEnd))
            {
                return Mathf.Max(0, cooldownEnd - Time.time);
            }
            return 0;
        }

        /// <summary>
        /// Get visible pings for local player
        /// </summary>
        public List<PingData> GetVisiblePings()
        {
            return _activePings.Where(p => IsPingVisible(p)).ToList();
        }

        /// <summary>
        /// Get pings near a position
        /// </summary>
        public List<PingData> GetPingsNearPosition(Vector3 position, float radius)
        {
            return _activePings.Where(p =>
                IsPingVisible(p) &&
                Vector3.Distance(p.WorldPosition, position) <= radius
            ).ToList();
        }

        /// <summary>
        /// Get the nearest ping to a position
        /// </summary>
        public PingData GetNearestPing(Vector3 position)
        {
            return _activePings
                .Where(p => IsPingVisible(p))
                .OrderBy(p => Vector3.Distance(p.WorldPosition, position))
                .FirstOrDefault();
        }

        /// <summary>
        /// Get color for a ping type
        /// </summary>
        public Color GetPingColor(PingType type)
        {
            return type switch
            {
                PingType.Enemy => _enemyPingColor,
                PingType.Danger => _dangerPingColor,
                PingType.Item => _itemPingColor,
                PingType.Help => _helpPingColor,
                _ => _defaultPingColor
            };
        }

        /// <summary>
        /// Get icon/symbol for ping type
        /// </summary>
        public static string GetPingIcon(PingType type)
        {
            return type switch
            {
                PingType.Default => "!",
                PingType.Location => ">",
                PingType.Enemy => "X",
                PingType.Danger => "!!",
                PingType.Item => "*",
                PingType.Help => "?",
                PingType.Attack => ">>",
                PingType.Defend => "[]",
                PingType.Custom => "#",
                _ => "!"
            };
        }

        /// <summary>
        /// Get display name for ping type
        /// </summary>
        public static string GetPingTypeName(PingType type)
        {
            return type switch
            {
                PingType.Default => "Ping",
                PingType.Location => "Going Here",
                PingType.Enemy => "Enemy",
                PingType.Danger => "Danger",
                PingType.Item => "Item",
                PingType.Help => "Need Help",
                PingType.Attack => "Attack",
                PingType.Defend => "Defend",
                PingType.Custom => "Custom",
                _ => "Ping"
            };
        }

        // Private methods

        private bool IsPingVisible(PingData ping)
        {
            if (ping.IsExpired) return false;

            switch (ping.Visibility)
            {
                case PingVisibility.All:
                    return true;
                case PingVisibility.Team:
                    return ping.SenderTeam == _localTeam || ping.SenderPuid == GetLocalPuid();
                case PingVisibility.Self:
                    return ping.SenderPuid == GetLocalPuid();
                default:
                    return false;
            }
        }

        private void StartCooldown(string puid)
        {
            _playerCooldowns[puid] = Time.time + _pingCooldown;
            OnCooldownStarted?.Invoke(puid, _pingCooldown);
        }

        private void SendPingToNetwork(PingData ping)
        {
            // Add locally first
            AddPingLocally(ping);

            // Send to server
            if (IsClientInitialized)
            {
                CmdSendPing(
                    ping.PingId,
                    ping.SenderPuid,
                    ping.SenderName,
                    ping.SenderTeam,
                    (int)ping.Type,
                    ping.WorldPosition,
                    ping.TargetObjectId ?? "",
                    ping.CustomLabel ?? "",
                    ping.Duration,
                    (int)ping.Visibility
                );
            }
        }

        private void AddPingLocally(PingData ping)
        {
            _activePings.Add(ping);

            if (!_playerPingIds.ContainsKey(ping.SenderPuid))
            {
                _playerPingIds[ping.SenderPuid] = new List<string>();
            }
            _playerPingIds[ping.SenderPuid].Add(ping.PingId);

            OnPingCreated?.Invoke(ping);
        }

        private void CleanupExpiredPings()
        {
            var expiredPings = _activePings.Where(p => p.IsExpired).ToList();
            foreach (var ping in expiredPings)
            {
                _activePings.Remove(ping);

                if (_playerPingIds.TryGetValue(ping.SenderPuid, out var pings))
                {
                    pings.Remove(ping.PingId);
                }

                OnPingExpired?.Invoke(ping);
            }
        }

        private string GetLocalPuid()
        {
            return EOSManager.Instance?.LocalProductUserId?.ToString();
        }

        private string GetLocalName()
        {
            var puid = GetLocalPuid();
            if (string.IsNullOrEmpty(puid)) return "Unknown";
            return EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? "Unknown";
        }

        // Network RPCs

        [ServerRpc(RequireOwnership = false)]
        private void CmdSendPing(string pingId, string senderPuid, string senderName, int senderTeam,
            int type, Vector3 worldPosition, string targetObjectId, string customLabel, float duration, int visibility)
        {
            // Broadcast to all clients
            RpcReceivePing(pingId, senderPuid, senderName, senderTeam, type, worldPosition, targetObjectId, customLabel, duration, visibility);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void RpcReceivePing(string pingId, string senderPuid, string senderName, int senderTeam,
            int type, Vector3 worldPosition, string targetObjectId, string customLabel, float duration, int visibility)
        {
            // Don't add if we already have this ping
            if (_activePings.Any(p => p.PingId == pingId)) return;

            var ping = new PingData
            {
                PingId = pingId,
                SenderPuid = senderPuid,
                SenderName = senderName,
                SenderTeam = senderTeam,
                Type = (PingType)type,
                WorldPosition = worldPosition,
                TargetObjectId = string.IsNullOrEmpty(targetObjectId) ? null : targetObjectId,
                CustomLabel = string.IsNullOrEmpty(customLabel) ? null : customLabel,
                Duration = duration,
                Visibility = (PingVisibility)visibility
            };

            AddPingLocally(ping);
        }

        [ObserversRpc]
        private void RpcRemovePing(string pingId)
        {
            var ping = _activePings.Find(p => p.PingId == pingId);
            if (ping != null)
            {
                _activePings.Remove(ping);

                if (_playerPingIds.TryGetValue(ping.SenderPuid, out var pings))
                {
                    pings.Remove(pingId);
                }

                OnPingRemoved?.Invoke(ping);
            }
        }
    }

    /// <summary>
    /// Visual indicator component for world pings
    /// </summary>
    public class EOSPingIndicator : MonoBehaviour
    {
        [SerializeField] private TextMesh _labelText;
        [SerializeField] private SpriteRenderer _iconRenderer;
        [SerializeField] private LineRenderer _lineRenderer;
        [SerializeField] private float _fadeStartTime = 1f;
        [SerializeField] private float _bobSpeed = 2f;
        [SerializeField] private float _bobAmount = 0.2f;

        private PingData _pingData;
        private Vector3 _basePosition;
        private float _initialY;

        public PingData PingData => _pingData;

        public void Initialize(PingData data)
        {
            _pingData = data;
            _basePosition = data.WorldPosition;
            _initialY = _basePosition.y;
            transform.position = _basePosition;

            // Set color
            var color = EOSPingManager.Instance?.GetPingColor(data.Type) ?? Color.white;

            if (_labelText != null)
            {
                _labelText.text = !string.IsNullOrEmpty(data.CustomLabel)
                    ? data.CustomLabel
                    : EOSPingManager.GetPingTypeName(data.Type);
                _labelText.color = color;

                if (EOSPingManager.Instance?.ShowSenderName == true)
                {
                    _labelText.text = $"[{data.SenderName}] {_labelText.text}";
                }
            }

            if (_iconRenderer != null)
            {
                _iconRenderer.color = color;
            }

            if (_lineRenderer != null)
            {
                _lineRenderer.startColor = color;
                _lineRenderer.endColor = new Color(color.r, color.g, color.b, 0.3f);
            }
        }

        private void Update()
        {
            if (_pingData == null) return;

            // Check if expired
            if (_pingData.IsExpired)
            {
                Destroy(gameObject);
                return;
            }

            // Bob animation
            float yOffset = Mathf.Sin(Time.time * _bobSpeed) * _bobAmount;
            transform.position = new Vector3(_basePosition.x, _initialY + 1f + yOffset, _basePosition.z);

            // Fade out
            float remaining = _pingData.TimeRemaining;
            if (remaining < _fadeStartTime)
            {
                float alpha = remaining / _fadeStartTime;

                if (_labelText != null)
                {
                    var c = _labelText.color;
                    _labelText.color = new Color(c.r, c.g, c.b, alpha);
                }

                if (_iconRenderer != null)
                {
                    var c = _iconRenderer.color;
                    _iconRenderer.color = new Color(c.r, c.g, c.b, alpha);
                }
            }

            // Look at camera
            if (Camera.main != null)
            {
                transform.LookAt(Camera.main.transform);
                transform.Rotate(0, 180, 0);
            }

            // Update line to ping location
            if (_lineRenderer != null)
            {
                _lineRenderer.SetPosition(0, transform.position);
                _lineRenderer.SetPosition(1, _basePosition);
            }
        }
    }

    /// <summary>
    /// Spawns and manages ping indicators in the world
    /// </summary>
    public class EOSPingVisualizer : MonoBehaviour
    {
        public static EOSPingVisualizer Instance { get; private set; }

        [SerializeField] private GameObject _pingIndicatorPrefab;
        [SerializeField] private bool _autoCreateIndicators = true;

        private readonly Dictionary<string, EOSPingIndicator> _indicators = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            if (EOSPingManager.Instance != null)
            {
                EOSPingManager.Instance.OnPingCreated += OnPingCreated;
                EOSPingManager.Instance.OnPingExpired += OnPingExpired;
                EOSPingManager.Instance.OnPingRemoved += OnPingRemoved;
            }
        }

        private void OnDisable()
        {
            if (EOSPingManager.Instance != null)
            {
                EOSPingManager.Instance.OnPingCreated -= OnPingCreated;
                EOSPingManager.Instance.OnPingExpired -= OnPingExpired;
                EOSPingManager.Instance.OnPingRemoved -= OnPingRemoved;
            }
        }

        private void OnPingCreated(PingData ping)
        {
            if (!_autoCreateIndicators) return;
            if (!EOSPingManager.Instance.ShowPingIndicators) return;

            CreateIndicator(ping);
        }

        private void OnPingExpired(PingData ping)
        {
            RemoveIndicator(ping.PingId);
        }

        private void OnPingRemoved(PingData ping)
        {
            RemoveIndicator(ping.PingId);
        }

        /// <summary>
        /// Create a visual indicator for a ping
        /// </summary>
        public EOSPingIndicator CreateIndicator(PingData ping)
        {
            if (_pingIndicatorPrefab == null)
            {
                // Create a basic indicator if no prefab assigned
                return CreateBasicIndicator(ping);
            }

            var go = Instantiate(_pingIndicatorPrefab, ping.WorldPosition, Quaternion.identity);
            var indicator = go.GetComponent<EOSPingIndicator>();

            if (indicator == null)
            {
                indicator = go.AddComponent<EOSPingIndicator>();
            }

            indicator.Initialize(ping);
            _indicators[ping.PingId] = indicator;

            return indicator;
        }

        /// <summary>
        /// Remove a visual indicator
        /// </summary>
        public void RemoveIndicator(string pingId)
        {
            if (_indicators.TryGetValue(pingId, out var indicator))
            {
                if (indicator != null)
                {
                    Destroy(indicator.gameObject);
                }
                _indicators.Remove(pingId);
            }
        }

        /// <summary>
        /// Clear all indicators
        /// </summary>
        public void ClearAllIndicators()
        {
            foreach (var indicator in _indicators.Values)
            {
                if (indicator != null)
                {
                    Destroy(indicator.gameObject);
                }
            }
            _indicators.Clear();
        }

        private EOSPingIndicator CreateBasicIndicator(PingData ping)
        {
            // Create a simple text-based indicator
            var go = new GameObject($"Ping_{ping.PingId}");
            go.transform.position = ping.WorldPosition + Vector3.up;

            // Add TextMesh
            var textMesh = go.AddComponent<TextMesh>();
            textMesh.text = !string.IsNullOrEmpty(ping.CustomLabel)
                ? ping.CustomLabel
                : EOSPingManager.GetPingTypeName(ping.Type);
            textMesh.characterSize = 0.1f;
            textMesh.fontSize = 48;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = EOSPingManager.Instance?.GetPingColor(ping.Type) ?? Color.white;

            var indicator = go.AddComponent<EOSPingIndicator>();
            indicator.Initialize(ping);
            _indicators[ping.PingId] = indicator;

            return indicator;
        }
    }
}
