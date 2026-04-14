using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.BoxCutterWorldPhysics;
using static BoxCutter.BoxCutterCaller;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxObj;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace BoxCutter
{
    public static class DestructionTrigger
    {
        public const float BoundEpsilon = 1e-4f;

        public static void TriggerDestruction(CallerData callerData, BoxObj[] boxes)
        {
            Physics.SyncTransforms();
            GetHitBoxes(callerData, true);
            _ = RunDestruction(boxes, callerData);
        }

        public static List<BoxObj> GetHitBoxes(CallerData callerData, bool onlyLookForOnes = false)
        {
            Collider[] colliders = callerData.boxMode ? Physics.OverlapBox(callerData.pos, (callerData.boxBounds + BoundEpsilon) / 2, callerData.boxQuat) : Physics.OverlapSphere(callerData.pos, callerData.radius);

            //List<BoxObj> hitBoxCutters = new List<BoxObj>();

            int colliderLength = colliders.Length;

            for (int i = 0; i < colliderLength; i++)
            {
                Collider col = colliders[i];
                /*Transform colTrans = col.transform;
                Transform colParent = colTrans.parent;*/

                if (col is BoxCollider)
                {
                    if (col.TryGetComponent(out BoxCutterOneDebris oneDebris))
                    {
                        oneDebris.ReturnToPool();
                    }
                    /*else if (!onlyLookForOnes && colParent != null)
                    {
                        if (colParent.TryGetComponent(out BoxColliderHolder holder))
                        {
                            hitBoxCutters.Add(holder.boxObj);
                        }
                    }*/
                }
                /*else if (!onlyLookForOnes && col is MeshCollider)
                {
                    if (col.TryGetComponent(out BoxColliderHolder holder))
                    {
                        hitBoxCutters.Add(holder.boxObj);
                    }
                }*/
            }

            /*var distinctHits = hitBoxCutters
                .GroupBy(b => b.uniqueId)
                .Select(g => g.First())
                .ToList();
                
            return distinctHits;*/
            
            if (onlyLookForOnes) return new List<BoxObj>();

            BoxObj[] hitBoxesInCells = BoxCutterCollisionUtil.FindCallerHitCell(callerData);
            byte[] isHitArr = BoxCutterCollisionUtil.CallerIntersectWithBoxBatch(hitBoxesInCells, callerData);

            List<BoxObj> finalBoxes = new List<BoxObj>();
            int boxAmount = hitBoxesInCells.Length;
            for (int i = 0; i < boxAmount; i++)
            {
                if (isHitArr[i] == 1)
                {
                    finalBoxes.Add(hitBoxesInCells[i]);
                }
            }
            
            return finalBoxes;
        }
        
        public static BoxObj[] GetTrueBoxArr(CallerData callerData, List<BoxObj> ogBoxList, List<int> validInheritIds = null)
        {
            int hitBoxCount = ogBoxList.Count;
            List<BoxObj> boxList = new List<BoxObj>();

            if (callerData.useWhiteList)
            {
                int whiteListedCount = callerData.whiteListedIds.Count;

                for (int i = 0; i < hitBoxCount; i++)
                {
                    BoxObj box = ogBoxList[i];

                    int boxCutterId = box.inheritableId;
                    bool whiteListed = false;

                    for (int v = 0; v < whiteListedCount; v++)
                    {
                        int whiteId = callerData.whiteListedIds[v];
                        if (boxCutterId == whiteId)
                        {
                            whiteListed = true;
                            break;
                        }
                    }

                    if (whiteListed)
                        boxList.Add(box);
                }
            }
            else
            {
                int blackListedCount = callerData.blackListedIds.Count;

                for (int i = 0; i < hitBoxCount; i++)
                {
                    BoxObj box = ogBoxList[i];

                    int boxCutterId = box.inheritableId;
                    bool blackListed = false;

                    for (int v = 0; v < blackListedCount; v++)
                    {
                        int blackId = callerData.blackListedIds[v];
                        if (boxCutterId == blackId)
                        {
                            blackListed = true;
                            break;
                        }
                    }

                    if (!blackListed)
                        boxList.Add(box);
                }
            }

            if (validInheritIds != null && validInheritIds.Count > 0)
            {
                var valid = new HashSet<int>(validInheritIds);
                for (int v = boxList.Count - 1; v >= 0; v--)
                {
                    if (!valid.Contains(boxList[v].inheritableId))
                        boxList.RemoveAt(v);
                }
            }

            int boxLength = boxList.Count;
            BoxObj[] boxArr = new BoxObj[boxLength];
            boxList.CopyTo(boxArr);

            return boxArr;
        }

        public static void CleanNullFromBox(BoxObj[] boxArr)
        {
            int boxLength = boxArr.Length;

            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = boxArr[i];
                box.BuildConnectionData();
            }

            CleanBoxInheritChild(boxArr);
        }

        private static void CleanBoxInheritChild(BoxObj[] boxArr)
        {
            int boxLength = boxArr.Length;

            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = boxArr[i];
                int inheritChildCount = box.inheritChildDataList.Count;
                int inheritChildMinusOne = inheritChildCount - 1;

                for (int v = inheritChildMinusOne; v >= 0; v--)
                {
                    InheritChildData childData = box.inheritChildDataList[v];

                    if (childData.childTrans == null)
                    {
                        box.inheritChildDataList.RemoveAt(v);
                    }
                }
            }
        }

        public static async Task RunDestruction(BoxObj[] boxArr, CallerData callerData)
        {
            //─────────────────────────────────────────────────────────────────────────────────────
            BoxCutterManager manager = BoxCutterManagerInstance;
            //─────────────────────────────────────────────────────────────────────────────────────
            // Set Up Box
            int boxLength = boxArr.Length;

            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = boxArr[i];
                //box.destroying = true;
                box.StopReturnToPool();
            }

            manager.destroying = true;
            //─────────────────────────────────────────────────────────────────────────────────────
            bool canPrintMs = callerData.canPrintMs;
            BoxCutterCaller caller = callerData.ogCaller;

            // Execute destruction pipeline
            Stopwatch sw = Stopwatch.StartNew();
            (SubBox[][][] mainIslandSubs, SubBox[][][] debrisIslandSubs, Vector3[] hitBoxPosArr, Quaternion[] hitBoxRotArr, Vector3[] hitBoxSizeArr, bool[] missedBoxes) pipelineIslandData = await DestructionPipeline.Destroy(boxArr, callerData);
            // Debugging
            //await Task.Delay(1000);
            /*sw.Stop();
            Debug.Log($"Destroy Ms: {sw.ElapsedMilliseconds}");*/
            if (!IsPlaying) return;

            // Remove Invalid Boxes and their data
            int count = boxArr.Length;

            var filteredBoxes = new List<BoxObj>(count);
            var filteredMissed = new List<bool>(count);
            var filteredMain = new List<SubBox[][]>(count);
            var filteredDebris = new List<SubBox[][]>(count);
            var filteredHitPos = new List<Vector3>(count);
            var filteredHitRot = new List<Quaternion>(count);
            var filteredHitSize = new List<Vector3>(count);

            for (int i = 0; i < count; i++)
            {
                if (pipelineIslandData.missedBoxes[i])
                    continue;

                filteredBoxes.Add(boxArr[i]);
                filteredMissed.Add(pipelineIslandData.missedBoxes[i]);
                filteredMain.Add(pipelineIslandData.mainIslandSubs[i]);
                filteredDebris.Add(pipelineIslandData.debrisIslandSubs[i]);
                filteredHitPos.Add(pipelineIslandData.hitBoxPosArr[i]);
                filteredHitRot.Add(pipelineIslandData.hitBoxRotArr[i]);
                filteredHitSize.Add(pipelineIslandData.hitBoxSizeArr[i]);
            }

            boxArr = filteredBoxes.ToArray();

            // If All Miss Early Exit
            if (boxArr.Length == 0)
            {
                Finish();
                return;
            }

            pipelineIslandData = (
                mainIslandSubs: filteredMain.ToArray(),
                debrisIslandSubs: filteredDebris.ToArray(),
                hitBoxPosArr: filteredHitPos.ToArray(),
                hitBoxRotArr: filteredHitRot.ToArray(),
                hitBoxSizeArr: filteredHitSize.ToArray(),
                missedBoxes: filteredMissed.ToArray()
            );

            BuildPipeline.CreateBoxObjData[] createBoxData = BuildPipeline.BuildCreateBoxData(boxArr);

            List<BoxCutterOneDebris> allOneDebris = new List<BoxCutterOneDebris>();

            // Create main fragments with OneDebris tracking
            var mainCreateData = BuildPipeline.CreateAllNewMainBoxObjs(createBoxData, pipelineIslandData.mainIslandSubs, callerData, oneDebrisCallback: oneDebris => allOneDebris.Add(oneDebris));
            CreatedBoxData[] mainCreatedBoxData = mainCreateData.createdBoxData;
            BoxObj[] mainAllCreatedBoxes = mainCreateData.allCreatedBoxesArr;

            sw.Restart();
            SetUpAttachment(mainCreatedBoxData, mainAllCreatedBoxes);
            if (canPrintMs) PrintPassMs(sw, "Attached");

            IslandData[] islandDataArr = AttachedDetection(mainCreatedBoxData);

            //sw.Restart();
            await SetConvexForIsland(islandDataArr);
            //Debug.Log($"Set Convex For Island Ms: {sw.ElapsedMilliseconds}");

            //await Task.Delay(1000);
            //sw.Restart();
            await BuildPipeline.BuildNewKdTree(mainAllCreatedBoxes, IsWebGL || !manager.asyncDestruction);
            //Debug.Log($"Build Kd Tree Ms: {sw.ElapsedMilliseconds}");
            //sw.Restart();
            await BuildPipeline.BuildPost(mainAllCreatedBoxes, false, callerData);
            //Debug.Log($"Build Post Ms: {sw.ElapsedMilliseconds}");
            
            // Re-parents child trans to created boxes
            BuildPipeline.ReParentChildTrans(mainCreatedBoxData);

            if (manager.CheckExit()) return;
            // Return BoxObjs to object pool
            BuildPipeline.ReturnBoxes(boxArr, false);
            //─────────────────────────────────────────────────────────────────────────────────────
            /*Debug.Log($"[DestructionTrigger] Island count before ReParent: {islandDataArr.Length}");
            for (int i = 0; i < islandDataArr.Length; i++)
            {
                IslandData island = islandDataArr[i];
                Debug.Log($"[Island {i}] Anchored: {island.anchored}, Boxes count: {island.boxes?.Length ?? 0}, CreatedBox count: {island.createdBox?.Length ?? 0}");

                if (island.boxes != null)
                {
                    for (int j = 0; j < island.boxes.Length; j++)
                    {
                        BoxObj box = island.boxes[j];
                        Debug.Log($"  [Island {i}, Box {box.obj.name}] UniqueId: {box?.uniqueId}, InheritableId: {box?.inheritableId}, ParentHolder: {(box?.parentHolder != null ? "exists" : "null")}");
                    }
                }
            }*/
            //─────────────────────────────────────────────────────────────────────────────────────
            // Call Events
            bool callerExists = caller != null;
            if (callerExists) caller.RaiseOnIslandsCreated(islandDataArr, callerData);
            if (callerExists) caller.RaiseHitResults(islandDataArr);
            
            // Fire per-BoxObj fragment creation callbacks
            FirePerBoxObjFragmentCallbacks(mainCreatedBoxData);

            // Fire event for main fragment batch
            if (caller != null)
            {
                caller.RaiseOnFragmentBatchCreated(new FragmentBatch
                {
                    Fragments = mainCreatedBoxData,
                    OneDebris = new BoxCutterOneDebris[0], // No OneDebris in main fragments typically
                    IsComplete = false,
                    CreationProgress = 0.5f
                });
            }
            //─────────────────────────────────────────────────────────────────────────────────────
            UpdateBoxCutterGroup(islandDataArr);

            SpawnDebris(allOneDebris, callerData, mainCreatedBoxData, createBoxData, pipelineIslandData.debrisIslandSubs);
            await ReParentBox(islandDataArr, callerData);
            //─────────────────────────────────────────────────────────────────────────────────────
            Finish();
            sw.Stop();
            //─────────────────────────────────────────────────────────────────────────────────────
        }
        
        private static void Finish()
        {
            BoxCutterManager manager = BoxCutterManagerInstance;
            manager.destroying = false;
            manager.onDestroyFinish.Invoke();

            // After onDestroyFinish, any pending boxes will all add to the manager, where the following code will find the one with the lowest queue id to insure FIFO 
            RunNextInQueue();
        }

        private static void RunNextInQueue()
        {
            BoxCutterManager manager = BoxCutterManagerInstance;

            int queuedCount = manager.currentFrameQueuedBoxes.Count;

            int lId = IntInfinity;
            BoxObj lBox = null;
            QueuedDestroyData lQueued = null;
            int lQueuedIdx = 0;

            for (int i = 0; i < queuedCount; i++)
            {
                BoxObj box = manager.currentFrameQueuedBoxes[i];
                QueuedDestroyData queuedDestroy = box.queuedCallers[0];
                int queuedId = queuedDestroy.queueId;
                if (queuedId < lId)
                {
                    lId = queuedId;
                    lBox = box;
                    lQueued = queuedDestroy;
                    lQueuedIdx = i;
                }

                //Debug.Log("In Queued: " + box.obj.name + " | " + queuedId);
            }

            if (lBox == null)
            {
                // Nothing in queue
                //Debug.LogWarning($"[BoxCutter] System could not find a box in the queue, please contact support to resolve the issue.");
                return;
            }
            
            manager.currentFrameQueuedBoxes.RemoveAt(lQueuedIdx);
            
            const float threshold = 0.01f;
            const float dotThreshold = 1.0f - threshold;
            
            Transform obj = lBox.obj;
            
            Vector3 rootCallWorldPos = obj.TransformPoint(lQueued.localPos);
            Quaternion rootCallWorldRot = obj.TransformRotation(lQueued.localRot);
            
            lQueued.callerData.pos = rootCallWorldPos;
            lQueued.callerData.boxQuat = rootCallWorldRot;
            // Find other queued boxes via a coarse 1. Find all matching caller data id's -> 2. Overlap check -> 3. Batch process everything
            ref CallerData lCallerData = ref lQueued.callerData;
            bool boxMode = lCallerData.boxMode;
            int lCallId = lCallerData.id;

            /*manager.currentFrameQueuedBoxes.Clear();
            TriggerDestruction(lQueued.callerData, batchBoxes);*/
            
            Physics.SyncTransforms();
            List<BoxObj> hitBoxes = GetHitBoxes(lQueued.callerData);
            
            int hitCount = hitBoxes.Count;
            BoxObj[] hitBoxesArr = new BoxObj[hitCount];
            hitBoxes.CopyTo(hitBoxesArr);
            bool[] sameCaller = new bool[hitCount];
            int sameCallerAcc = 0;
            
            for (int i = 0; i < hitCount; i++)
            {
                BoxObj hitBox = hitBoxes[i];
                if (hitBox.uniqueId == lBox.uniqueId || hitBox.fellInTurn != lBox.fellInTurn)
                    continue;

                sameCaller[i] = true;
                sameCallerAcc++;
                
                int hitQueuedCount = hitBox.queuedCallers.Count;
                for (int v = 0; v < hitQueuedCount; v++)
                {
                    QueuedDestroyData destroyData = hitBox.queuedCallers[v];
                    
                    if (destroyData.callerData.id == lCallId)
                    {
                        QueuedDestroyData qDestroyData = hitBox.queuedCallers[v];
                        Transform qBoxObj = hitBox.obj;
                        
                        // Position Comparison
                        Vector3 qWorldPos = qBoxObj.TransformPoint(qDestroyData.localPos);
                        float dx = qWorldPos.x - rootCallWorldPos.x;
                        float dy = qWorldPos.y - rootCallWorldPos.y;
                        float dz = qWorldPos.z - rootCallWorldPos.z;

                        if ((dx < 0 ? -dx : dx) > threshold || 
                            (dy < 0 ? -dy : dy) > threshold || 
                            (dz < 0 ? -dz : dz) > threshold) 
                            break;

                        // Rotation Comparison
                        if (boxMode)
                        {
                            Quaternion qWorldRot = qBoxObj.TransformRotation(qDestroyData.localRot);
                            // Dot product handles the q == -q case automatically
                            float dot = (qWorldRot.x * rootCallWorldRot.x) + 
                                        (qWorldRot.y * rootCallWorldRot.y) + 
                                        (qWorldRot.z * rootCallWorldRot.z) + 
                                        (qWorldRot.w * rootCallWorldRot.w);

                            // Use absolute value of dot product
                            float absDot = dot < 0 ? -dot : dot;

                            if (absDot < dotThreshold) 
                                break;
                        }
            
                        hitBox.queuedCallers.RemoveAt(v);
                        break;
                    }
                }
            }

            int sameCallerIdx = 0;
            for (int i = 0; i < hitCount; i++)
            {
                if (sameCaller[i]) hitBoxesArr[sameCallerIdx++] = hitBoxes[i];
            }
            
            Array.Resize(ref hitBoxesArr, sameCallerAcc + 1);
            hitBoxesArr[sameCallerAcc] = lBox;
            
            lBox.queuedCallers.RemoveAt(0);
            manager.currentFrameQueuedBoxes.Clear();
            _ = RunDestruction(hitBoxesArr, lQueued.callerData);
        }

        private static void SetUpAttachment(CreatedBoxData[] createdBoxData, BoxObj[] allCreatedBoxesArr)
        {
            int allCreatedBoxesLength = allCreatedBoxesArr.Length;
            for (int i = 0; i < allCreatedBoxesLength; i++)
            {
                BoxObj createdBox = allCreatedBoxesArr[i];
                createdBox.ready = true;
                createdBox.CalcPartitionLocation(true);
            }
            
            int createdBoxLength = createdBoxData.Length;
            for (int i = 0; i < createdBoxLength; i++)
            {
                CreatedBoxData createdBox = createdBoxData[i];
                createdBox.SourceBox.RemoveFromPartition();
            }
        }
        
        public static void UpdateBoxCutterGroup(IslandData[] islandDataArr)
        {
            int islandLength = islandDataArr.Length;

            for (int i = 0; i < islandLength; i++)
            {
                IslandData islandData = islandDataArr[i];
                if (!islandData.anchored) continue;

                int createdLength = islandData.createdBox.Length;

                for (int v = 0; v < createdLength; v++)
                {
                    BoxObj createBox = islandData.createdBox[v];
                    if (createBox == null || createBox.group == null) continue;
                    createBox.group.boxList.Add(createBox);
                }
            }
        }

        private static async void SpawnDebris(List<BoxCutterOneDebris> allOneDebris, CallerData callerData, CreatedBoxData[] mainCreatedBoxData, BuildPipeline.CreateBoxObjData[] createBoxData, SubBox[][][] debrisIslandSubs)
        {
            BoxCutterCaller caller = callerData.ogCaller;

            // Create debris fragments - use sync for WebGL, async for other platforms
            List<BoxCutterOneDebris> debrisOneDebris = new List<BoxCutterOneDebris>();
            CreatedBoxData[] debrisCreatedBoxData = new CreatedBoxData[] { };

            try
            {
                debrisCreatedBoxData = await BuildPipeline.CreateDebris(createBoxData, debrisIslandSubs, callerData,
                    batchCallback: (fragments, oneDebris, progress) =>
                    {
                        if (caller != null)
                        {
                            caller.RaiseOnFragmentBatchCreated(new FragmentBatch
                            {
                                Fragments = fragments,
                                OneDebris = oneDebris,
                                IsComplete = false,
                                CreationProgress = 0.5f + (progress * 0.5f)
                            });
                        }
                    },
                    oneDebrisCallback: (oneDebris) =>
                    {
                        debrisOneDebris.Add(oneDebris);
                        allOneDebris.Add(oneDebris);
                    },
                    cancellationToken: caller != null ? (caller.destroyCancellationTokenSource?.Token ?? CancellationToken.None) : CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Async operation was cancelled (likely due to exiting play mode)
                return;
            }
            finally
            {
                // Fire per-BoxObj fragment creation callbacks for debris
                FirePerBoxObjFragmentCallbacks(debrisCreatedBoxData);

                // Fire final completion event with all fragments
                if (caller != null)
                {
                    caller.RaiseOnAllFragmentsComplete(new FragmentBatch
                    {
                        Fragments = CombineFragmentArrays(mainCreatedBoxData, debrisCreatedBoxData),
                        OneDebris = allOneDebris.ToArray(),
                        IsComplete = true,
                        CreationProgress = 1.0f
                    });

                    caller.RaiseOnDestructionComplete(callerData);
                }
            }
        }

        /// <summary>
        /// Fires the OnFragmentsCreated callback for each source BoxObj with its corresponding fragments.
        /// Groups fragments by their source BoxObj and triggers individual callbacks.
        /// </summary>
        /// <param name="createdBoxDataArray">Array of created fragment data</param>
        private static void FirePerBoxObjFragmentCallbacks(CreatedBoxData[] createdBoxDataArray)
        {
            if (createdBoxDataArray == null || createdBoxDataArray.Length == 0) return;

            int dataLength = createdBoxDataArray.Length;
            for (int i = 0; i < dataLength; i++)
            {
                CreatedBoxData createdBoxData = createdBoxDataArray[i];
                if (createdBoxData?.SourceBox != null)
                {
                    // Fire the callback on the source BoxObj with its fragments
                    createdBoxData.SourceBox.FireOnFragmentsCreated(new CreatedBoxData[] { createdBoxData });
                }
            }
        }

        /// <summary>
        /// Combines main and debris fragment arrays into a single array.
        /// </summary>
        /// <param name="mainFragments">Main fragment array</param>
        /// <param name="debrisFragments">Debris fragment array</param>
        /// <returns>Combined fragment array</returns>
        private static CreatedBoxData[] CombineFragmentArrays(CreatedBoxData[] mainFragments, CreatedBoxData[] debrisFragments)
        {
            if (mainFragments == null) mainFragments = new CreatedBoxData[0];
            if (debrisFragments == null) debrisFragments = new CreatedBoxData[0];

            CreatedBoxData[] combined = new CreatedBoxData[mainFragments.Length + debrisFragments.Length];
            Array.Copy(mainFragments, 0, combined, 0, mainFragments.Length);
            Array.Copy(debrisFragments, 0, combined, mainFragments.Length, debrisFragments.Length);
            return combined;
        }

        public static async Task SetConvexForIsland(IslandData[] islandDataArr)
        {
            BoxCutterManager manager = BoxCutterManagerInstance;
            
            bool sync = IsWebGL || !manager.asyncDestruction;
            int islandLength = islandDataArr.Length;
            
            for (int i = 0; i < islandLength; i++)
            {
                IslandData islandData = islandDataArr[i];

                int boxesCount = islandData.boxes.Length;
                BoxObj[] islandBoxes = islandData.boxes;
                if (islandData.anchored)
                {
                    continue;
                }

                for (int v = 0; v < boxesCount; v++)
                {
                    BoxObj box = islandBoxes[v];

                    int holderCount = box.boxColliderHolderList.Count;

                    for (int j = 0; j < holderCount; j++)
                    {
                        BoxColliderHolder holder = box.boxColliderHolderList[j];

                        if (box.colliderGenMode != ColliderGenModeEnum.Perfect)
                        {
                            if (box.CheckExit()) return;
                            if (holder.isMeshCollider && !holder.meshCollider.convex)
                            {
                                // Regen as Box Colliders
                                await box.CalcColliders(sync);
                                if (box.CheckExit() || !IsPlaying) return;
                                break;
                            }
                        }
                    }
                }
            }
        }

        public static async Task ReParentBox(IslandData[] islandDataArr, CallerData callerData)
        {
            BoxCutterManager manager = BoxCutterManagerInstance;
            int islandLength = islandDataArr.Length;

            bool[] sameIslandArr = new bool[islandLength];
            Vector3[] lastVeloArr = new Vector3[islandLength];

            // This is here to prevent a bug where Boxes can be reparented under the same parent when it should not be. Imagine this:
            // A singular object under a rb is destroyed and split into 2 parts. The system will check for the parent holder and if the inheritId matches
            // in this case both parts have the same inheritId as they come from the same object, but without the usedParents check both would be reparented.
            List<BoxCutterParent> usedParents = new List<BoxCutterParent>();

            for (int i = 0; i < islandLength; i++)
            {
                IslandData islandData = islandDataArr[i];

                BoxObj[] islandBoxes = islandData.boxes;
                int boxesCount = islandBoxes.Length;
                if (islandData.anchored)
                {
                    continue;
                }

                /*bool useSameParentHolder = true;

                BoxObj firstBox = islandBoxes[0];

                if (firstBox.parentHolder != null)
                {
                    if (usedParents.Contains(firstBox.parentHolder))
                    {
                        useSameParentHolder = false;
                    }
                    else
                    {
                        BoxObj[] preExistingChildren = firstBox.parentHolder.childrenBoxes;
                        int preExistingChildrenCount = preExistingChildren.Length;

                        for (int j = 0; j < preExistingChildrenCount; j++)
                        {
                            BoxObj preExistingChild = preExistingChildren[j];
                            bool matched = false;
                            for (int k = 0; k < boxesCount; k++)
                            {
                                if (preExistingChild.inheritableId == islandBoxes[k].inheritableId)
                                {
                                    matched = true;
                                    break;
                                }
                            }

                            if (!matched)
                            {
                                useSameParentHolder = false;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    useSameParentHolder = false;
                }

                if (useSameParentHolder)
                {
                    for (int v = 0; v < boxesCount; v++)
                        manager.FreezeParent(islandBoxes[v]);

                    for (int v = 0; v < boxesCount; v++)
                    {
                        BoxObj box = islandBoxes[v];
                        ParentHolder(box, false);
                    }

                    for (int v = 0; v < boxesCount; v++)
                        manager.UnFreezeParent(islandBoxes[v]);

                    usedParents.Add(firstBox.parentHolder);
                    sameIslandArr[i] = true;
                    continue;
                }*/

                float totalVolume = 0f;
                float3 center = float3Zero;
                bool foundLastVelo = false;

                for (int v = 0; v < boxesCount; v++)
                {
                    BoxObj compareBox = islandBoxes[v];

                    center += new float3(compareBox.centerPosX, compareBox.centerPosY, compareBox.centerPosZ);
                    totalVolume += CalcVolume(compareBox);

                    if (!foundLastVelo && compareBox.lastVelocity != vecZero)
                    {
                        lastVeloArr[i] = compareBox.lastVelocity;
                        foundLastVelo = true;
                    }
                }
                
                /*if (manager.CheckExit()) return;
                bool sync = IsWebGL || !manager.asyncDestruction;

                for (int v = 0; v < boxesCount; v++)
                {
                    BoxObj box = islandBoxes[v];

                    int holderCount = box.boxColliderHolderList.Count;

                    for (int j = 0; j < holderCount; j++)
                    {
                        BoxColliderHolder holder = box.boxColliderHolderList[j];

                        if (box.colliderGenMode != ColliderGenModeEnum.Perfect)
                        {
                            if (box.CheckExit()) return;
                            if (holder.isMeshCollider && !holder.meshCollider.convex)
                            {
                                // Regen as Box Colliders
                                await box.CalcColliders(sync);
                                if (box.CheckExit() || !IsPlaying) return;
                                break;
                            }
                        }
                    }
                }*/
                
                if (manager.CheckExit() || !IsPlaying) return;

                GameObject parentObj = new GameObject("Rb Parent");
                BoxCutterParent islandParent = parentObj.AddComponent<BoxCutterParent>();
                Rigidbody islandParentRb = parentObj.AddComponent<Rigidbody>();
                islandParentRb.isKinematic = true;
                islandParent.childrenBoxes = islandBoxes;
                Transform islandParentObj = islandParent.transform;
                islandParent.obj = islandParentObj;
                islandParent.rb = islandParentRb;

                Vector3 centerPos = center / boxesCount;
                //islandParentRb.MovePosition(centerPos);
                islandParentObj.position = centerPos;
                islandParentRb.position = centerPos; 
                
                islandParentRb.mass = totalVolume;

                // Check for exit before freezing parents - cleanup if exiting
                if (manager.CheckExit() || !IsPlaying)
                {
                    Object.Destroy(parentObj);
                    return;
                }

                for (int v = 0; v < boxesCount; v++)
                    manager.FreezeParent(islandBoxes[v]);
                
                /*for (int v = 0; v < boxesCount; v++)
                {
                    BoxObj box = islandBoxes[v];
                    BoxCutterParent parentHolder = box.parentHolder;
                    if (parentHolder == null) continue;
                    parentHolder.rb.isKinematic = true;
                }*/

                // Seperated to make sure all the box's parents have been set to kinematic
                for (int v = 0; v < boxesCount; v++)
                {
                    BoxObj box = islandBoxes[v];

                    if (box.CheckExit(1))
                    {
                        Object.Destroy(parentObj);
                        return;
                    }

                    box.obj.parent = islandParentObj;
                    box.parentHolder = islandParent;
                }

                // Seperated so the rb is not static for more than a frame due to calc collider
                for (int v = 0; v < boxesCount; v++)
                {
                    BoxObj box = islandBoxes[v];

                    if (box.CheckExit(1))
                    {
                        Object.Destroy(parentObj);
                        return;
                    }

                    ParentHolder(box, false);
                }
                
                islandParentRb.interpolation = RigidbodyInterpolation.Interpolate;

                for (int v = 0; v < boxesCount; v++)
                    manager.UnFreezeParent(islandBoxes[v]);
            }

            await manager.SetKineToFalse(islandDataArr, sameIslandArr, lastVeloArr, callerData);
        }
    }
}