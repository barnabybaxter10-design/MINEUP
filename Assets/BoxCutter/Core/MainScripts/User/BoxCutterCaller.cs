using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Serialization;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.BoxObj;
using Debug = UnityEngine.Debug;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxIntersectWithBoxJob;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;
using static BoxCutter.BoxCutterWorldPhysics;

namespace BoxCutter
{
    /// <summary>
    /// Main entry point for triggering BoxCutter destruction events.
    /// Handles collision detection, force application, and coordinates the entire destruction pipeline.
    /// </summary>
    [ExecuteInEditMode]
    [DefaultExecutionOrder(1)]
    public class BoxCutterCaller : MonoBehaviour
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Debug & Visualization
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Print millisecond timing information for performance debugging")]
        public bool canPrintMs;
        
        [Tooltip("Enable visualization of destruction process in Scene view")]
        public bool visualizeDestruction = false;
        [Tooltip("Array of BoxObj components to visualize during destruction preview")]
        public BoxObj[] visualizeBoxArr = new BoxObj[] { };
        [SerializeField] private List<List<SubBox>> ogVisualizeSubList = new List<List<SubBox>>();
        [Tooltip("Show gizmos in Scene view to visualize destruction area")]
        public bool canShowGizmos = true;
        [Tooltip("Alpha transparency value for gizmo rendering (0-1)")]
        public float gizmosAlpha = 0.5f;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Impact Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        
        [Tooltip("Position offset from the caller's transform for destruction center")]
        public Vector3 posOffset;
        [Tooltip("Use box-shaped destruction area instead of spherical")]
        public bool boxMode;
        [Tooltip("Automatically set box bounds from transform's local scale")]
        public bool boxBoundsFromTrans;
        [Tooltip("Size of the box destruction area (only used when boxMode is true)")]
        public float3 boxBounds;
        [Tooltip("Automatically set box rotation from transform's rotation")]
        public bool rotFromTrans;
        [Tooltip("Euler angles for box rotation (only used when boxMode is true)")]
        public float3 boxRot;
        [Tooltip("Radius of spherical destruction area (only used when boxMode is false)")]
        public float radius = 2f;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Physics & Force Settings
        //─────────────────────────────────────────────────────────────────────────────────────
        
        [Tooltip("Allow spawning of debris fragments during destruction")]
        public bool canSpawnDebris = true;
        [Tooltip("Force multiplier applied to spawned debris for physics simulation")]
        public float spawnForce = 1.5f;
        [Tooltip("Force multiplier range for non uniform force")]
        public float2 forceRange = new float2(0.75f, 1.25f);
        [Tooltip("Use custom force application point instead of destruction center")]
        public bool canForceOffset;
        [Tooltip("Apply force offset in world space instead of local space")]
        public bool worldForceOffset;
        [Tooltip("Local or world space offset for force application point")]
        public Vector3 forceOffset;
        public Vector3 forceOffsetPos;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Filtering & Randomization
        //─────────────────────────────────────────────────────────────────────────────────────
        
        [Tooltip("Use whitelist mode - only objects in whitelist will be affected")]
        public bool useWhiteList = false;
        [Tooltip("BoxObj components to exclude from destruction even if within impact area")]
        public List<BoxObj> blackListedObjs = new List<BoxObj>();
        public List<int> blackListedIds = new List<int>();
        [Tooltip("BoxObj components to include in destruction (whitelist mode only)")]
        public List<BoxObj> whiteListedObjs = new List<BoxObj>();
        public List<int> whiteListedIds = new List<int>();
        [Tooltip("By default all fragment types use normalization to ensure each type roughly spawns the same number of fragments, turning this off will prevent that and dramatically increase the number of fragments spawned")]
        public bool turnOffStrengthNormalizer;
        [Tooltip("Falloff factor controlling how destruction strength decreases with distance")]
        public float strength;
        [Tooltip("Use random seed for fragmentation instead of fixed seed")]
        public bool canRandomSeed;
        [Tooltip("Fixed seed value for deterministic fragmentation patterns")]
        public uint seed = 42;

        [HideInInspector] public Transform obj;

        private float3 callerPos;

        private CallerData callerData;
        
        /// <summary>
        /// Cancellation token source for async operations - cancelled when object is destroyed
        /// </summary>
        public CancellationTokenSource destroyCancellationTokenSource;

        /// <summary>
        /// Batch data structure containing fragments created during destruction.
        /// Provides unified access to both BoxObj fragments and OneDebris objects.
        /// </summary>
        [Serializable]
        public struct FragmentBatch
        {
            public CreatedBoxData[] Fragments;
            public BoxCutterOneDebris[] OneDebris;
            public bool IsComplete;
            public float CreationProgress;
        }

        /// <summary>
        /// Encapsulates all destruction parameters passed through the pipeline.
        /// Provides a complete snapshot of the destruction event configuration.
        /// </summary>
        [Serializable]
        public class CallerData
        {
            public BoxCutterCaller ogCaller;
            public int id;
            
            public Vector3 pos;
            public bool canSpawnDebris;
            public Vector3 forcePos;

            public float radius;
            public float force;
            public float2 forceRange;

            public bool boxMode;

            public float3 boxBounds;
            public Quaternion boxQuat;
            
            public float boxBoundsX;
            public float boxBoundsY;
            public float boxBoundsZ;
            public float boxRotX;
            public float boxRotY;
            public float boxRotZ;
            public float boxRotW;

            public float boxIRotX;
            public float boxIRotY;
            public float boxIRotZ;
            public float boxIRotW;

            public bool noFallFacNormalizing;
            public float strength;

            public bool canRandomSeed;
            public uint seed = 42;

            public bool canPrintMs;
            
            public bool useWhiteList;
            public List<int> whiteListedIds;

            public List<int> blackListedIds;

            /// <summary>
            /// Creates a deep copy of this CallerData instance.
            /// </summary>
            /// <returns>A new CallerData object with all values copied</returns>
            public CallerData Clone()
            {
                return new CallerData
                {
                    ogCaller = this.ogCaller,
                    id = BoxCutterManagerInstance.callerId++,

                    pos = this.pos,
                    canSpawnDebris = this.canSpawnDebris,
                    forcePos = this.forcePos,

                    radius = this.radius,
                    force = this.force,
                    forceRange = this.forceRange,

                    boxMode = this.boxMode,

                    boxBounds = this.boxBounds,
                    boxQuat = this.boxQuat,

                    boxBoundsX = this.boxBoundsX,
                    boxBoundsY = this.boxBoundsY,
                    boxBoundsZ = this.boxBoundsZ,
                    boxRotX = this.boxRotX,
                    boxRotY = this.boxRotY,
                    boxRotZ = this.boxRotZ,
                    boxRotW = this.boxRotW,

                    boxIRotX = this.boxIRotX,
                    boxIRotY = this.boxIRotY,
                    boxIRotZ = this.boxIRotZ,
                    boxIRotW = this.boxIRotW,

                    noFallFacNormalizing = this.noFallFacNormalizing,
                    strength = this.strength,

                    canRandomSeed = this.canRandomSeed,
                    seed = this.seed,

                    canPrintMs = this.canPrintMs,

                    useWhiteList = this.useWhiteList,
                    whiteListedIds = this.whiteListedIds != null ? new List<int>(this.whiteListedIds) : null,

                    blackListedIds = this.blackListedIds != null ? new List<int>(this.blackListedIds) : null,
                };
            }
        }

        public event Action<BoxObj[], CallerData> OnFindHitBoxes;
        public event Action<FragmentBatch> OnFragmentBatchCreated;
        public event Action<FragmentBatch> OnAllFragmentsComplete;
        public event Action<IslandData[]> HitResults;
        
        public event Action<CallerData> OnShootStart;
        public event Action<List<BoxObj>> OnCollisionDetected;
        public event Action<BoxObj[]> OnTargetsFiltered;
        public event Action<BoxObj, CallerData> OnObjectDestroyed;
        public event Action<IslandData[], CallerData> OnIslandsCreated;
        public event Action<CallerData> OnDestructionComplete;

        private bool _init = false;

        void Start()
        {
            StartInit();
        }

        public void StartInit()
        {
            if (!IsPlaying) return;
            obj = transform;

            destroyCancellationTokenSource = new CancellationTokenSource();
            
            Init();

            _init = true;
        }

        private void Init()
        {
            int blackListCount = blackListedObjs.Count;
            int blackStartIdx = blackListCount - 1;
            for (int i = blackStartIdx; i >= 0; i--)
            {
                if (blackListedObjs[i] == null)
                {
                    blackListedObjs.RemoveAt(i);
                    continue;
                }
                blackListedIds.Add(blackListedObjs[i].inheritableId);
            }

            int whiteListCount = whiteListedObjs.Count;
            int whiteStartIdx = whiteListCount - 1;
            for (int i = whiteStartIdx; i >= 0; i--)
            {
                if (whiteListedObjs[i] == null)
                {
                    whiteListedObjs.RemoveAt(i);
                    continue;
                }
                whiteListedIds.Add(whiteListedObjs[i].inheritableId);
            }
        }

        void Update()
        {
            if (!IsPlaying)
            {
                obj = transform;
            }

            if (boxMode)
            {
                if (boxBoundsFromTrans)
                {
                    Vector3 scale = obj.localScale;
                    boxBounds = scale;
                }

                if (rotFromTrans)
                {
                    Quaternion rot = obj.rotation;
                    boxRot = rot.eulerAngles;
                }
            }
        }

        /// <summary>
        /// Triggers a destruction event at the caller's current position and configuration.
        /// Entry point for all destruction operations.
        /// </summary>
        public void Explode()
        {
            if (!_init) StartInit();
            DetectHit(BuildCallerData());
        }
        
        /// <summary>
        /// Triggers a destruction event at the caller's current position and configuration.
        /// Entry point for all destruction operations.
        /// </summary>
        /// <param name="cusCallerData">Optional override with your own caller data</param>
        public void Explode(CallerData cusCallerData)
        {
            if (!_init) StartInit();
            DetectHit(cusCallerData);
        }

        private void GetCallerPos()
        {
            callerPos = obj.position + right * posOffset.x + up * posOffset.y + forward * posOffset.z;
        }

        private void GetPos()
        {
            GetCallerPos();
            GetApplyPos();
        }

        // Quality of life epsilon so exact bounds are not needed
        private const float AngleSnapEpsilon = 1e-3f;

        private static float SnapAngleToInt(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);

            float rounded = Mathf.Round(angle);
            if (Mathf.Abs(angle - rounded) <= AngleSnapEpsilon)
                angle = rounded;

            // Prefer 0 over 360 exactly
            if (angle >= 360f - AngleSnapEpsilon) angle = 0f;

            return angle;
        }

        /// <summary>
        /// Builds a complete snapshot of the caller's destruction parameters and configuration.
        /// This data is passed through the destruction pipeline to maintain consistent settings across all stages.
        /// </summary>
        /// <returns>A CallerData object containing all destruction parameters including position, force, bounds, filters, and runtime settings.</returns>
        public CallerData BuildCallerData()
        {
            GetPos();
            Quaternion boxQuat = Quaternion.Euler(new float3(
                SnapAngleToInt(boxRot.x),
                SnapAngleToInt(boxRot.y),
                SnapAngleToInt(boxRot.z)
            ));

            float boundEpsilon = DestructionTrigger.BoundEpsilon;

            callerData = new CallerData
            {
                ogCaller = this,
                
                pos = callerPos,
                canSpawnDebris = canSpawnDebris,
                forcePos = forceOffsetPos,

                radius = radius,
                force = spawnForce,
                forceRange = forceRange,

                boxMode = boxMode,
                
                boxBounds = boxBounds,
                boxQuat = boxQuat,
                
                boxBoundsX = boxBounds.x + boundEpsilon,
                boxBoundsY = boxBounds.y + boundEpsilon,
                boxBoundsZ = boxBounds.z + boundEpsilon,

                boxRotX = boxQuat.x,
                boxRotY = boxQuat.y,
                boxRotZ = boxQuat.z,
                boxRotW = boxQuat.w,

                boxIRotX = -boxQuat.x,
                boxIRotY = -boxQuat.y,
                boxIRotZ = -boxQuat.z,
                boxIRotW = boxQuat.w,

                noFallFacNormalizing = turnOffStrengthNormalizer,
                strength = strength,
                canRandomSeed = canRandomSeed,
                seed = seed,

                canPrintMs = canPrintMs,
                
                useWhiteList = useWhiteList,
                whiteListedIds = new List<int>(whiteListedIds),
                blackListedIds = new List<int>(blackListedIds),
            };
            
            return callerData;
        }

        /// <summary>
        /// Core destruction method that orchestrates the entire pipeline.
        /// Handles collision detection, fragmentation, island detection, and physics setup.
        /// </summary>
        /// <param name="callerDataTo">The caller data to run the destruction with</param>
        private void DetectHit(CallerData callerDataTo)
        {
            if (!IsPlaying)
            {
                Debug.LogWarning("[Boxcutter] BoxCutter is disabled in Edit mode. Please enter Play mode to run it.");
                return;
            }

            // Check if GameObject is still valid before proceeding
            if (this == null || gameObject == null)
            {
                Debug.LogWarning("[Boxcutter] BoxCutterCaller has been destroyed, cancelling destruction");
                return;
            }

            // Ensure physics state is current before collision detection
            OnShootStart?.Invoke(callerDataTo);
            
            Physics.SyncTransforms();
            List<BoxObj> hitBoxCutters = DestructionTrigger.GetHitBoxes(callerDataTo);
            OnCollisionDetected?.Invoke(hitBoxCutters);
            
            // Filter and prepare target objects
            BoxObj[] boxArr = DestructionTrigger.GetTrueBoxArr(callerDataTo, hitBoxCutters);
            OnTargetsFiltered?.Invoke(boxArr);
            DestructionTrigger.CleanNullFromBox(boxArr);
            //DestructionTrigger.FreezeRb(boxArr);

            // Notify external systems of the hit event
            OnFindHitBoxes?.Invoke(hitBoxCutters.ToArray(), callerDataTo);
            int boxLength = boxArr.Length;

            BoxCutterManager manager = BoxCutterManagerInstance;
            List<BoxObj> readyBoxes = new List<BoxObj>();
            bool isAsync = manager.asyncDestruction;
            // Trigger per-object hit events

            bool asyncOn = isAsync && manager.destroying;
            
            bool[] addedQueue = new bool[boxLength];
            
            //Debug.Log("Hit Length: " + boxLength);
            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = boxArr[i];

                if (asyncOn)
                {
                    //Debug.Log("Added");
                    bool added = box.AddCaller(callerDataTo);
                    addedQueue[i] = added;
                }
                else
                {
                    readyBoxes.Add(box);
                    
                    box.FireOnHit(ref callerDataTo);
                    OnObjectDestroyed?.Invoke(box, callerDataTo);   
                }
            }

            if (readyBoxes.Count > 0) _ = DestructionTrigger.RunDestruction(readyBoxes.ToArray(), callerDataTo);
        }
        
        /// <summary>
        /// Checks if a Transform is a BoxCutter Object or not.
        /// </summary>
        /// <param name="trans">The transform you want to check</param>
        /// <returns></returns>
        public static bool IsBoxObj(Transform trans)
        {
            return IsBoxObj(trans, out BoxObj boxObj);
        }
        
        /// <summary>
        /// Checks if a Transform is a BoxCutter Object or not.
        /// </summary>
        /// <param name="trans">The transform you want to check</param>
        /// <param name="box">The box found</param>
        /// <returns></returns>
        public static bool IsBoxObj(Transform trans, out BoxObj box)
        {
            box = null;

            BoxColliderHolder holder;
            Transform parent = trans.parent;
            
            if (trans.TryGetComponent(out holder))
            {
                box = holder.boxObj; 
                return true;
            }
            if (parent !=null && parent.TryGetComponent(out holder))
            {
                box = holder.boxObj; 
                return true;
            }

            return false;
        }

        private void GetApplyPos()
        {
            Vector3 pos = obj.position;

            if (!canForceOffset)
            {
                forceOffsetPos = pos;
            }
            else if (worldForceOffset)
            {
                forceOffsetPos = pos + right * forceOffset.x + up * forceOffset.y + forward * forceOffset.z;
            }
            else
            {
                forceOffsetPos = pos + obj.TransformDirection(right) * forceOffset.x + obj.TransformDirection(up) * forceOffset.y + obj.TransformDirection(forward) * forceOffset.z;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!canShowGizmos || obj == null) return;
            
            Color ogColor = Gizmos.color;
            Matrix4x4 ogMatrix = Gizmos.matrix;

            if (visualizeDestruction)
            {
                BoxCutterManagerInstance.callerVisualizerDataList.Add(new CallerVisualizerData
                {
                    callerData = BuildCallerData(),
                    boxObjArr = visualizeBoxArr,
                });
            }

            GetPos();
            if (!boxMode)
            {
                BoxCutterGizmosUtil.DrawSphere(boxCutterPrimaryColor.WithA(gizmosAlpha), callerPos, radius);
            }
            else
            {
                Quaternion rot = Quaternion.Euler(boxRot);
                BoxCutterGizmosUtil.DrawBox(boxCutterPrimaryColor.WithA(gizmosAlpha), callerPos, rot, boxBounds);
            }

            if (canForceOffset)
            {
                GetApplyPos();
                Vector3 offsetPos = forceOffsetPos;

                Gizmos.color = boxCutterPrimaryColor;
                BoxCutterGizmosUtil.DrawSphere(boxCutterPrimaryColor, offsetPos, 0.5f);

                Vector3 dir = new Vector3(callerPos.x - offsetPos.x, callerPos.y - offsetPos.y, callerPos.z - offsetPos.z);
                float totalLen = dir.magnitude;
                if (totalLen > 0f)
                {
                    Vector3 dirNorm = dir / totalLen;
                    float dashLen = 0.75f;
                    float gapLen = 0.3f;
                    float thickness = 0.1f;
                    float travelled = 0f;
                    bool drawDash = true;

                    Matrix4x4 oldMat2 = Gizmos.matrix;
                    Gizmos.color = boxCutterPrimaryColor;

                    while (travelled < totalLen)
                    {
                        float segment = drawDash ? dashLen : gapLen;
                        if (travelled + segment > totalLen)
                            segment = totalLen - travelled;

                        if (drawDash)
                        {
                            float half = segment * 0.5f;
                            Vector3 center = offsetPos + dirNorm * (travelled + half);
                            Quaternion cubeRot = Quaternion.LookRotation(dirNorm);
                            BoxCutterGizmosUtil.DrawBox(boxCutterPrimaryColor, center, cubeRot, new Vector3(thickness, thickness, segment));
                        }

                        travelled += segment;
                        drawDash = !drawDash;
                    }

                    Gizmos.matrix = oldMat2;
                }
            }

            Gizmos.color = ogColor;
            Gizmos.matrix = ogMatrix;
        }
#endif

        public void RaiseHitResults(IslandData[] islandDataArr)
        {
            HitResults?.Invoke(islandDataArr);
        }

        public void RaiseOnFragmentBatchCreated(FragmentBatch fragBatch)
        {
            OnFragmentBatchCreated?.Invoke(fragBatch);
        }

        public void RaiseOnIslandsCreated(IslandData[] islandDataArr, CallerData tempCallerData)
        {
            OnIslandsCreated?.Invoke(islandDataArr, tempCallerData);
        }

        public void RaiseOnAllFragmentsComplete(FragmentBatch fragBatch)
        {
            OnAllFragmentsComplete?.Invoke(fragBatch);
        }

        public void RaiseOnDestructionComplete(CallerData tempCallerData)
        {
            OnDestructionComplete?.Invoke(tempCallerData);
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Unity Lifecycle & Cancellation
        //─────────────────────────────────────────────────────────────────────────────────────
        
        void OnDestroy()
        {
            // Cancel all async operations when object is destroyed
            destroyCancellationTokenSource?.Cancel();
            destroyCancellationTokenSource?.Dispose();
        }
    }
}