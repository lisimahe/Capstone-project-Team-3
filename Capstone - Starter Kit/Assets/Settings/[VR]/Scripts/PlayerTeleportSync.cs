using System.Collections;
using UnityEngine;
using Unity.XR.CoreUtils;

namespace MissionBit
{
    /// <summary>
    /// Keeps an optional visual "player child" glued to the XR Rig after teleports.
    /// Prefer parenting the mesh under the XR Origin — this is a safety net for leftover
    /// bodies that used to be left floating when only the rig moved.
    ///
    /// If you deleted the Player mesh from the prefab, you do not need this component.
    /// <see cref="PlayerTeleportBootstrap"/> already handles multi-teleport without it.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class PlayerTeleportSync : MonoBehaviour
    {
        [Header("Assign in Inspector (optional — auto-resolved)")]
        [SerializeField] private Transform xrRig;
        [SerializeField] private Transform playerChild;
        [SerializeField] private bool matchRotation = false;
        [SerializeField] private bool autoFindChildNamedPlayer = true;
        [SerializeField] private bool continuousFollow = true;

        private PlayerTeleportBootstrap _bootstrap;

        private void Awake()
        {
            ResolveRefs();
        }

        private void LateUpdate()
        {
            if (!continuousFollow) return;
            if (xrRig == null || playerChild == null) return;

            // Only correct drift — cheap and prevents the "body left behind" look.
            Vector3 target = new Vector3(xrRig.position.x, playerChild.position.y, xrRig.position.z);
            if ((playerChild.position - target).sqrMagnitude > 0.0001f)
                playerChild.position = target;

            if (matchRotation)
                playerChild.rotation = Quaternion.Euler(0f, xrRig.eulerAngles.y, 0f);
        }

        /// <summary>Call immediately after teleporting the XR Rig (also safe to call every time).</summary>
        public void SyncAfterTeleport()
        {
            ResolveRefs();
            if (xrRig == null || playerChild == null)
            {
                // Not an error — the Player mesh may have been intentionally removed.
                return;
            }

            StartCoroutine(SyncRoutine());
        }

        private IEnumerator SyncRoutine()
        {
            yield return null;
            Physics.SyncTransforms();

            playerChild.position = new Vector3(xrRig.position.x, playerChild.position.y, xrRig.position.z);

            if (matchRotation)
                playerChild.rotation = Quaternion.Euler(0f, xrRig.eulerAngles.y, 0f);

            // One more frame for CharacterControllerDriver height updates.
            yield return null;
            playerChild.position = new Vector3(xrRig.position.x, playerChild.position.y, xrRig.position.z);
        }

        private void ResolveRefs()
        {
            if (_bootstrap == null)
                _bootstrap = PlayerTeleportBootstrap.Instance ?? PlayerTeleportBootstrap.FindOrCreate();

            if (xrRig == null)
            {
                if (_bootstrap != null)
                    xrRig = _bootstrap.OriginTransform;
                else
                {
                    var origin = FindObjectOfType<XROrigin>();
                    if (origin != null) xrRig = origin.transform;
                }
            }

            if (playerChild == null && autoFindChildNamedPlayer && xrRig != null)
            {
                for (int i = 0; i < xrRig.childCount; i++)
                {
                    Transform c = xrRig.GetChild(i);
                    if (c.name.IndexOf("player", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        playerChild = c;
                        break;
                    }
                }
            }
        }
    }
}
