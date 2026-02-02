using FishNet.Object;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Demo
{
    /// <summary>
    /// Simple top-down camera that follows the local player.
    /// Attach to the Main Camera.
    /// </summary>
    public class SimpleCamera : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _smoothSpeed = 5f;
        [SerializeField] private Vector3 _offset = new Vector3(0f, 10f, -5f);

        private Transform _target;

        private void LateUpdate()
        {
            // Find local player if we don't have a target
            if (_target == null)
            {
                FindLocalPlayer();
            }

            if (_target != null)
            {
                Vector3 targetPos = _target.position + _offset;
                transform.position = Vector3.Lerp(transform.position, targetPos, _smoothSpeed * Time.deltaTime);
                transform.LookAt(_target.position);
            }
        }

        private void FindLocalPlayer()
        {
            // Find all PlayerBall instances and get the one we own
            var players = FindObjectsByType<PlayerBall>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                if (player.IsOwner)
                {
                    _target = player.transform;
                    EOSDebugLogger.Log(DebugCategory.SimpleCamera, "SimpleCamera", "Found local player");
                    break;
                }
            }
        }
    }
}
