using UnityEngine;

public class TELEPORTBACK : MonoBehaviour
{
    [Header("Teleport Settings")]
    [SerializeField] private Transform teleportTarget;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool useTrigger = true;
    [SerializeField] private Vector3 offset = Vector3.zero;

    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger)
            return;

        TeleportIfPlayer(other.transform);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (useTrigger)
            return;

        TeleportIfPlayer(collision.transform);
    }

    private void TeleportIfPlayer(Transform other)
    {
        if (!other.CompareTag(playerTag))
            return;

        if (teleportTarget == null)
        {
            Debug.LogWarning($"{name}: No teleport target assigned.");
            return;
        }

        other.position = teleportTarget.position + offset;
        other.rotation = teleportTarget.rotation;
    }
}
