using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoxCutter
{
    //Initial destruction events often incur a performance spike due to Just-In-Time (JIT) overhead associated with Unity's Burst compiled jobs.
    // This script "pre warms" the system by triggering a destruction immediately upon scene load, ensuring the user experiences a smooth framerate during actual gameplay.
    [RequireComponent(typeof(BoxCutterCaller), typeof(BoxObj))]
    public class InitMonoJit : MonoBehaviour
    {
        void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForEndOfFrame();
            BoxCutterCaller.CallerData callData = GetComponent<BoxCutterCaller>().BuildCallerData();
            _ = DestructionTrigger.RunDestruction(new BoxObj[]{GetComponent<BoxObj>()}, callData);
        }
    }
}