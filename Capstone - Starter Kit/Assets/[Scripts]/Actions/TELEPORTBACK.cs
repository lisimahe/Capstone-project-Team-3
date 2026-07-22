using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using MissionBit;

/// <summary>
/// Kill-zone / "teleport back" volume that can fire repeatedly for the same XR Rig.
///
/// Why the old version only worked once:
///   Setting CharacterController transforms directly leaves Unity's trigger pair in a
///   stuck "still overlapping" state, so OnTriggerEnter never fires again. Combined with
///   moving the Origin by raw position (instead of MoveCameraToWorldLocation), the headset
///   pose and any separate Player mesh drifted apart.
///
/// This version:
///   * routes every teleport through <see cref="PlayerTeleportBootstrap"/>
///   * re-arms with a distance check (does NOT depend on OnTriggerExit)
///   * ignores stale Stay overlaps until the player has left the volume
///   * works with or without a visual Player child on the prefab
///   * works for desktop XR Device Simulator and real headsets
/// </summary>
[DisallowMultipleComponent]
public class TELEPORTBACK : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Where the player is sent back to (floor point).")]
    [SerializeField] private Transform teleportTarget;

    [Tooltip("Added to the target position. Prefer Y=0 when Snap To Ground is on.")]
    [SerializeField] private Vector3 offset = Vector3.zero;

    [Tooltip("Rotate the player to face the target's yaw when arriving.")]
    [SerializeField] private bool matchYaw = true;

    [Header("Detection")]
    [SerializeField] private bool useTrigger = true;
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Minimum seconds between teleports from THIS volume.")]
    [SerializeField] private float cooldownSeconds = 0.75f;

    [Tooltip("How far outside this collider the player must travel before we re-arm.")]
    [SerializeField] private float rearmPadding = 0.6f;

    [Tooltip("Safety timeout — force re-arm even if distance check fails.")]
    [SerializeField] private float forceRearmSeconds = 3f;

    [Header("Grounding")]
    [SerializeField] private bool snapToGround = true;
    [SerializeField] private LayerMask groundMask = default;
    [SerializeField] private float groundProbeHeight = 3f;
    [SerializeField] private float groundProbeDistance = 25f;

    [Header("Physics")]
    [SerializeField] private bool resetVelocity = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private float _lastTeleportTime = -999f;
    private bool _armed = true;
    private Coroutine _rearmRoutine;
    private Collider _volume;

    // Track colliders we have already consumed while disarmed (avoids Stay spam).
    private readonly HashSet<int> _ignoredWhileDisarmed = new HashSet<int>();

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void Awake()
    {
        _volume = GetComponent<Collider>();
    }

    private void Start()
    {
        if (_volume == null)
            _volume = GetComponent<Collider>();

        if (_volume != null && !_volume.isTrigger)
        {
            _volume.isTrigger = true;
            Debug.LogWarning($"{name}: TELEPORTBACK collider was not a trigger. Fixed automatically.", this);
        }

        if (teleportTarget == null)
            Debug.LogError($"{name}: TELEPORTBACK has no Teleport Target assigned!", this);

        // Warm the bootstrap so the first trigger is never racing Awake order.
        PlayerTeleportBootstrap.FindOrCreate();
    }

    private void OnDisable()
    {
        if (_rearmRoutine != null)
        {
            StopCoroutine(_rearmRoutine);
            _rearmRoutine = null;
        }
        _armed = true;
        _ignoredWhileDisarmed.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (useTrigger) TryTeleport(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // Only useful while armed. While disarmed, Stay would just spam.
        if (useTrigger && _armed) TryTeleport(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) return;
        _ignoredWhileDisarmed.Remove(other.GetInstanceID());

        // Exit is best-effort — CharacterController teleports often skip it.
        // Distance-based re-arm below is the reliable path.
        if (!_armed && IsPlayerCollider(other))
            TryImmediateRearm();
    }

    private void TryTeleport(Collider other)
    {
        if (other == null || !_armed) return;
        if (Time.time - _lastTeleportTime < cooldownSeconds) return;
        if (teleportTarget == null) return;

        if (!IsPlayerCollider(other)) return;

        var bootstrap = PlayerTeleportBootstrap.FindOrCreate();
        if (bootstrap == null) return;

        Transform rig = bootstrap.OriginTransform;
        if (rig == null) return;

        Vector3 floor = ResolveFloorDestination(rig);

        _armed = false;
        _lastTeleportTime = Time.time;
        _ignoredWhileDisarmed.Add(other.GetInstanceID());

        bool ok = bootstrap.TeleportToFloor(
            floor,
            matchYaw ? teleportTarget.rotation : (Quaternion?)null,
            matchYaw);

        if (!ok)
        {
            // Hard fallback — still better than leaving the player in the kill zone.
            FallbackTeleport(rig, floor);
        }

        if (resetVelocity)
            ResetVelocity(rig);

        if (debugLogs)
            Debug.Log($"[TELEPORTBACK] '{name}' -> {floor} (armed=false, will re-arm on leave).", this);

        if (_rearmRoutine != null)
            StopCoroutine(_rearmRoutine);
        _rearmRoutine = StartCoroutine(RearmWhenClear(bootstrap));
    }

    /// <summary>Public entry (UI button / UnityEvent). Always allowed subject to cooldown.</summary>
    public void Teleport()
    {
        if (Time.time - _lastTeleportTime < cooldownSeconds) return;

        var bootstrap = PlayerTeleportBootstrap.FindOrCreate();
        if (bootstrap == null)
        {
            Debug.LogWarning($"{name}: TELEPORTBACK.Teleport() — no bootstrap/XROrigin.", this);
            return;
        }

        _armed = false;
        _lastTeleportTime = Time.time;

        Vector3 floor = ResolveFloorDestination(bootstrap.OriginTransform);
        bootstrap.TeleportToFloor(floor, matchYaw ? teleportTarget.rotation : (Quaternion?)null, matchYaw);

        if (_rearmRoutine != null)
            StopCoroutine(_rearmRoutine);
        _rearmRoutine = StartCoroutine(RearmWhenClear(bootstrap));
    }

    public void Teleport(Transform rig)
    {
        if (rig == null) return;
        Teleport();
    }

    private bool IsPlayerCollider(Collider other)
    {
        if (other == null) return false;

        // Prefer XROrigin anywhere in parents — works even if Player mesh was deleted.
        if (other.GetComponentInParent<XROrigin>() != null)
            return true;

        if (other.GetComponentInParent<PlayerTeleportBootstrap>() != null)
            return true;

        for (Transform t = other.transform; t != null; t = t.parent)
        {
            if (!string.IsNullOrEmpty(playerTag) && t.CompareTag(playerTag))
                return true;
        }

        return false;
    }

    private Vector3 ResolveFloorDestination(Transform rig)
    {
        // Match original semantics: XZ always take offset; Y offset only when not ground-snapping
        // (otherwise ground decides Y and a leftover +0.5 would float the player).
        Vector3 destination = teleportTarget.position + new Vector3(offset.x, 0f, offset.z);

        if (snapToGround)
        {
            if (TryFindGround(rig, destination, out float groundY))
                destination.y = groundY;
            else
                destination.y = teleportTarget.position.y;
        }
        else
        {
            destination.y = teleportTarget.position.y + offset.y;
        }

        return destination;
    }

    private IEnumerator RearmWhenClear(PlayerTeleportBootstrap bootstrap)
    {
        float start = Time.unscaledTime;

        // Give physics a frame to settle after the teleport.
        yield return null;
        Physics.SyncTransforms();
        yield return null;

        while (!_armed)
        {
            if (Time.unscaledTime - start >= forceRearmSeconds)
            {
                if (debugLogs)
                    Debug.Log($"[TELEPORTBACK] '{name}' force re-armed after timeout.", this);
                break;
            }

            if (!IsRigStillInVolume(bootstrap))
                break;

            yield return null;
        }

        _armed = true;
        _ignoredWhileDisarmed.Clear();
        _rearmRoutine = null;

        if (debugLogs)
            Debug.Log($"[TELEPORTBACK] '{name}' re-armed.", this);
    }

    private void TryImmediateRearm()
    {
        var bootstrap = PlayerTeleportBootstrap.Instance ?? PlayerTeleportBootstrap.FindOrCreate();
        if (bootstrap == null) return;
        if (IsRigStillInVolume(bootstrap)) return;

        _armed = true;
        _ignoredWhileDisarmed.Clear();
        if (_rearmRoutine != null)
        {
            StopCoroutine(_rearmRoutine);
            _rearmRoutine = null;
        }
    }

    private bool IsRigStillInVolume(PlayerTeleportBootstrap bootstrap)
    {
        if (bootstrap == null || _volume == null)
            return false;

        Bounds b = _volume.bounds;
        return bootstrap.IsNearBounds(b, rearmPadding);
    }

    private bool TryFindGround(Transform rig, Vector3 destination, out float groundY)
    {
        groundY = 0f;

        int mask = groundMask.value;
        if (mask == 0)
            mask = ~(1 << (rig != null ? rig.gameObject.layer : 0));

        Vector3 start = new Vector3(destination.x, teleportTarget.position.y + groundProbeHeight, destination.z);
        float distance = groundProbeHeight + groundProbeDistance;

        if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        return false;
    }

    private void FallbackTeleport(Transform rig, Vector3 floor)
    {
        CharacterController cc = rig.GetComponent<CharacterController>();
        bool ccOn = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;

        rig.position = floor;
        if (matchYaw && teleportTarget != null)
            rig.rotation = Quaternion.Euler(0f, teleportTarget.eulerAngles.y, 0f);

        Physics.SyncTransforms();
        if (cc != null) cc.enabled = ccOn;
        Physics.SyncTransforms();
    }

    private static void ResetVelocity(Transform rig)
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

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (teleportTarget == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(teleportTarget.position + offset, 0.25f);
        Gizmos.DrawLine(transform.position, teleportTarget.position + offset);
    }
#endif
}
