using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using static BoxCutter.BoxIntersectWithBoxJob;
using static BoxCutter.BoxObj;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterSpatialGrid;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               BoxCutter World Physics System
    //─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Interface for replacing the default require anchors check with a custom implementation
    /// </summary>
    public interface IRequireLogic
    {
        bool Run(BoxObj sourceBox, BoxObj[] attachedBoxes);
    }
    
    /// <summary>
    /// Core physics system for BoxCutter managing fragment connectivity and falling behavior.
    /// Handles island detection, anchor attachment analysis, and physics parent creation.
    /// Coordinates between spatial collision detection and Unity's physics simulation.
    /// </summary>
    public static class BoxCutterWorldPhysics
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Data Structures
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Represents a connected group of BoxObj fragments discovered during attachment analysis.
        /// Contains all fragments that are physically connected and their anchor attachment state.
        /// </summary>
        [Serializable]
        public class IslandData
        {
            /// <summary>Original BoxObj before fragmentation</summary>
            public BoxObj sourceBox;

            /// <summary>Primary fragment that initiated the island detection</summary>
            public BoxObj[] createdBox;

            /// <summary>All BoxObj fragments connected to this island</summary>
            public BoxObj[] boxes;

            /// <summary>Whether this island is connected to an anchored object</summary>
            public bool anchored;
        }

        /// <summary>
        /// Container for tracking fragments created from a single source BoxObj during destruction.
        /// Links original object to all generated fragments for processing coordination.
        /// </summary>
        public class CreatedBoxData
        {
            /// <summary>Original BoxObj that was fragmented</summary>
            [ReadOnly] public BoxObj SourceBox;

            /// <summary>All fragment BoxObj instances generated from the source</summary>
            [ReadOnly] public List<BoxObj> CreatedBoxCutters;
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Primary Attachment Analysis
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Main entry point for attachment detection and physics processing.
        /// Analyzes fragment connectivity, determines falling behavior, and handles anchor destruction.
        /// </summary>
        /// <param name="readDataList">Array of fragmentation data to process</param>
        /// <returns>Array of island data containing all connected fragment groups</returns>
        public static IslandData[] AttachedDetection(CreatedBoxData[] readDataList)
        {
            // Perform attachment analysis to identify connected islands
            IslandData[] firstPassIslandData = SolveAttachment(readDataList);

            // Handle destruction of anchors that are now unsupported
            //DestroyAnchors(firstPassIslandData);

            return firstPassIslandData;
        }

        /// <summary>
        /// Analyzes fragment connectivity to determine which pieces should fall together.
        /// Uses spatial overlap testing and non-convex collision detection for accurate results.
        /// Assigns fall turn identifiers to coordinate physics simulation timing.
        /// </summary>
        /// <param name="readDataList">Array of fragmentation data to analyze</param>
        /// <returns>Array of island data representing connected fragment groups</returns>
        private static IslandData[] SolveAttachment(CreatedBoxData[] readDataList)
        {
            BoxCutterManager boxCutterManager = BoxCutterManagerInstance;
            List<BoxObj> globalVisitedBoxes = new List<BoxObj>(); // Track processed fragments
            List<IslandData> islandDataList = new List<IslandData>(); // Accumulate discovered islands

            int readDataLength = readDataList.Length;
            for (int readIndex = 0; readIndex < readDataLength; readIndex++)
            {
                CreatedBoxData readData = readDataList[readIndex];
                List<BoxObj> createdBoxCutters = readData.CreatedBoxCutters;
                BoxObj sourceBox = readData.SourceBox;

                int createdCount = createdBoxCutters.Count;
                BoxObj[] createdBoxCuttersArr = new BoxObj[createdCount];
                createdBoxCutters.CopyTo(createdBoxCuttersArr);

                // Process each fragment to find its connected island
                for (int createdIndex = 0; createdIndex < createdCount; createdIndex++)
                {
                    BoxObj createdBox = createdBoxCuttersArr[createdIndex];
                    
                    // Skip fragments that don't need processing
                    if (createdBox.connectionState == ConnectionStateEnum.Anchored ||
                        createdBox.fallVisited ||
                        createdBox.allSubList.Count == 0) 
                    {
                        continue;
                    }

                    // Find all fragments attached to this one
                    (BoxObj[], bool) attachedData = GetAttached(createdBox, ref globalVisitedBoxes);
                    
                    BoxObj[] attachedBoxes = attachedData.Item1;
                    bool connectedToAnchor = attachedData.Item2;

                    bool requiredIsValid = VerifyRequiredAllValid(attachedBoxes);
                    
                    if (!requiredIsValid)
                    {
                        connectedToAnchor = false;
                    }
                    else
                    {
                        // If there is any rig mode and since the required is all valid then switch the anchor flag to true
                        int attachedLength = attachedBoxes.Length;
                        for (int i = 0; i < attachedLength; i++)
                        {
                            BoxObj box = attachedBoxes[i];
                            if (box.canRequireBoxes && box.useRequireAsAnchor)
                            {
                                connectedToAnchor = true;
                                break;
                            }
                        }
                    }

                    // Create island data for this connected group
                    islandDataList.Add(new IslandData
                    {
                        sourceBox = sourceBox,
                        createdBox = createdBoxCuttersArr,
                        boxes = attachedBoxes,
                        anchored = connectedToAnchor
                    });
                }
            }

            // Clean up visited flags after processing
            int allVisitedCount = globalVisitedBoxes.Count;
            for (int i = 0; i < allVisitedCount; i++)
            {
                BoxObj boxObj = globalVisitedBoxes[i];
                boxObj.fallVisited = false; // Reset for next processing cycle
            }

            // Apply fell turn assignments after all attachment analysis is complete
            int islandLength = islandDataList.Count;
            for (int i = 0; i < islandLength; i++)
            {
                IslandData islandData = islandDataList[i];
                
                // If not anchored, assign fall turn to all fragments in this island
                if (!islandData.anchored)
                {
                    int fallTurn = ++boxCutterManager.boxCutterCurrentFallTurn; // Unique fall timing ID
                    BoxObj[] attachedBoxes = islandData.boxes;
                    int attachedLength = attachedBoxes.Length;

                    // Apply disconnected state and fall timing to all attached fragments
                    for (int v = 0; v < attachedLength; v++)
                    {
                        BoxObj attachedBox = attachedBoxes[v];
                        if (attachedBox.connectionState != ConnectionStateEnum.Anchored) 
                            attachedBox.connectionState = ConnectionStateEnum.Disconnected;
                        
                        attachedBox.fellInTurn = fallTurn;
                    }
                }
            }

            // Convert list to array for return
            IslandData[] islandDataListArray = new IslandData[islandLength];
            islandDataList.CopyTo(islandDataListArray);
            return islandDataListArray;
        }
        
        /// <summary>
        /// Checks if any boxes that require the island to contain a box is missing, if so then the island should not be anchored
        /// </summary>
        /// <param name="attachedBoxes">The detected attached boxes island</param>
        /// <returns></returns>
        private static bool VerifyRequiredAllValid(BoxObj[] attachedBoxes)
        {
            int attachedLength = attachedBoxes.Length;
            for (int i = 0; i < attachedLength; i++)
            {
                BoxObj box = attachedBoxes[i];
                        
                if (!box.canRequireBoxes) continue;
                
                int requiredCount = box.requiredBoxesInheritId.Count;

                if (box.canCusRequireLogic)
                {
                    IRequireLogic cusLogic = box.cusRequireLogic as IRequireLogic;
                    if (cusLogic == null)
                    {
                        Debug.LogWarning("[BoxCutter] The provided cusRequireLogic does not have the IRequireLogic interface implemented");
                        continue;
                    }
                    return cusLogic.Run(box, attachedBoxes);
                }
                
                for (int reqIdx = 0; reqIdx < requiredCount; reqIdx++)
                {
                    int reqInheritId = box.requiredBoxesInheritId[reqIdx];

                    bool match = false;
                    for (int compareIdx = 0; compareIdx < attachedLength; compareIdx++)
                    {
                        if (reqInheritId == attachedBoxes[compareIdx].inheritableId)
                        {
                            match = true;
                            break;
                        }
                    }

                    if (!match)
                        return false;
                }
            }

            return true;
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Attachment Discovery Algorithm
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Performs breadth-first search to find all BoxObj fragments connected to a starting fragment.
        /// Uses both bounding box collision and optional non-convex mesh collision for accuracy.
        /// Returns both the connected fragments and whether the group is anchored to static geometry.
        /// </summary>
        /// <param name="boxObj">Starting fragment to analyze connections from</param>
        /// <param name="visitedBoxes">Global list of processed fragments to avoid duplicates</param>
        /// <param name="cusCompareBoxes">CompareBoxes that overrides the default gathering</param>
        /// <returns>Tuple containing array of attached fragments and anchor connection status</returns>
        private static (BoxObj[], bool) GetAttached(BoxObj boxObj, ref List<BoxObj> visitedBoxes, BoxObj[] cusCompareBoxes = null)
        {
            List<BoxObj> attachedBoxesList = new List<BoxObj> { boxObj }; // Start with the source fragment
            bool connectedToAnchor = false; // Track if any anchor connection is found

            // Use breadth-first search to explore connected fragments
            Queue<BoxObj> currentSet = new Queue<BoxObj>();
            currentSet.Enqueue(boxObj);
            
            bool canOverrideCompareBoxes = cusCompareBoxes != null;

            while (currentSet.Count > 0)
            {
                BoxObj aBox = currentSet.Dequeue();

                BoxObj[] compareBoxes = null;
                if (canOverrideCompareBoxes)
                    compareBoxes = cusCompareBoxes;
                else
                    compareBoxes = GetAllBoxesInCells(aBox);
                int totalCompareCount = compareBoxes.Length;

                SubBox[] aAllSubArr = new SubBox[aBox.allSubList.Count];
                aBox.allSubList.CopyTo(aAllSubArr);
                using NativeArray<SubBox> aAllSubs = new NativeArray<SubBox>(aAllSubArr, Allocator.TempJob);
                
                BoxObj[] candidates = new BoxObj[totalCompareCount];
                int candidateAmount = 0;

                for (int i = 0; i < totalCompareCount; i++)
                {
                    BoxObj bBox = compareBoxes[i];

                    //Debug.Log("Candidates: " +aBox.obj.name + " | " + bBox.obj.name);
                    if (CheckValid(aBox, bBox) != 0)
                        continue;
                    
                    bool canWlAttached = aBox.canWlAttach;
                    //Debug.Log("Passed Valid: " + aBox.obj.name + " to " + bBox.obj.name);
                    if (canWlAttached)
                    {
                        int wlAttachedBoxLength = aBox.wlAttachedBoxInheritId.Count;
                        int[] wlAttachedBoxInheritIds = new int[wlAttachedBoxLength];
                        aBox.wlAttachedBoxInheritId.CopyTo(wlAttachedBoxInheritIds);
                        
                        bool match = false;
                        for (int v = 0; v < wlAttachedBoxLength; v++)
                        {
                            //Debug.Log(bBox.obj.name + " | " + bBox.inheritableId + " | " + wlAttachedBoxInheritIds[v]);
                            if (wlAttachedBoxInheritIds[v] == bBox.inheritableId)
                            {
                                match = true;
                                break;
                            }
                        }

                        if (!match)
                            continue;
                    }

                    bool connection = CheckConnection(ref aBox, ref bBox);
                    
                    //Debug.Log("Connection: " + aBox.obj.name + " | " + bBox.obj.name + " | " + connection);
                    if (connection) candidates[candidateAmount++] = bBox;
                }

                // Check for connection between candidates and aBox
                if (candidateAmount > 0)
                {
                    Array.Resize(ref candidates, candidateAmount);

                    byte[] intersectMask = BoxCutterCollisionUtil.BoxIntersectWithManyBoxes(aBox, candidates);

                    for (int i = 0; i < candidateAmount; i++)
                    {
                        if (intersectMask[i] == 0) continue;

                        BoxObj bBox = candidates[i];

                        attachedBoxesList.Add(bBox);

                        if (bBox.connectionState == ConnectionStateEnum.Anchored)
                        {
                            boxObj.connectionState = ConnectionStateEnum.Connected;
                            connectedToAnchor = true;

                            continue;
                        }

                        bBox.fallVisited = true;
                        visitedBoxes.Add(bBox);

                        currentSet.Enqueue(bBox);
                    }
                }

                aBox.fallVisited = true;
                visitedBoxes.Add(aBox);
            }

            int attachedCount = attachedBoxesList.Count;
            BoxObj[] attachedBoxesArr = new BoxObj[attachedCount];
            attachedBoxesList.CopyTo(attachedBoxesArr);

            return (attachedBoxesArr, connectedToAnchor);
        }

        private static BoxObj[] GetAllBoxesInCells(BoxObj boxObj)
        {
            int totalCompareCount = 0;
            
            int cellCount = boxObj.cellDataList.Count;
            for (int i = 0; i < cellCount; i++)
                totalCompareCount += boxObj.cellDataList[i].boxObjList.Count;

            BoxObj[] allBoxes = new BoxObj[totalCompareCount];

            int offsetCopy = 0;
            for (int i = 0; i < cellCount; i++)
            {
                WorldCellData worldCellData = boxObj.cellDataList[i];

                List<BoxObj> boxCutterList = worldCellData.boxObjList;
                boxCutterList.CopyTo(allBoxes, offsetCopy);
                offsetCopy += boxCutterList.Count;
            }
            
            return allBoxes;
        }

        private static byte CheckValid(BoxObj aBox, BoxObj bBox, bool skipFallCheck = false)
        {
            if (!skipFallCheck && bBox.fallVisited)
                return 1;
            if (aBox.uniqueId == bBox.uniqueId)
                return 2;
            if (bBox.fellInTurn != aBox.fellInTurn)
                return 3;
            if (aBox.cusGroupId != bBox.cusGroupId && bBox.connectionState != ConnectionStateEnum.Anchored)
                return 4;

            return 0;
        }

        /// <summary>
        /// Tests collision contact between two BoxObj instances for anchor support determination.
        /// Uses both bounding box collision and optional non-convex mesh detection.
        /// </summary>
        /// <param name="aBox">First BoxObj to test collision with</param>
        /// <param name="bBox">Second BoxObj to test collision with</param>
        /// <returns>True if objects are in contact and support relationship exists</returns>
        public static bool CheckConnection(ref BoxObj aBox, ref BoxObj bBox)
        {
            aBox.UpdateLiveVars();
            bBox.UpdateLiveVars();

            float offset = BoxCutterManagerInstance.connectionLeniencyDist;

            if (BoxCollisionUtil.CheckSATOverlapRaw
                (
                    aBox.centerPosX, aBox.centerPosY, aBox.centerPosZ,
                    aBox.qx, aBox.qy, aBox.qz, aBox.qw,
                    aBox.sizeX, aBox.sizeY, aBox.sizeZ,
                    bBox.centerPosX, bBox.centerPosY, bBox.centerPosZ,
                    bBox.qx, bBox.qy, bBox.qz, bBox.qw,
                    bBox.sizeX + offset, bBox.sizeY + offset, bBox.sizeZ + offset
                ))
            {
                return true;
            }

            return false;
        }
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Volume & Mass Calculations
        //─────────────────────────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Calculates realistic volume for BoxObj fragments used in physics mass calculations.
        /// Ensures minimum volume based on voxel size to prevent unrealistic lightweight fragments.
        /// Applies mass factor scaling for material density simulation.
        /// </summary>
        /// <param name="boxObj">BoxObj to calculate volume for</param>
        /// <returns>Volume value scaled by mass factor for physics use</returns>
        public static float CalcVolume(BoxObj boxObj)
        {
            float voxelSize = boxObj.voxelSize;
            float massFac = boxObj.massFac; // Material density multiplier

            if (voxelSize == 0) voxelSize = boxObj.voxelSize; // Ensure valid voxel size

            // Calculate raw bounding box volume
            float volume = boxObj.sizeX * boxObj.sizeY * boxObj.sizeZ;
            
            // Apply minimum volume constraint to prevent unrealistic physics
            if (volume < voxelSize)
                return voxelSize * massFac;

            return volume * massFac;
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Physics Parent Management
        //─────────────────────────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Manages physics parent relationships for BoxObj fragments during reorganization.
        /// Temporarily disables physics to prevent interference during transform hierarchy changes.
        /// Preserves velocity and angular velocity when re-enabling physics simulation.
        /// </summary>
        /// <param name="boxObj">BoxObj to manage parent relationships for</param>
        /// <param name="togglePhysics">Whether to temporarily disable physics during reorganization</param>
        public static void ParentHolder(BoxObj boxObj, bool togglePhysics)
        {
            int holderListCount = boxObj.boxColliderHolderList.Count;
            
            if (togglePhysics) BoxCutterManagerInstance.FreezeParent(boxObj);

            boxObj.RemoveFromGroup();
                
            // Reorganize transform hierarchy for all collider holders
            for (int i = 0; i < holderListCount; i++)
            {
                BoxColliderHolder holder = boxObj.boxColliderHolderList[i];
                holder.obj.parent = holder.boxObj.obj; // Re-parent collider to fragment
            }
            
            // Re-enable physics with preserved velocities
            if (togglePhysics) BoxCutterManagerInstance.UnFreezeParent(boxObj);
        }
    }

}