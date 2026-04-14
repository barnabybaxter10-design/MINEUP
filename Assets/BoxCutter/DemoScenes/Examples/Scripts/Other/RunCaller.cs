using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using BoxCutter;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace BoxCutter
{
    public class RunCaller : MonoBehaviour
    {
        public RunCallerData[] callerArr = new RunCallerData[] { };

        [Serializable]
        public struct RunCallerData
        {
            public BoxCutterCaller voxelDestructCaller;
            public float delay;
            public bool breakPoint;
            public bool disable;
            public bool pauseEditor;
        }

        void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run(float delay = 0)
        {
            yield return new WaitForSeconds(delay);

            for (int i = 0; i < callerArr.Length; i++)
            {
                RunCallerData runData = callerArr[i];

                if (runData.delay != 0) yield return new WaitForSeconds(runData.delay);
                if (runData.disable) continue;

                runData.voxelDestructCaller.Explode();

#if UNITY_EDITOR
                if (runData.pauseEditor) EditorApplication.isPaused = true;
#endif
                if (runData.breakPoint) yield break;
            }
        }
    }
}