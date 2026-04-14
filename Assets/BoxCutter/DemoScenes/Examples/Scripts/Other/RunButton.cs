using System;
using System.Collections;
using System.Collections.Generic;
using BoxCutter;
using TMPro;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxCutter
{
    public class RunButton : MonoBehaviour
    {
        public TextMeshPro text;
        public RunCaller[] toBeRanCallers = new RunCaller[] { };

        [Serializable]
        public struct RunCaller
        {
            public BoxCutterCaller voxelDestructCaller;
            public float delay;
        }

        public RunBuild[] toBeRanBuild = new RunBuild[] { };

        [Serializable]
        public struct RunBuild
        {
            public BoxCutterBuildingExample buildingExample;
            public float delay;
        }

        private Color originalColor;
        private bool isHovering = false;

        private IEnumerator RunCallers()
        {
            int callerLength = toBeRanCallers.Length;
            for (int i = 0; i < callerLength; i++)
            {
                RunCaller caller = toBeRanCallers[i];
                yield return new WaitForSecondsRealtime(caller.delay);
                caller.voxelDestructCaller.Explode();
            }
        }

        private IEnumerator RunBuilds()
        {
            int buildLength = toBeRanBuild.Length;
            for (int i = 0; i < buildLength; i++)
            {
                RunBuild build = toBeRanBuild[i];
                yield return new WaitForSecondsRealtime(build.delay);
                build.buildingExample.Attach();
            }
        }

        void Start()
        {
            if (text != null)
            {
                originalColor = text.color;
            }
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);
        RaycastHit hit;
        
        bool currentlyHovering = false;
        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider.gameObject == gameObject)
            {
                currentlyHovering = true;
                
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    StartCoroutine(RunCallers());
                    StartCoroutine(RunBuilds());
                }
            }
        }
#else
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            bool currentlyHovering = false;
            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.gameObject == gameObject)
                {
                    currentlyHovering = true;

                    if (Input.GetMouseButtonDown(0))
                    {
                        StartCoroutine(RunCallers());
                        StartCoroutine(RunBuilds());
                    }
                }
            }
#endif

            if (currentlyHovering != isHovering)
            {
                isHovering = currentlyHovering;
                if (text != null)
                {
                    text.color = isHovering ? BoxCutterGlobalVars.boxCutterPrimaryColor : originalColor;
                }
            }
        }
    }
}