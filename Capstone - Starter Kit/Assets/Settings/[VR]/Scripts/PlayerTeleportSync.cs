using System.Collections;
using UnityEngine;

namespace MissionBit
{
    /// <summary>
    /// Optional helper: snaps a "player child" transform back onto the XR Rig one
    /// frame after a teleport, in case a child body drifts out of alignment.
    ///
    /// This is NOT required by the TELEPORTBACK trigger - TELEPORTBACK moves the
    /// XR Origin root, so child bodies follow automatically. This component is kept
    /// as a standalone utility (it previously lived, incorrectly, inside
    /// PlayerMovement.cs, which broke both scripts).
    /// </summary>
    public class PlayerTeleportSync : MonoBehaviour
    {
        [Header("Assign in Inspector")]
        [SerializeField] private Transform xrRig;
        [SerializeField] private Transform playerChild;

        [Tooltip("Also copy the rig's yaw onto the player child.")]
        [SerializeField] private bool matchRotation = false;

        /// <summary>Call this immediately after teleporting the XR Rig.</summary>
        public void SyncAfterTeleport()
        {
            if (xrRig == null || playerChild == null)
            {
                Debug.LogWarning($"{name}: PlayerTeleportSync needs both 'xrRig' and 'playerChild' assigned.", this);
                return;
            }

            StartCoroutine(SyncRoutine());
        }

        private IEnumerator SyncRoutine()
        {
            // Wait one frame so the teleport has finished updating.
            yield return null;

            playerChild.position = xrRig.position;

            if (matchRotation)
                playerChild.rotation = Quaternion.Euler(0f, xrRig.eulerAngles.y, 0f);
        }
    }
}
