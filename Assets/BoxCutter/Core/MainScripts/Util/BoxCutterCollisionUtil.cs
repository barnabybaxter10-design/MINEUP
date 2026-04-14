using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using static BoxCutter.BoxCutterCaller;
using static BoxCutter.BoxIntersectWithBoxJob;
using static BoxCutter.BoxObj;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterSpatialGrid;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    public static class BoxCutterCollisionUtil
    {
        public static BoxObj[] FindCallerHitCell(CallerData callerData)
        {
            BoxCutterSpatialGrid spatialGrid = BoxCutterSpatialGridInstance;

            int totalCells = spatialGrid.totalCells;
            using NativeArray<byte> intersectionResults = new NativeArray<byte>(totalCells, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            
            var cellIntersectionJob = new CellIntersectionJob
            {
                // Grid configuration
                GridMin = spatialGrid.gridMin,
                CellSizeX = spatialGrid.cellSizeX,
                CellSizeY = spatialGrid.cellSizeY,
                CellSizeZ = spatialGrid.cellSizeZ,
                CellAmountPerX = spatialGrid.cellAmountPerX,
                CellAmountPerY = spatialGrid.cellAmountPerY,
                CellAmountPerZ = spatialGrid.cellAmountPerZ,
                
                // Caller data
                CallerPosX = callerData.pos.x,
                CallerPosY = callerData.pos.y,
                CallerPosZ = callerData.pos.z,
                CallerRadius = callerData.radius,
                CallerBoxSizeX = callerData.boxBoundsX,
                CallerBoxSizeY = callerData.boxBoundsY,
                CallerBoxSizeZ = callerData.boxBoundsZ,
                CallerBoxQuatX = callerData.boxRotX,
                CallerBoxQuatY = callerData.boxRotY,
                CallerBoxQuatZ = callerData.boxRotZ,
                CallerBoxQuatW = callerData.boxRotW,
                
                IsBoxMode = callerData.boxMode,
                
                // Output
                IntersectionResults = intersectionResults
            }.Schedule(totalCells, 32);
            cellIntersectionJob.Complete();

            byte[] intersectionArr = new byte[totalCells];
            intersectionResults.CopyTo(intersectionArr);
            
            // Get Boxes
            int totalBoxAmount = 0;

            for (int i = 0; i < totalCells; i++)
            {
                if (intersectionArr[i] == 0) continue;
                totalBoxAmount += spatialGrid.gridData[i].boxObjList.Count;
            }
            
            int[] ids = new int[totalBoxAmount];
            BoxObj[] boxArr = new BoxObj[totalBoxAmount];
            for (int i = 0; i < totalBoxAmount; i++)
            {
                ids[i] = -1000;
            }

            int boxAmount = 0;
            for (int i = 0; i < totalCells; i++)
            {
                if (intersectionArr[i] == 0) continue;

                BoxCutterSpatialGrid.WorldCellData worldCell = spatialGrid.gridData[i];
                int boxCount = worldCell.boxObjList.Count;
                for (int v = 0; v < boxCount; v++)
                {
                    BoxObj boxObj = worldCell.boxObjList[v];

                    bool exists = false;
                    for (int j = 0; j < boxAmount; j++)
                    {
                        if (boxObj.uniqueId == ids[j])
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        ids[boxAmount] = boxObj.uniqueId;
                        boxArr[boxAmount++] = boxObj;
                    }
                }
            }
            
            Array.Resize(ref boxArr, boxAmount);
            return boxArr;
        }
        
        public static byte[] CallerIntersectWithBoxBatch(BoxObj[] boxArr, CallerData callerData)
        {
            int boxAmount = boxArr.Length;
            // Get Flattened Subs
            int totalSubAmount = 0;
            for (int i = 0; i < boxAmount; ++i)
            {
                totalSubAmount += boxArr[i].allSubList.Count;
            }

            var boxTransArr = new DestructionPipeline.BoxTrans[boxAmount];
            var allSubsArr = new SubBox[totalSubAmount];
            var boxPointerArr = new int[totalSubAmount];
            int cursorFlat = 0;
            for (int i = 0; i < boxAmount; i++)
            {
                BoxObj boxObj = boxArr[i];
                int subCount = boxObj.allSubList.Count;
                SubBox[] localBoxArr = new SubBox[subCount];
                boxObj.allSubList.CopyTo(localBoxArr);

                boxObj.UpdateLiveVars(true);
                boxTransArr[i] = boxObj.CreateBoxData();
                
                for (int j = 0; j < subCount; j++)
                {
                    allSubsArr[cursorFlat] = localBoxArr[j];
                    boxPointerArr[cursorFlat] = i;
                    cursorFlat++;
                }
            }

            using NativeArray<SubBox> subBoxNa = new NativeArray<SubBox>(allSubsArr, Allocator.TempJob);
            using NativeArray<int> boxPointerNa = new NativeArray<int>(boxPointerArr, Allocator.TempJob);
            using NativeArray<DestructionPipeline.BoxTrans> boxTransNa = new NativeArray<DestructionPipeline.BoxTrans>(boxTransArr, Allocator.TempJob);
            using NativeArray<byte> boxHit = new NativeArray<byte>(boxAmount, Allocator.TempJob);

            var preciseHandle = new PreciseHitCheck
            {
                AllSubs = subBoxNa,
                SubBoxPointerToBox = boxPointerNa,
                BoxData = boxTransNa,
                BoxHit = boxHit,
                
                HitPosX = callerData.pos.x,
                HitPosY = callerData.pos.y,
                HitPosZ = callerData.pos.z,
                Radius = callerData.radius,
                BoxMode = callerData.boxMode,
                BoxRotX = callerData.boxRotX,
                BoxRotY = callerData.boxRotY,
                BoxRotZ = callerData.boxRotZ,
                BoxRotW = callerData.boxRotW,
                BoxSizeX = callerData.boxBoundsX,
                BoxSizeY = callerData.boxBoundsY,
                BoxSizeZ = callerData.boxBoundsZ,
            }.Schedule(totalSubAmount, 32);
            
            preciseHandle.Complete();

            // Final Filter
            byte[] boxHitArr = new byte[boxAmount];
            boxHit.CopyTo(boxHitArr);

            return boxHitArr;
        }

        public static bool CallerIntersectWithBox(CallerData callerData, BoxObj box, bool precise)
        {
            if (callerData.boxMode)
            {
                if (!BoxCollisionUtil.CheckSATOverlapRaw
                    (
                        box.centerPosX, box.centerPosY, box.centerPosZ, box.qx, box.qy, box.qz, box.qw, box.sizeX, box.sizeY, box.sizeZ,
                        callerData.pos.x, callerData.pos.y, callerData.pos.z, callerData.boxRotX, callerData.boxRotY, callerData.boxRotZ, callerData.boxRotW, callerData.boxBoundsX, callerData.boxBoundsY, callerData.boxBoundsZ
                    ))
                {
                    return false;
                }
            }
            else
            {
                if (!BoxCollisionUtil.IsSphereIntersectingCuboidRaw
                    (
                        callerData.pos.x, callerData.pos.y, callerData.pos.z, callerData.radius,
                        box.centerPosX, box.centerPosY, box.centerPosZ, box.qx, box.qy, box.qz, box.qw, box.sizeX, box.sizeY, box.sizeZ
                    ))
                {
                    return false;
                }
            }

            if (!precise) return true;
            
            // Precise check
            int subCount = box.allSubList.Count;
            SubBox[] subArr = new SubBox[subCount];
            box.allSubList.CopyTo(subArr);
            using NativeArray<SubBox> subBoxNa = new NativeArray<SubBox>(subArr, Allocator.TempJob);
            using NativeReference<bool> boxHit = new NativeReference<bool>(Allocator.TempJob);

            var preciseHandle = new PreciseSingleHitCheck
            {
                AllSubs = subBoxNa,
                BoxTrans = box.CreateBoxData(),
                BoxHit = boxHit,
                
                HitPosX = callerData.pos.x,
                HitPosY = callerData.pos.y,
                HitPosZ = callerData.pos.z,
                Radius = callerData.radius,
                BoxMode = callerData.boxMode,
                BoxRotX = callerData.boxRotX,
                BoxRotY = callerData.boxRotY,
                BoxRotZ = callerData.boxRotZ,
                BoxRotW = callerData.boxRotW,
                BoxSizeX = callerData.boxBoundsX,
                BoxSizeY = callerData.boxBoundsY,
                BoxSizeZ = callerData.boxBoundsZ,
            }.Schedule(subCount, 32);
            
            preciseHandle.Complete();

            return boxHit.Value;
        }
        
        /// <summary>
        /// Tests collision contact between two BoxObj instances for anchor support determination.
        /// Uses both bounding box collision and optional non-convex mesh detection.
        /// Returns true if the objects are physically touching and support relationship exists.
        /// </summary>
        /// <param name="aBox">First BoxObj to test collision with</param>
        /// <param name="bBox">Second BoxObj to test collision with</param>
        /// <param name="aAllSubs">All SubBox instances of the first BoxObj</param>
        /// <returns>True if objects are in contact and support relationship exists</returns>
        public static bool BoxIntersectWithBox(ref BoxObj aBox, ref BoxObj bBox, NativeArray<SubBox> aAllSubs)
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
                SubBox[] bAllSubsArr = new SubBox[bBox.allSubList.Count];
                bBox.allSubList.CopyTo(bAllSubsArr);

                using NativeArray<SubBox> bAllSubs = new NativeArray<SubBox>(bAllSubsArr, Allocator.TempJob);
                using NativeReference<bool> resultRef = new NativeReference<bool>(Allocator.TempJob);

                using NativeArray<CellData> aGridCellDataNa = new NativeArray<CellData>(aBox.gridCellDataArr, Allocator.TempJob);
                using NativeArray<CellData> bGridCellDataNa = new NativeArray<CellData>(bBox.gridCellDataArr, Allocator.TempJob);
                using NativeArray<int> aGridOriginalIndicesNa = new NativeArray<int>(aBox.gridOriginalIndicesArr, Allocator.TempJob);
                using NativeArray<int> bGridOriginalIndicesNa = new NativeArray<int>(bBox.gridOriginalIndicesArr, Allocator.TempJob);
                
                //EditorApplication.isPaused = true;

                BoxIntersectWithBoxJob boxIntersectWithBoxJob = new BoxIntersectWithBoxJob
                {
                    AKdCellData = aGridCellDataNa,
                    BKdCellData = bGridCellDataNa,

                    AKdSubs = aAllSubs,
                    BKdSubs = bAllSubs,

                    AKdOriginalIndices = aGridOriginalIndicesNa,
                    BKdOriginalIndices = bGridOriginalIndicesNa,

                    AKdCellLength = aBox.gridCellLength,
                    BKdCellLength = bBox.gridCellLength,

                    AVoxelSize = aBox.voxelSize,
                    BVoxelSize = bBox.voxelSize,

                    ARotX = aBox.qx,
                    ARotY = aBox.qy,
                    ARotZ = aBox.qz,
                    ARotW = aBox.qw,

                    AStartPosX = aBox.startPosX,
                    AStartPosY = aBox.startPosY,
                    AStartPosZ = aBox.startPosZ,

                    APreLocalRightX = aBox.vRightX,
                    APreLocalRightY = aBox.vRightY,
                    APreLocalRightZ = aBox.vRightZ,

                    APreLocalUpX = aBox.vUpX,
                    APreLocalUpY = aBox.vUpY,
                    APreLocalUpZ = aBox.vUpZ,

                    APreLocalForwardX = aBox.vFwdX,
                    APreLocalForwardY = aBox.vFwdY,
                    APreLocalForwardZ = aBox.vFwdZ,

                    BRotX = bBox.qx,
                    BRotY = bBox.qy,
                    BRotZ = bBox.qz,
                    BRotW = bBox.qw,

                    BStartPosX = bBox.startPosX,
                    BStartPosY = bBox.startPosY,
                    BStartPosZ = bBox.startPosZ,

                    BPreLocalRightX = bBox.vRightX,
                    BPreLocalRightY = bBox.vRightY,
                    BPreLocalRightZ = bBox.vRightZ,

                    BPreLocalUpX = bBox.vUpX,
                    BPreLocalUpY = bBox.vUpY,
                    BPreLocalUpZ = bBox.vUpZ,

                    BPreLocalForwardX = bBox.vFwdX,
                    BPreLocalForwardY = bBox.vFwdY,
                    BPreLocalForwardZ = bBox.vFwdZ,

                    Offset = offset,

                    Result = resultRef,
                };

                JobHandle handle = boxIntersectWithBoxJob.Schedule(aBox.gridCellLength, 64);
                handle.Complete();

                return resultRef.Value;
            }

            return false;
        }
        
        public static byte[] BoxIntersectWithManyBoxes(BoxObj aBox, BoxObj[] bBoxes)
        {
            int bBoxLength = bBoxes.Length;
            
            SubBox[] aAllSubsArr = new SubBox[aBox.allSubList.Count];
            aBox.allSubList.CopyTo(aAllSubsArr);
            
            using NativeArray<SubBox> aAllSubsNa = new NativeArray<SubBox>(aAllSubsArr, Allocator.TempJob);
            using NativeArray<CellData> aGridCellDataNa = new NativeArray<CellData>(aBox.gridCellDataArr, Allocator.TempJob);
            using NativeArray<int> aGridOriginalIndicesNa = new NativeArray<int>(aBox.gridOriginalIndicesArr, Allocator.TempJob);
            
            using NativeArray<byte> results = new NativeArray<byte>(bBoxLength, Allocator.TempJob);
            
            // Build Flattened NAs
            int subAmount = 0;
            int cellAmount = 0;
            int ogIndicesAmount = 0;

            //byte[] tempResults = new byte[bBoxLength];
            
            for (int i = 0; i < bBoxLength; i++)
            {
                BoxObj bBox = bBoxes[i];

                /*if (BoxIntersectWithBox(ref aBox, ref bBox, aAllSubsNa))
                {
                    tempResults[i] = 1;
                }
                else
                {
                    tempResults[i] = 0;
                }*/
                
                subAmount += bBox.allSubList.Count;
                cellAmount += bBox.gridCellDataArr.Length;
                ogIndicesAmount += bBox.gridOriginalIndicesArr.Length;
            }

            //return tempResults;

            SubBox[] bAllSubsArr = new SubBox[subAmount];
            CellData[] bAllCellArr = new CellData[cellAmount];
            int[] bAllOgIndicesArr = new int[ogIndicesAmount];
            int[] bCellToBoxPointerArr = new int[cellAmount];
            BoxTrans[] bBoxTransArr = new BoxTrans[bBoxLength];

            int subsAcc = 0;
            int cellAcc = 0;
            int ogIndicesAcc = 0;
            
            for (int i = 0; i < bBoxLength; i++)
            {
                BoxObj bBox = bBoxes[i];
                int subCount = bBox.allSubList.Count;
                int cellCount = bBox.gridCellDataArr.Length;
                int ogIdxCount = bBox.gridOriginalIndicesArr.Length;

                bBox.allSubList.CopyTo(0, bAllSubsArr, subsAcc, subCount);
                
                for (int j = 0; j < ogIdxCount; j++)
                {
                    bAllOgIndicesArr[ogIndicesAcc + j] = bBox.gridOriginalIndicesArr[j] + subsAcc;
                }
                
                for (int v = 0; v < cellCount; v++)
                {
                    CellData cell = bBox.gridCellDataArr[v];
                    
                    cell.rangeStart += ogIndicesAcc;
                    cell.rangeEnd += ogIndicesAcc;
                    bAllCellArr[cellAcc + v] = cell;
                    
                    bCellToBoxPointerArr[cellAcc + v] = i;
                }
                
                bBoxTransArr[i] = bBox.CreateBoxData();

                // Only increment once per category
                subsAcc += subCount;
                cellAcc += cellCount;
                ogIndicesAcc += ogIdxCount;
            }
            
            using NativeArray<SubBox> bAllSubsNa = new NativeArray<SubBox>(bAllSubsArr, Allocator.TempJob);
            using NativeArray<CellData> bGridCellDataNa = new NativeArray<CellData>(bAllCellArr, Allocator.TempJob);
            using NativeArray<int> bAllOgIndicesNa = new NativeArray<int>(bAllOgIndicesArr, Allocator.TempJob);
            using NativeArray<int> bCellToBoxPointerNa = new NativeArray<int>(bCellToBoxPointerArr, Allocator.TempJob);
            using NativeArray<BoxTrans> bBoxTransNa = new NativeArray<BoxTrans>(bBoxTransArr, Allocator.TempJob);
            
            // Run Job
            var handle = new BoxIntersectWithManyBoxesJob
            {
                ASubs = aAllSubsNa,
                ACellData = aGridCellDataNa,
                AOriginalIndices = aGridOriginalIndicesNa,
                
                BSubs = bAllSubsNa,
                BCellData = bGridCellDataNa,
                BOriginalIndices = bAllOgIndicesNa,
                BCellToBoxPointer = bCellToBoxPointerNa,
                BBoxTrans = bBoxTransNa,
                
                Result = results,
                
                AVoxelSize = aBox.voxelSize,
                ARotX = aBox.qx,
                ARotY = aBox.qy,
                ARotZ = aBox.qz,
                ARotW = aBox.qw,

                AStartPosX = aBox.startPosX,
                AStartPosY = aBox.startPosY,
                AStartPosZ = aBox.startPosZ,

                APreLocalRightX = aBox.vRightX,
                APreLocalRightY = aBox.vRightY,
                APreLocalRightZ = aBox.vRightZ,

                APreLocalUpX = aBox.vUpX,
                APreLocalUpY = aBox.vUpY,
                APreLocalUpZ = aBox.vUpZ,

                APreLocalForwardX = aBox.vFwdX,
                APreLocalForwardY = aBox.vFwdY,
                APreLocalForwardZ = aBox.vFwdZ,
                
                Offset = BoxCutterManagerInstance.connectionLeniencyDist,
            }.Schedule(cellAmount, 32);
            
            handle.Complete();
            
            byte[] resultsArr = new byte[bBoxLength];
            results.CopyTo(resultsArr);

            return resultsArr;
        }
    }
}