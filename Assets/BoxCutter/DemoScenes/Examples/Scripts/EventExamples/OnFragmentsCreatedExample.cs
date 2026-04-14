// Event signature: Action<FragmentBatch>

using BoxCutter;
using UnityEngine;

namespace BoxCutter
{
    public class OnFragmentsCreatedExample : MonoBehaviour
    {
        private BoxCutterCaller caller;

        void Start()
        {
            caller = GetComponent<BoxCutterCaller>();
            caller.OnFragmentBatchCreated += HandleFragmentBatch;
            caller.OnAllFragmentsComplete += HandleAllFragmentsComplete;
        }

        private void HandleFragmentBatch(BoxCutterCaller.FragmentBatch batch)
        {
            Debug.Log($"Fragment batch created: Count={batch.Fragments.Length}, OneDebris={batch.OneDebris.Length}, Progress={batch.CreationProgress}");

            // Apply physics forces to disconnected fragments
            foreach (BoxCutterWorldPhysics.CreatedBoxData createdFrag in batch.Fragments)
            {
                foreach (BoxObj fragment in createdFrag.CreatedBoxCutters)
                {
                    if (fragment.connectionState == BoxCutterManager.ConnectionStateEnum.Disconnected)
                    {
                        Rigidbody rb = fragment.parentHolder.rb;
                        rb.AddForce(Vector3.up * 1000f);
                    }

                    // Apply color changes to fragments
                    foreach (Material mat in fragment.mats)
                    {
                        mat.SetColor("_BaseColor", Color.red);
                    }
                }
            }

            // Handle OneDebris objects
            foreach (BoxCutterOneDebris oneDebris in batch.OneDebris)
            {
                foreach (Material mat in oneDebris.meshRend.materials)
                {
                    mat.SetColor("_BaseColor", Color.yellow);
                }
            }
        }

        private void HandleAllFragmentsComplete(BoxCutterCaller.FragmentBatch finalBatch)
        {
            Debug.Log($"All fragments complete! Total fragments: {finalBatch.Fragments.Length}, OneDebris: {finalBatch.OneDebris.Length}");
        }
    }
}