using UnityEngine;

public class TELEPORTBACK : MonoBehaviour
{
    [Header("Teleport Settings")]
    [SerializeField] private Transform teleportTarget;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool useTrigger = true;
    [SerializeField] private Vector3 offset = new Vector3(0, 0.5f, 0);
    [SerializeField] private bool resetVelocity = true;
    [SerializeField] private float cooldownSeconds = 0.5f;

    private Collider triggerCollider;
    private float lastTeleportTime = -999f;

    private void Start()
    {
        // Cache collider and validate setup
        triggerCollider = GetComponent<Collider>();
        
        if (useTrigger && triggerCollider != null && !triggerCollider.isTrigger)
        {
            Debug.LogWarning($"{name}: Using trigger mode but collider is not set as trigger. Enabling it.", gameObject);
            triggerCollider.isTrigger = true;
        }

        if (teleportTarget == null)
        {
            Debug.LogError($"{name}: Teleport Target not assigned! Assign a target in the Inspector.", gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger)
            return;

        TeleportIfPlayer(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!useTrigger)
            return;

        // Keep checking in case they're still in the trigger zone
        TeleportIfPlayer(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (useTrigger)
            return;

        TeleportIfPlayer(collision.gameObject);
    }

    private void TeleportIfPlayer(GameObject playerObject)
    {
        // Null check
        if (playerObject == null)
        {
            Debug.LogWarning("PlayerObject is null");
            return;
        }

        Debug.Log($"Collision detected with: {playerObject.name}, tag: {playerObject.tag}");

        // Tag check
        if (!playerObject.CompareTag(playerTag))
        {
            Debug.Log($"Tag mismatch: object has '{playerObject.tag}', looking for '{playerTag}'");
            return;
        }

        Debug.Log("Tag check passed");

        // Cooldown check - prevent multiple teleports in quick succession
        if (Time.time - lastTeleportTime < cooldownSeconds)
        {
            Debug.Log($"Cooldown active. Last teleport was {Time.time - lastTeleportTime:F2}s ago");
            return;
        }

        Debug.Log("Cooldown check passed");

        // Target validation
        if (teleportTarget == null)
        {
            Debug.LogError($"{name}: No teleport target assigned. Assign target in Inspector.", gameObject);
            return;
        }

        Debug.Log("Target validation passed");

        // Attempt teleport
        if (TeleportPlayer(playerObject))
        {
            lastTeleportTime = Time.time;
            Debug.Log($"Successfully teleported {playerObject.name} to {teleportTarget.name}");
        }
        else
        {
            Debug.LogError("TeleportPlayer returned false - check error logs above");
        }
    }

    private bool TeleportPlayer(GameObject playerObject)
    {
        try
        {
            Transform teleportTransform = playerObject.transform;

            if (teleportTransform == null)
            {
                Debug.LogError($"Cannot find transform to teleport on {playerObject.name}", playerObject);
                return false;
            }

            Debug.Log($"[BEFORE] {playerObject.name} position: {teleportTransform.position}");

            // Calculate final position
            Vector3 targetPosition = teleportTarget.position + offset;
            Debug.Log($"Target position: {teleportTarget.position}, Offset: {offset}, Final target: {targetPosition}");

            // Teleport the XR Rig root (camera will follow as a child)
            teleportTransform.position = targetPosition;
            teleportTransform.rotation = teleportTarget.rotation;

            Debug.Log($"[AFTER] {playerObject.name} position: {teleportTransform.position}");

            // Reset physics
            ResetPlayerPhysics(playerObject);

            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Teleport failed: {ex.Message}", playerObject);
            return false;
        }
    }

    private void ResetPlayerPhysics(GameObject playerObject)
    {
        if (!resetVelocity)
            return;

        // Reset Rigidbody (only if not kinematic)
        Rigidbody rb = playerObject.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Reset CharacterController
        CharacterController cc = playerObject.GetComponent<CharacterController>();
        if (cc != null && cc.enabled)
        {
            cc.Move(Vector3.zero);
        }

        // Reset Rigidbody on children (XR controllers, etc.) - only if not kinematic
        foreach (Rigidbody childRb in playerObject.GetComponentsInChildren<Rigidbody>())
        {
            if (childRb != null && !childRb.isKinematic)
            {
                childRb.velocity = Vector3.zero;
                childRb.angularVelocity = Vector3.zero;
            }
        }
    }

    // Editor validation
    private void OnValidate()
    {
        if (cooldownSeconds < 0.1f)
            cooldownSeconds = 0.1f;
    }

    // Debug visualization
    private void OnDrawGizmosSelected()
    {
        if (teleportTarget != null)
        {
            // Draw line from trigger to target
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, teleportTarget.position);

            // Draw target sphere
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(teleportTarget.position + offset, 0.3f);
        }
    }
}
