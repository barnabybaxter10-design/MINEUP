// Event signature: Action<BoxObj, CreatedBoxData[]>

using System;
using BoxCutter;
using UnityEngine;

namespace BoxCutter
{
    public class OnFragmentsExample : MonoBehaviour
    {
        private BoxObj boxObj;
        private int totalFragments = 0;

        void Start()
        {
            boxObj = GetComponent<BoxObj>();
            boxObj.OnFragmentsCreated += HandleFragmentsCreated;
        }

        private void HandleFragmentsCreated(BoxObj sourceBox, BoxCutterWorldPhysics.CreatedBoxData[] fragments)
        {
            foreach (BoxCutterWorldPhysics.CreatedBoxData frag in fragments)
            {
                totalFragments += frag.CreatedBoxCutters.Count;
                Debug.Log($"{sourceBox.name} created {frag.CreatedBoxCutters.Count} fragments");
                Debug.Log($"Total fragments in scene: {totalFragments}");

                // Apply custom code to fragments
                foreach (BoxObj fragBox in frag.CreatedBoxCutters)
                {
                    foreach (Material mat in fragBox.mats)
                    {
                        mat.SetColor("_BaseColor", Color.red);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            boxObj.OnFragmentsCreated -= HandleFragmentsCreated;
        }
    }
}