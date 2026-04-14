using System;
using UnityEngine;
using UnityEngine.Serialization;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    /// <summary>
    /// Manages a collection of box colliders for fragmentation and destruction purposes.
    /// Pre-generates colliders based on pooling configuration to optimize runtime performance.
    /// </summary>
    [ExecuteInEditMode]
    public class BoxColliderHolder : MonoBehaviour
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Object References
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("The BoxObj component this collider holder is associated with")]
        public BoxObj boxObj;
        [Tooltip("Reference to the main GameObject")]
        public GameObject gameObj;
        [Tooltip("Transform component reference for position calculations")]
        public Transform obj;
        [Tooltip("Array of pre-generated box collider transforms for performance optimization")]
        public Transform[] boxColObjArr = new Transform[]{};
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Pooling Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        [Space(10)]
        [Tooltip("If the holder will be be used as a mesh collider")]
        public bool isMeshCollider;
        [Tooltip("Type of prefab configuration to use for determining collider count")]
        public BoxCutterObjectPool.BoxCutterPrefabEnum boxCutterPrefabEnum;
        [Tooltip("Number of collider instances to pre-generate based on pooling configuration")]
        public int instanceAmount = 0;
        [Tooltip("Whether this object uses the pooling system for collider management")]
        public bool pooled = true;
        
        public MeshCollider meshCollider;

        /// <summary>
        /// Generates box colliders by destroying existing ones and creating new ones based on prefab configuration.
        /// Uses pooling system to determine the optimal number of colliders to pre-create.
        /// </summary>
        public void Generate()
        {
            if (isMeshCollider)
            {
                instanceAmount = 1;
                
                int boxLength = boxColObjArr.Length;

                for (int i = 0; i < boxLength; i++)
                {
                    if (boxColObjArr[i] != null)
                        DestroyImmediate(boxColObjArr[i].gameObject);
                }

                boxColObjArr = new Transform[] { };

                if (TryGetComponent(out meshCollider))
                {

                }
                else
                {
                    meshCollider = gameObject.AddComponent<MeshCollider>();
                }

                meshCollider.convex = true;
                return;
            }

            // Clear existing colliders to prevent accumulation
            BoxCollider[] foundBoxColliders = GetComponentsInChildren<BoxCollider>();
            
            int colliderLength = foundBoxColliders.Length;
            for (int i = 0; i < colliderLength; i++)
            {
                DestroyImmediate(foundBoxColliders[i].gameObject);
            }
            
            // Lookup optimal instance count from global configuration
            instanceAmount = boxColliderCapLookup[boxCutterPrefabEnum];
            
            gameObj = gameObject;
            obj = transform;
            boxColObjArr = new Transform[instanceAmount];
            
            // Pre-create colliders for performance optimization
            for (int i = 0; i < instanceAmount; i++)
            {
                GameObject boxColliderGo = new GameObject("BoxCollider");
                boxColliderGo.transform.parent = gameObj.transform;
                boxColliderGo.AddComponent<BoxCollider>();
                boxColObjArr[i] = boxColliderGo.transform;
            }
        }
    }
}