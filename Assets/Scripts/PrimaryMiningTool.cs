using UnityEngine;
using UnityEngine.Events;
using BoxCutter;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PrimaryMiningTool : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private BoxCutterCaller miningCaller;
    [SerializeField] private Transform hitEffectSpawnPoint;

    [Header("Availability")]
    [SerializeField] private bool startsUnlocked = true;

    [Header("Mining")]
    [Min(0.5f)]
    [SerializeField] private float miningRange = 6f;
    [Min(0.05f)]
    [SerializeField] private float miningCooldown = 0.35f;
    [Min(0.1f)]
    [SerializeField] private float miningStrength = 1f;
    [Min(0f)]
    [SerializeField] private float cameraKickAmount = 1.25f;
    [SerializeField] private LayerMask miningMask = ~0;

    [Header("Feedback Hooks")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip mineSwingClip;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private UnityEvent onMineSwing;

    [Header("Upgrade Multipliers")]
    [Min(0.1f)]
    [SerializeField] private float strengthMultiplier = 1f;
    [Min(0.1f)]
    [SerializeField] private float cooldownMultiplier = 1f;

    private float nextMineTime;
    private bool isUnlocked;

    private void Awake()
    {
        isUnlocked = startsUnlocked;

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }

    public void SetUnlocked(bool unlocked)
    {
        isUnlocked = unlocked;
    }

    public void SetStrengthMultiplier(float value)
    {
        strengthMultiplier = Mathf.Max(0.1f, value);
    }

    public void SetCooldownMultiplier(float value)
    {
        cooldownMultiplier = Mathf.Max(0.1f, value);
    }

    private void Update()
    {
        if (!isUnlocked || miningCaller == null || playerCamera == null)
        {
            return;
        }

        if (Time.time < nextMineTime)
        {
            return;
        }

        if (!GetMineInputDown())
        {
            return;
        }

        TryMine();
    }

    private bool GetMineInputDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private void TryMine()
    {
        nextMineTime = Time.time + (miningCooldown / cooldownMultiplier);

        PlaySwingFeedback();

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (!Physics.Raycast(ray, out RaycastHit hit, miningRange, miningMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        if (!BoxCutterCaller.IsBoxObj(hit.collider.transform, out _))
        {
            return;
        }

        ApplyCallerHitSettings(hit);
        miningCaller.Explode();

        if (hitEffectPrefab != null)
        {
            Vector3 spawnPos = hitEffectSpawnPoint != null ? hitEffectSpawnPoint.position : hit.point;
            Quaternion spawnRot = Quaternion.LookRotation(hit.normal);
            Instantiate(hitEffectPrefab, spawnPos, spawnRot);
        }
    }

    private void ApplyCallerHitSettings(RaycastHit hit)
    {
        miningCaller.obj = transform;
        miningCaller.posOffset = transform.InverseTransformPoint(hit.point);

        miningCaller.spawnForce = miningStrength * strengthMultiplier;
        miningCaller.strength = miningStrength * strengthMultiplier;

        if (cameraKickAmount > 0f)
        {
            playerCamera.transform.Rotate(-cameraKickAmount, 0f, 0f, Space.Self);
        }
    }

    private void PlaySwingFeedback()
    {
        onMineSwing?.Invoke();

        if (audioSource != null && mineSwingClip != null)
        {
            audioSource.PlayOneShot(mineSwingClip);
        }
    }
}
