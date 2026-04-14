using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    /// <summary>
    /// Manages object pooling for BoxCutter system components to optimize memory allocation.
    /// Handles creation, distribution, and return of pooled objects with automatic maintenance.
    /// </summary>
    [DefaultExecutionOrder(-2)]
    public class BoxCutterObjectPool : MonoBehaviour
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Singleton & Constants
        //─────────────────────────────────────────────────────────────────────────────────────
        public static BoxCutterObjectPool BoxCutterPoolInstance { get; private set; }
        public static Vector3 farAwayPos = new(-10000, -10000, -10000);

        private void Awake()
        {
            if (!Application.isPlaying) return;
            if (BoxCutterPoolInstance != null)
            {
                Destroy(gameObject);
                return;
            }

            BoxCutterPoolInstance = this;
            InitializeObjectPools();
        }

        /// <summary>
        /// Configuration settings for individual object pools.
        /// Defines pool behavior, maintenance rules, and parent hierarchy options.
        /// </summary>
        [Serializable]
        public class BoxCutterOPSetting
        {
            [ReadOnly] public int pooledOBJCount = 0;
            public Component obj;
            public BoxCutterPrefabEnum boxCutterPrefabType;
            public int poolSize;
            public Queue<Component> pooledCompList = new Queue<Component>();

            // Pool maintenance configuration
            public bool maintainPool;
            public int refillDuration = 2;
            public float objSpawnPerSecond;

            // Hierarchy and visibility options
            public bool canCustomOBJParent;
            public GameObject objParent;
            public bool alwaysActive;
        }

        public enum BoxCutterPrefabEnum
        {
            BoxcutterObj,
            FiftyBoxcutterBoxCollider,
            TenBoxcutterBoxCollider,
            FiveBoxcutterBoxCollider,
            TwoBoxcutterBoxCollider,
            OneBoxcutterBoxCollider,
            OneDebrisBox,
            MeshCollider
        }

        [Tooltip("List of object pool configurations for different BoxCutter component types")]
        public List<BoxCutterOPSetting> objectPoolList = new List<BoxCutterOPSetting>();
        public Dictionary<BoxCutterPrefabEnum, BoxCutterOPSetting> poolDictionary;

        /// <summary>
        /// Initializes all object pools based on configuration settings.
        /// Creates dictionary lookup and pre-populates pools with initial objects.
        /// </summary>
        public void InitializeObjectPools()
        {
            // Move pooler away from scene view for cleaner editor experience
            transform.position = farAwayPos;
            Physics.SyncTransforms();

            // Create fast lookup dictionary for pool settings
            poolDictionary = objectPoolList.ToDictionary(setting => setting.boxCutterPrefabType, setting => setting);
            
            // Initialize each pool with its configured objects
            foreach (var setting in objectPoolList)
            {
                CreatePooledObj(setting);
            }
        }

        private void CreatePooledObj(BoxCutterOPSetting setting)
        {
            int initialCount = setting.poolSize;
            for (int i = 0; i < initialCount; i++)
            {
                CreateObj(setting, false);
            }

            if (setting.maintainPool)
            {
                StartCoroutine(MaintainPoolCoroutine(setting));
            }
        }

        private IEnumerator MaintainPoolCoroutine(BoxCutterOPSetting setting)
        {
            while (true)
            {
                int missingCount = setting.poolSize - setting.pooledCompList.Count;

                if (missingCount > 0)
                {
                    float interval = setting.refillDuration / (float)missingCount;

                    interval = Mathf.Clamp(interval, 0.01f, 1f);

                    CreateObj(setting, false);

                    yield return new WaitForSeconds(interval);
                }
                else
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }
        }

        private Component CreateObj(BoxCutterOPSetting setting, bool callCreated)
        {
            Component newComp = Instantiate(setting.obj, setting.canCustomOBJParent ? setting.objParent.transform : transform);
            if (!setting.alwaysActive) newComp.gameObject.SetActive(false);
            newComp.transform.localPosition = vecZero;

            newComp.name = $"{setting.obj.name} | {setting.pooledCompList.Count}";
            if (!callCreated)
                setting.pooledCompList.Enqueue(newComp);

            return newComp;
        }

        /// <summary>
        /// Retrieves an object from the specified pool type.
        /// Creates a new object if the pool is empty.
        /// </summary>
        /// <param name="boxCutterPrefabType">Type of prefab to retrieve from pool</param>
        /// <returns>Available component from the pool</returns>
        public Component GetPooledObj(BoxCutterPrefabEnum boxCutterPrefabType)
        {
            var setting = GetSetting(boxCutterPrefabType);

            // Create new object if pool is exhausted
            if (setting.pooledCompList.Count == 0)
                return CreateObj(setting, true);

            return setting.pooledCompList.Dequeue();
        }

        public Component GetPooledObj(BoxCutterOPSetting setting)
        {
            if (setting.pooledCompList.Count == 0)
                return CreateObj(setting, true);

            return setting.pooledCompList.Dequeue();
        }

        public BoxCutterOPSetting GetSetting(BoxCutterPrefabEnum boxCutterPrefabType)
        {
            if (poolDictionary.TryGetValue(boxCutterPrefabType, out var setting))
            {
                return setting;
            }

            Debug.LogWarning($"[BoxCutter] Prefab of type {boxCutterPrefabType} not found in pool.");
            return null;
        }

        /// <summary>
        /// Returns an object to its designated pool for reuse.
        /// Resets object state and re-parents under the pooler hierarchy.
        /// </summary>
        /// <param name="comp">Component to return to pool</param>
        /// <param name="boxCutterPrefabType">Pool type to return the object to</param>
        public void ReturnObjToPool(Component comp, BoxCutterPrefabEnum boxCutterPrefabType)
        {
            var setting = GetSetting(boxCutterPrefabType);
            ReturnObjToPool(comp, setting);
        }

        private void ReturnObjToPool(Component comp, BoxCutterOPSetting setting)
        {
            if (!setting.alwaysActive) comp.gameObject.SetActive(false);
            comp.transform.parent = setting.canCustomOBJParent ? setting.objParent.transform : transform;
            setting.pooledCompList.Enqueue(comp);
        }

        private void Update()
        {
            RefreshSpawnPerSecond();
        }

        private void OnValidate()
        {
            RefreshSpawnPerSecond();
        }

        private void RefreshSpawnPerSecond()
        {
            foreach (BoxCutterOPSetting objectPool in objectPoolList)
            {
                objectPool.pooledOBJCount = objectPool.pooledCompList.Count;

                if (objectPool.maintainPool && objectPool.refillDuration != 0)
                {
                    int missingCount = objectPool.poolSize - objectPool.pooledCompList.Count;
                    if (missingCount > 0)
                    {
                        objectPool.objSpawnPerSecond = missingCount / (float)objectPool.refillDuration;
                    }
                    else
                    {
                        objectPool.objSpawnPerSecond = 0;
                    }
                }
            }
        }
    }
}