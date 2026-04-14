using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterObjectPool;

namespace BoxCutter
{
    /// <summary>
    /// Represents a single debris fragment generated from destruction.
    /// Manages automatic return to object pool after a specified delay for memory efficiency.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider), typeof(MeshRenderer))]
    public class BoxCutterOneDebris : MonoBehaviour
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Component References
        //─────────────────────────────────────────────────────────────────────────────────────
        public Transform obj;
        public GameObject gameObj;
        public Rigidbody rb;
        public MeshRenderer meshRend;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Pool Return Management
        //─────────────────────────────────────────────────────────────────────────────────────
        private bool returnScheduled = false;
        private float returnTime = 0f;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                                 State And Flags
        //─────────────────────────────────────────────────────────────────────────────────────
        public int uniqueId = -1;
        public bool pooled = true;
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                                  Activate Data
        //─────────────────────────────────────────────────────────────────────────────────────
        
        public void Init()
        {
            obj = transform;
            gameObj = gameObject;
            rb = GetComponent<Rigidbody>();
            meshRend = GetComponent<MeshRenderer>();
        }
        void Update()
        {
            // Check if scheduled return time has elapsed
            if (returnScheduled && time >= returnTime)
            {
                ReturnToPool();
            }
        }

        /// <summary>
        /// Immediately returns this debris object to the pool for reuse.
        /// Sets rigidbody to kinematic to stop physics simulation.
        /// </summary>
        public void ReturnToPool()
        {
            BoxCutterManagerInstance.ReturnBoxcutter(this);
        }
        
        /// <summary>
        /// Schedules this debris object to return to pool after the specified delay.
        /// Used to allow debris to simulate physics for a period before cleanup.
        /// </summary>
        /// <param name="delay">Time in seconds before returning to pool</param>
        public void ReturnToPool(float delay)
        {
            returnTime = time + delay;
            returnScheduled = true;
        }
    }
}