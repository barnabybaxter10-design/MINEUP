using UnityEngine;

namespace BoxCutter
{
    [RequireComponent(typeof(Rigidbody))]
    public class BoxCutterRb : MonoBehaviour
    {
        private Rigidbody rb;
        public BoxCutterCaller caller;
        
        public float gravityGunHoldDistMul = 1;
        public float throwForceMultiplier = 1;

        [HideInInspector] public bool canCut;
        private bool hit;

        private Vector3 lastVelo;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            lastVelo = rb.linearVelocity;
        }

        private void OnCollisionEnter(Collision other)
        {
            if (!canCut) return;
            Vector3 velo = rb.linearVelocity;
            if (velo.magnitude < 0.05f) return;

            Transform hitTrans = other.collider.transform;

            if (BoxCutterCaller.IsBoxObj(hitTrans, out BoxObj box))
            {
                Vector3 veloNorm = lastVelo.normalized;

                // Simple offsetting of the destruction position based on the velocity of the object to predict the impact point
                if (caller.boxMode)
                    caller.posOffset = new Vector3(veloNorm.x * caller.boxBounds.x / 2, veloNorm.y * caller.boxBounds.y / 2, veloNorm.z * caller.boxBounds.z / 2);
                else
                    caller.posOffset = veloNorm * caller.radius / 2;

                caller.Explode();

                canCut = false;
            }
        }
    }
}