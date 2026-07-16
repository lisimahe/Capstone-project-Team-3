using UnityEngine;

public class TELEPORTBACK : MonoBehaviour
{
    [Header("Teleport Settings")]
    [SerializeField] private Transform teleportTarget;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool useTrigger = true;
    [SerializeField] private Vector3 offset = new Vector3(0, 0.5f, 0);
    [SerializeField] private bool resetVelocity = true;

    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger)
            return;

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
        if (!playerObject.CompareTag(playerTag))
            return;

        if (teleportTarget == null)
        {
            Debug.LogWarning($"{name}: No teleport target assigned.");
            return;
        }

        // Teleport to target position
        Transform playerTransform = playerObject.transform;
        playerTransform.position = teleportTarget.position + offset;
        playerTransform.rotation = teleportTarget.rotation;

        // Reset rigidbody physics state to prevent falling through ground
        if (resetVelocity)
        {
            Rigidbody rb = playerObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // Reset character controller if present
            CharacterController cc = playerObject.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.Move(Vector3.zero);
            }
        }

        Debug.Log($"Teleported {playerObject.name} to {teleportTarget.name}");
    }
}
