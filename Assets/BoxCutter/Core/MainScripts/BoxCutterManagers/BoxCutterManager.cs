using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine.Serialization;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.BoxCutterSpatialGrid;
using static BoxCutter.BoxCutterObjectPool;
using static BoxCutter.BoxCutterWorldPhysics;
using Debug = UnityEngine.Debug;
using static BoxCutter.BoxCutterCaller;
using static BoxCutter.DestructionTrigger;

namespace BoxCutter
{
    /// <summary>
    /// Central manager for the BoxCutter destruction system.
    /// Handles object pooling, voxel sizing, spatial management, and orchestrates the destruction pipeline.
    /// </summary>
    [ExecuteInEditMode]
    [DefaultExecutionOrder(-1)]
    public class BoxCutterManager : MonoBehaviour
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Singleton Instance
        //─────────────────────────────────────────────────────────────────────────────────────
        public static BoxCutterManager BoxCutterManagerInstance { get; private set; }
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               System Enumerations
        //─────────────────────────────────────────────────────────────────────────────────────
        public enum ConnectionStateEnum
        {
            //Uninitialized,
            Disconnected,
            Connected,
            Anchored,
        }

        public enum ColliderGenModeEnum
        {
            Auto,
            Coarse,
            Perfect,
            Smart,
        }

        public enum AsyncDestructionMode
        {
            Minimal,
            Core,
            Full,
        }
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Voxel Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Default voxel size for destruction calculations - smaller values create finer detail")]
        public float defSetVoxelSize = 1f;
        public static float defVoxelSize;
        public static float3 defVoxelSize3D;
        public static float defHalfVoxelSize;
        public static float3 defHalfVoxelSize3D;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Pooling Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Maps box collider prefab types to their pooling settings and capacity limits.
        /// Used to optimize collider generation based on expected fragment counts.
        /// </summary>
        public static (BoxCutterPrefabEnum prefab, BoxCutterOPSetting opSetting, int capacity)[] boxColliderHolderMap = 
        {
            (BoxCutterPrefabEnum.FiftyBoxcutterBoxCollider, new BoxCutterOPSetting(), 50),
            (BoxCutterPrefabEnum.TenBoxcutterBoxCollider, new BoxCutterOPSetting(), 10),
            (BoxCutterPrefabEnum.FiveBoxcutterBoxCollider, new BoxCutterOPSetting(), 5),
            (BoxCutterPrefabEnum.TwoBoxcutterBoxCollider, new BoxCutterOPSetting(), 2),
            (BoxCutterPrefabEnum.OneBoxcutterBoxCollider, new BoxCutterOPSetting(), 1)
        };

        /// <summary>
        /// Look up table for box collider prefab types and their capacity limits.
        /// </summary>
        public static readonly Dictionary<BoxCutterPrefabEnum, int> boxColliderCapLookup = boxColliderHolderMap.ToDictionary(x => x.prefab, x => x.capacity);
        
        /// <summary>
        /// Same type of map for boxColliderHolderMap, but for active box colliders in the OP.
        /// </summary>
        public static (BoxCutterPrefabEnum prefab, BoxCutterOPSetting opSetting, int capacity)[] activeBoxColliderHolderMap = new (BoxCutterPrefabEnum prefab, BoxCutterOPSetting opSetting, int capacity)[]{};

        public static BoxCutterPrefabEnum BoxPrefabEnum = BoxCutterPrefabEnum.BoxcutterObj;
        public static BoxCutterOPSetting BoxCutterOPSetting;
        
        public static BoxCutterPrefabEnum OneDebrisPrefabEnum = BoxCutterPrefabEnum.OneDebrisBox;
        public static BoxCutterOPSetting OneDebrisBoxOPSetting;
        
        private BoxCutterObjectPool pool;
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                                    Async Queue & Cancellation
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Global cancellation token source cancelled when play mode exits</summary>
        public CancellationTokenSource destroyCancellationTokenSource;

        //public List<int> processingIds = new List<int>();
        //public List<TriggerRequest> triggerRequests = new List<TriggerRequest>();

        //public List<BoxObj> queuedBoxes = new List<BoxObj>();
        
        public Action onDestroyFinish;
        public bool destroying;
        public List<BoxObj> currentFrameQueuedBoxes = new List<BoxObj>();
        
        public int queueId;
        public int callerId;
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                                    RB FREEZE
        //─────────────────────────────────────────────────────────────────────────────────────

        public List<FreezeData> freezeDataList = new List<FreezeData>();
        
        [Serializable]
        public class FreezeData
        {
            public BoxCutterParent ParentHolder;
            public Vector3 LastVelo;
            public Vector3 LastAngVelo;

            public string CalledBy;
        }
        
        //─────────────────────────────────────────────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
public const bool IsWebGL = true;
#else
        public const bool IsWebGL = false;
#endif

        public Extend.RenderPipelineType renderPipelineType;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Physics & Detection Settings
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Ignore diagonal connections when detecting attached islands for more stable physics")]
        public bool ignoreDiagonalsInIsland;
        [Tooltip("Maximum number of sub-objects allowed in each spatial grid cell for efficient pruning")]
        public int kdMaxSubInCell = 35;
        [Tooltip("The higher the ratio the tighter the cells are.")]
        public float validCellFillRatio = 0.5f;
        [Tooltip("When determining if two objects are connected, this distance is used to counter any floating point imprecision issues when determining if they are close enough to be considered connected")]
        public float connectionLeniencyDist = 0.01f;
        
        public static MeshColliderCookingOptions cookingOptions = MeshColliderCookingOptions.UseFastMidphase | MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices;
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Enables the destruction pipeline to be asynchronous")]
        public bool asyncDestruction = true;
        [Tooltip("Level of how much of the pipeline should be asynchronous")]
        public AsyncDestructionMode asyncDestructionMode;
        [Tooltip("Enforce a maximum amount of frames the destruction pipeline will use")]
        public int asyncDestructionMaxFrames = -1;
        [Tooltip("Enables the debris mesh and collider pipeline to be asynchronous")]
        public bool asyncDebris;
        [Tooltip("Enforce a maximum amount of frames the debris generation will use")]
        public int asyncDebrisMaxFrames = -1;
        [Tooltip("Maximum number debris that can spawn in one frame to spread the workload over multiple frames")]
        public int maxDebrisPerFrame = 150;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Island Return Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Automatically return fallen island fragments to their original positions")]
        public bool canReturnFallenIslands;
        public static bool CanReturnFallenIslands => BoxCutterManagerInstance.canReturnFallenIslands;
        [Tooltip("Time delay in seconds before fallen islands start returning to position")]
        [Range(1, 100)] public float returnFallenIslandDelay = 4;
        public static float ReturnFallenIslandDelay => BoxCutterManagerInstance.returnFallenIslandDelay;
        [Tooltip("Duration in seconds for the return animation of fallen islands")]
        [Range(1, 100)] public float returnFallenIslandDura = 15;
        public static float ReturnFallenIslandDura => BoxCutterManagerInstance.returnFallenIslandDura;

        [Tooltip("Enable to enforce a max amount of debris and islands that can be active in the scene")]
        public bool canMaxActiveDebrisAndIslands;
        public static int MaxActiveDebrisAndIslands => BoxCutterManagerInstance.maxActiveDebrisAndIslands;
        [Min(1)] public int maxActiveDebrisAndIslands = 1000;

        public List<BoxObj> activeDebris = new List<BoxObj>();
        public List<BoxCutterOneDebris> activeOneDebris = new List<BoxCutterOneDebris>();

        public int activeDebrisCount;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Debris Return Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Automatically return debris fragments to object pool after specified time")]
        public bool canReturnDebris;
        public static bool CanReturnDebris => BoxCutterManagerInstance.canReturnDebris;
        [Tooltip("Time delay in seconds before debris fragments are returned to pool")]
        [Range(0, 1000)] public float returnDebrisDelay = 7;
        public static float ReturnDebrisDelay => BoxCutterManagerInstance.returnDebrisDelay;
        [Tooltip("Duration in seconds for the debris cleanup process")]
        [Range(0, 1000)] public float returnDebrisDura = 4;
        public static float ReturnDebrisDura => BoxCutterManagerInstance.returnDebrisDura;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Runtime State Tracking
        //─────────────────────────────────────────────────────────────────────────────────────
        [ReadOnly] public int boxAllUniqueID = 1;
        [ReadOnly] public int boxCutterCurrentFallTurn = 0;
        [ReadOnly] public int boxGroupId = 0;
        [ReadOnly] public int oneDebrisUniqueID = 1;
        
        public Transform obj;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Debug & Utility
        //─────────────────────────────────────────────────────────────────────────────────────
        public static List<BoxCutterDebugBox> boxcutterDebugObjs = new List<BoxCutterDebugBox>();
        public Transform blankObj;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Time Management
        //─────────────────────────────────────────────────────────────────────────────────────
        public static float time;
        public static float deltaTime;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Visualizer Data
        //─────────────────────────────────────────────────────────────────────────────────────
        [Serializable]
        public class CallerVisualizerData
        {
            public CallerData callerData;
            public BoxObj[] boxObjArr;
        }
        
        public List<CallerVisualizerData> callerVisualizerDataList = new List<CallerVisualizerData>();

        // Keeps original state for the current visualize pass (edit mode).
        
        private readonly Dictionary<BoxObj, BoxStateSnap> _restoreMap =
            new Dictionary<BoxObj, BoxStateSnap>(64);

#if UNITY_EDITOR
        private bool visualizing; // reentrancy guard for OnDrawGizmos/visualizer
#endif

        private sealed class BoxStateSnap
        {
            public readonly List<SubBox> AllSubs;
            public readonly float SizeX, SizeY, SizeZ;

            public BoxStateSnap(BoxObj b)
            {
                SizeX = b.sizeX; SizeY = b.sizeY; SizeZ = b.sizeZ;

                var src = b.allSubList;
                int n = (src != null) ? src.Count : 0;
                AllSubs = (n > 0) ? new List<SubBox>(n) : new List<SubBox>();

                for (int i = 0; i < n; i++)
                {
                    var s = src[i];
                    AllSubs.Add(new SubBox
                    {
                        minX = s.minX, maxX = s.maxX,
                        minY = s.minY, maxY = s.maxY,
                        minZ = s.minZ, maxZ = s.maxZ,
                        magicaIndex = s.magicaIndex
                    });
                }
            }

            public void Restore(BoxObj b)
            {
                b.allSubList = AllSubs;
                b.sizeX = SizeX; b.sizeY = SizeY; b.sizeZ = SizeZ;
            }
        }

        private void SaveSnapshotIfNeeded(BoxObj b)
        {
            if (b != null && !_restoreMap.ContainsKey(b))
                _restoreMap.Add(b, new BoxStateSnap(b));
        }
        
        void OnEnable()
        {
            SetUpSingleton();
        }

        /// <summary>
        /// Ensures only one BoxCutterManager instance exists in the scene.
        /// Destroys duplicate instances and initializes the singleton reference.
        /// </summary>
        private void SetUpSingleton()
        {
            boxcutterDebugObjs.Clear();
            
            // Enforce singleton pattern
            if (BoxCutterManagerInstance != null && BoxCutterManagerInstance != this)
            {
                if (!Application.isPlaying)
                    DestroyImmediate(gameObject);
                else
                    Destroy(gameObject);
                return;
            }
            
            BoxCutterManagerInstance = this;
        }

        private void Awake()
        {
            boxcutterDebugObjs.Clear();

            // Initialize runtime systems
            if (Application.isPlaying)
            {
                // Create cancellation token source for async operations
                destroyCancellationTokenSource = new CancellationTokenSource();

                Init();
                blankObj = new GameObject("BlankGameObj").transform;
                blankObj.parent = obj;

                renderPipelineType = Extend.GetCurrentRenderPipeline();
            }
        }

        private void Start()
        {
            if (!IsPlaying) RestoreBoxes();
        }

        /// <summary>
        /// Initializes the BoxCutter system during runtime startup.
        /// Sets up object pooling, voxel calculations, and validates configuration.
        /// </summary>
        private void Init()
        {
            if (!IsPlaying) return;
            
            obj = transform;
            obj.position = vecZero;
            
            CalcVoxelSizes();
            
            // Locate and validate object pooler
            BoxCutterObjectPool[] poolers = FindObjectsByType<BoxCutterObjectPool>(FindObjectsSortMode.None);
            
            if (poolers.Length == 0)
            {
                Debug.LogError("[BoxCutter] No BoxCutterObjectPoolers found. At least one is required.");
                return;
            }
            
            if (poolers.Length > 1)
            {
                Debug.LogError("[BoxCutter] There cannot be more than 1 BoxCutterObjectPoolers.");
            }
            
            pool = poolers[0];
            
            // Initialize pooler if needed
            if (pool.poolDictionary == null || pool.poolDictionary.Count == 0)
            {
                pool.InitializeObjectPools();
            }
            
            // Cache pooling settings for quick access
            BoxCutterOPSetting = pool.GetSetting(BoxPrefabEnum);
            OneDebrisBoxOPSetting = pool.GetSetting(OneDebrisPrefabEnum);
            // Update box collider holder map with actual pooling settings
            int mapLength = boxColliderHolderMap.Length;
            for (int i = 0; i < mapLength; i++)
            {
                BoxCutterPrefabEnum prefabEnum = boxColliderHolderMap[i].prefab;
                int cap = boxColliderHolderMap[i].capacity;
                BoxCutterOPSetting actualOpSetting = pool.GetSetting(prefabEnum);
                
                boxColliderHolderMap[i] = (prefabEnum, actualOpSetting, cap);
            }
            
            // Build active map
            List<BoxCutterOPSetting> opSettings = BoxCutterPoolInstance.objectPoolList;
            int opSettingsLength = opSettings.Count;
            int activeIndex = 0;
            activeBoxColliderHolderMap = new (BoxCutterPrefabEnum, BoxCutterOPSetting, int)[mapLength];
            for (int i = 0; i < opSettingsLength; i++)
            {
                BoxCutterPrefabEnum prefabEnum = opSettings[i].boxCutterPrefabType;
                BoxCutterOPSetting opSetting = opSettings[i];

                foreach (var boxColliderHolder in boxColliderCapLookup)
                {
                    if (boxColliderHolder.Key == prefabEnum)
                    {
                        activeBoxColliderHolderMap[activeIndex++] = (prefabEnum, opSetting, boxColliderHolder.Value);
                        break;
                    }
                }
            }
            
            Array.Resize(ref activeBoxColliderHolderMap, activeIndex);
            if (activeIndex == 0)
            {
                Debug.LogWarning("[BoxCutter] There are no active BoxColliderHolders in your Object Pooler.");
            }
        }

        /// <summary>
        /// Calculates and caches voxel size values used throughout the destruction system.
        /// Precomputes commonly used values to avoid repeated calculations during runtime.
        /// </summary>
        private void CalcVoxelSizes()
        {
            defVoxelSize = defSetVoxelSize;
            defVoxelSize3D = new float3(defVoxelSize, defVoxelSize, defVoxelSize);
            defHalfVoxelSize = defVoxelSize * 0.5f;
            defHalfVoxelSize3D = new float3(defHalfVoxelSize, defHalfVoxelSize, defHalfVoxelSize);
        }

        /// <summary>
        /// Retrieves a BoxObj from the object pool and assigns it a unique identifier.
        /// Registers the object with the manager's tracking systems for lifecycle management.
        /// </summary>
        /// <returns>A properly initialized BoxObj ready for use</returns>
        public BoxObj GetBoxObj()
        {
            BoxObj newBoxObj = (BoxObj)pool.GetPooledObj(BoxCutterOPSetting);
            
            newBoxObj.pooled = false;
            
            return newBoxObj;
        }

        /// <summary>
        /// Returns a BoxObj to the object pool after cleanup.
        /// Removes the object from tracking systems and performs necessary cleanup operations.
        /// </summary>
        /// <param name="boxObj">The BoxObj to return to the pool</param>
        public void ReturnBoxcutter(BoxObj boxObj)
        {
            boxObj.ReturnToPool();
        }

        /// <summary>
        /// Retrieves an BoxCutterOneDebris from the object pool
        /// </summary>
        /// <returns>A properly initialized BoxCutterOneDebris ready for use</returns>
        public BoxCutterOneDebris GetOneDebris()
        {
            BoxCutterOneDebris oneDebris = (BoxCutterOneDebris)pool.GetPooledObj(OneDebrisBoxOPSetting);
            oneDebris.uniqueId = oneDebrisUniqueID++;
            oneDebris.pooled = false;
            RegisterActiveDebris(oneDebris);
            return oneDebris;
        }

        #region ENFORCE MAX DEBRIS

        public void RegisterActiveDebris(BoxObj box)
        {
            if (!canMaxActiveDebrisAndIslands) return;
            RemoveNullActiveDebris();
            RemoveOldestDebris();
            activeDebris.Add(box);
        }
        
        public void RegisterActiveDebris(BoxCutterOneDebris box)
        {
            if (!canMaxActiveDebrisAndIslands) return;
            RemoveNullActiveDebris();
            RemoveOldestDebris();
            activeOneDebris.Add(box);
        }

        private void RemoveOldestDebris()
        {
            int totalDebrisCount = activeDebris.Count + activeOneDebris.Count;

            if (totalDebrisCount < maxActiveDebrisAndIslands) return;

            // The return to pools unregister the debris as well
            if (activeOneDebris.Count > 0)
                activeOneDebris[0].ReturnToPool();
            else
                activeDebris[0].ReturnToPool();
        }

        private void RemoveNullActiveDebris()
        {
            // Too expensive, commented out
            /*int debrisCount = activeDebris.Count;
            
            // Remove Oldest
            if (debrisCount >= maxActiveDebrisAndIslands)
            {
                // Remove Nulls
                int startIdx = debrisCount - 1;
                for (int i = startIdx; i >= 0; i--)
                {
                    var nullData = activeDebris[i];
                    if (nullData.isOne)
                    {
                        if (nullData.oneDebris == null)
                        {
                            activeDebris.RemoveAt(i);
                            debrisCount--;
                        }
                    }
                    else
                    {
                        if (nullData.box == null)
                        {
                            activeDebris.RemoveAt(i);
                            debrisCount--;
                        }
                    }
                }
            }*/
        }
        
        public void UnRegisterActiveDebris(BoxObj box)
        {
            if (!canMaxActiveDebrisAndIslands) return;
            
            int debrisCount = activeDebris.Count;

            for (int i = 0; i < debrisCount; i++)
            {
                //Null check too expensive, re-add "data.box != null" if needed
                if (activeDebris[i].uniqueId == box.uniqueId)
                {
                    activeDebris.RemoveAt(i);
                    return;
                }
            }
        }
        
        public void UnRegisterActiveDebris(BoxCutterOneDebris box)
        {
            if (!canMaxActiveDebrisAndIslands) return;
            
            int debrisCount = activeOneDebris.Count;

            for (int i = 0; i < debrisCount; i++)
            {
                //Null check too expensive, re-add "data.oneDebris != null" if needed
                if (activeOneDebris[i].uniqueId == box.uniqueId)
                {
                    activeOneDebris.RemoveAt(i);
                    return;
                }
            }
        }

        public void RemoveExcessDebris()
        {
            RemoveNullActiveDebris();

            int debrisCount = activeDebris.Count + activeOneDebris.Count;

            if (debrisCount <= maxActiveDebrisAndIslands) return;

            int removeAmount = debrisCount - maxActiveDebrisAndIslands;
            
            for (int i = 0; i < removeAmount; i++)
            {
                RemoveOldestDebris();
            }
        }
        #endregion
        
        /// <summary>
        /// Returns an OneDebris to the object pool.
        /// Removes the object from tracking systems and performs necessary cleanup operations.
        /// </summary>
        /// <param name="oneDebris">The oneDebris to return to the pool</param>
        public void ReturnBoxcutter(BoxCutterOneDebris oneDebris)
        {
            if (destroyCancellationToken.IsCancellationRequested) return;
            oneDebris.rb.isKinematic = true;
            UnRegisterActiveDebris(oneDebris);
            oneDebris.uniqueId = -1;
            oneDebris.pooled = true;
            BoxCutterPoolInstance.ReturnObjToPool(oneDebris, OneDebrisPrefabEnum);
        }
        
        public void ReturnColliderHolder(BoxColliderHolder holder)
        {
            if (destroyCancellationToken.IsCancellationRequested) return;
            BoxCutterPoolInstance.ReturnObjToPool(holder, holder.boxCutterPrefabEnum);
         
            holder.boxObj = null;
            holder.pooled = true;
            holder.obj.position = farAwayPos;

            if (holder.isMeshCollider)
            {
                holder.meshCollider.sharedMesh = null;
                holder.meshCollider.convex = false;
            }
        }

        public void FreezeParent(BoxObj box, [CallerMemberName] string callerName = null)
        {
            BoxCutterParent parentHolder = box.parentHolder;
            
            if (parentHolder == null) return;
            
            RemoveNullFreezeParents();
            
            int freezeCount = freezeDataList.Count;

            for (int i = 0; i < freezeCount; i++)
            {
                FreezeData freezeData = freezeDataList[i];

                // Already frozen, return
                if (freezeData.ParentHolder == parentHolder)
                    return;
            }
            
            freezeDataList.Add(new FreezeData
            {
                ParentHolder = parentHolder,
                LastVelo = parentHolder.rb.linearVelocity,
                LastAngVelo = parentHolder.rb.angularVelocity,
                CalledBy = callerName
            });

            parentHolder.rb.isKinematic = true;
        }

        public void UnFreezeParent(BoxObj box, [CallerMemberName] string callerName = null)
        {
            BoxCutterParent parentHolder = box.parentHolder;
            
            if (parentHolder == null) return;

            RemoveNullFreezeParents();
            
            int freezeCount = freezeDataList.Count;
            int freezeCountMinusOne = freezeCount - 1;

            for (int i = freezeCountMinusOne; i >= 0; i--)
            {
                FreezeData freezeData = freezeDataList[i];
                
                if (freezeData.ParentHolder == parentHolder && freezeData.CalledBy == callerName)
                {
                    parentHolder.rb.isKinematic = false;
                    parentHolder.rb.linearVelocity = freezeData.LastVelo;
                    parentHolder.rb.angularVelocity = freezeData.LastAngVelo;
                    
                    freezeDataList.RemoveAt(i);
                    
                    return;
                }
            }
        }

        private void RemoveNullFreezeParents()
        {
            int freezeCount = freezeDataList.Count;
            int freezeCountMinusOne = freezeCount - 1;

            for (int i = freezeCountMinusOne; i >= 0; i--)
            {
                FreezeData freezeData = freezeDataList[i];
                if (freezeData.ParentHolder == null)
                {
                    freezeDataList.RemoveAt(i);
                }
            }
        }
        
        public async Task SetKineToFalse(IslandData[] islandDataArr, bool[] sameIslandArr, Vector3[] lastVeloArr, CallerData callerData)
        {
            int islandArr = islandDataArr.Length;

            if (islandArr == 0)
                return;

            await Task.Yield();

            HashSet<Rigidbody> uniqueParents = new HashSet<Rigidbody>();
            for (int i = 0; i < islandArr; i++)
            {
                bool sameIsland = sameIslandArr[i];
                BoxObj[] boxcutterObjs = islandDataArr[i].boxes;
                Vector3 lastVelo = lastVeloArr[i];

                int boxLength = boxcutterObjs.Length;
                for (int v = 0; v < boxLength; v++)
                {
                    BoxObj compareBox = boxcutterObjs[v];

                    // Because it waits a frame it is possible that the box has already been destroyed
                    if (compareBox.connectionState != ConnectionStateEnum.Disconnected || compareBox.parentHolder == null) continue;

                    Rigidbody boxParent = compareBox.parentHolder.rb;

                    if (!uniqueParents.Contains(boxParent))
                    {
                        if (!sameIsland)
                        {
                            boxParent.isKinematic = false;
                            boxParent.linearVelocity = lastVelo * 0.85f;
                        }
                        
                        Vector3 forcePos = callerData.forcePos;
                        float2 forceRange = callerData.forceRange;
                        float force = callerData.force;

                        if (callerData.canSpawnDebris)
                        {
                            Vector3 boxPos = boxParent.position;
                            Vector3 forceDir = new Vector3(boxPos.x - forcePos.x, boxPos.y - forcePos.y, boxPos.z - forcePos.z);

                            ApplyForce(boxParent, forceDir, forceRange, force);
                        }

                        uniqueParents.Add(boxParent);
                    }
                }
            }
        }

        /// <summary>
        /// Applies impulse force to a rigidbody with mass-scaled calculations.
        /// Force scaling ensures consistent behavior across objects of different masses.
        /// </summary>
        /// <param name="rb">Target rigidbody to apply force to</param>
        /// <param name="forceDir">Direction vector for the force application</param>
        /// <param name="randomRange">Random min max range for force</param>
        /// <param name="force">Base force magnitude</param>
        /// <param name="applyForce">Optional parameter to not actually apply the force.</param>
        /// <param name="mass">Optional parameter to specify the mass of the object.</param>
        public static Vector3 ApplyForce(Rigidbody rb, Vector3 forceDir, float2 randomRange, float force, bool applyForce = true, float mass = 1f)
        {
            float massTo = mass;
            if (applyForce) massTo = rb.mass;
            Vector3 finalForce = force * math.sqrt(massTo) * 1.25f * UnityEngine.Random.Range(randomRange.x, randomRange.y) * forceDir.normalized;
            if (applyForce) rb.AddForce(finalForce, ForceMode.Impulse);
            return finalForce;
        }

        /// <summary>
        /// Calculates the mass of a single debris object based on its dimensions and voxel size.
        /// </summary>
        /// <param name="sizeX">The world size of the debris object in the X dimension</param>
        /// <param name="sizeY">The world size of the debris object in the Y dimension</param>
        /// <param name="sizeZ">The world size of the debris object in the Z dimension</param>
        /// <param name="voxelSize">The size of a single voxel in the voxel grid</param>
        /// <param name="massFac">The mass multiplier for the debris object</param>
        /// <returns></returns>
        public static float CalcOneDebrisMass(float sizeX, float sizeY, float sizeZ, float voxelSize, float massFac)
        {
            return (sizeX / voxelSize * sizeY / voxelSize * sizeZ / voxelSize) * voxelSize * massFac;
        }
        
        #region UPDATE

        public void Update()
        {
            if (IsPlaying)
            {
                // Cache time values for static access throughout the system
                time = Time.time;
                deltaTime = Time.deltaTime;

                if (canMaxActiveDebrisAndIslands)
                {
                    activeDebrisCount = activeDebris.Count + activeOneDebris.Count;
                    RemoveExcessDebris();
                }
                
                // Run queued destruction triggers
                //RunTriggerRequests();
                
                return;
            }
            
            // Recalculate voxel sizes during edit mode for real-time updates
            CalcVoxelSizes();
        }

        /*private void RunTriggerRequests()
        {
            int requestCount = triggerRequests.Count;
            if (requestCount == 0) return;

            List<int> removeIndexes = new List<int>(requestCount);

            for (int i = 0; i < requestCount; i++)
            {
                TriggerRequest triggerRequest = triggerRequests[i];

                // If there is no caller data or no ogCaller, drop it from the queue
                var callerData = triggerRequest.CallerData;
                var ogCaller = callerData?.ogCaller;
                if (ogCaller == null)
                {
                    removeIndexes.Add(i);
                    continue;
                }

                // Drop canceled callers (don’t keep them in the queue)
                var token = ogCaller.destroyCancellationTokenSource?.Token ?? CancellationToken.None;
                if (token.IsCancellationRequested)
                {
                    removeIndexes.Add(i);
                    continue;
                }

                bool anyBusy = false;
                int pendingCount = triggerRequest.ValidInheritIds.Count;
                for (int v = 0; v < pendingCount; v++)
                {
                    if (processingIds.Contains(triggerRequest.ValidInheritIds[v]))
                    {
                        anyBusy = true;
                        break;
                    }
                }

                if (anyBusy) continue;

                TriggerDestruction(triggerRequest.CallerData, triggerRequest.ValidInheritIds);
                removeIndexes.Add(i);
            }

            for (int i = removeIndexes.Count - 1; i >= 0; i--)
                triggerRequests.RemoveAt(removeIndexes[i]);
        }*/

        public bool CheckExit()
        {
            return destroyCancellationTokenSource?.IsCancellationRequested == true;
        }

        #endregion

        #region UTILS

        /// <summary>
        /// Utility method for performance profiling of destruction pipeline passes.
        /// Logs execution time and restarts the stopwatch for continuous measurement.
        /// </summary>
        /// <param name="stopwatch">Stopwatch instance tracking execution time</param>
        /// <param name="passName">Name of the pipeline pass being measured</param>
        public static void PrintPassMs(Stopwatch stopwatch, string passName)
        {
            float ms = stopwatch.ElapsedMilliseconds;
            if (ms != 0) Debug.Log($"[Boxcutter] {passName}: {ms} ms");
            stopwatch.Restart();
        }

        public static async Task WaitJobComplete(JobHandle handle, bool sync, int maxFrames = -1)
        {
            if (sync || IsWebGL)
            {
                handle.Complete();
                return;
            }
            
            int frameCount = 0;
            
            while (!handle.IsCompleted)
            {
                await Task.Yield();
                frameCount++;
                if (maxFrames > 0 && frameCount >= maxFrames)
                {
                    handle.Complete();
                    return;
                }
            }

            handle.Complete();
        }

        #endregion

        /// <summary>
        /// Renders debug visualization boxes in the scene view.
        /// Supports both standard Unity gizmos and enhanced custom rendering.
        /// </summary>
        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            Color ogColor = Gizmos.color;
            Matrix4x4 ogMatrix = Gizmos.matrix;
            
            DrawDebugBoxes();
            DrawVisualizeBoxes();
            
            // Restore original gizmo state
            Gizmos.color = ogColor;
            Gizmos.matrix = ogMatrix;
#endif
        }

        private void DrawDebugBoxes()
        {
            int boxcutterDebugObjCount = boxcutterDebugObjs.Count;
            for (int i = 0; i < boxcutterDebugObjCount; i++)
            {
                BoxCutterDebugBox boxCutterDebugBox = boxcutterDebugObjs[i];
                
                // Use enhanced rendering for better visual quality when enabled
                if (boxCutterDebugBox.enhanceGizmos)
                {
                    BoxCutterGizmosUtil.DrawBox(boxCutterDebugBox.color, boxCutterDebugBox.pos, boxCutterDebugBox.rot, boxCutterDebugBox.size);
                    continue;
                }
                
                // Standard gizmo rendering
                Gizmos.color = boxCutterDebugBox.color;
                Gizmos.matrix = Matrix4x4.TRS(boxCutterDebugBox.pos, boxCutterDebugBox.rot, vecOne);
                Gizmos.DrawWireCube(vecZero, boxCutterDebugBox.size);
            }
        }

        #region VISUALIZATION

        private void DrawVisualizeBoxes()
        {
            int callerCount = callerVisualizerDataList.Count;

            if (!IsPlaying && callerCount != 0)
            {
#if UNITY_EDITOR
                if (visualizing) return; // avoid re-entrancy in editor
                visualizing = true;
#endif
                try
                {
                    Dictionary<BoxObj, int> amountOfBoxes = new Dictionary<BoxObj, int>();

                    // Normalize inputs; also snapshot boxes up-front so we can restore later.
                    for (int i = 0; i < callerCount; i++)
                    {
                        CallerVisualizerData callerVisualizerData = callerVisualizerDataList[i];
                        callerVisualizerData.boxObjArr = callerVisualizerData.boxObjArr.Where(b => b != null).ToArray();
                        int visualBoxObjLength = callerVisualizerData.boxObjArr.Length;

                        for (int v = 0; v < visualBoxObjLength; v++)
                        {
                            BoxObj box = callerVisualizerData.boxObjArr[v];
                            SaveSnapshotIfNeeded(box);
                            amountOfBoxes[box] = amountOfBoxes.ContainsKey(box) ? amountOfBoxes[box] + 1 : 1;
                        }
                    }

                    Dictionary<BoxObj, int> currentAmountOfBoxes = new Dictionary<BoxObj, int>();
                    Dictionary<BoxObj, List<SubBox>> processedAlready = new Dictionary<BoxObj, List<SubBox>>();

                    for (int i = 0; i < callerCount; i++)
                    {
                        List<List<SubBox>> tempVisualizeBoxList = new List<List<SubBox>>();
                        CallerVisualizerData callerVisualizerData = callerVisualizerDataList[i];
                        int visualBoxObjLength = callerVisualizerData.boxObjArr.Length;

                        for (int v = 0; v < visualBoxObjLength; v++)
                        {
                            BoxObj box = callerVisualizerData.boxObjArr[v];

                            SaveSnapshotIfNeeded(box);

                            int allSubObjCount = box.allSubList.Count;
                            List<SubBox> ogSubs;
                            if (!processedAlready.TryGetValue(box, out ogSubs))
                            {
                                ogSubs = new List<SubBox>(allSubObjCount);
                                for (int j = 0; j < allSubObjCount; j++)
                                {
                                    SubBox sub = box.allSubList[j];
                                    ogSubs.Add(new SubBox
                                    {
                                        minX = sub.minX,
                                        maxX = sub.maxX,
                                        minY = sub.minY,
                                        maxY = sub.maxY,
                                        minZ = sub.minZ,
                                        maxZ = sub.maxZ,
                                        magicaIndex = sub.magicaIndex,
                                    });
                                }

                                processedAlready.Add(box, ogSubs);
                            }

                            tempVisualizeBoxList.Add(ogSubs);

                            Extend.RefreshBoxObjInEditor(box);
                        }

                        var destructData = DestructionPipeline.Destroy(callerVisualizerData.boxObjArr, callerVisualizerData.callerData, overrideSync:true);
                        bool canPrintMs = callerVisualizerData.callerData.canPrintMs;

                        for (int v = 0; v < visualBoxObjLength; v++)
                        {
                            BoxObj box = callerVisualizerData.boxObjArr[v];

                            SubBox[][] mainIslandSubs = destructData.Result.Item1[v];
                            SubBox[][] debrisIslandSubs = destructData.Result.Item2[v];

                            if (canPrintMs) Debug.Log("[BoxCutter] Main Island Amount: " + mainIslandSubs.Length + " | Debris Island Amount: " + debrisIslandSubs.Length);

                            int currentAmount = 1;
                            if (currentAmountOfBoxes.TryGetValue(box, out int amount))
                            {
                                currentAmount = amount + 1;
                                currentAmountOfBoxes[box] = currentAmount;
                            }
                            else
                            {
                                currentAmountOfBoxes.Add(box, currentAmount);
                            }

                            // Combine main and debris island subs together
                            int mainSubLength = mainIslandSubs.Length;
                            int debrisSubLength = debrisIslandSubs.Length;
                            SubBox[][] combinedIslandSubs = new SubBox[mainSubLength + debrisSubLength][];
                            Array.Copy(mainIslandSubs, 0, combinedIslandSubs, 0, mainSubLength);
                            Array.Copy(debrisIslandSubs, 0, combinedIslandSubs, mainSubLength, debrisSubLength);

                            // Create flattened 1D SubBox list from combinedIslandSubs
                            List<SubBox> flattenedSubBoxList = new List<SubBox>();
                            for (int islandIndex = 0; islandIndex < combinedIslandSubs.Length; islandIndex++)
                            {
                                SubBox[] island = combinedIslandSubs[islandIndex];
                                for (int subIndex = 0; subIndex < island.Length; subIndex++)
                                {
                                    flattenedSubBoxList.Add(island[subIndex]);
                                }
                            }

                            // Here so subsequent callers see prior mutations
                            box.allSubList = flattenedSubBoxList;

                            if (currentAmount == amountOfBoxes[box])
                            {
                                DrawIslandSubs(combinedIslandSubs, box);
                            }

                            if (box.magicaVoxelData == null)
                            {
                                box.sizeX = 0;
                                box.sizeY = 0;
                                box.sizeZ = 0;
                            }
                        }
                    }
                }
                finally
                {
                    // Always restore, even if anything above throws or re-enters.
                    RestoreBoxes();
#if UNITY_EDITOR
                    visualizing = false;
#endif
                }
            }
        }


        private void RestoreBoxes()
        {
            // Only used for the edit-mode visualizer
            if (IsPlaying) return;

            if (_restoreMap.Count > 0)
            {
                foreach (var kv in _restoreMap)
                {
                    var box = kv.Key;
                    if (box == null) continue; // could have been deleted in editor
                    kv.Value.Restore(box);
                }
                _restoreMap.Clear();
            }

            // Visualizer requests are one-shot; clear them after restoration.
            if (callerVisualizerDataList.Count > 0)
                callerVisualizerDataList.Clear();
        }
        
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InstallEditorGuards()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => BoxCutterManagerInstance?.RestoreBoxes();
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode || s == PlayModeStateChange.ExitingPlayMode)
                    BoxCutterManagerInstance?.RestoreBoxes();
            };
        }
#endif

        private void OnDisable()
        {
            // Cancel all async operations when play mode exits
            destroyCancellationTokenSource?.Cancel();
            destroyCancellationTokenSource?.Dispose();
            destroyCancellationTokenSource = null;

            RestoreBoxes();
        }

        private void OnDestroy()
        {
            // Cancel all async operations when manager is destroyed
            destroyCancellationTokenSource?.Cancel();
            destroyCancellationTokenSource?.Dispose();
            destroyCancellationTokenSource = null;

            RestoreBoxes();
        }
        
        public void DrawIslandSubs(SubBox[][] subObjs, BoxObj box)
        {
            int subIslandLength = subObjs.Length;
            Quaternion quatRot = new Quaternion(box.qx, box.qy, box.qz, box.qw);

            for (int i = 0; i < subIslandLength; i++)
            {
                SubBox[] subs = subObjs[i];
                int subInIslandLength = subs.Length;

                Color colorTo = GetColorForGroup(i);
                Gizmos.color = colorTo;

                for (int v = 0; v < subInIslandLength; v++)
                {
                    SubBox subBox = subs[v];

                    box.GetSubObjPositionAndSize(subBox.minX, subBox.minY, subBox.minZ, subBox.maxX, subBox.maxY, subBox.maxZ, out float subPosX, out float subPosY, out float subPosZ, out float subSizeX, out float subSizeY, out float subSizeZ);

                    Vector3 posTo = new Vector3(subPosX, subPosY, subPosZ);
                    Vector3 sizeTo = new Vector3(subSizeX, subSizeY, subSizeZ);
                    
                    BoxCutterGizmosUtil.DrawBox(colorTo, posTo, quatRot, sizeTo);
                }
            }
        }

        public Color GetColorForGroup(int groupIndex)
        {
            if (groupIndex >= 0 && groupIndex < fragDebugColors.Length)
                return fragDebugColors[groupIndex];

            float hue = (groupIndex * 0.6180339887f) % 1f;
            return Color.HSVToRGB(hue, 0.5f, 1.0f);
        }

        #endregion
    }
}