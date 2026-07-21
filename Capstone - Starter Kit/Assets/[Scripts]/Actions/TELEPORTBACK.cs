using UnityEngine;
using Unity.XR.CoreUtils;

/// <summary>
/// Teleports the player (the XR Rig / XR Origin) back to a designated point.
///
/// This fixes the classic "the rig teleports but the player is left floating in
/// the air" bug. The old version moved a child body AND its parent to the target
/// position (doubling the offset) and dropped the rig at the target's Y - which,
/// for a marker sitting at eye/torso height, left the player hanging above the
/// floor. This version:
///   * moves the XR ORIGIN transform (the true rig root) - never a child body,
///   * never drags a parent container along with it,
///   * snaps the rig's feet to the ground directly under the target so the
///     player always lands on the floor instead of in the air,
///   * clears CharacterController grounding and rigidbody momentum so the player
///     doesn't carry old velocity or hang mid-air after arriving.
///
/// SETUP:
///   1. Put this on a GameObject with a Collider that has "Is Trigger" enabled.
///   2. Assign "Teleport Target" (e.g. TeleportBackPoint).
///   3. Make sure the XR Rig is tagged "Player" and has an XROrigin component.
/// You can also drive it manually by calling Teleport() (e.g. from a UI button).
/// </summary>
[DisallowMultipleComponent]
public class TELEPORTBACK : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Where the player is sent back to. X/Z (and yaw) are always used; " +
             "Y is only used directly when 'Snap To Ground' is off.")]
    [SerializeField] private Transform teleportTarget;

    [Tooltip("Extra nudge applied at the destination. The Y component is only " +
             "applied when 'Snap To Ground' is OFF (otherwise the floor decides Y).")]
    [SerializeField] private Vector3 offset = Vector3.zero;

    [Tooltip("Rotate the player to face the target's yaw when arriving.")]
    [SerializeField] private bool matchYaw = true;

    [Header("Detection")]
    [Tooltip("Automatically teleport when a collider tagged 'Player Tag' enters this trigger.")]
    [SerializeField] private bool useTrigger = true;

    [SerializeField] private string playerTag = "Player";

    [Tooltip("Minimum seconds between teleports (prevents rapid re-triggering while inside the volume).")]
    [SerializeField] private float cooldownSeconds = 0.5f;

    [Header("Grounding")]
    [Tooltip("Drop the rig onto the floor beneath the target so the player never hangs in the air.")]
    [SerializeField] private bool snapToGround = true;

    [Tooltip("Layers treated as 'ground'. Leave as Nothing to use everything except the player's own layer.")]
    [SerializeField] private LayerMask groundMask = default;

    [Tooltip("How far above the target to start the downward ground probe.")]
    [SerializeField] private float groundProbeHeight = 3f;

    [Tooltip("How far down (below the probe start) to search for the ground.")]
    [SerializeField] private float groundProbeDistance = 25f;

    [Header("Physics")]
    [Tooltip("Zero out rigidbody / character-controller momentum after teleporting.")]
    [SerializeField] private bool resetVelocity = true;

    private float lastTeleportTime = -999f;

    // Auto-configure the collider as a trigger when the component is first added.
    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void Start()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"{name}: TELEPORTBACK collider was not a trigger. Fixed automatically.", this);
        }

        if (teleportTarget == null)
            Debug.LogError($"{name}: TELEPORTBACK has no Teleport Target assigned in the Inspector!", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (useTrigger) TryTeleport(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // Keep re-checking while the player lingers, but the cooldown throttles it.
        if (useTrigger) TryTeleport(other);
    }

    private void TryTeleport(Collider other)
    {
        if (other == null) return;
        if (Time.time - lastTeleportTime < cooldownSeconds) return;

        Transform rig = ResolveRig(other);
        if (rig == null) return;

        DoTeleport(rig);
    }

    /// <summary>Public entry point (e.g. hook to a UI button). Finds the tagged player automatically.</summary>
    public void Teleport()
    {
        Transform rig = ResolveRig(null);
        if (rig != null)
            DoTeleport(rig);
        else
            Debug.LogWarning($"{name}: TELEPORTBACK.Teleport() could not find an XR Origin or a '{playerTag}' object.", this);
    }

    /// <summary>Teleport a specific rig transform (bypasses discovery).</summary>
    public void Teleport(Transform rig)
    {
        if (rig != null)
            DoTeleport(rig);
    }

    /// <summary>
    /// Works out which transform actually represents the player rig. Moving the
    /// XR Origin is what correctly repositions the headset view, so we prefer it.
    /// </summary>
    private Transform ResolveRig(Collider other)
    {
        if (other != null)
        {
            // 1) The real XR Origin (this is the transform that must move).
            var origin = other.GetComponentInParent<XROrigin>();
            if (origin != null) return origin.transform;

            // 2) Otherwise, the nearest ancestor carrying the player tag.
            for (Transform t = other.transform; t != null; t = t.parent)
                if (t.CompareTag(playerTag)) return t;
        }

        // 3) Manual / no-collider path: search the scene.
        var originInScene = FindObjectOfType<XROrigin>();
        if (originInScene != null) return originInScene.transform;

        var tagged = GameObject.FindWithTag(playerTag);
        return tagged != null ? tagged.transform : null;
    }

    private void DoTeleport(Transform rig)
    {
        if (teleportTarget == null)
        {
            Debug.LogError($"{name}: Teleport Target not assigned!", this);
            return;
        }

        lastTeleportTime = Time.time;

        // Horizontal placement first; Y is decided below.
        Vector3 destination = teleportTarget.position + new Vector3(offset.x, 0f, offset.z);

        if (snapToGround)
        {
            destination.y = TryFindGround(rig, destination, out float groundY)
                ? groundY
                : teleportTarget.position.y; // best effort - no arbitrary lift that would float the player
        }
        else
        {
            destination.y = teleportTarget.position.y + offset.y;
        }

        // Disable the CharacterController while we hard-set the position so it
        // neither fights the move nor re-applies its previous grounded state.
        CharacterController cc = rig.GetComponent<CharacterController>();
        bool ccWasEnabled = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;

        rig.position = destination;

        if (matchYaw)
            rig.rotation = Quaternion.Euler(0f, teleportTarget.eulerAngles.y, 0f);

        if (cc != null) cc.enabled = ccWasEnabled;

        if (resetVelocity)
            ResetVelocity(rig);

        Debug.Log($"[TELEPORTBACK] Sent '{rig.name}' to {destination} (grounded: {snapToGround}).", this);
    }

    /// <summary>Raycast straight down from above the destination to find the floor.</summary>
    private bool TryFindGround(Transform rig, Vector3 destination, out float groundY)
    {
        groundY = 0f;

        int mask = groundMask.value;
        if (mask == 0) // unconfigured -> everything except the rig's own layer
            mask = ~(1 << rig.gameObject.layer);

        Vector3 start = new Vector3(destination.x, teleportTarget.position.y + groundProbeHeight, destination.z);
        float distance = groundProbeHeight + groundProbeDistance;

        if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        return false;
    }

    private void ResetVelocity(Transform rig)
    {
        foreach (Rigidbody rb in rig.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb != null && !rb.isKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}

