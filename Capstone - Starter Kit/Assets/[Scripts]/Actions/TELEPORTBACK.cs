using UnityEngine;

/// <summary>
/// Teleports the player back to a designated spawn point when they hit this trigger.
/// SETUP INSTRUCTIONS:
/// 1. Attach this script to a game object with a BoxCollider set as a TRIGGER
/// 2. In Inspector, set "Teleport Target" to your spawn point (e.g., TeleportBackPoint)
/// 3. Make sure player has tag "Player"
/// 4. That's it! Player will teleport when they touch this trigger.
/// </summary>
public class TELEPORTBACK : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] private Transform teleportTarget;
    [SerializeField] private Vector3 positionOffset = new Vector3(0, 0.5f, 0);
    
    [Header("Detection")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float cooldownSeconds = 0.2f;
    
    private float lastTeleportTime = -999f;
    private Collider triggerCollider;

    private void Start()
    {
        // Validate setup
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
        {
            triggerCollider.isTrigger = true;
            Debug.LogWarning($"{gameObject.name}: Collider was not set as trigger. Fixed automatically.", gameObject);
        }

        if (teleportTarget == null)
        {
            Debug.LogError($"{gameObject.name}: SETUP ERROR - Teleport Target is not assigned in Inspector!", gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CheckAndTeleport(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        // Allow repeated teleports while player stays in trigger
        CheckAndTeleport(other.gameObject);
    }

    private void CheckAndTeleport(GameObject hitObject)
    {
        // Validate this is the player
        if (hitObject == null || !hitObject.CompareTag(playerTag))
            return;

        // Check cooldown to prevent spam
        if (Time.time - lastTeleportTime < cooldownSeconds)
            return;

        // Make sure target exists
        if (teleportTarget == null)
        {
            Debug.LogError($"{gameObject.name}: Teleport Target not assigned!", gameObject);
            return;
        }

        // Perform teleport
        Teleport(hitObject);
    }

    private void Teleport(GameObject playerObject)
    {
        lastTeleportTime = Time.time;

        Vector3 newPosition = teleportTarget.position + positionOffset;
        
        // Teleport the player
        playerObject.transform.position = newPosition;
        playerObject.transform.rotation = teleportTarget.rotation;
        
        // Also teleport the XR Rig parent if it exists
        if (playerObject.transform.parent != null)
        {
            playerObject.transform.parent.position = newPosition;
            playerObject.transform.parent.rotation = teleportTarget.rotation;
        }

        // Reset velocity to prevent momentum carryover
        ResetVelocity(playerObject);

        Debug.Log($"Teleported player and rig to {newPosition}");
    }

    private void ResetVelocity(GameObject playerObject)
    {
        // Reset Rigidbody if not kinematic
        Rigidbody rb = playerObject.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Reset CharacterController if present
        CharacterController cc = playerObject.GetComponent<CharacterController>();
        if (cc != null && cc.enabled)
        {
            cc.Move(Vector3.zero);
        }

        // Reset velocity on all child rigidbodies (controllers, etc.)
        foreach (Rigidbody childRb in playerObject.GetComponentsInChildren<Rigidbody>())
        {
            if (childRb != null && !childRb.isKinematic)
            {
                childRb.velocity = Vector3.zero;
                childRb.angularVelocity = Vector3.zero;
            }
        }
    }
}

