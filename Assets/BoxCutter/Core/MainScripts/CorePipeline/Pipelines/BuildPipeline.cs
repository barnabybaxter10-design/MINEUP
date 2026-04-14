using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static BoxCutter.BoxObj;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.DestructionPipeline;
using static BoxCutter.BoxCutterWorldPhysics;
using static BoxCutter.BoxCutterCaller;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.MeshBuildPipeline;
using Object = UnityEngine.Object;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               BoxCutter Build Pipeline System
    //─────────────────────────────────────────────────────────────────────────────────────
    
    /// <summary>
    /// Core build pipeline for creating new BoxObj fragments from destruction islands.
    /// Handles mesh generation, collider creation, physics setup, and object pooling.
    /// Coordinates the entire fragment creation process from voxel data to finished objects.
    /// </summary>
    public static class BuildPipeline
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Main Pipeline Entry Point
        //─────────────────────────────────────────────────────────────────────────────────────

        public static CreateBoxObjData[] BuildCreateBoxData(BoxObj[] boxArr)
        {
            int boxLength = boxArr.Length;
            CreateBoxObjData[] boxData = new CreateBoxObjData[boxLength];
            
            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = boxArr[i];
                box.UpdateLiveVars(true);
                boxData[i] = new CreateBoxObjData
                {
                    SourceBox = box,
                    
                    CanCusVoxelSize = box.canCusVoxelSize,
                    CusVoxelSize = box.cusVoxelSize,
                    VoxelResolution = box.voxelResolution,
                    VoxelSize = box.voxelSize,
                    VoxelSize3D = box.voxelSize3D,
                    
                    MassFac = box.massFac,
                    
                    Qx = box.qx, Qy = box.qy, Qz = box.qz, Qw = box.qw,
                    
                    Obj = box.obj,
                    
                    ColliderGenMode = box.colliderGenMode,
                    
                    CanShowGizmos = box.canShowGizmos,
                    CanDebug = box.canDebug,
                    CanShowPartition = box.canShowPartitions,
                    
                    ConnectionState = box.connectionState,
                    Mats = new List<Material>(box.mats),
                    
                    IsDynamic = box.isDynamic,
                    
                    CanAdvancedFrag = box.canAdvancedFrag,
                    FragSettings = box.fragSettings,
                    PlaneBasedOnCaller = box.planeBasedOnCaller,
                    
                    UniqueId = box.uniqueId,
                    InheritableId = box.inheritableId,
                    
                    FellInTurn = box.fellInTurn,
                    
                    ForceIgnoreColorCollider = box.forceIgnoreColorCollider,
                    
                    LastVelo = box.parentHolder == null ? vecZero : box.parentHolder.rb.linearVelocity,
                    LastAngVelo = box.parentHolder == null ? vecZero : box.parentHolder.rb.angularVelocity,
                    
                    CusGroupId = box.cusGroupId,
                    
                    CanWlAttach = box.canWlAttach,
                    WlAttachedBoxInheritId = new List<int>(box.wlAttachedBoxInheritId),
                    
                    CanRequireBoxes = box.canRequireBoxes,
                    RequiredBoxesInheritId = new List<int>(box.requiredBoxesInheritId),
                    UseRequireAsAnchor = box.useRequireAsAnchor,
                    
                    CanCusRequireLogic = box.canCusRequireLogic,
                    CusRequireLogic = box.cusRequireLogic,
                    
                    Group = box.group,
                    
                    CanInheritChildTrans = box.canInheritChildTrans,
                    InheritChildDataList = new List<InheritChildData>(box.inheritChildDataList),

                    CanCustomData = box.canCustomData,
                    CustomDataList = new List<CustomDataEntry>(box.customDataList),

                    // Capture spatial data to avoid dependency on SourceBox after pooling
                    StartPosX = box.startPosX, StartPosY = box.startPosY, StartPosZ = box.startPosZ,
                    VRightX = box.vRightX, VRightY = box.vRightY, VRightZ = box.vRightZ,
                    VUpX = box.vUpX, VUpY = box.vUpY, VUpZ = box.vUpZ,
                    VFwdX = box.vFwdX, VFwdY = box.vFwdY, VFwdZ = box.vFwdZ,
                    
                    // Not cached as the user may change the layer and tag during runtime 
                    Layer = box.gameObj.layer,
                    Tag = box.gameObj.tag,
                    
                    ParentHolder = box.parentHolder,
                    BoxColliderHolderList = new List<BoxColliderHolder>(box.boxColliderHolderList),
                    
                    CanOverrideDiagInIsland = box.canOverrideDiagInIsland,
                    DiagInIslandTo = box.diagInIslandTo,
                    CanOverrideValidCellFillRatio = box.canOverrideKdCellTightness,
                    ValidCellFillTo = box.kdCellTightnessTo,
                    
                    QuadRects = box.quadRects,
                };
            }
            
            return boxData;
        }
        
        /// <summary>
        /// Main pipeline entry point for creating all new BoxObj fragments from destruction data.
        /// Executes the complete build sequence: object creation, spatial acceleration, mesh generation,
        /// collider setup, and physics initialization. Handles both debris and fragment modes.
        /// </summary>
        /// <param name="createBoxData">Array of data containers storing the data needed to create the new BoxObjs</param>
        /// <param name="allSubIsland">3D array of SubBox islands for each source object</param>
        /// <param name="callerData">Caller information containing force and physics data</param>
        /// <param name="buildSkeleton">Builds only the BoxObj and KdTree structures, does not create mesh or colliders</param>
        /// <param name="oneDebrisCallback">Callback invoked when OneDebris objects are created</param>
        /// <returns>Array of created box data linking source objects to their fragments</returns>
        public static (CreatedBoxData[] createdBoxData, BoxObj[] allCreatedBoxesArr) CreateAllNewMainBoxObjs(CreateBoxObjData[] createBoxData, SubBox[][][] allSubIsland, CallerData callerData, bool buildSkeleton = false, System.Action<BoxCutterOneDebris> oneDebrisCallback = null)
        {
            // Create base BoxObj instances from destruction islands
            BuildBaseBoxes(createBoxData, allSubIsland, false, callerData, buildSkeleton, out CreatedBoxData[] createdBoxData, out BoxObj[] allCreatedBoxesArr, oneDebrisCallback);

            // Transfer pending callers to the new boxes
            if (!buildSkeleton) TransferPending(createBoxData, allCreatedBoxesArr);

            return (createdBoxData, allCreatedBoxesArr);
        }
        
        public static async Task BuildPost(BoxObj[] allCreatedBoxesArr, bool debrisMode, CallerData callerData)
        {
            // Generate meshes for visual rendering
            await CreateBoxMesh(allCreatedBoxesArr, debrisMode);

            // Create collision geometry and physics setup
            await CreateBoxColliders(allCreatedBoxesArr, debrisMode);
            
            // Setup debris-specific physics parents if in debris mode
            if (debrisMode) CreateDebrisParents(allCreatedBoxesArr, callerData);

            EnableBoxes(allCreatedBoxesArr, debrisMode);
        }

        public static void TransferPending(CreateBoxObjData[] createdBoxData, BoxObj[] allCreatedBoxesArr)
        {
            int createLength = createdBoxData.Length;
            int createdLength = allCreatedBoxesArr.Length;

            for (int i = 0; i < createLength; i++)
            {
                BoxObj sourceBox = createdBoxData[i].SourceBox;

                for (int v = 0; v < createdLength; v++)
                {
                    BoxObj createdBox = allCreatedBoxesArr[v];
                    createdBox.queuedCallers = sourceBox.TransferPending(createdBox);
                }
            }
        }
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Mesh Generation System
        //─────────────────────────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Generates meshes for all created BoxObj fragments based on their mesh generation mode.
        /// Separates objects into accurate (voxel-based) and naive (simple cube) mesh generation.
        /// Routes each category to the appropriate mesh building pipeline for optimal performance.
        /// </summary>
        /// <param name="createdBoxesArr">Array of BoxObj fragments requiring mesh generation</param>
        /// <param name="debrisMode">If the created mesh will be a debris or not</param>
        private static async Task CreateBoxMesh(BoxObj[] createdBoxesArr, bool debrisMode)
        {
            BoxCutterManager manager = BoxCutterManagerInstance;

            bool syncMode = IsWebGL || (debrisMode && !manager.asyncDebris) || !manager.asyncDestruction;
            int maxFrames = debrisMode ? manager.asyncDebrisMaxFrames : manager.asyncDestructionMaxFrames;

            await BuildAccurate(createdBoxesArr, syncMode, maxFrames, canUseExistingQuads:!debrisMode);
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                              Collider Generation System
        //─────────────────────────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Creates collision geometry for all BoxObj fragments with physics state preservation.
        /// Temporarily disables parent physics to prevent interference during collider generation.
        /// Preserves velocity and angular velocity across the collider creation process.
        /// </summary>
        /// <param name="createdBoxesArr">Array of BoxObj fragments requiring collision geometry</param>
        /// <param name="debrisMode">True if generating colliders for debris fragments</param>
        public static async Task CreateBoxColliders(BoxObj[] createdBoxesArr, bool debrisMode)
        {
            //─────────────────────────────────────────────────────────────────────────────────────
            if (createdBoxesArr == null || createdBoxesArr.Length == 0) return;

            BoxCutterManager manager  = BoxCutterManagerInstance;
            bool syncMode = IsWebGL || (debrisMode && !manager.asyncDebris) || !manager.asyncDestruction;
            Allocator allocator = syncMode ? Allocator.TempJob : Allocator.Persistent;
            
            int maxFrames = debrisMode ? manager.asyncDebrisMaxFrames : manager.asyncDestructionMaxFrames;
            //─────────────────────────────────────────────────────────────────────────────────────
            // Partition work
            List<BoxObj> singleMeshBoxes = new();
            List<BoxObj> smartCellsBoxes = new();
            List<BoxObj> boxColsBoxes = new();

            int createdLength = createdBoxesArr.Length;
            for (int i = 0; i < createdLength; i++)
            {
                var box = createdBoxesArr[i];

                ColliderGenModeEnum genMode = box.GetFinalColliderGen();

                if (genMode == ColliderGenModeEnum.Coarse)
                    singleMeshBoxes.Add(box);
                else if (genMode == ColliderGenModeEnum.Smart)
                    smartCellsBoxes.Add(box);
                else
                    boxColsBoxes.Add(box);
            }
            //─────────────────────────────────────────────────────────────────────────────────────
            //                                     Coarse Path
            //─────────────────────────────────────────────────────────────────────────────────────
            int singleCount = singleMeshBoxes.Count;

            if (singleCount > 0)
            {
#if UNITY_6000_3_OR_NEWER
                EntityId[] meshIdsArr = new EntityId[singleCount];
#else
                int[] meshIdsArr = new int[singleCount];
#endif
                bool[] convexArr = new bool[singleCount];

                int singleMeshCount = singleMeshBoxes.Count;
                for (int i = 0; i < singleMeshCount; i++)
                {
                    BoxObj box = singleMeshBoxes[i];
                    if (box.CheckExit(1))
                    {
#if UNITY_6000_3_OR_NEWER
                        meshIdsArr[i] = EntityId.None;
#else
                        meshIdsArr[i] = -1;
#endif
                        continue;
                    }
                    
#if UNITY_6000_3_OR_NEWER
                    meshIdsArr[i] = box.meshFilter.sharedMesh.GetEntityId();
#else
                    meshIdsArr[i] = box.meshFilter.sharedMesh.GetInstanceID();
#endif
                    convexArr[i] = box.ShouldConvex();
                }

#if UNITY_6000_3_OR_NEWER
                using var meshIdNa = new NativeArray<EntityId>(meshIdsArr, allocator);
#else
                using var meshIdNa = new NativeArray<int>(meshIdsArr, allocator);
#endif
                using var convexNa = new NativeArray<bool>(convexArr, allocator);
                JobHandle handle = new BakeMultiMeshJobBatch { MeshId = meshIdNa, Convex = convexNa, Options = cookingOptions }.Schedule(singleCount, 64);
                
                await WaitJobComplete(handle, syncMode, maxFrames);
                if (manager.CheckExit()) return;

                // Assign colliders
                for (int i = 0; i < singleCount; i++)
                {
                    BoxObj box = singleMeshBoxes[i];
                    bool convex = convexArr[i];
                    string caller = $"Calc Single Mesh (Batched): {box.uniqueId}";
                    if (box.CheckExit()) continue;
                    manager.FreezeParent(box, caller);
                    box.UpdateLiveVars(true);
                    box.ReturnColliderHolderToPool(false);
                    box.CreateMeshCollider(convex, cookingOptions, box.meshFilter.sharedMesh);
                    manager.UnFreezeParent(box, caller);
                }
            }
            //───────────────────────────────────────────────────────────────────────────────────── 
            //                                     Smart Path
            //─────────────────────────────────────────────────────────────────────────────────────
            int smartCount = smartCellsBoxes.Count;
            if (smartCount > 0)
            {
                // 1) Build ALL per-cell meshes once
                var cellData = await BuildAccuratePerKdCell(smartCellsBoxes.ToArray(), syncMode, maxFrames);

                Mesh[] cellMeshes = cellData.meshes;
                int[] cellOwners = cellData.cellOwners;
                int[] cellStartPerBox = cellData.cellStartPerBox;

                int cellMeshLength = cellMeshes.Length;
#if UNITY_6000_3_OR_NEWER
                EntityId[] meshIdsArr = new EntityId[cellMeshLength];
#else
                int[] meshIdsArr = new int[cellMeshLength];
#endif
                bool[] convexArr = new bool[cellMeshLength];

                for (int i = 0; i < cellMeshLength; i++)
                {
                    BoxObj box = smartCellsBoxes[cellOwners[i]];
                    if (box.CheckExit(1)) 
                    {
#if UNITY_6000_3_OR_NEWER
                        meshIdsArr[i] = EntityId.None;
#else
                        meshIdsArr[i] = -1;
#endif
                        continue;
                    }
                    
#if UNITY_6000_3_OR_NEWER
                    meshIdsArr[i] = cellMeshes[i].GetEntityId();
#else
                    meshIdsArr[i] = cellMeshes[i].GetInstanceID();
#endif
                    convexArr[i] = box.ShouldConvex();
                }

#if UNITY_6000_3_OR_NEWER
                using var meshIdNa = new NativeArray<EntityId>(meshIdsArr, allocator);
#else
                using var meshIdNa = new NativeArray<int>(meshIdsArr, allocator);
#endif
                using var convexNa = new NativeArray<bool>(convexArr, allocator);
                JobHandle handle = new BakeMultiMeshJobBatch { MeshId = meshIdNa, Convex = convexNa, Options = cookingOptions }.Schedule(cellMeshLength, 64);
                await WaitJobComplete(handle, syncMode, maxFrames);
                if (manager.CheckExit()) return;

                for (int b = 0; b < smartCount; b++)
                {
                    if (manager.CheckExit()) return;
                    var box = smartCellsBoxes[b];

                    if (box.CheckExit()) continue;
                    
                    bool convex = convexArr[cellStartPerBox[b]];

                    int start = cellStartPerBox[b];
                    int count = box.kdCellLength;

                    string caller = $"Calc Smart Mesh (Batched): {box.uniqueId}";
                    manager.FreezeParent(box, caller);

                    box.UpdateLiveVars(true);
                    box.ReturnColliderHolderToPool(false);

                    for (int i = 0; i < count; i++)
                    {
                        var mesh = cellMeshes[start + i];
                        box.CreateMeshCollider(convex, cookingOptions, mesh);
                    }

                    manager.UnFreezeParent(box, caller);
                }
            }
            //───────────────────────────────────────────────────────────────────────────────────── 
            //                                     Box Path
            //─────────────────────────────────────────────────────────────────────────────────────
            int boxCount = boxColsBoxes.Count;
            if (boxCount > 0)
            {
                for (int i = 0; i < boxCount; i++)
                {
                    BoxObj box = boxColsBoxes[i];
                    box.CalcBoxColliders();
                }
            }
            //─────────────────────────────────────────────────────────────────────────────────────
        }
        
        /// <summary>
        /// Calculates return delay array for fragments based on mode and total count.
        /// Creates evenly distributed delays with deterministic shuffling for reproducible behavior.
        /// </summary>
        /// <param name="totalCount">Total number of fragments to create delays for</param>
        /// <param name="debrisMode">True for debris mode, false for fragment mode</param>
        /// <returns>Array of return delay values, or empty array if returns are disabled</returns>
        private static float[] CalculateReturnDelays(int totalCount, bool debrisMode)
        {
            float defReturnDelay = debrisMode ? ReturnDebrisDelay : ReturnFallenIslandDelay;
            float returnDur = debrisMode ? ReturnDebrisDura : ReturnFallenIslandDura;
            bool canReturn = debrisMode ? CanReturnDebris : CanReturnFallenIslands;
            
            if (!canReturn || totalCount == 0)
                return new float[0];

            float[] returnDelayArr = new float[totalCount];
            float returnInterval = returnDur / totalCount;
            
            for (int i = 0; i < totalCount; i++)
            {
                returnDelayArr[i] = defReturnDelay + returnInterval * i;
            }
            
            // Deterministic shuffle for reproducible debugging
            System.Random rng = new System.Random(69);
            for (int i = returnDelayArr.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (returnDelayArr[i], returnDelayArr[j]) = (returnDelayArr[j], returnDelayArr[i]);
            }
            
            return returnDelayArr;
        }

        /// <summary>
        /// Builds BoxObj fragments from islands with common logic for both debris and main fragment creation.
        /// Handles the core box creation loop with proper return delay management and data structure population.
        /// </summary>
        /// <param name="createBoxData">Array of data containers for creating BoxObjs</param>
        /// <param name="allSubIsland">3D array of SubBox islands for each source object</param>
        /// <param name="returnDelayArr">Pre-calculated return delay array</param>
        /// <param name="debrisMode">True for debris generation, false for fragment islands</param>
        /// <param name="callerData">Caller information containing force and physics data</param>
        /// <param name="buildSkeleton">Builds only the BoxObj and KdTree structures</param>
        /// <param name="createdBoxData">Output array of created box data linking source to fragments</param>
        /// <param name="allCreatedBoxesList">Output list of all created BoxObj instances</param>
        /// <param name="oneDebrisCallback">Callback invoked when OneDebris objects are created</param>
        private static void BuildBoxesFromIslands(
            CreateBoxObjData[] createBoxData, SubBox[][][] allSubIsland, float[] returnDelayArr,
            bool debrisMode, in CallerData callerData, bool buildSkeleton,
            out CreatedBoxData[] createdBoxData, List<BoxObj> allCreatedBoxesList, System.Action<BoxCutterOneDebris> oneDebrisCallback = null)
        {
            int boxLength = allSubIsland.Length;
            bool canReturn = returnDelayArr.Length > 0;
            createdBoxData = new CreatedBoxData[boxLength];
            int returnIndex = 0;
            
            for (int i = 0; i < boxLength; i++)
            {
                CreateBoxObjData box = createBoxData[i];
                ref SubBox[][] subIsland = ref allSubIsland[i];
                int islandCount = subIsland.Length;

                List<BoxObj> createdBoxes = new List<BoxObj>();

                for (int v = 0; v < islandCount; v++)
                {
                    BoxObj createdBox = BuildBaseBoxObj(box, ref subIsland[v], GetListSubObjPosAndSize(subIsland[v], box), canReturn, callerData, buildSkeleton, canReturn ? returnDelayArr[returnIndex++] : 0, isDebris: debrisMode, oneDebrisCallback: oneDebrisCallback);
                    if (createdBox != null)
                    {
                        createdBoxes.Add(createdBox);
                        allCreatedBoxesList.Add(createdBox);
                    }
                }

                createdBoxData[i] = new CreatedBoxData
                {
                    SourceBox = box.SourceBox,
                    CreatedBoxCutters = createdBoxes,
                };
            }
        }

        private static void BuildBaseBoxes(
            CreateBoxObjData[] createBoxArr, in SubBox[][][] allSubIsland,
            bool debrisMode, in CallerData callerData, bool buildSkeleton, out CreatedBoxData[] createdBoxData, out BoxObj[] allCreatedBoxesArr, System.Action<BoxCutterOneDebris> oneDebrisCallback = null)
        {
            bool canReturn = debrisMode ? CanReturnDebris : CanReturnFallenIslands;
            List<BoxObj> allCreatedBoxesList = new List<BoxObj>();
            
            // Calculate total fragment count for return delay distribution
            int totalFragmentCount = 0;
            if (canReturn)
                for (int i = 0; i < allSubIsland.Length; i++)
                    totalFragmentCount += allSubIsland[i].Length;
            
            float[] returnDelayArr = CalculateReturnDelays(totalFragmentCount, debrisMode);
            BuildBoxesFromIslands(createBoxArr, allSubIsland, returnDelayArr, debrisMode, callerData, buildSkeleton, out createdBoxData, allCreatedBoxesList, oneDebrisCallback);

            allCreatedBoxesArr = new BoxObj[allCreatedBoxesList.Count];
            allCreatedBoxesList.CopyTo(allCreatedBoxesArr);
        }

        /// <summary>
        /// Creates debris BoxObjs asynchronously across multiple frames for better performance.
        /// Processes debris in batches limited by maxDebrisPerFrame setting.
        /// </summary>
        /// <param name="createBoxDataArr">Array of data containers storing the data needed to create the new BoxObjs</param>
        /// <param name="allSubIsland">3D array of SubBox islands for each source object</param>
        /// <param name="callerData">Caller information containing force and physics data</param>
        /// <param name="buildSkeleton">Builds only the BoxObj and KdTree structures, does not create mesh or colliders</param>
        /// <param name="batchCallback">Callback invoked when each batch of debris is created</param>
        /// <param name="oneDebrisCallback">Callback invoked when OneDebris objects are created</param>
        /// <param name="cancellationToken">Token to cancel the async operation if scene reloads</param>
        /// <returns>Task that completes with the created debris data</returns>
        public static async Task<CreatedBoxData[]> CreateDebris(
            CreateBoxObjData[] createBoxDataArr, SubBox[][][] allSubIsland,
            CallerData callerData, bool buildSkeleton = false,
            Action<CreatedBoxData[], BoxCutterOneDebris[], float> batchCallback = null,
            Action<BoxCutterOneDebris> oneDebrisCallback = null,
            CancellationToken cancellationToken = default)
        {
            BoxCutterManager manager = BoxCutterManagerInstance;
            bool sync = IsWebGL || !manager.asyncDebris;
            if (!sync) await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            int boxLength = allSubIsland.Length;
            bool canReturn = CanReturnDebris;

            // 1. Count total debris
            int totalDebrisCount = 0;
            for (int b = 0; b < boxLength; b++)
                totalDebrisCount += allSubIsland[b].Length;

            // 2. Return delays
            float[] returnDelayArr = canReturn ? CalculateReturnDelays(totalDebrisCount, true) : Array.Empty<float>();
            int returnIndex = 0;

            // 3. Per-box created lists
            List<BoxObj>[] perBoxCreatedLists = new List<BoxObj>[boxLength];
            for (int i = 0; i < boxLength; i++)
                perBoxCreatedLists[i] = new List<BoxObj>();

            // map created box -> its source box index (for fast batch callback building)
            Dictionary<BoxObj, int> boxOwner = new Dictionary<BoxObj, int>();

            CreatedBoxData[] createdBoxDataArr = new CreatedBoxData[boxLength];

            // 4. Flatten all work
            var pending = new List<(int boxIdx, int islandIdx)>(totalDebrisCount);
            for (int b = 0; b < boxLength; b++)
            {
                var islands = allSubIsland[b];
                for (int isl = 0; isl < islands.Length; isl++)
                    pending.Add((b, isl));
            }

            // 5. Shuffle once
            System.Random rng = new System.Random();
            for (int i = pending.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pending[i], pending[j]) = (pending[j], pending[i]);
            }

            // 6. Batching
            List<BoxObj> batchCreatedBoxes = new List<BoxObj>();
            List<BoxCutterOneDebris> batchOneDebris = new List<BoxCutterOneDebris>();
            List<BoxCutterOneDebris> allOneDebrisList = new List<BoxCutterOneDebris>();

            int maxPerFrame = IsWebGL || manager.maxDebrisPerFrame == 0 ? IntInfinity : manager.maxDebrisPerFrame;
            int processedThisFrame = 0;
            int processedTotal = 0;

            async Task FlushBatch(float progress, bool lastBatch = false)
            {
                int createdBoxesCount = batchCreatedBoxes.Count;
                int createdOnesCount = batchOneDebris.Count;
                
                if (createdBoxesCount == 0 && createdOnesCount == 0)
                    return;

                // Due to the max debris limit its possible that created boxes in the same destruction event are returned back to the pool thus have their data reset
                int boxStartIdx = createdBoxesCount - 1;
                for (int i = boxStartIdx; i >= 0; i--)
                {
                    if (batchCreatedBoxes[i].pooled)
                        batchCreatedBoxes.RemoveAt(i);
                }

                int debrisStartIdx = createdOnesCount - 1;
                for (int i = debrisStartIdx; i >= 0; i--)
                {
                    if (batchOneDebris[i].pooled)
                        batchOneDebris.RemoveAt(i);
                }
                
                BoxObj[] batchArr = batchCreatedBoxes.ToArray();
                BoxCutterOneDebris[] oneDebrisArr = batchOneDebris.ToArray();

                await BuildNewKdTree(batchArr, sync);
                if (!buildSkeleton)
                    await BuildPost(batchArr, true, callerData);

                if (batchCallback != null)
                {
                    HashSet<int> touched = new HashSet<int>();
                    foreach (var box in batchArr)
                    {
                        if (boxOwner.TryGetValue(box, out int owner))
                            touched.Add(owner);
                    }

                    List<CreatedBoxData> batchData = new List<CreatedBoxData>();
                    foreach (int owner in touched)
                    {
                        batchData.Add(new CreatedBoxData
                        {
                            SourceBox = createBoxDataArr[owner].SourceBox,
                            CreatedBoxCutters = perBoxCreatedLists[owner],
                        });
                    }

                    batchCallback(batchData.ToArray(), oneDebrisArr, progress);
                }

                batchCreatedBoxes.Clear();
                batchOneDebris.Clear();
                processedThisFrame = 0;

                if (!lastBatch) await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }

            // 7. Main loop
            int pendingCount = pending.Count;
            for (int idx = 0; idx < pendingCount; idx++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (processedThisFrame >= maxPerFrame &&
                    (batchCreatedBoxes.Count > 0 || batchOneDebris.Count > 0))
                {
                    float prog = totalDebrisCount > 0 ? (float)processedTotal / totalDebrisCount : 1.0f;
                    await FlushBatch(prog);
                }

                var (boxIdx, islandIdx) = pending[idx];

                SubBox[] original = allSubIsland[boxIdx][islandIdx];
                SubBox[] islandCopy = new SubBox[original.Length];
                Array.Copy(original, islandCopy, original.Length);
                var createBoxData = createBoxDataArr[boxIdx];

                Action<BoxCutterOneDebris> combinedCallback = (oneDebris) =>
                {
                    allOneDebrisList.Add(oneDebris);
                    batchOneDebris.Add(oneDebris);
                    oneDebrisCallback?.Invoke(oneDebris);
                };

                BoxObj createdBox = BuildBaseBoxObj(
                    createBoxData,
                    ref islandCopy,
                    GetListSubObjPosAndSize(islandCopy, createBoxData),
                    canReturn,
                    callerData,
                    buildSkeleton,
                    canReturn ? returnDelayArr[returnIndex++] : 0,
                    isDebris: true,
                    oneDebrisCallback: combinedCallback);

                if (createdBox != null)
                {
                    perBoxCreatedLists[boxIdx].Add(createdBox);
                    boxOwner[createdBox] = boxIdx;
                    batchCreatedBoxes.Add(createdBox);
                }

                processedThisFrame++;
                processedTotal++;
            }

            // 8. Final flush
            if (batchCreatedBoxes.Count > 0 || batchOneDebris.Count > 0)
            {
                await FlushBatch(1.0f, true);
            }

            // 9. Build final return
            for (int i = 0; i < boxLength; i++)
            {
                createdBoxDataArr[i] = new CreatedBoxData
                {
                    SourceBox = createBoxDataArr[i].SourceBox,
                    CreatedBoxCutters = perBoxCreatedLists[i],
                };
            }

            return createdBoxDataArr;
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Data Structures
        //─────────────────────────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Contains position and size data for a SubBox island in both world and voxel coordinates.
        /// Used for positioning and scaling newly created BoxObj fragments during the build process.
        /// Includes both the computed world position/size and original voxel bounds for reference.
        /// </summary>
        public struct SubIslandPosAndSizeData
        {
            /// <summary>World position X coordinate of the island center</summary>
            public float PosX;
            /// <summary>World position Y coordinate of the island center</summary>
            public float PosY;
            /// <summary>World position Z coordinate of the island center</summary>
            public float PosZ;
            /// <summary>World size along X axis in Unity units</summary>
            public float SizeX;
            /// <summary>World size along Y axis in Unity units</summary>
            public float SizeY;
            /// <summary>World size along Z axis in Unity units</summary>
            public float SizeZ;

            /// <summary>Minimum voxel coordinate along X axis</summary>
            public int MinX;
            /// <summary>Minimum voxel coordinate along Y axis</summary>
            public int MinY;
            /// <summary>Minimum voxel coordinate along Z axis</summary>
            public int MinZ;
            /// <summary>Maximum voxel coordinate along X axis</summary>
            public int MaxX;
            /// <summary>Maximum voxel coordinate along Y axis</summary>
            public int MaxY;
            /// <summary>Maximum voxel coordinate along Z axis</summary>
            public int MaxZ;
        }
        
        /// <summary>
        /// Calculates position and size data for a collection of SubBox fragments.
        /// Finds the bounding box of all SubBoxes and transforms to world coordinates.
        /// Used to determine the position and scale of newly created BoxObj fragments.
        /// </summary>
        /// <param name="subObjs">Array of SubBox fragments to analyze</param>
        /// <param name="createBoxData">Captured BoxObj data with spatial information</param>
        /// <returns>Position and size data in both world and voxel coordinates</returns>
        public static SubIslandPosAndSizeData GetListSubObjPosAndSize(in SubBox[] subObjs, CreateBoxObjData createBoxData)
        {
            // Find bounding box of all SubBoxes in voxel coordinates
            int minX = IntInfinity;
            int minY = IntInfinity;
            int minZ = IntInfinity;
            int maxX = -IntInfinity;
            int maxY = -IntInfinity;
            int maxZ = -IntInfinity;

            int length = subObjs.Length;
            for (int i = 0; i < length; i++)
            {
                ref SubBox sub = ref subObjs[i];

                // Update minimum bounds
                if (sub.minX < minX) minX = sub.minX;
                if (sub.minY < minY) minY = sub.minY;
                if (sub.minZ < minZ) minZ = sub.minZ;

                // Update maximum bounds
                if (sub.maxX > maxX) maxX = sub.maxX;
                if (sub.maxY > maxY) maxY = sub.maxY;
                if (sub.maxZ > maxZ) maxZ = sub.maxZ;
            }

            // Calculate center point in voxel coordinates
            float halfX = (minX + maxX) * 0.5f;
            float halfY = (minY + maxY) * 0.5f;
            float halfZ = (minZ + maxZ) * 0.5f;

            return new SubIslandPosAndSizeData
            {
                // Transform center to world coordinates using captured spatial data
                PosX = createBoxData.StartPosX + createBoxData.VRightX * halfX + createBoxData.VUpX * halfY + createBoxData.VFwdX * halfZ,
                PosY = createBoxData.StartPosY + createBoxData.VRightY * halfX + createBoxData.VUpY * halfY + createBoxData.VFwdY * halfZ,
                PosZ = createBoxData.StartPosZ + createBoxData.VRightZ * halfX + createBoxData.VUpZ * halfY + createBoxData.VFwdZ * halfZ,

                // Calculate world size from voxel dimensions using captured voxel size
                SizeX = (maxX - minX) * createBoxData.VoxelSize,
                SizeY = (maxY - minY) * createBoxData.VoxelSize,
                SizeZ = (maxZ - minZ) * createBoxData.VoxelSize,

                // Store original voxel bounds for reference
                MinX = minX,
                MinY = minY,
                MinZ = minZ,
                MaxX = maxX,
                MaxY = maxY,
                MaxZ = maxZ
            };
        }

        public static async Task BuildNewKdTree(BoxObj[] allCreatedBoxesArr, bool sync = false)
        {
            Allocator allocator = Allocator.Persistent;
            
            int boxLength = allCreatedBoxesArr.Length;
            
            SubBox[][] subsPerBox = new SubBox[boxLength][];
            for (int i = 0; i < boxLength; ++i)
            {
                BoxObj box = allCreatedBoxesArr[i];
                SubBox[] subs = new SubBox[box.allSubList.Count];
                box.allSubList.CopyTo(subs);
                subsPerBox[i] = subs;
            }

            BuildFlatSubArrays(
                subsPerBox,
                out NativeArray<SubBox> flatSubsNa,
                out NativeArray<int> subStartsNa,
                out NativeArray<int> subCountsNa,
                allocator
            );
            
            boxLength = allCreatedBoxesArr.Length;
            float defFillRatioValue = BoxCutterManagerInstance.validCellFillRatio;
            
            float[] fillRatioArr = new float[boxLength];
            
            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = allCreatedBoxesArr[i];
                fillRatioArr[i] = box.canOverrideKdCellTightness ? box.kdCellTightnessTo : defFillRatioValue;
            }
            
            using NativeArray<float> ratioToNa = new NativeArray<float>(fillRatioArr, allocator);

            (NativeArray<int> kdOrigIdxNa, NativeArray<CellData> kdCellsNa, NativeArray<int> kdCellOwnerNa, NativeArray<int2> leafCellCountNa) kdData = await BuildKdTree(flatSubsNa, ratioToNa, subStartsNa, subCountsNa, allocator, sync);
            
            int boxCnt = allCreatedBoxesArr.Length;
            int2[] leafCellCountArr = kdData.leafCellCountNa.ToArray();
            
            int[] leafBase = new int[boxCnt];
            int[] cellBase = new int[boxCnt];
            for (int i = 1; i < boxCnt; ++i)
            {
                leafBase[i] = leafBase[i - 1] + leafCellCountArr[i - 1].x;
                cellBase[i] = cellBase[i - 1] + leafCellCountArr[i - 1].y;
            }

            int[] subStartsArr = subStartsNa.ToArray();
            int[] kdOrigIdxArr = kdData.kdOrigIdxNa.ToArray();
            CellData[] kdCellsArr = kdData.kdCellsNa.ToArray();

            for (int b = 0; b < boxCnt; ++b)
            {
                BoxObj createdBox = allCreatedBoxesArr[b];

                int leafCnt = leafCellCountArr[b].x;
                int cellCnt = leafCellCountArr[b].y;
                int subOff = subStartsArr[b];

                createdBox.kdOriginalIndicesArr = new int[leafCnt];
                for (int i = 0; i < leafCnt; i++)
                {
                    int globalOrig = kdOrigIdxArr[leafBase[b] + i];
                    createdBox.kdOriginalIndicesArr[i] = globalOrig - subOff;
                }

                createdBox.kdCellDataArr = new CellData[cellCnt];
                for (int k = 0; k < cellCnt; k++)
                {
                    var cd = kdCellsArr[cellBase[b] + k];
                    cd.rangeStart -= leafBase[b];
                    cd.rangeEnd -= leafBase[b];
                    createdBox.kdCellDataArr[k] = cd;
                }
                
                createdBox.kdLeafLength = leafCnt;
                createdBox.kdCellLength = cellCnt;
            }

            flatSubsNa.Dispose();
            subStartsNa.Dispose();
            subCountsNa.Dispose();
            kdData.kdOrigIdxNa.Dispose();
            kdData.kdCellsNa.Dispose();
            kdData.kdCellOwnerNa.Dispose();
            kdData.leafCellCountNa.Dispose();
        }

        public class CreateBoxObjData
        {
            public BoxObj SourceBox;
            
            public bool CanCusVoxelSize;
            public float CusVoxelSize;
            public int VoxelResolution;
            public float VoxelSize;
            public float3 VoxelSize3D;
            
            public float MassFac;
            
            public float Qx, Qy, Qz, Qw;
            public Transform Obj;
            
            public ColliderGenModeEnum ColliderGenMode;

            public bool CanShowGizmos;
            public bool CanDebug;
            public bool CanShowPartition;
            
            public ConnectionStateEnum ConnectionState;
            public List<Material> Mats;
            
            public bool IsDynamic;

            public bool CanAdvancedFrag;
            public FragSettings FragSettings;
            public bool PlaneBasedOnCaller;

            public int UniqueId;
            public int InheritableId;

            public int FellInTurn;

            public bool ForceIgnoreColorCollider;
            
            public Vector3 LastVelo;
            public Vector3 LastAngVelo;

            public int CusGroupId;

            public bool CanWlAttach;
            public List<int> WlAttachedBoxInheritId;

            public bool CanRequireBoxes;
            public List<int> RequiredBoxesInheritId;
            public bool UseRequireAsAnchor;

            public bool CanCusRequireLogic;
            public MonoBehaviour CusRequireLogic;

            public BoxCutterGroup Group;

            public bool CanInheritChildTrans;
            public List<InheritChildData> InheritChildDataList;

            public bool CanCustomData;
            public List<CustomDataEntry> CustomDataList;

            // Spatial data captured to avoid dependency on SourceBox after pooling
            public float StartPosX, StartPosY, StartPosZ;
            public float VRightX, VRightY, VRightZ; 
            public float VUpX, VUpY, VUpZ;
            public float VFwdX, VFwdY, VFwdZ;

            public string Tag;
            public LayerMask Layer;

            public BoxCutterParent ParentHolder;
            public List<BoxColliderHolder> BoxColliderHolderList;

            public bool CanOverrideDiagInIsland;
            public bool DiagInIslandTo;
            public bool CanOverrideValidCellFillRatio;
            public float ValidCellFillTo;
            
            public QuadRect[] QuadRects;
        }

        public static BoxObj BuildBaseBoxObj
        (
            CreateBoxObjData createBox, ref SubBox[] subObjArr, SubIslandPosAndSizeData posAndSizeData,
            bool canReturn, in CallerData callerData,
            bool isSkeleton,
            float returnDelay, bool isDebris = false, Action<BoxCutterOneDebris> oneDebrisCallback = null
        )
        {
            BoxCutterManager boxCutterManager = BoxCutterManagerInstance;
            
            float voxelSize = createBox.VoxelSize;
            float massFac = createBox.MassFac;

            int subLength = subObjArr.Length;
            float newPosX = posAndSizeData.PosX;
            float newPosY = posAndSizeData.PosY;
            float newPosZ = posAndSizeData.PosZ;
            float newSizeX = posAndSizeData.SizeX;
            float newSizeY = posAndSizeData.SizeY;
            float newSizeZ = posAndSizeData.SizeZ;

            int minX = posAndSizeData.MinX;
            int minY = posAndSizeData.MinY;
            int minZ = posAndSizeData.MinZ;

            Vector3 groupObjPosTo = new Vector3(newPosX, newPosY, newPosZ);
            Quaternion groupObjRotTo = new Quaternion(createBox.Qx, createBox.Qy, createBox.Qz, createBox.Qw);
            float force = callerData.force;
            Vector3 forcePos = callerData.forcePos;
            float2 forceRange = callerData.forceRange;
            Vector3 forceDir = new Vector3(newPosX - forcePos.x, newPosY - forcePos.y, newPosZ - forcePos.z);
            bool oneDebrisBox = isDebris && subLength == 1;

            if (oneDebrisBox)
            {
                BoxCutterOneDebris oneDebris = BoxCutterManagerInstance.GetOneDebris();

                Transform debrisObj = oneDebris.obj;
                debrisObj.position = groupObjPosTo;
                debrisObj.rotation = groupObjRotTo;
                debrisObj.localScale = new Vector3(newSizeX, newSizeY, newSizeZ);

                oneDebris.meshRend.SetMaterials(createBox.Mats);

                int magicaIndex = subObjArr[0].magicaIndex;

                oneDebris.gameObject.SetActive(true);
                
                var mpb = new MaterialPropertyBlock();
                oneDebris.meshRend.GetPropertyBlock(mpb);
                string offsetProp = "_BaseMap_ST";

                if (boxCutterManager.renderPipelineType == Extend.RenderPipelineType.HDRP)
                    offsetProp = "_BaseColorMap_ST";
                else if (boxCutterManager.renderPipelineType == Extend.RenderPipelineType.BuiltIn)
                    offsetProp = "_MainTex_ST";
                
                mpb.SetVector(offsetProp, new Vector4(1f, 1f, (magicaIndex + 0.5f) / 256f, 0f));
                oneDebris.meshRend.SetPropertyBlock(mpb);

                oneDebris.rb.isKinematic = false;
                oneDebris.rb.mass = CalcOneDebrisMass(newSizeX, newSizeY, newSizeZ, voxelSize, massFac);
                oneDebris.rb.linearVelocity = createBox.LastVelo;
                oneDebris.rb.angularVelocity = createBox.LastAngVelo;
                ApplyForce(oneDebris.rb, forceDir, forceRange, force);

                oneDebris.gameObj.layer = createBox.Layer;
                oneDebris.gameObj.tag = createBox.Tag;

                if (canReturn)
                    oneDebris.ReturnToPool(returnDelay);

                // Invoke callback for OneDebris creation
                oneDebrisCallback?.Invoke(oneDebris);

                return null;
            }

            BoxObj sourceBox = createBox.SourceBox;
            BoxObj newBoxObj = boxCutterManager.GetBoxObj();
            if (isDebris) boxCutterManager.RegisterActiveDebris(newBoxObj);

            Transform newObj = newBoxObj.obj;
            int[] colorMap = new int[255];
            int totalColors = 0;

            for (int i = 0; i < subLength; i++)
            {
                ref SubBox subBox = ref subObjArr[i];

                // 1. Shift the SubBox (Voxel Chunk) coordinates to the new local origin
                subBox.minX -= minX;
                subBox.minY -= minY;
                subBox.minZ -= minZ;

                subBox.maxX -= minX;
                subBox.maxY -= minY;
                subBox.maxZ -= minZ;

                subObjArr[i] = subBox;
                colorMap[subBox.magicaIndex]++;
                
                // 2. Transfer and Shift Quads
                int oldStart = subBox.quadStart;
                int oldEnd = subBox.quadEnd;

                for (int q = oldStart; q < oldEnd; q++)
                {
                    ref QuadRect rect = ref createBox.QuadRects[q];
                    int minU, maxU, minV, maxV;

                    Dir d = (Dir)rect.dir;
        
                    switch (d)
                    {
                        case Dir.NegX:
                        case Dir.PosX:
                            // Normal is X. Plane is on Z (U) and Y (V) axes.
                            minU = rect.minU - minZ;
                            maxU = rect.maxU - minZ;
                            minV = rect.minV - minY;
                            maxV = rect.maxV - minY;
                            break;

                        case Dir.NegY:
                        case Dir.PosY:
                            // Normal is Y. Plane is on X (U) and Z (V) axes.
                            minU = rect.minU - minX;
                            maxU = rect.maxU - minX;
                            minV = rect.minV - minZ;
                            maxV = rect.maxV - minZ;
                            break;

                        case Dir.NegZ:
                        default: // Dir.PosZ
                            // Normal is Z. Plane is on X (U) and Y (V) axes.
                            minU = rect.minU - minX;
                            maxU = rect.maxU - minX;
                            minV = rect.minV - minY;
                            maxV = rect.maxV - minY;
                            break;
                    }

                    createBox.QuadRects[q] = new QuadRect()
                    {
                        dir = rect.dir,
                        minU = minU,
                        maxU = maxU,
                        minV = minV,
                        maxV = maxV,
                    };
                }
            }

            for (int i = 0; i < 255; i++)
                if (colorMap[i] > 0)
                    totalColors++;

            newBoxObj.obj.position = groupObjPosTo;
            newObj.rotation = groupObjRotTo;

            if (!isDebris)
            {
                bool hasObjParent = false;
                BoxCutterParent parentHolder = null;
                Transform parentTo = null;

                if (sourceBox.parentHolder != null)
                {
                    parentHolder = sourceBox.parentHolder;
                    parentTo = parentHolder.obj;
                    hasObjParent = true;
                }
                else
                {
                    if (createBox.Obj != null)
                    {
                        Transform objParent = createBox.Obj.parent;
                        if (objParent != null)
                        {
                            parentTo = objParent;
                            hasObjParent = true;
                        }
                    }
                }

                newBoxObj.obj.localScale = vecOne;

                if (hasObjParent)
                {
                    boxCutterManager.FreezeParent(sourceBox);
                    newBoxObj.obj.parent = parentTo;
                    boxCutterManager.UnFreezeParent(sourceBox);
                }
            }

            newBoxObj.colliderGenMode = createBox.ColliderGenMode;

            newBoxObj.sizeX = newSizeX;
            newBoxObj.sizeY = newSizeY;
            newBoxObj.sizeZ = newSizeZ;

            newBoxObj.canShowGizmos = createBox.CanShowGizmos;
            newBoxObj.canDebug = createBox.CanDebug;
            newBoxObj.canShowPartitions = createBox.CanShowPartition;

            newBoxObj.connectionState = isDebris ? ConnectionStateEnum.Disconnected : createBox.ConnectionState;
            newBoxObj.allSubList = subObjArr.ToList();

            newBoxObj.mats = new List<Material>(createBox.Mats);

            newBoxObj.minXOffset = minX;
            newBoxObj.minYOffset = minY;
            newBoxObj.minZOffset = minZ;

            newBoxObj.isDynamic = createBox.IsDynamic;

            newBoxObj.canCusVoxelSize = createBox.CanCusVoxelSize;
            newBoxObj.cusVoxelSize = createBox.CusVoxelSize;
            newBoxObj.voxelResolution = createBox.VoxelResolution;
            newBoxObj.voxelSize = voxelSize;
            newBoxObj.voxelSize3D = createBox.VoxelSize3D;

            newBoxObj.canAdvancedFrag = createBox.CanAdvancedFrag;
            newBoxObj.fragSettings = createBox.FragSettings;
            newBoxObj.planeBasedOnCaller = createBox.PlaneBasedOnCaller;

            newBoxObj.uniqueId = boxCutterManager.boxAllUniqueID++;
            newBoxObj.inheritableId = createBox.InheritableId;

            newBoxObj.fellInTurn = createBox.FellInTurn;

            newBoxObj.totalColors = totalColors;
            newBoxObj.forceIgnoreColorCollider = createBox.ForceIgnoreColorCollider;

            newBoxObj.gameObj.layer = createBox.Layer;
            newBoxObj.gameObj.tag = createBox.Tag;
            
            newBoxObj.myLayer = createBox.Layer;
            newBoxObj.myTag = createBox.Tag;
            
            if (createBox.ConnectionState == ConnectionStateEnum.Disconnected)
            {
                newBoxObj.lastVelocity = createBox.LastVelo;
            }

            newBoxObj.RefreshLocalDirs();
            newBoxObj.RefreshPosRot();
            newBoxObj.RefreshTrueStartPos();
            newBoxObj.meshRend.SetMaterials(createBox.Mats);

            newBoxObj.cusGroupId = createBox.CusGroupId;

            newBoxObj.canWlAttach = createBox.CanWlAttach;
            newBoxObj.wlAttachedBoxInheritId = new List<int>(createBox.WlAttachedBoxInheritId); // Need to create a copy of the ref

            newBoxObj.canRequireBoxes = createBox.CanRequireBoxes;
            newBoxObj.requiredBoxesInheritId = new List<int>(createBox.RequiredBoxesInheritId);
            newBoxObj.useRequireAsAnchor = createBox.UseRequireAsAnchor;

            newBoxObj.canCusRequireLogic = createBox.CanCusRequireLogic;
            newBoxObj.cusRequireLogic = createBox.CusRequireLogic;

            // Records the source's parent holder so it can be frozen when creating colliders
            newBoxObj.parentHolder = createBox.ParentHolder;
            newBoxObj.group = createBox.Group;

            if (!isDebris)
            {
                newBoxObj.canInheritChildTrans = createBox.CanInheritChildTrans;
                newBoxObj.inheritChildDataList = new List<InheritChildData>(createBox.InheritChildDataList);
            }

            newBoxObj.canCustomData = createBox.CanCustomData;
            newBoxObj.customDataList = new List<CustomDataEntry>(createBox.CustomDataList);

            newBoxObj.canOverrideDiagInIsland = createBox.CanOverrideDiagInIsland;
            newBoxObj.diagInIslandTo = createBox.DiagInIslandTo;
            
            newBoxObj.canOverrideKdCellTightness = createBox.CanOverrideValidCellFillRatio;
            newBoxObj.kdCellTightnessTo = createBox.ValidCellFillTo;
            
            newBoxObj.cusInitAlready = true;
            newBoxObj.ready = false;
            
            newBoxObj.quadRects = createBox.QuadRects;
            
            newBoxObj.BuildSpatialData(false);
            BoxCutterManagerInstance.onDestroyFinish += newBoxObj.RunDestroy;
            
            //if (!isSkeleton) newBoxGameObj.SetActive(true);
            if (canReturn)
            {
                newBoxObj.returnDelay = returnDelay;
                newBoxObj.hasReturnDelay = true;
            }

            return newBoxObj;
        }

        /// <summary>
        /// Returns all the boxes back to the pool when the pipeline is done.
        /// </summary>
        /// <param name="boxArr">All the original boxes</param>
        /// <param name="buildSkeleton">If the function should return or remove from partition</param>
        public static void ReturnBoxes(BoxObj[] boxArr, bool buildSkeleton)
        {
            int boxLength = boxArr.Length;
            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = boxArr[i];
                if (box == null) continue;

                if (buildSkeleton)
                {
                    box.RemoveFromPartition();
                }
                else
                {
                    box.ReturnToPool();
                }
            }
        }

        public static void EnableBoxes(BoxObj[] createdBoxes, bool isDebris)
        {
            int createdLength = createdBoxes.Length;
            
            for (int i = 0; i < createdLength; i++)
            {
                BoxObj box = createdBoxes[i];

                if (box.gameObj == null) continue;
                box.gameObj.SetActive(true);
                box.ready = true;
                box.StartReturnToPoolDelay();
                
                /*if (!isDebris)
                {*/
                    box.CalcPartitionLocation(true);
                //}
            }
        }
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                           Re-parent Any Connected Child
        //─────────────────────────────────────────────────────────────────────────────────────

        public static void ReParentChildTrans(CreatedBoxData[] createdDataArr)
        {
            float offset = BoxCutterManagerInstance.connectionLeniencyDist;
            
            int createdLength = createdDataArr.Length;

            for (int i = 0; i < createdLength; i++)
            {
                //─────────────────────────────────────────────────────────────────────────────
                CreatedBoxData createdData = createdDataArr[i];
                
                BoxObj sourceBox = createdData.SourceBox;
                List<BoxObj> createdBoxes = createdData.CreatedBoxCutters;
                //─────────────────────────────────────────────────────────────────────────────
                if (!sourceBox.canInheritChildTrans) continue;
                //─────────────────────────────────────────────────────────────────────────────
                // Create Child Data
                int childAmount = sourceBox.inheritChildDataList.Count;
                int childAmountMinusOne = childAmount - 1;

                // Build NA
                Vector3[] childPosArr = new Vector3[childAmount];
                Quaternion[] childRotArr = new Quaternion[childAmount];
                Vector3[] childSizeArr = new Vector3[childAmount];
                    
                for (int j = 0; j < childAmount; j++)
                {
                    InheritChildData childData = sourceBox.inheritChildDataList[j];
                    
                    childPosArr[j] = childData.childTrans.position + childData.boundPosOffset;
                    childRotArr[j] = Quaternion.Euler(childData.childTrans.eulerAngles + childData.boundRotOffset);
                    childSizeArr[j] = childData.boundScale;
                }
                    
                using NativeArray<Vector3> childPosNa = new NativeArray<Vector3>(childPosArr, Allocator.TempJob);
                using NativeArray<Quaternion> childRotNa = new NativeArray<Quaternion>(childRotArr, Allocator.TempJob);
                using NativeArray<Vector3> childSizeNa = new NativeArray<Vector3>(childSizeArr, Allocator.TempJob);
                //─────────────────────────────────────────────────────────────────────────────
                bool[] childReParented = new bool[childAmount];
                //─────────────────────────────────────────────────────────────────────────────
                int createdCount = createdBoxes.Count;

                for (int v = 0; v < createdCount; v++)
                {
                    //─────────────────────────────────────────────────────────────────────────
                    BoxObj createdBox = createdBoxes[v];
                    //─────────────────────────────────────────────────────────────────────────
                    // Ref Data
                    SubBox[] subArr = new SubBox[createdBox.allSubList.Count];
                    createdBox.allSubList.CopyTo(subArr);
                    using NativeArray<SubBox> subBoxNa = new NativeArray<SubBox>(subArr, Allocator.TempJob);

                    using NativeArray<CellData> kdCellDataNa = new NativeArray<CellData>(createdBox.gridCellDataArr, Allocator.TempJob);
                    using NativeArray<int> kdOriginalIndicesNa = new NativeArray<int>(createdBox.gridOriginalIndicesArr, Allocator.TempJob);  
                    //─────────────────────────────────────────────────────────────────────────
                    // Output
                    using NativeArray<bool> results = new NativeArray<bool>(childAmount, Allocator.TempJob);
                    //─────────────────────────────────────────────────────────────────────────
                    ChildTransConnection childTransConnectionJob = new ChildTransConnection
                    {
                        KdCellData = kdCellDataNa,
                        Subs = subBoxNa,
                        KdOriginalIndices = kdOriginalIndicesNa,
                        KdCellLength = createdBox.gridCellLength,
                        
                        VoxelSize = createdBox.voxelSize,
                        
                        RotX = createdBox.qx,
                        RotY = createdBox.qy,
                        RotZ = createdBox.qz,
                        RotW = createdBox.qw,
                        
                        StartPosX = createdBox.startPosX,
                        StartPosY = createdBox.startPosY,
                        StartPosZ = createdBox.startPosZ,
                        
                        PreLocalRightX = createdBox.vRightX,
                        PreLocalRightY = createdBox.vRightY,
                        PreLocalRightZ = createdBox.vRightZ,
                        
                        PreLocalUpX = createdBox.vUpX,
                        PreLocalUpY = createdBox.vUpY,
                        PreLocalUpZ = createdBox.vUpZ,
                        
                        PreLocalForwardX = createdBox.vFwdX,
                        PreLocalForwardY = createdBox.vFwdY,
                        PreLocalForwardZ = createdBox.vFwdZ,

                        Offset = offset,
                        
                        ChildPos = childPosNa,
                        ChildRots = childRotNa,
                        ChildSizes = childSizeNa,
                        
                        Results = results
                    };

                    childTransConnectionJob.Schedule(childAmount, 64).Complete();
                    //─────────────────────────────────────────────────────────────────────────
                    bool[] resultsArr = new bool[childAmount];
                    results.CopyTo(resultsArr);
                    //─────────────────────────────────────────────────────────────────────────
                    if (createdBox.uniqueId == sourceBox.uniqueId)
                    {
                        for (int j = childAmountMinusOne; j >= 0; j--)
                        {
                            if (!resultsArr[j])
                            {
                                Object.Destroy(createdBox.inheritChildDataList[j].childTrans.gameObject);
                                //createdBox.inheritChildDataList[j].childTrans.parent = null;
                                createdBox.inheritChildDataList.RemoveAt(j);
                            }
                        }
                    }
                    else
                    {
                        for (int j = childAmountMinusOne; j >= 0; j--)
                        {
                            if (resultsArr[j])
                            {
                                createdBox.inheritChildDataList[j].childTrans.parent = createdBox.obj;
                                childReParented[j] = true;
                            }
                            else
                            {
                                createdBox.inheritChildDataList.RemoveAt(j);
                            }
                        }
                    }
                    //─────────────────────────────────────────────────────────────────────────
                }
                //─────────────────────────────────────────────────────────────────────────────
                // Children that have been re-parented should be removed from the source's list so they are not destroyed
                for (int v = childAmountMinusOne; v >= 0; v--)
                {
                    if (childReParented[v])
                    {
                        sourceBox.inheritChildDataList.RemoveAt(v);
                    }
                }
                //─────────────────────────────────────────────────────────────────────────────
            }
        }
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Debris Physics Setup
        //─────────────────────────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Creates physics parent holders for all debris fragments to enable proper physics simulation.
        /// Each debris fragment gets its own parent holder with Rigidbody for independent physics behavior.
        /// Applies initial force and torque based on the destruction impact parameters.
        /// </summary>
        /// <param name="allCreatedBoxesArr">Array of debris BoxObj fragments needing physics parents</param>
        /// <param name="callerData">Caller data containing force and impact information</param>
        private static void CreateDebrisParents(BoxObj[] allCreatedBoxesArr, in CallerData callerData)
        {
            int createdLength = allCreatedBoxesArr.Length;
            // Create physics parent for each debris fragment
            for (int i = 0; i < createdLength; i++)
            {
                allCreatedBoxesArr[i].CreateParentHolder(true, callerData, "Debris Parent");
            }
        }
    }

}