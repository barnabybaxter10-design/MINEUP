using UnityEngine;
using BoxCutter;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxCutter
{
    [RequireComponent(typeof(Camera))]
    public class GravityGun : MonoBehaviour
    {
        [Header("Grab Settings")] public LayerMask pickupMask;
        public float maxPickupDistance = 10f;

        [Header("Hold Point")] public Transform holdPoint;
        public float holdSpring = 150f;
        public float holdDamping = 5f;

        [Header("Throw & Pull")] public float throwForce = 600f;

        [Header("Beam Settings")] [Tooltip("LineRenderer used for the beam")]
        public LineRenderer beam;

        [Tooltip("Where the beam starts")] public Transform beamOrigin;

        [Tooltip("Number of segments used to draw the curve (>= 4)")] [Range(4, 64)]
        public int curveResolution = 24;

        [Tooltip("Sag factor (proportional to distance)")]
        public float sagFactor = 0.15f;

        [Tooltip("Small ripple amplitude along beam")]
        public float jitterAmplitude = 0.02f;

        [Tooltip("How far to push the beam endpoint out from the surface")]
        public float beamEndOffset = 0.1f;

        [Header("Orientation Settings")] [Tooltip("How strong the torque spring is when aligning the hit normal to face the camera.")]
        public float orientTorqueStrength = 100f;

        [Tooltip("Damping on the angular velocity to smooth the rotation.")]
        public float orientTorqueDamping = 10f;

        [Header("Spinner")] public Transform spinner;
        public float spinUpSpeed = 180f;
        public float maxSpinSpeed = 720f;
        public float spinDownSpeed = 360f;

        Rigidbody heldRb;

        private Vector3 hitPointLocal;
        private Vector3 hitNormalLocal;

        private float baseHoldDistance;
        private bool hasBaseDistance = false;

        private float currentSpinSpeed = 0f;

        void Awake()
        {
            if (!beamOrigin) beamOrigin = transform;
            if (!beam)
            {
                beam = gameObject.AddComponent<LineRenderer>();
                beam.enabled = false;
                beam.widthMultiplier = 0.04f;
            }

            beam.positionCount = curveResolution;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Start()
        {
            baseHoldDistance = Vector3.Distance(transform.position, holdPoint.position);
            hasBaseDistance = true;
        }

        void Update()
        {
            bool firePressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                firePressed = Mouse.current.leftButton.wasPressedThisFrame;
            }
#else
            firePressed = Input.GetMouseButtonDown(0);
#endif

            if (firePressed)
            {
                if (!heldRb) TryGrab();
                else ThrowHeld();
            }

            UpdateBeam();

            if (heldRb)
            {
                UpdateHoldPointDistance();
            }
            else
            {
                if (hasBaseDistance)
                    holdPoint.position = transform.position + transform.forward * baseHoldDistance;
            }

            UpdateSpinner();
        }

        void FixedUpdate()
        {
            if (heldRb)
            {
                ApplyHoldForces();
                ApplyOrientationTorque();
            }
        }

        private void ApplyOrientationTorque()
        {
            // Reconstruct the world space hit point and normal
            Vector3 worldHitPoint = heldRb.transform.TransformPoint(hitPointLocal);
            Vector3 worldHitNormal = heldRb.transform.TransformDirection(hitNormalLocal);

            Vector3 dirToCamera = (transform.position - worldHitPoint).normalized;

            // Find the axis and angle between current normal and desired
            Vector3 axis = Vector3.Cross(worldHitNormal, dirToCamera);
            float angle = Vector3.Angle(worldHitNormal, dirToCamera) * Mathf.Deg2Rad;

            // If the normal is already facing the camera, skip the tiny remainder
            if (axis.sqrMagnitude < 1e-6f) return;

            axis.Normalize();

            Vector3 springTorque = axis * (angle * orientTorqueStrength);
            Vector3 dampingTorque = -heldRb.angularVelocity * orientTorqueDamping;

            heldRb.AddTorque(springTorque + dampingTorque, ForceMode.Acceleration);
        }

        void TryGrab()
        {
            Vector3 screenPos = Vector3.zero;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPos = Mouse.current.position.ReadValue();
            }
#else
            screenPos = Input.mousePosition;
#endif

            Ray ray = GetComponent<Camera>().ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, maxPickupDistance, pickupMask) && hit.rigidbody)
            {
                hitPointLocal = hit.transform.InverseTransformPoint(hit.point);
                hitNormalLocal = hit.transform.InverseTransformDirection(hit.normal);

                heldRb = hit.rigidbody;
                heldRb.useGravity = false;
                heldRb.linearDamping = 10;
                heldRb.angularDamping = 10;
                beam.enabled = true;
            }
        }

        void ThrowHeld()
        {
            heldRb.useGravity = true;
            heldRb.linearDamping = 0;
            heldRb.angularDamping = 0.05f;

            float forceMul = 1;
            if (heldRb.TryGetComponent(out BoxCutterRb boxCutterRb))
            {
                boxCutterRb.canCut = true;
                forceMul = boxCutterRb.throwForceMultiplier;
            }

            // Impulse calculation F = m * a (applied as instant velocity change)
            float impulse = throwForce * heldRb.mass;
            heldRb.AddForce(impulse * forceMul * transform.forward, ForceMode.Impulse);

            heldRb = null;
            beam.enabled = false;
        }

        void ApplyHoldForces()
        {
            Vector3 worldHitPoint = heldRb.transform.TransformPoint(hitPointLocal);

            Vector3 toTarget = holdPoint.position - worldHitPoint;

            Vector3 springForce = toTarget * holdSpring;

            Vector3 pointVelocity = heldRb.GetPointVelocity(worldHitPoint);
            Vector3 dampingForce = -pointVelocity * holdDamping;

            heldRb.AddForceAtPosition(springForce + dampingForce, worldHitPoint, ForceMode.Acceleration);
        }

        void UpdateBeam()
        {
            if (!heldRb || !beam)
            {
                beam.enabled = false;
                return;
            }

            beam.enabled = true;

            Vector3 p0 = beamOrigin.position;

            Vector3 worldHitPoint = heldRb.transform.TransformPoint(hitPointLocal);
            Vector3 worldHitNormal = heldRb.transform.TransformDirection(hitNormalLocal);
            Vector3 p3 = worldHitPoint + worldHitNormal * beamEndOffset;

            Vector3 mid = (p0 + p3) * 0.5f;
            float distance = Vector3.Distance(p0, p3);
            Vector3 sagDir = Physics.gravity != Vector3.zero
                ? -Physics.gravity.normalized
                : -transform.up;
            
            // Bezier control points calculation
            Vector3 p1 = Vector3.Lerp(p0, mid, 0.33f) + distance * sagFactor * sagDir;
            Vector3 p2 = Vector3.Lerp(mid, p3, 0.66f) + distance * sagFactor * sagDir;

            for (int i = 0; i < curveResolution; i++)
            {
                float t = i / (curveResolution - 1f);
                
                // Cubic Bezier Formula: B(t) = (1-t)^3 P0 + 3(1-t)^2 t P1 + 3(1-t)t^2 P2 + t^3 P3
                Vector3 pos =
                    Mathf.Pow(1 - t, 3) * p0 +
                    3 * Mathf.Pow(1 - t, 2) * t * p1 +
                    3 * (1 - t) * Mathf.Pow(t, 2) * p2 +
                    Mathf.Pow(t, 3) * p3;

                float noise = Mathf.PerlinNoise(t * 2f, Time.time) - 0.5f;
                Vector3 normal = Vector3.Cross((p3 - p0).normalized, transform.up);
                beam.SetPosition(i, pos + noise * jitterAmplitude * distance * normal);
            }
        }

        private void UpdateHoldPointDistance()
        {
            if (!hasBaseDistance || heldRb == null)
                return;

            float newDistance = baseHoldDistance;

            var renderers = heldRb.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length > 0)
            {
                // Combine renderer's world space bounds
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    combined.Encapsulate(renderers[i].bounds);
                
                float mul = 1;
                if (heldRb.TryGetComponent(out BoxCutterRb boxCutterRb))
                    mul = boxCutterRb.gravityGunHoldDistMul;

                Vector3 f = transform.forward;
                Vector3 absF = new Vector3(Mathf.Abs(f.x), Mathf.Abs(f.y), Mathf.Abs(f.z));
                float halfSizeAlongView = Vector3.Dot(combined.extents, absF) * mul;

                newDistance += halfSizeAlongView;
            }

            // Reposition the holdPoint out at the adjusted distance
            holdPoint.position = transform.position + transform.forward * newDistance;
        }

        private void UpdateSpinner()
        {
            if (!spinner) return;

            if (heldRb)
            {
                currentSpinSpeed = Mathf.MoveTowards(currentSpinSpeed, maxSpinSpeed, spinUpSpeed * Time.deltaTime);
            }
            else
            {
                currentSpinSpeed = Mathf.MoveTowards(currentSpinSpeed, 0f, spinDownSpeed * Time.deltaTime);
            }

            spinner.Rotate(0, 0, currentSpinSpeed * Time.deltaTime, Space.Self);
        }
    }
}