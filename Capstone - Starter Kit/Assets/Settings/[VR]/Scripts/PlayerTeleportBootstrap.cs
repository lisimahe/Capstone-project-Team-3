using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;

namespace MissionBit
{
    /// <summary>
    /// Single source of truth for moving the player (XR Origin) on desktop simulator
    /// and real VR headsets. Fixes the "teleport works once" / "body left behind" class
    /// of bugs by:
    ///   1. Always moving via <see cref="XROrigin.MoveCameraToWorldLocation"/> (never by
    ///      shoving a child body or naively setting origin.position alone).
    ///   2. Disabling CharacterController, syncing physics, then re-enabling so trigger
    ///      overlaps re-arm for the next visit.
    ///   3. Ensuring a reliable non-trigger capsule probe exists even if the visual
    ///      "Player" mesh was deleted from the prefab.
    ///   4. Snapping any leftover body meshes that are children of the rig back into place.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class PlayerTeleportBootstrap : MonoBehaviour
    {
        public static PlayerTeleportBootstrap Instance { get; private set; }

        [Header("Resolved at runtime (optional overrides)")]
        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private TeleportationProvider teleportationProvider;
        [SerializeField] private string playerTag = "Player";

        [Header("Probe (trigger detection without a Player mesh)")]
        [Tooltip("If the prefab has no solid body collider, create a hidden capsule so kill-zones still fire.")]
        [SerializeField] private bool ensureTeleportProbe = true;
        [SerializeField] private float probeHeight = 1.75f;
        [SerializeField] private float probeRadius = 0.28f;

        [Header("Post-teleport")]
        [SerializeField] private float locomotionFreezeSeconds = 0.08f;
        [SerializeField] private bool syncBodyChildren = true;
        [SerializeField] private bool debugLogs = false;

        private Transform _probe;
        private Coroutine _freezeRoutine;
        private readonly List<LocomotionProvider> _locomotionProviders = new List<LocomotionProvider>(8);
        private readonly List<Behaviour> _frozen = new List<Behaviour>(8);

        /// <summary>Floor destination just used (world). Useful for kill-zone re-arm checks.</summary>
        public Vector3 LastFloorDestination { get; private set; }

        public XROrigin Origin => xrOrigin;
        public Transform OriginTransform
        {
            get
            {
                if (xrOrigin == null) return transform;
                if (xrOrigin.Origin != null) return xrOrigin.Origin.transform;
                return xrOrigin.transform;
            }
        }

        public Camera HeadCamera => xrOrigin != null ? xrOrigin.Camera : Camera.main;

        public static PlayerTeleportBootstrap FindOrCreate()
        {
            if (Instance != null)
                return Instance;

            var existing = FindObjectOfType<PlayerTeleportBootstrap>();
            if (existing != null)
            {
                existing.EnsureInitialized();
                return existing;
            }

            var origin = FindObjectOfType<XROrigin>();
            if (origin == null)
            {
                Debug.LogError("[PlayerTeleportBootstrap] No XROrigin in the scene.");
                return null;
            }

            var bootstrap = origin.GetComponent<PlayerTeleportBootstrap>();
            if (bootstrap == null)
                bootstrap = origin.gameObject.AddComponent<PlayerTeleportBootstrap>();

            bootstrap.EnsureInitialized();
            return bootstrap;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[PlayerTeleportBootstrap] Duplicate on '{name}'. Keeping the first instance.", this);
                return;
            }

            Instance = this;
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void EnsureInitialized()
        {
            if (xrOrigin == null)
                xrOrigin = GetComponent<XROrigin>() ?? GetComponentInParent<XROrigin>() ?? FindObjectOfType<XROrigin>();

            if (xrOrigin == null)
            {
                Debug.LogError($"[PlayerTeleportBootstrap] '{name}' has no XROrigin.", this);
                return;
            }

            if (characterController == null)
                characterController = xrOrigin.GetComponent<CharacterController>()
                                     ?? xrOrigin.GetComponentInChildren<CharacterController>();

            if (teleportationProvider == null)
                teleportationProvider = xrOrigin.GetComponent<TeleportationProvider>()
                                        ?? xrOrigin.GetComponentInChildren<TeleportationProvider>();

            CacheLocomotionProviders();

            // Keep the rig discoverable even if someone stripped the Player mesh.
            if (!xrOrigin.gameObject.CompareTag(playerTag))
            {
                try { xrOrigin.gameObject.tag = playerTag; }
                catch (UnityException) { /* tag may not exist in TagManager */ }
            }

            if (ensureTeleportProbe)
                EnsureProbe();

            if (debugLogs)
                Debug.Log($"[PlayerTeleportBootstrap] Ready on '{xrOrigin.name}' (CC={(characterController != null)}, probe={(_probe != null)}).", this);
        }

        private void CacheLocomotionProviders()
        {
            _locomotionProviders.Clear();
            if (xrOrigin == null) return;
            xrOrigin.GetComponentsInChildren(true, _locomotionProviders);
        }

        private void EnsureProbe()
        {
            const string probeName = "TeleportProbe";
            var existing = OriginTransform.Find(probeName);
            if (existing != null)
            {
                _probe = existing;
                return;
            }

            // CharacterController already generates trigger messages when present.
            // Probe is a fallback body so detection still works if CC is missing/disabled
            // or if the visual Player mesh (and its colliders) were deleted.
            if (characterController != null && characterController.enabled)
            {
                // Still add a light marker object for debugging / future use, but no extra collider.
                var marker = new GameObject(probeName);
                marker.transform.SetParent(OriginTransform, false);
                marker.transform.localPosition = Vector3.zero;
                marker.hideFlags = HideFlags.DontSave;
                _probe = marker.transform;
                return;
            }

            var go = new GameObject(probeName);
            go.layer = xrOrigin.gameObject.layer;
            try { go.tag = playerTag; } catch (UnityException) { /* ignore */ }

            var t = go.transform;
            t.SetParent(OriginTransform, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = false;
            capsule.height = probeHeight;
            capsule.radius = probeRadius;
            capsule.center = new Vector3(0f, probeHeight * 0.5f, 0f);
            capsule.direction = 1;

            // Kinematic RB required for trigger messages when there is no CharacterController.
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            _probe = t;
        }

        /// <summary>
        /// Teleport so the player's feet land on <paramref name="floorWorldPosition"/>.
        /// Safe to call repeatedly (desktop + headset).
        /// </summary>
        public bool TeleportToFloor(Vector3 floorWorldPosition, Quaternion? yawRotation = null, bool matchYaw = true)
        {
            EnsureInitialized();
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                Debug.LogError("[PlayerTeleportBootstrap] Cannot teleport — XROrigin/Camera missing.", this);
                return false;
            }

            LastFloorDestination = floorWorldPosition;

            // XR Toolkit floor formula: place the CAMERA at floor + camera-in-origin height,
            // which slides the Origin underneath without breaking room-scale offsets.
            Transform originT = OriginTransform;
            Vector3 up = originT != null ? originT.up : Vector3.up;
            float cameraHeight = Mathf.Max(0.1f, xrOrigin.CameraInOriginSpaceHeight);
            Vector3 cameraWorldDestination = floorWorldPosition + up * cameraHeight;

            bool ccWasEnabled = characterController != null && characterController.enabled;
            if (characterController != null)
                characterController.enabled = false;

            FreezeLocomotion(true);

            xrOrigin.MoveCameraToWorldLocation(cameraWorldDestination);

            if (matchYaw && yawRotation.HasValue)
            {
                Vector3 forward = yawRotation.Value * Vector3.forward;
                xrOrigin.MatchOriginUpCameraForward(up, forward);
            }
            else if (matchYaw)
            {
                // Keep current yaw; only position was requested.
            }

            // Critical: without this, CharacterController trigger pairs stay stuck and
            // OnTriggerEnter will never fire again for the same kill-zone.
            Physics.SyncTransforms();

            if (characterController != null)
            {
                characterController.enabled = ccWasEnabled;
                // Nudge so the controller re-evaluates contacts against the new pose.
                if (ccWasEnabled)
                    characterController.Move(Vector3.zero);
            }

            Physics.SyncTransforms();
            ResetVelocities();

            if (syncBodyChildren)
                SnapBodyChildrenToRig();

            // Notify optional body-sync helpers (safe if Player mesh was deleted).
            var syncs = xrOrigin.GetComponentsInChildren<PlayerTeleportSync>(true);
            for (int i = 0; i < syncs.Length; i++)
            {
                if (syncs[i] != null)
                    syncs[i].SyncAfterTeleport();
            }

            if (_freezeRoutine != null)
                StopCoroutine(_freezeRoutine);
            _freezeRoutine = StartCoroutine(UnfreezeAfterDelay(locomotionFreezeSeconds));

            if (debugLogs)
                Debug.Log($"[PlayerTeleportBootstrap] Teleported floor->{floorWorldPosition} cam->{cameraWorldDestination}", this);

            return true;
        }

        public bool TeleportTo(Transform target, bool matchYaw = true, Vector3 offset = default)
        {
            if (target == null) return false;
            Vector3 floor = target.position + new Vector3(offset.x, offset.y, offset.z);
            return TeleportToFloor(floor, target.rotation, matchYaw);
        }

        /// <summary>
        /// Optional path through XR Interaction Toolkit's TeleportationProvider when assigned.
        /// Falls back to direct MoveCameraToWorldLocation if the provider is missing/busy.
        /// </summary>
        public bool TeleportViaProvider(Transform target, bool matchYaw = true)
        {
            if (teleportationProvider == null || target == null)
                return TeleportTo(target, matchYaw);

            var request = new TeleportRequest
            {
                requestTime = Time.time,
                destinationPosition = target.position,
                destinationRotation = target.rotation,
                matchOrientation = matchYaw ? MatchOrientation.TargetUpAndForward : MatchOrientation.None
            };

            bool queued = teleportationProvider.QueueTeleportRequest(request);
            if (!queued)
                return TeleportTo(target, matchYaw);

            LastFloorDestination = target.position;
            StartCoroutine(PostProviderCleanup());
            return true;
        }

        private IEnumerator PostProviderCleanup()
        {
            // Provider applies on its update; sync afterwards so triggers re-arm.
            yield return null;
            Physics.SyncTransforms();
            if (characterController != null && characterController.enabled)
                characterController.Move(Vector3.zero);
            Physics.SyncTransforms();
            ResetVelocities();
            if (syncBodyChildren)
                SnapBodyChildrenToRig();
        }

        private void FreezeLocomotion(bool freeze)
        {
            if (!freeze)
            {
                for (int i = 0; i < _frozen.Count; i++)
                {
                    if (_frozen[i] != null)
                        _frozen[i].enabled = true;
                }
                _frozen.Clear();
                return;
            }

            _frozen.Clear();
            for (int i = 0; i < _locomotionProviders.Count; i++)
            {
                var p = _locomotionProviders[i];
                if (p == null || !p.enabled) continue;
                // Keep TeleportationProvider enabled so provider-based teleports can finish.
                if (p is TeleportationProvider) continue;
                p.enabled = false;
                _frozen.Add(p);
            }
        }

        private IEnumerator UnfreezeAfterDelay(float seconds)
        {
            if (seconds > 0f)
                yield return new WaitForSecondsRealtime(seconds);
            FreezeLocomotion(false);
            _freezeRoutine = null;
        }

        private void ResetVelocities()
        {
            if (xrOrigin == null) return;
            var bodies = xrOrigin.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                var rb = bodies[i];
                if (rb == null || rb.isKinematic) continue;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Keeps visual "Player" meshes glued to the rig after teleport. Does not require
        /// those meshes — safe if they were deleted from the prefab.
        /// </summary>
        public void SnapBodyChildrenToRig()
        {
            if (xrOrigin == null) return;

            Transform originT = OriginTransform;

            for (int i = 0; i < originT.childCount; i++)
            {
                Transform child = originT.GetChild(i);
                if (child == null) continue;
                if (child == xrOrigin.CameraFloorOffsetObject?.transform) continue;
                if (_probe != null && child == _probe) continue;

                // Only nudge objects that look like a body / avatar, not controllers.
                string n = child.name;
                bool looksLikeBody =
                    n.IndexOf("player", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("avatar", System.StringComparison.OrdinalIgnoreCase) >= 0;

                if (!looksLikeBody) continue;

                // Pin to rig feet in XZ; preserve authored local Y.
                child.localPosition = new Vector3(0f, child.localPosition.y, 0f);
            }
        }

        /// <summary>
        /// Returns true when the rig's feet are still overlapping (or very near) a world-space bounds.
        /// Used by TELEPORTBACK to re-arm without relying on OnTriggerExit.
        /// </summary>
        public bool IsNearBounds(Bounds worldBounds, float padding)
        {
            Vector3 feet = OriginTransform.position;
            Bounds expanded = worldBounds;
            expanded.Expand(padding * 2f);
            // Treat as a vertical column — ignore exact Y so floating/sinking doesn't false-negative.
            Vector3 flat = new Vector3(feet.x, expanded.center.y, feet.z);
            return expanded.Contains(flat);
        }
    }
}
