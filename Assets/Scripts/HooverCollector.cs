using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class HooverCollector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScrapWallet wallet;

    [Header("Hoover Settings")]
    [Min(0.1f)]
    [SerializeField] private float hooverRadius = 6f;
    [Min(0.1f)]
    [SerializeField] private float pullStrength = 35f;
    [Min(0.1f)]
    [SerializeField] private float collectDistance = 1.5f;
    [SerializeField] private LayerMask chunkLayerMask = ~0;

    [Header("Legacy Input Fallback")]
    [SerializeField] private KeyCode fallbackHooverKey = KeyCode.E;

    private Collider[] overlapResults = new Collider[64];
    private readonly HashSet<HooverChunk> processedChunks = new HashSet<HooverChunk>();

#if ENABLE_INPUT_SYSTEM
    private InputAction hooverAction;
#endif

    private void Awake()
    {
        if (wallet == null)
        {
            wallet = GetComponent<ScrapWallet>();
        }

#if ENABLE_INPUT_SYSTEM
        hooverAction = new InputAction("Hoover");
        hooverAction.AddBinding("<Keyboard>/e");
        hooverAction.AddBinding("<Mouse>/rightButton");
        hooverAction.AddBinding("<Gamepad>/leftTrigger");
#endif
    }

    private void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        hooverAction?.Enable();
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        hooverAction?.Disable();
#endif
    }

    private void Update()
    {
        if (wallet == null || !IsHooverHeld())
        {
            return;
        }

        ProcessNearbyChunks();
    }

    private bool IsHooverHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (hooverAction != null)
        {
            return hooverAction.IsPressed();
        }
#endif
        return Input.GetKey(fallbackHooverKey) || Input.GetMouseButton(1);
    }

    private void ProcessNearbyChunks()
    {
        processedChunks.Clear();

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            hooverRadius,
            overlapResults,
            chunkLayerMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount >= overlapResults.Length)
        {
            overlapResults = new Collider[overlapResults.Length * 2];
        }

        float collectDistanceSqr = collectDistance * collectDistance;

        for (int i = 0; i < hitCount; i++)
        {
            Collider candidateCollider = overlapResults[i];
            if (candidateCollider == null)
            {
                continue;
            }

            HooverChunk chunk = candidateCollider.attachedRigidbody != null
                ? candidateCollider.attachedRigidbody.GetComponent<HooverChunk>()
                : candidateCollider.GetComponentInParent<HooverChunk>();

            if (chunk == null || !processedChunks.Add(chunk))
            {
                continue;
            }

            Vector3 toCollector = transform.position - chunk.transform.position;
            if (toCollector.sqrMagnitude <= collectDistanceSqr)
            {
                chunk.TryCollect(wallet);
                continue;
            }

            chunk.PullTowards(transform.position, pullStrength);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, hooverRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, collectDistance);
    }
}
