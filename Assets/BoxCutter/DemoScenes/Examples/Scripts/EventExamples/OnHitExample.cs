// Event signature: Action<BoxObj, CallerData>
using BoxCutter;
using UnityEngine;


namespace BoxCutter
{
    public class OnHitExample : MonoBehaviour
    {
        private BoxObj boxObj;
        private int hitCount = 0;

        void Start()
        {
            boxObj = GetComponent<BoxObj>();
            boxObj.OnHit += HandleObjectHit;
        }

        private void HandleObjectHit(BoxObj hitBox, BoxCutterCaller.CallerData callerData)
        {
            hitCount++;
            Debug.Log($"{hitBox.name} was hit! Total hits: {hitCount}");
            Debug.Log($"Hit at position: {callerData.pos}, force: {callerData.force}");
        }

        void OnDestroy()
        {
            boxObj.OnHit -= HandleObjectHit;
        }
    }
}