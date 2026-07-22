using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class FireworksRadius : MonoBehaviour
{
    [Header("Player detection")]
    [Tooltip("The tag used by the player's rig. Leave empty to detect any object in Player Layers.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Only colliders on these layers can activate the fireworks.")]
    [SerializeField] private LayerMask playerLayers = ~0;

    [Tooltip("World-space radius used for the proximity check.")]
    [Min(0.01f)]
    [SerializeField] private float radius = 5f;

    [Header("Fireworks")]
    [Tooltip("Firework prefabs to instantiate when the player enters the radius.")]
    [SerializeField] private List<GameObject> fireworks = new List<GameObject>();

    [Tooltip("Optional spawn points. If empty, fireworks spawn at this object's position and rotation.")]
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    [Tooltip("Also check for a player already inside the radius. This makes the trigger reliable when the player is spawned inside it or has no Rigidbody.")]
    [SerializeField] private bool pollForPlayer = true;

    private SphereCollider radiusCollider;
    private Collider[] overlapResults = new Collider[16];
    private bool hasTriggered;

    public bool HasTriggered => hasTriggered;

    private void Awake()
    {
        radiusCollider = GetComponent<SphereCollider>();
        radiusCollider.isTrigger = true;
        radiusCollider.radius = radius;
    }

    void Update()
    {
        if (!pollForPlayer || hasTriggered)
            return;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            radius,
            overlapResults,
            playerLayers,
            QueryTriggerInteraction.Collide);

        // If the buffer was full, retry with a larger one so a crowded area cannot
        // hide the player behind unrelated colliders.
        if (count == overlapResults.Length)
        {
            overlapResults = Physics.OverlapSphere(
                transform.position,
                radius,
                playerLayers,
                QueryTriggerInteraction.Collide);
            count = overlapResults.Length;
        }

        for (int i = 0; i < count; i++)
        {
            if (IsPlayer(overlapResults[i]))
            {
                SpawnFireworks();
                return;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!hasTriggered && IsPlayer(other))
            SpawnFireworks();
    }

    private bool IsPlayer(Collider candidate)
    {
        if (candidate == null)
            return false;

        // XR rigs often have several child colliders, so check the collider,
        // attached Rigidbody, and all parents for the player tag and layer.
        Transform current = candidate.transform;
        while (current != null)
        {
            bool isPlayerLayer = (playerLayers.value & (1 << current.gameObject.layer)) != 0;
            bool hasPlayerTag = string.IsNullOrWhiteSpace(playerTag) || current.tag == playerTag;

            if (isPlayerLayer && hasPlayerTag)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void SpawnFireworks()
    {
        if (hasTriggered)
            return;

        if (fireworks == null)
            return;

        bool hasValidPrefab = false;
        for (int i = 0; i < fireworks.Count; i++)
        {
            if (fireworks[i] != null)
            {
                hasValidPrefab = true;
                break;
            }
        }

        if (!hasValidPrefab)
        {
            Debug.LogWarning($"{nameof(FireworksRadius)} on {name} has no firework prefabs assigned.", this);
            return;
        }

        // Set this before Instantiate so callbacks or multiple colliders cannot
        // cause a second launch during the same frame.
        hasTriggered = true;

        int spawnPointCount = spawnPoints == null ? 0 : spawnPoints.Count;
        for (int i = 0; i < fireworks.Count; i++)
        {
            GameObject prefab = fireworks[i];
            if (prefab == null)
                continue;

            Transform point = spawnPointCount > 0 ? spawnPoints[i % spawnPointCount] : transform;
            if (point == null)
                point = transform;

            Instantiate(prefab, point.position, point.rotation);
        }
    }

    private void OnValidate()
    {
        radius = Mathf.Max(0.01f, radius);

        if (radiusCollider == null)
            radiusCollider = GetComponent<SphereCollider>();

        if (radiusCollider != null)
        {
            radiusCollider.isTrigger = true;
            radiusCollider.radius = radius;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = hasTriggered ? Color.gray : Color.magenta;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
