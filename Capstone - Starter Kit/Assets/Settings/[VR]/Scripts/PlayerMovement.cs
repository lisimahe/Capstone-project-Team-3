using System.Collections;
using UnityEngine;

namespace MissionBit
{
    public class PlayerTeleportSync : MonoBehaviour
    {
        [Header("Assign in Inspector")]
        [SerializeField] private Transform xrRig;
        [SerializeField] private Transform playerChild;

        /// <summary>
        /// Call this immediately after teleporting the XR Rig.
        /// </summary>
        public void SyncAfterTeleport()
        {
            StartCoroutine(SyncRoutine());
        }

        private IEnumerator SyncRoutine()
        {
            // Wait until the teleport has finished updating.
            yield return null;

            // Snap the player child to the XR Rig.
            playerChild.position = xrRig.position;

            // Optional: Match rotation as well.
            // playerChild.rotation = xrRig.rotation;
        }
    }
}