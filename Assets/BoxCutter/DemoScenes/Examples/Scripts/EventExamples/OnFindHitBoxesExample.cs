// Event signature: Action<BoxObj[], CallerData>
using BoxCutter;
using UnityEngine;

namespace BoxCutter
{
    public class OnFindHitBoxesExample : MonoBehaviour
    {
        private BoxCutterCaller caller;

        void Start()
        {
            caller = GetComponent<BoxCutterCaller>();
            caller.OnFindHitBoxes += HandleExplosionHit;
        }

        private void HandleExplosionHit(BoxObj[] hitObjects, BoxCutterCaller.CallerData callerData)
        {
            Debug.Log($"Explosion hit {hitObjects.Length} objects at {callerData.pos}");

            // Track destruction statistics
            foreach (BoxObj hitBox in hitObjects)
            {
                Debug.Log($"Destroyed: {hitBox.name} at distance: {Vector3.Distance(hitBox.transform.position, callerData.pos)}");
            }
        }
        
        private void OnDestroy()
        {
            caller.OnFindHitBoxes -= HandleExplosionHit;
        }
    }
}