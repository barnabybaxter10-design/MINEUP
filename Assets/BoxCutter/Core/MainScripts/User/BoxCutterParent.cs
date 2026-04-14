using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace BoxCutter
{
    /// <summary>
    /// Parent container for managing multiple child BoxObj fragments.
    /// Automatically destroys itself when all child objects have been removed or destroyed.
    /// </summary>
    public class BoxCutterParent : MonoBehaviour
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Component References
        //─────────────────────────────────────────────────────────────────────────────────────
        public Rigidbody rb;
        public Transform obj;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Child Management
        //─────────────────────────────────────────────────────────────────────────────────────
        public BoxObj[] childrenBoxes = new BoxObj[] { };

        private bool hadChildren = false;

        private void Update()
        {
            int childCount = obj.childCount;

            //if (childCount > 0) hadChildren = true;
            
            // Self-cleanup when no children remain
            if (childCount == 0) //hadChildren && 
            {
                Destroy(gameObject);
            }
        }
    }
}