using UnityEngine;

namespace FishNet.Transport.EOSNative.Migration
{
    /// <summary>
    /// Marker component to exclude a NetworkObject from host migration.
    /// By default, ALL NetworkObjects are automatically migrated.
    /// Add this component to opt-out specific objects (e.g., temporary effects, projectiles).
    /// </summary>
    /// <remarks>
    /// Use cases for DoNotMigrate:
    /// - Temporary visual effects that should just disappear
    /// - Projectiles mid-flight (will be re-fired by game logic)
    /// - Objects that are intentionally transient
    /// - Scene objects that reset on host change
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("FishNet/EOS Native/Do Not Migrate")]
    public class DoNotMigrate : MonoBehaviour
    {
        // Marker component - no logic needed
        // HostMigrationManager checks for this component to exclude objects
    }
}
