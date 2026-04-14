using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using static BoxCutter.BoxObj;
using static BoxCutter.BoxCutterWorldPhysics;
using static BoxCutter.BoxCutterCaller;
using static BoxCutter.BoxCutterManager;
using Debug = UnityEngine.Debug;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               BoxCutter Destruction Pipeline System
    //─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Core destruction pipeline for processing voxel-based fragmentation and island detection.
    /// Handles the complete destruction workflow from impact detection to fragment island generation.
    /// Manages parallel processing of multiple BoxObj instances with optimized memory usage.
    /// </summary>
    public static class DestructionPipeline
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Data Structures
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Comprehensive transformation data for BoxObj instances used in Burst-compiled jobs.
        /// Contains all spatial, rotation, and coordinate system data needed for voxel processing.
        /// Optimized for efficient memory layout and fast access in parallel processing.
        /// </summary>
        [Serializable]
        public struct BoxTrans
        {
            public float voxelSize;

            public float posX;
            public float posY;
            public float posZ;

            public float rotX;
            public float rotY;
            public float rotZ;
            public float rotW;

            public float qx;
            public float qy;
            public float qz;
            public float qw;

            public float iqx;
            public float iqy;
            public float iqz;
            public float iqw;

            public float startPosX;
            public float startPosY;
            public float startPosZ;

            public float rightX;
            public float rightY;
            public float rightZ;

            public float upX;
            public float upY;
            public float upZ;

            public float fwdX;
            public float fwdY;
            public float fwdZ;

            public float vRightX;
            public float vRightY;
            public float vRightZ;

            public float vUpX;
            public float vUpY;
            public float vUpZ;

            public float vFwdX;
            public float vFwdY;
            public float vFwdZ;

            public int id;

            public float sizeX;
            public float sizeY;
            public float sizeZ;
            
            public float callerStrengthMul;

            public float magicaOffsetX;
            public float magicaOffsetY;
            public float magicaOffsetZ;

            public float3 lossyScale;
        }

        /// <summary>
        /// Pair structure linking a SubBox fragment to its parent BoxObj index.
        /// Used for tracking fragment ownership during parallel processing operations.
        /// Optimized for Burst compilation and efficient memory access patterns.
        /// </summary>
        [BurstCompile]
        public struct SubObjOutputPair
        {
            /// <summary>The SubBox fragment data</summary>
            public SubBox SubBox;

            /// <summary>Index of the parent BoxObj that owns this SubBox</summary>
            public int Index;
        }

        /// <summary>
        /// Output structure for greedy meshing operations containing fragment and metadata.
        /// Stores processed SubBox data along with ownership and boundary information.
        /// Used to track which fragments are inside or outside the destruction area.
        /// </summary>
        [BurstCompile]
        public struct GreedyOut
        {
            /// <summary>Index of the BoxObj that owns this fragment</summary>
            public int BoxIdx;

            /// <summary>Out-of-bounds flag: 1 if outside destruction area, 0 if inside</summary>
            public byte OobFlag;

            /// <summary>The processed SubBox fragment data</summary>
            public SubBox Sub;
        }

        /// <summary>
        /// Parallel job for building owner index arrays used by fragmentation jobs.
        /// Uses binary search to efficiently map each element index to its owning box.
        /// </summary>
        [BurstCompile]
        public struct BuildOwnerArrayJob : IJobParallelFor
        {
            /// <summary>Offset array where OffsetPerBox[b] is the starting index for box b</summary>
            [ReadOnly] public NativeArray<int> OffsetPerBox;

            /// <summary>Base offset to subtract from global indices</summary>
            [ReadOnly] public int BaseOffset;

            /// <summary>Index offset to add to found box index (for local vs global indexing)</summary>
            [ReadOnly] public int BoxIndexOffset;

            /// <summary>Output owner array - element i maps to owning box index</summary>
            [WriteOnly] public NativeArray<int> OwnerOut;

            public void Execute(int i)
            {
                int globalIdx = i + BaseOffset;

                // Binary search to find owner box
                int lo = 0, hi = OffsetPerBox.Length - 1;
                while (lo < hi)
                {
                    int mid = lo + ((hi - lo + 1) >> 1);
                    if (globalIdx >= OffsetPerBox[mid])
                        lo = mid;
                    else
                        hi = mid - 1;
                }

                OwnerOut[i] = lo + BoxIndexOffset;
            }
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Main Pipeline Entry Point
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Main destruction pipeline entry point processing BoxObj array through complete fragmentation workflow.
        /// Executes sorting, hit detection, fragmentation, island detection, and debris generation in sequence.
        /// Returns both main fragment islands and debris islands with positional data for BuildPipeline.
        /// </summary>
        /// <param name="boxArr">Array of BoxObj instances to process for destruction</param>
        /// <param name="callerData">Caller information containing impact parameters and settings</param>
        /// <param name="overrideSync">Overrides the default sync value</param>
        /// <returns>Tuple containing main islands, debris islands, and hit box transformation data</returns>
        public static async Task<(SubBox[][][] mainIslandSubs, SubBox[][][] debrisIslandSubs, Vector3[] hitBoxPosArr, Quaternion[] hitBoxRotArr, Vector3[] hitBoxSizeArr, bool[] missedBoxes)> Destroy(BoxObj[] boxArr, CallerData callerData, bool? overrideSync = null)
        {
            int boxLength = boxArr.Length;
            
            (SubBox[][][] mainIslandSubs, SubBox[][][] debrisIslandSubs, Vector3[] hitBoxPosArr, Quaternion[] hitBoxRotArr, Vector3[] hitBoxSizeArr, bool[]) ReturnEmpty()
            {
                bool[] missedArr = new bool[boxLength];
                for (int i = 0; i < boxLength; i++)
                    missedArr[i] = true;
                
                return (new SubBox[][][]{}, new SubBox[][][]{}, new Vector3[]{}, new Quaternion[]{}, new Vector3[]{}, missedArr);
            }
            
            // Initialize performance monitoring and pipeline state
            Stopwatch sw = new Stopwatch();
            bool canPrintMs = callerData.canPrintMs;

            BoxCutterManager manger = BoxCutterManagerInstance;
            bool sync = IsWebGL || !manger.asyncDestruction;
            if (overrideSync.HasValue) sync = overrideSync.Value;
            Allocator allocator = sync ? Allocator.TempJob : Allocator.Persistent;
            
            AsyncDestructionMode asyncMode = manger.asyncDestructionMode;
            bool ignoreNonCoreSync = sync || asyncMode <= AsyncDestructionMode.Core;
            
            // Sort BoxObj instances by fragmentation type for optimal batch processing
            SortBoxViaFragType(
                boxArr,
                out BoxObj[] sorted,
                out FragSettings[] fragSettings,
                out Dictionary<FragmentType, int2> fragBoxGroupRanges, // Fragment type ranges
                out int[] sortToOrig // Mapping from sorted to original indices
            );
            boxArr = sorted; // Use sorted array for remaining pipeline

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Sort");

            // Reset calculation variables and build transformation data for jobs
            ResetBoxCalcVars(boxArr);
            using NativeArray<BoxTrans> boxDataNa = BuildBoxData(boxArr, allocator, sync); // Burst-compatible transform data

            SubBox[][] subsPerBox = new SubBox[boxLength][];
            for (int i = 0; i < boxLength; ++i)
            {
                BoxObj box = boxArr[i];
                SubBox[] subs = new SubBox[box.allSubList.Count];
                box.allSubList.CopyTo(subs);
                subsPerBox[i] = subs;
            }

            GetOverrides(boxArr, out NativeArray<bool> diagTo, out NativeArray<float> ratioTo, allocator, sync);

            // Find all SubBox fragments that are affected by the destruction impact
            (NativeArray<SubBox> hitSubsNa, NativeArray<int> hitSubStartsNa, NativeArray<int> hitSubCountsNa, SubBox[][] survivorsPerBox, bool[] missedBoxes) findAllHitData = await FindAllHitSubs(
                boxArr,
                boxDataNa, 
                callerData, 
                subsPerBox,
                allocator,
                ignoreNonCoreSync
            );

            // Check if the caller misses everything if so early exit
            bool allMiss = true;
            for (int i = 0; i < boxLength; i++)
            {
                if (!findAllHitData.missedBoxes[i])
                {
                    allMiss = false;
                    break;
                }
            }

            if (allMiss)
            {
                diagTo.Dispose();
                ratioTo.Dispose();
                
                findAllHitData.hitSubsNa.Dispose();
                findAllHitData.hitSubStartsNa.Dispose();
                findAllHitData.hitSubCountsNa.Dispose();
                return ReturnEmpty();
            }

            subsPerBox = findAllHitData.survivorsPerBox;

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Find All Hit");
            
            // Perform 27 slice to get the main points of interests
            (NativeList<SubObjOutputPair> hitCentralSubNl, NativeArray<int3> hitMinNa, NativeArray<int3> hitMaxNa) slice27Data = await Slice27(
                boxArr, 
                boxDataNa, 
                callerData, 
                findAllHitData.hitSubsNa, 
                findAllHitData.hitSubStartsNa, 
                findAllHitData.hitSubCountsNa, 
                subsPerBox, 
                allocator,
                ignoreNonCoreSync
            );

            // Convert native arrays to managed arrays for return data
            int3[] hitMinArr = new int3[slice27Data.hitMinNa.Length];
            int3[] hitMaxArr = new int3[slice27Data.hitMaxNa.Length];
            slice27Data.hitMinNa.CopyTo(hitMinArr);
            slice27Data.hitMaxNa.CopyTo(hitMaxArr);

            // Prepare transformation data for BuildPipeline
            Vector3[] hitBoxPosArr = new Vector3[boxLength];
            Quaternion[] hitBoxRotArr = new Quaternion[boxLength];
            Vector3[] hitBoxSizeArr = new Vector3[boxLength];

            // Calculate world-space position and size for each affected BoxObj
            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = boxArr[i];

                int3 hitMin = hitMinArr[i];
                int3 hitMax = hitMaxArr[i];

                // Transform voxel bounds to world coordinates
                box.GetSubObjPositionAndSize(hitMin.x, hitMin.y, hitMin.z, hitMax.x, hitMax.y, hitMax.z,
                    out float posX, out float posY, out float posZ,
                    out float sizeX, out float sizeY, out float sizeZ);

                hitBoxPosArr[i] = new Vector3(posX, posY, posZ);
                hitBoxRotArr[i] = new Quaternion(box.qx, box.qy, box.qz, box.qw);
                hitBoxSizeArr[i] = new Vector3(sizeX, sizeY, sizeZ);
            }

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Slice");

            (NativeArray<byte> validNa, int[] gridOfs, int3[] gSize) buildGridData = await BuildGridIndexes(
                boxArr,
                slice27Data.hitCentralSubNl,
                slice27Data.hitMinNa,
                slice27Data.hitMaxNa,
                allocator,
                ignoreNonCoreSync
            );

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Build Grid");

            using var oobFlagsNa = new NativeArray<byte>(buildGridData.validNa.Length, allocator, NativeArrayOptions.UninitializedMemory);

            using NativeArray<byte> fragHitBoxesNa = new NativeArray<byte>(boxLength, allocator);
            await Frag(
                boxArr,
                fragSettings,
                fragHitBoxesNa,
                boxDataNa,
                buildGridData.validNa,
                oobFlagsNa,
                slice27Data.hitMinNa,
                slice27Data.hitMaxNa,
                buildGridData.gridOfs,
                buildGridData.gSize,
                fragBoxGroupRanges,
                callerData,
                allocator,
                sync
            );

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Frag");

            byte[] fragHitBoxesArr = new byte[boxLength];
            fragHitBoxesNa.CopyTo(fragHitBoxesArr);

            /*bool fragAllMiss = true;
            for (int i = 0; i < boxLength; i++)
            {
                // fragHitBoxesArr[i] == 1 means the box WAS hit (had voxels pass corner check)
                // fragHitBoxesArr[i] == 0 means the box was MISSED (no voxels passed corner check)
                if (fragHitBoxesArr[i] == 0)
                {
                    findAllHitData.missedBoxes[i] = true;
                }
                else
                {
                    fragAllMiss = false;
                }
            }

            if (fragAllMiss)
            {
                // All boxes missed - early exit and cleanup
                diagTo.Dispose();
                ratioTo.Dispose();

                findAllHitData.hitSubsNa.Dispose();
                findAllHitData.hitSubStartsNa.Dispose();
                findAllHitData.hitSubCountsNa.Dispose();

                slice27Data.hitCentralSubNl.Dispose();
                slice27Data.hitMinNa.Dispose();
                slice27Data.hitMaxNa.Dispose();

                buildGridData.validNa.Dispose();

                return ReturnEmpty();
            }*/

            (NativeArray<SubBox> inSubsNa, NativeArray<int> inSubStartsNa, NativeArray<int> inSubCountsNa) greedyData = await Greedy(boxArr, buildGridData.validNa, oobFlagsNa, buildGridData.gridOfs, buildGridData.gSize, slice27Data.hitMinNa, allocator, oobAppendTo: subsPerBox, sync: sync);

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Greedy");

            // Build combined arrays: subsPerBox (survivors) first, then greedy fragments
            // survivorCounts tracks how many survivor SubBoxes per box (for connectivity check)
            int[] survivorCounts = new int[boxLength];
            for (int i = 0; i < boxLength; ++i)
                survivorCounts[i] = subsPerBox[i].Length;

            // Combine subsPerBox with greedy results
            SubBox[][] combinedSubsPerBox = new SubBox[boxLength][];
            int[] greedyStartsArr = new int[greedyData.inSubStartsNa.Length];
            greedyData.inSubStartsNa.CopyTo(greedyStartsArr);
            int[] greedyCountsArr = new int[greedyData.inSubCountsNa.Length];
            greedyData.inSubCountsNa.CopyTo(greedyCountsArr);
            SubBox[] greedySubsArr = new SubBox[greedyData.inSubsNa.Length];
            greedyData.inSubsNa.CopyTo(greedySubsArr);

            for (int i = 0; i < boxLength; ++i)
            {
                int survivorCount = survivorCounts[i];
                int greedyCount = greedyCountsArr[i];
                int totalCount = survivorCount + greedyCount;

                combinedSubsPerBox[i] = new SubBox[totalCount];

                // Copy survivors first
                if (survivorCount > 0)
                    Array.Copy(subsPerBox[i], 0, combinedSubsPerBox[i], 0, survivorCount);

                // Then copy greedy fragments
                if (greedyCount > 0)
                    Array.Copy(greedySubsArr, greedyStartsArr[i], combinedSubsPerBox[i], survivorCount, greedyCount);
            }

            // Build flat arrays for combined data
            BuildFlatSubArrays(
                combinedSubsPerBox,
                out NativeArray<SubBox> finalSubsNa,
                out NativeArray<int> finalSubStartsNa,
                out NativeArray<int> finalSubCountsNa,
                allocator
            );

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Build Combined");

            (NativeArray<int> islandKdIndicesNa, NativeArray<CellData> islandKdCellsNa, NativeArray<int> islandKdOwnerNa, NativeArray<int2> islandLeafCellCountNa) kdData = BuildGrid(
                finalSubsNa,
                ratioTo,
                finalSubStartsNa,
                finalSubCountsNa,
                Allocator.Persistent
            );

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Grid");

            (SubBox[][][] allBoxIslandSubs, SubBox[][][] allBoxDebrisSubIslands) = await ProcessIslands(
                boxArr,
                diagTo,
                finalSubsNa,
                finalSubStartsNa,
                finalSubCountsNa,
                survivorCounts,
                kdData.islandKdIndicesNa,
                kdData.islandKdCellsNa,
                kdData.islandKdOwnerNa,
                callerData.canSpawnDebris,
                allocator,
                sync
            );

            if (manger.CheckExit()) return ReturnEmpty();
            if (canPrintMs) PrintPassMs(sw, "Island");

            findAllHitData.hitSubsNa.Dispose();
            findAllHitData.hitSubStartsNa.Dispose();
            findAllHitData.hitSubCountsNa.Dispose();

            slice27Data.hitCentralSubNl.Dispose();
            slice27Data.hitMinNa.Dispose();
            slice27Data.hitMaxNa.Dispose();

            buildGridData.validNa.Dispose();

            greedyData.inSubsNa.Dispose();
            greedyData.inSubStartsNa.Dispose();
            greedyData.inSubCountsNa.Dispose();

            finalSubsNa.Dispose();
            finalSubStartsNa.Dispose();
            finalSubCountsNa.Dispose();

            kdData.islandKdIndicesNa.Dispose();
            kdData.islandKdCellsNa.Dispose();
            kdData.islandKdOwnerNa.Dispose();
            kdData.islandLeafCellCountNa.Dispose();

            diagTo.Dispose();
            ratioTo.Dispose();
            
            int sortedCount = sortToOrig.Length;
            
            int origCount = sortToOrig.Length == 0 ? 0 : sortToOrig.Max() + 1;

            if (origCount == 0)
                return (new SubBox[][][] { }, new SubBox[][][] { }, new Vector3[] { }, new Quaternion[] { }, new Vector3[] { }, new bool[]{});

            var mainUnsorted = new SubBox[origCount][][];
            var debrisUnsorted = new SubBox[origCount][][];

            for (int o = 0; o < origCount; ++o)
            {
                mainUnsorted[o] = Array.Empty<SubBox[]>();
                debrisUnsorted[o] = Array.Empty<SubBox[]>();
            }

            for (int s = 0; s < sortedCount; ++s)
            {
                int o = sortToOrig[s];
                if (o < 0 || o >= origCount) continue;

                // Guard against main array being length 0 when everything became debris.
                var mainPerBox   = (s < allBoxIslandSubs.Length)       ? allBoxIslandSubs[s]       : Array.Empty<SubBox[]>();
                var debrisPerBox = (s < allBoxDebrisSubIslands.Length) ? allBoxDebrisSubIslands[s] : Array.Empty<SubBox[]>();

                mainUnsorted[o] = mainPerBox;
                debrisUnsorted[o] = debrisPerBox;
            }

            allBoxIslandSubs = mainUnsorted;
            allBoxDebrisSubIslands = debrisUnsorted;
            
            Debug.Assert(sortToOrig.Length == allBoxIslandSubs.Length, "Internal error: sort mapping length drifted from island array length");
            sw.Stop();
            return (allBoxIslandSubs, allBoxDebrisSubIslands, hitBoxPosArr, hitBoxRotArr, hitBoxSizeArr, findAllHitData.missedBoxes);
        }

        public static void BuildFlatSubArrays(
            SubBox[][] arrays,
            out NativeArray<SubBox> outSubNa,
            out NativeArray<int> outStartsNa,
            out NativeArray<int> outCountsNa,
            Allocator allocator
        )
        {
            int count = arrays.Length;

            int[] starts = new int[count];
            int[] counts = new int[count];

            int total = 0;
            for (int i = 0; i < count; i++)
            {
                SubBox[] l = arrays[i];
                int c = l.Length;
                starts[i] = total;
                counts[i] = c;
                total += c;
            }

            SubBox[] flat = new SubBox[total];
            for (int b = 0; b < count; b++)
            {
                SubBox[] l = arrays[b];
                int c = counts[b];
                if (c == 0) continue;

                int dst = starts[b];
                for (int i = 0; i < c; i++)
                    flat[dst + i] = l[i];
            }

            outSubNa = new NativeArray<SubBox>(flat, allocator);
            outStartsNa = new NativeArray<int>(starts, allocator);
            outCountsNa = new NativeArray<int>(counts, allocator);
        }

        private static void SortBoxViaFragType(
            BoxObj[] boxArr,
            out BoxObj[] sortedBoxes,
            out FragSettings[] fragSettings,
            out Dictionary<FragmentType, int2> fragBoxRangesDict,
            out int[] sortedToOrigIdx)
        {
            int boxLength = boxArr.Length;
            sortedBoxes = new BoxObj[boxLength];
            FragSettings[] sortedFrags = new FragSettings[boxLength];
            sortedToOrigIdx = new int[boxLength];

            int enumCount = Enum.GetNames(typeof(FragmentType)).Length;

            fragSettings = new FragSettings[boxLength];
            
            FragmentType[] types = new FragmentType[boxLength];
            int[] counts = new int[enumCount];
            for (int i = 0; i < boxLength; i++)
            {
                BoxObj b = boxArr[i];
                FragmentType t = (b.canAdvancedFrag && b.fragSettings != null) ? b.fragSettings.fragmentType : FragmentType.Standard;
                fragSettings[i] = b.fragSettings;
                
                types[i] = t;
                counts[(int)t]++;
            }

            int[] starts = new int[enumCount];
            int running = 0;
            for (int t = 0; t < enumCount; t++)
            {
                starts[t] = running;
                running += counts[t];
            }

            int[] nextPos = new int[enumCount];
            Array.Copy(starts, nextPos, enumCount);

            for (int i = 0; i < boxLength; i++)
            {
                var t = types[i];
                int idx = (int)t;
                int dst = nextPos[idx]++;
                sortedBoxes[dst] = boxArr[i];
                sortedFrags[dst] = fragSettings[i];
                sortedToOrigIdx[dst] = i;
            }

            fragSettings = sortedFrags;

            fragBoxRangesDict = new Dictionary<FragmentType, int2>(enumCount);
            for (int t = 0; t < enumCount; t++)
            {
                int cnt = counts[t];
                if (cnt > 0)
                    fragBoxRangesDict[(FragmentType)t] = new int2(starts[t], cnt);
            }
        }

        private static void ResetBoxCalcVars(BoxObj[] boxArr)
        {
            int boxArrLength = boxArr.Length;

            for (int i = 0; i < boxArrLength; i++)
            {
                BoxObj box = boxArr[i];
                if (box.connectionState == ConnectionStateEnum.Disconnected || box.isDynamic) box.UpdateLiveVars();
                box.RefreshTrueStartPos();
            }
        }

        private static NativeArray<BoxTrans> BuildBoxData(BoxObj[] boxArr, Allocator allocator, bool sync = false)
        {
            int boxArrLength = boxArr.Length;
            BoxTrans[] boxDataArr = new BoxTrans [boxArrLength];

            for (int i = 0; i < boxArrLength; i++)
            {
                BoxObj box = boxArr[i];
                boxDataArr[i] = box.CreateBoxData();
            }

            NativeArray<BoxTrans> boxData = new NativeArray<BoxTrans>(boxDataArr, allocator);
            return boxData;
        }

        private static void GetOverrides(BoxObj[] boxArr, out NativeArray<bool> diagTo, out NativeArray<float> ratioTo, Allocator allocator, bool sync = false)
        {
            int boxLength = boxArr.Length;
            bool defDiagValue = BoxCutterManagerInstance.ignoreDiagonalsInIsland;
            float defFillRatioValue = BoxCutterManagerInstance.validCellFillRatio;
            
            bool[] ignoreDiagonalArr = new bool[boxLength];
            float[] fillRatioArr = new float[boxLength];
            
            for (int i = 0; i < boxLength; i++)
            {
                BoxObj box = boxArr[i];
                ignoreDiagonalArr[i] = box.canOverrideDiagInIsland ? box.diagInIslandTo : defDiagValue;
                fillRatioArr[i] = box.canOverrideKdCellTightness ? box.kdCellTightnessTo : defFillRatioValue;
            }
            
            diagTo = new NativeArray<bool>(ignoreDiagonalArr, allocator);
            ratioTo = new NativeArray<float>(fillRatioArr, allocator);
        }

        private static async Task<(NativeArray<SubBox> hitSubsNa, NativeArray<int> hitSubStartsNa, NativeArray<int> hitSubCountsNa, SubBox[][] survivorsPerBox, bool[] missedBoxes)> FindAllHitSubs(
            BoxObj[] boxArr,
            NativeArray<BoxTrans> boxDataNa,
            CallerData callerData,
            SubBox[][] inputSubLists,
            Allocator allocator,
            bool sync = false
        )
        {
            int boxLength = boxArr.Length;
            BoxCutterManager manager = BoxCutterManagerInstance;

            (NativeArray<SubBox>, NativeArray<int>, NativeArray<int>, SubBox[][], bool[]) ReturnEmpty()
            {
                bool[] allMissed = new bool[boxLength];
                for (int i = 0; i < boxLength; i++) allMissed[i] = true;

                return (new NativeArray<SubBox>(0, allocator), new NativeArray<int>(boxLength, allocator), new NativeArray<int>(boxLength, allocator), inputSubLists, allMissed);
            }

            if (boxLength == 0) return ReturnEmpty();

            int totalSubAmount = 0;
            var subStarts = new int[boxLength];
            var subCounts = new int[boxLength];
            for (int i = 0; i < boxLength; ++i)
            {
                subStarts[i] = totalSubAmount;
                subCounts[i] = inputSubLists[i].Length;
                totalSubAmount += subCounts[i];
            }

            if (totalSubAmount == 0) return ReturnEmpty();

            var allSubsArr = new SubBox[totalSubAmount];
            var boxPointerArr = new int[totalSubAmount];
            int cursorFlat = 0;
            for (int i = 0; i < boxLength; i++)
            {
                SubBox[] list = inputSubLists[i];
                for (int j = 0; j < list.Length; j++)
                {
                    allSubsArr[cursorFlat] = list[j];
                    boxPointerArr[cursorFlat] = i;
                    cursorFlat++;
                }
            }

            using var allSubsNa = new NativeArray<SubBox>(allSubsArr, allocator);
            using var boxPointerNa = new NativeArray<int>(boxPointerArr, allocator);
            using var hitIdxNl = new NativeList<int>(totalSubAmount, allocator);

            var handle = new FindAllHitSub
            {
                AllSubs = allSubsNa,
                SubBoxPointerToBox = boxPointerNa,
                HitIndices = hitIdxNl.AsParallelWriter(),
                BoxData = boxDataNa,
                HitPosX = callerData.pos.x, HitPosY = callerData.pos.y, HitPosZ = callerData.pos.z,
                Radius = callerData.radius,
                BoxMode = callerData.boxMode,
                BoxRotX = callerData.boxRotX, BoxRotY = callerData.boxRotY, BoxRotZ = callerData.boxRotZ, BoxRotW = callerData.boxRotW,
                BoxSizeX = callerData.boxBoundsX, BoxSizeY = callerData.boxBoundsY, BoxSizeZ = callerData.boxBoundsZ,
            }.Schedule(totalSubAmount, 64);

            await WaitJobComplete(handle, sync, manager.asyncDestructionMaxFrames);;
            if (manager.CheckExit()) return ReturnEmpty();

            // Process Hits
            int hitCount = hitIdxNl.Length;
            int[] hitIdxArr = new int[hitIdxNl.Length];
            hitIdxNl.AsArray().CopyTo(hitIdxArr);

            var hitCounts = new int[boxLength];
            for (int i = 0; i < hitCount; i++)
                hitCounts[boxPointerArr[hitIdxArr[i]]]++;

            // Calculate Missed Boxes
            bool[] missedBoxes = new bool[boxLength];
            for (int i = 0; i < boxLength; i++)
            {
                missedBoxes[i] = (hitCounts[i] == 0);
            }

            var hitStarts = new int[boxLength];
            for (int i = 1; i < boxLength; i++)
                hitStarts[i] = hitStarts[i - 1] + hitCounts[i - 1];

            var hitSubsArr = new SubBox[hitCount];
            var curs = (int[])hitStarts.Clone();

            // Create the mask here based on exact index matching from the job
            byte[] removeMask = new byte[totalSubAmount];

            for (int i = 0; i < hitCount; i++)
            {
                int sIdx = hitIdxArr[i];
                removeMask[sIdx] = 1;
                hitSubsArr[curs[boxPointerArr[sIdx]]++] = allSubsArr[sIdx];
            }

            SubBox[][] survivorsPerBox = new SubBox[boxLength][];
            for (int b = 0; b < boxLength; b++)
            {
                int count = 0;
                int bStart = subStarts[b];
                int bLen = subCounts[b];

                SubBox[] dst = new SubBox[bLen];
                for (int j = 0; j < bLen; j++)
                {
                    if (removeMask[bStart + j] == 0)
                        dst[count++] = inputSubLists[b][j];
                }

                Array.Resize(ref dst, count);
                survivorsPerBox[b] = dst;
            }

            return (new NativeArray<SubBox>(hitSubsArr, allocator),
                new NativeArray<int>(hitStarts, allocator),
                new NativeArray<int>(hitCounts, allocator),
                survivorsPerBox,
                missedBoxes);
        }

        private static async Task<(NativeList<SubObjOutputPair> hitCentralSubNl, NativeArray<int3> hitMinIntervalNa, NativeArray<int3> hitMaxIntervalNa)> Slice27(
            BoxObj[] boxArr,
            NativeArray<BoxTrans> boxDataNa,
            CallerData callerData,
            NativeArray<SubBox> hitSubsNa,
            NativeArray<int> hitSubStartsNa,
            NativeArray<int> hitSubCountsNa,
            SubBox[][] workingSubsPerBox,
            Allocator allocator,
            bool sync = false
        )
        {
            int boxLength = boxArr.Length;
            BoxCutterManager manager = BoxCutterManagerInstance;

            float3 hitSize = callerData.boxMode
                ? new float3(callerData.boxBoundsX, callerData.boxBoundsY, callerData.boxBoundsZ)
                : new float3(callerData.radius, callerData.radius, callerData.radius);

            int2[] hitRanges = new int2[boxLength];
            int[] hitSubStartsArr = new int[hitSubStartsNa.Length];
            hitSubStartsNa.CopyTo(hitSubStartsArr);
            int[] hitSubCountsArr = new int[hitSubCountsNa.Length];
            hitSubCountsNa.CopyTo(hitSubCountsArr);

            for (int i = 0; i < boxLength; i++)
            {
                int s = hitSubStartsArr[i], c = hitSubCountsArr[i];
                hitRanges[i] = new int2(s, s + c);
            }

            using var hitRangesNa = new NativeArray<int2>(hitRanges, allocator);

            NativeList<SubObjOutputPair> hitCentralSubNl = new NativeList<SubObjOutputPair>(hitSubsNa.Length, allocator);
            using var centralListNa = new NativeList<SubObjOutputPair>(hitSubsNa.Length * 26, allocator);
            NativeArray<int3> hitMinIntervalNa = new NativeArray<int3>(boxLength, allocator);
            NativeArray<int3> hitMaxIntervalNa = new NativeArray<int3>(boxLength, allocator);

            JobHandle handle = new Slice27
                {
                    InitialHitSubArr = hitSubsNa,
                    InitialHitSubRangeArr = hitRangesNa,

                    HitPosX = callerData.pos.x,
                    HitPosY = callerData.pos.y,
                    HitPosZ = callerData.pos.z,

                    BoxMode = callerData.boxMode,
                    HitSizeX = hitSize.x,
                    HitSizeY = hitSize.y,
                    HitSizeZ = hitSize.z,

                    HitRotX = callerData.boxRotX,
                    HitRotY = callerData.boxRotY,
                    HitRotZ = callerData.boxRotZ,
                    HitRotW = callerData.boxRotW,

                    BoxData = boxDataNa,

                    HitMinInterval = hitMinIntervalNa,
                    HitMaxInterval = hitMaxIntervalNa,

                    HitSubObjList = hitCentralSubNl.AsParallelWriter(),
                    CentralSubObjList = centralListNa.AsParallelWriter()
                }
                .Schedule(boxLength, 64);
            await WaitJobComplete(handle, sync, manager.asyncDestructionMaxFrames);;
            if (manager.CheckExit()) return (hitCentralSubNl, hitMinIntervalNa, hitMaxIntervalNa);

            using var centralNa = centralListNa.AsArray();
            int centralLength = centralNa.Length;
            SubObjOutputPair[] centralArr = new SubObjOutputPair[centralLength];
            centralNa.CopyTo(centralArr);

            int workingSubLength = workingSubsPerBox.Length;

            // First pass: count additional items per box
            int[] additionalCounts = new int[workingSubLength];
            for (int i = 0; i < centralLength; i++)
            {
                additionalCounts[centralArr[i].Index]++;
            }

            // Pre-allocate arrays with exact sizes and copy initial data
            int[] writeIndices = new int[workingSubLength];
            for (int i = 0; i < workingSubLength; i++)
            {
                int initialCount = workingSubsPerBox[i].Length;
                int totalSize = initialCount + additionalCounts[i];
                SubBox[] newArray = new SubBox[totalSize];

                Array.Copy(workingSubsPerBox[i], 0, newArray, 0, initialCount);
                workingSubsPerBox[i] = newArray;
                writeIndices[i] = initialCount;
            }

            // Second pass: append new items directly
            for (int i = 0; i < centralLength; i++)
            {
                SubObjOutputPair p = centralArr[i];
                workingSubsPerBox[p.Index][writeIndices[p.Index]++] = p.SubBox;
            }

            return (hitCentralSubNl, hitMinIntervalNa, hitMaxIntervalNa);
        }

        private static async Task<(NativeArray<byte> validNa, int[] gridOffsetPerBox, int3[] globalSizePerBox)> BuildGridIndexes(
            BoxObj[] boxArr, 
            NativeList<SubObjOutputPair> hitCentralSubNl, 
            NativeArray<int3> hitMinIntervalNa, 
            NativeArray<int3> hitMaxIntervalNa, 
            Allocator allocator,
            bool sync = false
        )
        {
            int boxCount = boxArr.Length;
            BoxCutterManager manager = BoxCutterManagerInstance;

            int3[] hitMinIntervalArr = new int3[boxCount];
            hitMinIntervalNa.CopyTo(hitMinIntervalArr);

            int3[] hitMaxIntervalArr = new int3[boxCount];
            hitMaxIntervalNa.CopyTo(hitMaxIntervalArr);

            int totalSub = hitCentralSubNl.Length;
            SubBox[] subArr = new SubBox[totalSub];
            int[] boxPtrArr = new int[totalSub];
            int[] gridOffsetPerBox = new int[boxCount];
            int3[] globalSizePerBox = new int3[boxCount];

            SubObjOutputPair[] hitCentralPairs = new SubObjOutputPair[totalSub];
            using var hitCentralNa = hitCentralSubNl.AsArray();
            hitCentralNa.CopyTo(hitCentralPairs);

            for (int i = 0; i < totalSub; i++)
            {
                var pair = hitCentralPairs[i];
                subArr[i] = pair.SubBox;
                boxPtrArr[i] = pair.Index;
            }

            int runningGridOf = 0;
            for (int i = 0; i < boxCount; ++i)
            {
                int3 gSize = hitMaxIntervalArr[i] - hitMinIntervalArr[i];
                
                // Ensure all dimensions are positive to prevent ArgumentOutOfRangeException
                gSize.x = math.max(0, gSize.x);
                gSize.y = math.max(0, gSize.y);
                gSize.z = math.max(0, gSize.z);
                
                gridOffsetPerBox[i] = runningGridOf;
                globalSizePerBox[i] = gSize;
                runningGridOf += gSize.x * gSize.y * gSize.z;
            }

            int totalGrid = runningGridOf;
            
            using var subNa = new NativeArray<SubBox>(subArr, allocator);
            using var boxPtrNa = new NativeArray<int>(boxPtrArr, allocator);
            using var gridOfsNa = new NativeArray<int>(gridOffsetPerBox, allocator);
            using var gSizeNa = new NativeArray<int3>(globalSizePerBox, allocator);

            NativeArray<byte> validNa = new NativeArray<byte>(totalGrid, allocator, NativeArrayOptions.UninitializedMemory);

            JobHandle handle = new ClearBufferJob { Buffer = validNa, Value = 255 }.Schedule(totalGrid, 32);

            handle.Complete();

            handle = new BuildGridIndexes
            {
                SubArr = subNa,
                BoxIndexArr = boxPtrNa,
                GridOffsetPerBox = gridOfsNa,
                GlobalSizePerBox = gSizeNa,
                HitMinPerBox = hitMinIntervalNa,
                Valid = validNa
            }.Schedule(totalSub, 32);

            await WaitJobComplete(handle, sync, manager.asyncDestructionMaxFrames);

            return (validNa, gridOffsetPerBox, globalSizePerBox);
        }

        private static async Task Frag(
            BoxObj[] boxArr,
            FragSettings[] fragSettings,
            NativeArray<byte> hitBoxes,
            NativeArray<BoxTrans> boxDataNa,
            NativeArray<byte> validNa,
            NativeArray<byte> oobFlagsNa,
            NativeArray<int3> hitMinIntervalNa,
            NativeArray<int3> hitMaxIntervalNa,
            int[] gridOffsetPerBox,
            int3[] globalSizePerBox,
            Dictionary<FragmentType, int2> fragBoxRanges,
            CallerData callerData,
            Allocator allocator,
            bool sync = false)
        {
            uint callerSeed = callerData.seed;
            float invStrength = 1f / (1 + 1e-6f);
            float3 hitPos = callerData.pos;
            bool callerBoxMode = callerData.boxMode;
            float radius2 = callerData.radius * callerData.radius;
            float hitIRotX = callerData.boxIRotX;
            float hitIRotY = callerData.boxIRotY;
            float hitIRotZ = callerData.boxIRotZ;
            float hitIRotW = callerData.boxIRotW;
            float3 halfBox = new float3(callerData.boxBoundsX, callerData.boxBoundsY, callerData.boxBoundsZ) * 0.5f;
            
            using var gridOffsetPerBoxNa = new NativeArray<int>(gridOffsetPerBox, allocator);

            int boxLength = boxArr.Length;
            BoxCutterManager manager = BoxCutterManagerInstance;
            int maxFrames = manager.asyncDestructionMaxFrames;

            foreach (var kv in fragBoxRanges)
            {
                FragmentType fragType = kv.Key;
                int startIndex = kv.Value.x;
                int sizeInBoxes = kv.Value.y;
                int endIndex = startIndex + sizeInBoxes;

                int gridStart = gridOffsetPerBox[startIndex];
                int gridSize = 0;
                for (int b = startIndex; b < endIndex; ++b)
                {
                    int3 gs = globalSizePerBox[b];
                    gridSize += gs.x * gs.y * gs.z;
                }

                var sliceOffsetPerBox = new int[boxLength];
                {
                    int running = 0;
                    for (int b = 0; b < boxLength; ++b)
                    {
                        sliceOffsetPerBox[b] = running;
                        running += globalSizePerBox[b].x;
                    }
                }
                using var sliceOffsetPerBoxNa = new NativeArray<int>(sliceOffsetPerBox, allocator);

                if (fragType == FragmentType.Standard)
                {
                    NativeArray<byte> fragValidNa = validNa.GetSubArray(gridStart, gridSize);
                    NativeArray<byte> fragOobFlagsNa = oobFlagsNa.GetSubArray(gridStart, gridSize);

                    // Build pre-computed owner array via parallel job
                    using var boxOwnerNa = new NativeArray<int>(gridSize, allocator, NativeArrayOptions.UninitializedMemory);
                    new BuildOwnerArrayJob
                    {
                        OffsetPerBox = gridOffsetPerBoxNa,
                        BaseOffset = gridStart,
                        BoxIndexOffset = 0,
                        OwnerOut = boxOwnerNa
                    }.Schedule(gridSize, 1024).Complete();

                    // Calculate per-object falloff factors using callerStrengthMul
                    float baseFallOffFac = callerData.strength * (callerData.noFallFacNormalizing ? 1 : 0.025f) * invStrength;
                    var fallOffFactorsArr = new float[boxLength];
                    for (int b = 0; b < boxLength; b++)
                    {
                        fallOffFactorsArr[b] = baseFallOffFac * boxArr[b].callerStrengthMul;
                    }
                    using var fallOffFactorsNa = new NativeArray<float>(fallOffFactorsArr, allocator);

                    JobHandle handle = new FragmentStandard
                    {
                        VoxelDataNa = fragValidNa,
                        OobFlagsNa = fragOobFlagsNa,

                        GridOffsetPerBox = gridOffsetPerBoxNa,
                        BoxOwnerNa = boxOwnerNa,
                        GridBase = gridStart,

                        HitBox = hitBoxes,
                        BoxDataArr = boxDataNa,
                        HitMinIntervalNa = hitMinIntervalNa,
                        HitMaxIntervalNa = hitMaxIntervalNa,

                        FallOffFactors = fallOffFactorsNa,
                        Seed = callerSeed,

                        HitPosX = hitPos.x,
                        HitPosY = hitPos.y,
                        HitPosZ = hitPos.z,

                        Radius2 = radius2,
                        BoxMode = callerBoxMode,

                        Iqx = hitIRotX,
                        Iqy = hitIRotY,
                        Iqz = hitIRotZ,
                        Iqw = hitIRotW,

                        HalfBoxBoundX = halfBox.x,
                        HalfBoxBoundY = halfBox.y,
                        HalfBoxBoundZ = halfBox.z
                    }.Schedule(gridSize, 64);

                    await WaitJobComplete(handle, sync, maxFrames);
                    if (manager.CheckExit()) return;
                }
                else if (fragType == FragmentType.Slab || fragType == FragmentType.Splinter)
                {
                    var slabParamArr = new SlabParam[boxLength];
                    for (int b = 0; b < boxLength; ++b)
                    {
                        FragSettings fs = fragSettings[b];

                        if (fs != null)
                        {
                            bool isSplinter = fs.fragmentType == FragmentType.Splinter;
                            
                            slabParamArr[b] = new SlabParam
                            {
                                CanRandomize = isSplinter ? (fs.canRandomSplinterAxis ? 1 : 0) : (fs.canRandomSlabAxis ? 1 : 0),
                                FixedPrimary = isSplinter ? fs.fixedSplinterPrimaryAxis : fs.fixedSlabPrimaryAxis,
                                FixedSecondary = isSplinter ? fs.fixedSplinterSecondaryAxis : fs.fixedSlabSecondaryAxis,
                                FixedTertiary = isSplinter ? fs.fixedSplinterTertiaryAxis : fs.fixedSlabTertiaryAxis,
                                PrimaryMin = isSplinter ? fs.splinterRangePrimary.x : fs.slabRangePrimary.x,
                                PrimaryMax = isSplinter ? fs.splinterRangePrimary.y : fs.slabRangePrimary.y,
                                SecondaryMin = isSplinter ? fs.splinterRangeSecondary.x : fs.slabRangeSecondary.x,
                                SecondaryMax = isSplinter ? fs.splinterRangeSecondary.y : fs.slabRangeSecondary.y,
                                TertiaryMin = isSplinter ? fs.splinterRangeTertiary.x : fs.slabRangeTertiary.x,
                                TertiaryMax = isSplinter ? fs.splinterRangeTertiary.y : fs.slabRangeTertiary.y,
                                SplinterAxisModeValue = (int)fs.splinterAxisMode,
                                OutRadiusScaleMul = fs.outRadiusScaleMul,

                                ClusterMode = isSplinter ? false : fs.canCluster, // Always disable clustering for splinters
                                MaxClusterRadius = fs.maxClusterRadius,

                                UseDiscMask = fs.useDiscMask,
                                UseRadialWeight = fs.radialWeight,
                                DensityFallExp = fs.densityFallExp,

                                SplinterMode = isSplinter
                            };
                        }
                    }

                    using var slabParamsNa = new NativeArray<SlabParam>(slabParamArr, allocator);

                    int startBox = kv.Value.x;
                    int endBox = startBox + kv.Value.y;

                    int groupVoxelCount = 0;
                    int groupSliceStart = sliceOffsetPerBox[startBox];
                    int groupSliceCount = 0;

                    for (int b = startBox; b < endBox; ++b)
                    {
                        int3 gs = globalSizePerBox[b];
                        groupVoxelCount += gs.x * gs.y * gs.z;
                        groupSliceCount += gs.x;
                    }

                    // Build pre-computed slice owner array via parallel job
                    using var sliceOwnerNa = new NativeArray<int>(groupSliceCount, allocator, NativeArrayOptions.UninitializedMemory);
                    new BuildOwnerArrayJob
                    {
                        OffsetPerBox = sliceOffsetPerBoxNa,
                        BaseOffset = groupSliceStart,
                        BoxIndexOffset = 0,
                        OwnerOut = sliceOwnerNa
                    }.Schedule(groupSliceCount, 1024).Complete();

                    var fragValidNa = validNa.GetSubArray(gridOffsetPerBox[startBox], groupVoxelCount);
                    var fragOobFlagsNa = oobFlagsNa.GetSubArray(gridOffsetPerBox[startBox], groupVoxelCount);
                    using var fragVisitedNa = new NativeArray<byte>(groupVoxelCount, allocator);

                    // Calculate per-object falloff factors using callerStrengthMul
                    float baseFallOffFac = callerData.strength * (callerData.noFallFacNormalizing ? 1 : 0.055f) * invStrength;
                    var fallOffFactorsArr = new float[boxLength];
                    for (int b = 0; b < boxLength; b++)
                    {
                        fallOffFactorsArr[b] = baseFallOffFac * boxArr[b].callerStrengthMul;
                    }
                    using var fallOffFactorsNa = new NativeArray<float>(fallOffFactorsArr, allocator);

                    JobHandle handle = new FragmentSlab
                    {
                        Visited = fragVisitedNa,
                        VoxelData = fragValidNa,
                        OobFlagsNa = fragOobFlagsNa,

                        HitBox = hitBoxes,

                        SliceOffsetPerBox = sliceOffsetPerBoxNa,
                        VoxelOffsetPerBox = gridOffsetPerBoxNa,
                        SliceOwnerNa = sliceOwnerNa,

                        VoxelSubBase = gridOffsetPerBox[startBox],
                        GridBase = groupSliceStart,

                        BoxDataArr = boxDataNa,
                        SlabParams = slabParamsNa,
                        HitMinIntervalNa = hitMinIntervalNa,
                        HitMaxIntervalNa = hitMaxIntervalNa,

                        MathRandom = new Unity.Mathematics.Random(callerSeed ^ 0xBADC0DEu),
                        HitPosX = hitPos.x, HitPosY = hitPos.y, HitPosZ = hitPos.z,
                        HitRadiusSq = radius2,
                        FallOffFactors = fallOffFactorsNa,
                        BoxMode = callerBoxMode,

                        Iqx = hitIRotX,
                        Iqy = hitIRotY,
                        Iqz = hitIRotZ,
                        Iqw = hitIRotW,

                        HalfBoxBoundX = halfBox.x, HalfBoxBoundY = halfBox.y, HalfBoxBoundZ = halfBox.z
                    }.Schedule(groupSliceCount, groupSliceCount);

                    await WaitJobComplete(handle, sync, maxFrames);
                    if (manager.CheckExit()) return;
                }
                else if (fragType == FragmentType.Radial)
                {
                    int boxCount = sizeInBoxes;
                    int radialStart = startIndex;

                    var segPrefix = new int[boxCount + 1];
                    var cntPrefix = new int[boxCount + 1];
                    var ringPrefix = new int[boxCount + 1];

                    // Calculate dynamic crack length based on impact parameters
                    float dynamicCrackLength = callerBoxMode 
                        ? math.max(callerData.boxBoundsX, math.max(callerData.boxBoundsY, callerData.boxBoundsZ))
                        : callerData.radius;

                    for (int i = 0; i < boxCount; ++i)
                    {
                        FragSettings fs = fragSettings[radialStart + i];

                        if (fs != null)
                        {
                            float effectiveCrackLength = dynamicCrackLength * fs.crackLengthMultiplier;
                            int maxSegPerSpoke = (int)(effectiveCrackLength / math.max(1e-6f, fs.minSegmentLength)) + 2;
                            int stride = maxSegPerSpoke - 1;

                            // Calculate procedural ring count with enhanced parameters
                            float ringArea = effectiveCrackLength * fs.ringCoverage;
                            int proceduralRingCount = math.max(1, (int)(ringArea * fs.ringDensity));
                            int ringBufferSize = proceduralRingCount + (int)(proceduralRingCount * .33f);

                            segPrefix[i + 1] = segPrefix[i] + fs.radialSpokes * stride;
                            cntPrefix[i + 1] = cntPrefix[i] + fs.radialSpokes;
                            ringPrefix[i + 1] = ringPrefix[i] + ringBufferSize;
                        }
                    }

                    using var segNa = new NativeArray<float4>(segPrefix[boxCount], allocator);
                    using var cntNa = new NativeArray<int>(cntPrefix[boxCount], allocator);
                    using var ringNa = new NativeArray<float>(ringPrefix[boxCount], allocator);
                    var paramNa = new NativeArray<RadialParam>(boxCount, allocator);
                    NativeArray<FragSettingsSnap> fsSnapNa = new NativeArray<FragSettingsSnap>(boxCount, allocator);

                    for (int i = 0; i < boxCount; ++i)
                    {
                        int boxIdx = radialStart + i;
                        BoxObj box = boxArr[boxIdx];
                        BoxTrans boxTrans = boxDataNa[boxIdx];
                        FragSettings src = fragSettings[boxIdx];
                        bool planeBasedOnCaller = box.planeBasedOnCaller;

                        // Calculate override plane vectors if needed
                        float3 overrideNormal = src.radialPlaneNormal;
                        float3 overrideForward = src.radialPlaneForward;

                        if (planeBasedOnCaller) PlaneBasedOnCallerBuilder.CalcPlane(callerData, boxTrans, out overrideNormal, out overrideForward);

                        float voxelSize = boxTrans.voxelSize;
                        float effectiveCrackLength = dynamicCrackLength * src.crackLengthMultiplier;
                        
                        // Calculate advanced procedural ring parameters
                        float ringArea = effectiveCrackLength * src.ringCoverage;
                        int proceduralRingCount = math.max(1, (int)(ringArea * src.ringDensity));
                        
                        // Calculate base ring spacing with falloff consideration
                        float baseRingSpacing = ringArea / math.max(1f, proceduralRingCount);
                        
                        // Apply falloff to create more natural ring distribution
                        float adjustedSpacing = baseRingSpacing * (1f - src.ringFalloff * 0.3f);

                        fsSnapNa[i] = new FragSettingsSnap
                        {
                            planeMode = planeBasedOnCaller ? (int)RadialPlaneMode.Custom : (int)src.planeMode,
                            radialSpokes = src.radialSpokes,
                            maxCrackLength = effectiveCrackLength,
                            minSegmentLength = src.minSegmentLength,
                            maxSegmentLength = src.maxSegmentLength,
                            maxZigZagAngleDeg = src.maxZigZagAngleDeg,
                            crackThickness = src.crackThickness * voxelSize,
                            circularRingCount = proceduralRingCount,
                            ringSpacing = adjustedSpacing,
                            ringGrowthExp = src.ringGrowthExp,
                            ringNoiseAmplitude = src.ringNoiseAmplitude,
                            ringNoiseFrequency = src.ringNoiseFrequency,
                            ringThickness = src.ringThickness * voxelSize,
                            radialRingGenType = (int)src.radialRingGenType,
                            hitRadius = callerData.radius,

                            randomSeed = callerSeed,

                            segBase = segPrefix[i],
                            cntBase = cntPrefix[i],
                            ringBase = ringPrefix[i],

                            // Use override values if planeBasedOnCaller is true, otherwise use original
                            radialPlaneNormal = overrideNormal,
                            radialPlaneForward = overrideForward,

                            segmentStride = (int)(effectiveCrackLength / math.max(1e-6f, src.minSegmentLength)) + 1 - 1
                        };
                    }

                    JobHandle handle = new BuildRadialData
                    {
                        FragSettingsArr = fsSnapNa,
                        BoxDataArr = boxDataNa,
                        Segments = segNa,
                        SegmentCounts = cntNa,
                        RingRadii = ringNa,
                        Params = paramNa,
                        GlobalSeed = callerSeed,
                        BoxStartGlobal = radialStart,
                    }.Schedule(boxCount, 1);
                    await WaitJobComplete(handle, sync, maxFrames);
                    if (manager.CheckExit()) return;
                    
                    RadialParam[] radialParamArr = new RadialParam[paramNa.Length];
                    paramNa.CopyTo(radialParamArr);
                    paramNa.Dispose();

                    for (int i = 0; i < boxCount; ++i)
                    {
                        RadialParam rp = radialParamArr[i];
                        int gi = radialStart + i;
                        FragSettings fs = fragSettings[gi];
                        float effectiveCrackLength = dynamicCrackLength * fs.crackLengthMultiplier;
                        
                        rp.voxelBase = gridOffsetPerBox[gi];
                        int3 gs = globalSizePerBox[gi];
                        rp.yz = gs.y * gs.z;
                        rp.boundYTo = gs.y;
                        rp.boundZTo = gs.z;
                        rp.hitRadiusSq = effectiveCrackLength * effectiveCrackLength;
                        radialParamArr[i] = rp;
                    }

                    paramNa = new NativeArray<RadialParam>(radialParamArr, allocator);
                    int groupVoxelCount = 0;
                    for (int i = radialStart; i < endIndex; ++i)
                    {
                        int3 gs = globalSizePerBox[i];
                        groupVoxelCount += gs.x * gs.y * gs.z;
                    }

                    NativeArray<byte> fragValidNa = validNa.GetSubArray(gridStart, groupVoxelCount);
                    var fragOobFlagsNa = oobFlagsNa.GetSubArray(gridStart, groupVoxelCount);
                    using var fragVisitedNa = new NativeArray<byte>(groupVoxelCount, allocator);
                    using var fragNewVoxelDataNa = new NativeArray<byte>(groupVoxelCount, allocator);

                    using var voxelOfsNa = new NativeArray<int>(gridOffsetPerBox.Skip(radialStart).Take(boxCount).ToArray(), allocator);

                    // Build pre-computed owner array via parallel job (local indices 0 to boxCount-1)
                    using var boxOwnerNa = new NativeArray<int>(groupVoxelCount, allocator, NativeArrayOptions.UninitializedMemory);
                    new BuildOwnerArrayJob
                    {
                        OffsetPerBox = voxelOfsNa,
                        BaseOffset = gridStart,
                        BoxIndexOffset = 0,
                        OwnerOut = boxOwnerNa
                    }.Schedule(groupVoxelCount, 1024).Complete();

                    handle = new FragmentRadial
                    {
                        VoxelData = fragValidNa,
                        OutVisited = fragVisitedNa,
                        NewVoxelData = fragNewVoxelDataNa,
                        OobFlagsNa = fragOobFlagsNa,
                        HitBox = hitBoxes,
                        BoxParams = paramNa,
                        VoxelOffsetPerBox = voxelOfsNa,
                        BoxOwnerNa = boxOwnerNa,
                        HitMinIntervalNa = hitMinIntervalNa,
                        AllSegments = segNa,
                        SegmentCounts = cntNa,
                        RingRadii = ringNa,
                        BoxDataArr = boxDataNa,
                        HitPos = callerData.pos,
                        GridBase = gridStart,
                        GlobalStartBoxIndex = radialStart
                    }.Schedule(groupVoxelCount, 64);
                    await WaitJobComplete(handle, sync, maxFrames);
                    if (manager.CheckExit()) return;
                    
                    paramNa.Dispose();
                    fsSnapNa.Dispose();
                    
                    NativeArray<byte>.Copy(fragNewVoxelDataNa, 0, validNa, gridStart, groupVoxelCount);
                }
            }
        }

        public static async Task<(NativeArray<SubBox> inSubsNa, NativeArray<int> inSubStartsNa, NativeArray<int> inSubCountsNa)> Greedy(
            BoxObj[] boxArr,
            NativeArray<byte> validNa,
            NativeArray<byte> oobFlagsNa,
            int[] gridOffsetPerBox,
            int3[] globalSizePerBox,
            NativeArray<int3> hitMinIntervalNa,
            Allocator allocator,
            bool ignoreColor = false,
            SubBox[][] oobAppendTo = null,
            bool sync = false
        )
        {
            (NativeArray<SubBox>, NativeArray<int>, NativeArray<int>) ReturnEmpty()
            {
                return (new NativeArray<SubBox>(0, allocator), new NativeArray<int>(0, allocator), new NativeArray<int>(0, allocator));
            }

            int boxCount = boxArr.Length;
            BoxCutterManager manager = BoxCutterManagerInstance;

            if (boxCount == 0) return ReturnEmpty();

            using var gridOfsNa = new NativeArray<int>(gridOffsetPerBox, allocator);
            using var sizePerBox = new NativeArray<int3>(globalSizePerBox, allocator);

            // Check if destruction is too big to prevent ArgumentOutOfRangeException
            // GreedyOut struct size: int (4) + byte padded (4) + SubBox (6 ints + byte padded = 28) = 36 bytes
            const int greedyOutSize = 36;
            const int maxCapacity = 2147483647 / greedyOutSize;
            if (validNa.Length > maxCapacity)
            {
                Debug.LogError($"[BoxCutter] Operation is too big! Voxel count ({validNa.Length}) exceeds maximum capacity ({maxCapacity}). Please reduce the amount of voxels in the model.");
                return (new NativeArray<SubBox>(0, allocator), new NativeArray<int>(boxCount, allocator), new NativeArray<int>(boxCount, allocator));
            }

            using var outNl = new NativeList<GreedyOut>(validNa.Length, allocator);
            JobHandle handle = new OnesGreedy
                {
                    HasArr = validNa,
                    OobFlagArr = oobFlagsNa,
                    GridOffsetPerBox = gridOfsNa,
                    SizePerBox = sizePerBox,
                    HitMinPerBox = hitMinIntervalNa,
                    IgnoreColor = ignoreColor,
                    Out = outNl.AsParallelWriter()
                }
                .Schedule(boxCount, 1);
            await WaitJobComplete(handle, sync, manager.asyncDestructionMaxFrames);
            if (manager.CheckExit()) return ReturnEmpty();

            var outsNa = outNl.AsArray();
            int totalOuts = outsNa.Length;

            bool hasAppend = oobAppendTo != null;

            int[] inCounts = new int[boxCount];

            int[] oobCounts = null;
            if (hasAppend) oobCounts = new int[oobAppendTo.Length];

            var outsArr = new GreedyOut[outsNa.Length];
            outsNa.CopyTo(outsArr);
            
            for (int i = 0; i < totalOuts; i++)
            {
                GreedyOut g = outsArr[i];
                if (g.OobFlag == 0)
                {
                    inCounts[g.BoxIdx]++;
                }
                else if (hasAppend)
                {
                    oobCounts[g.BoxIdx]++;
                }
            }

            if (hasAppend)
            {
                int oobLength = oobAppendTo.Length;
                for (int i = 0; i < oobLength; i++)
                {
                    SubBox[] existing = oobAppendTo[i];
                    int existingLen = existing.Length;
                    int newLen = existingLen + oobCounts[i];

                    var newArr = new SubBox[newLen];

                    Array.Copy(existing, newArr, existingLen);

                    oobAppendTo[i] = newArr;

                    oobCounts[i] = existingLen;
                }
            }

            var inStarts = new int[boxCount];
            for (int i = 1; i < boxCount; i++)
            {
                inStarts[i] = inStarts[i - 1] + inCounts[i - 1];
            }

            int totalIn = inStarts[boxCount - 1] + inCounts[boxCount - 1];

            var flatIn = new SubBox[totalIn];

            var inCursors = new int[boxCount];
            Array.Copy(inStarts, inCursors, boxCount);

            for (int i = 0; i < totalOuts; i++)
            {
                GreedyOut g = outsArr[i];
                int b = g.BoxIdx;

                if (g.OobFlag == 0)
                {
                    flatIn[inCursors[b]++] = g.Sub;
                }
                else if (hasAppend)
                {
                    oobAppendTo[b][oobCounts[b]++] = g.Sub;
                }
            }
            
            return (new NativeArray<SubBox>(flatIn, allocator), new NativeArray<int>(inStarts, allocator), new NativeArray<int>(inCounts, allocator));
        }

        private static async Task<(SubBox[][][] mainIslands, SubBox[][][] debrisIslands)> ProcessIslands(
            BoxObj[] boxArr,
            NativeArray<bool> diagNa,
            NativeArray<SubBox> subsNa,
            NativeArray<int> subStartsNa,
            NativeArray<int> subCountsNa,
            int[] survivorCounts,
            NativeArray<int> kdOrigNa,
            NativeArray<CellData> kdCellsNa,
            NativeArray<int> kdCellOwnerNa,
            bool canSpawnDebris,
            Allocator allocator,
            bool sync = false)
        {
            int boxCount = boxArr.Length;
            int totalSubs = subsNa.Length;
            BoxCutterManager manager = BoxCutterManagerInstance;

            if (totalSubs == 0)
            {
                var emptyMain = new SubBox[boxCount][][];
                var emptyDebris = new SubBox[boxCount][][];
                for (int i = 0; i < boxCount; i++)
                {
                    emptyMain[i] = new SubBox[][]{};
                    emptyDebris[i] = new SubBox[][]{};
                }
                return (emptyMain, emptyDebris);
            }

            // Gather edges for island detection
            using var edgeStream = new NativeStream(kdCellsNa.Length, allocator);

            JobHandle edgeHandle = new EdgeGathering
            {
                subObjArr = subsNa,
                kdOriginalIndices = kdOrigNa,
                kdCells = kdCellsNa,
                kdCellOwner = kdCellOwnerNa,
                filterDiagonalNa = diagNa,
                streamWriter = edgeStream.AsWriter()
            }.Schedule(kdCellsNa.Length, 64);

            using var parentNa = new NativeArray<int>(totalSubs, allocator);
            using var rankNa = new NativeArray<int>(totalSubs, allocator);

            JobHandle unionHandle = new UnionSetIsland
            {
                streamReader = edgeStream.AsReader(),
                parent = parentNa,
                rank = rankNa,
                foreachCount = kdCellsNa.Length
            }.Schedule(edgeHandle);

            await WaitJobComplete(unionHandle, sync, manager.asyncDestructionMaxFrames);
            if (manager.CheckExit()) return (new SubBox[][][]{}, new SubBox[][][]{});

            // Copy data to managed arrays
            SubBox[] subsArr = new SubBox[subsNa.Length];
            subsNa.CopyTo(subsArr);
            int[] starts = new int[subStartsNa.Length];
            subStartsNa.CopyTo(starts);
            int[] counts = new int[subCountsNa.Length];
            subCountsNa.CopyTo(counts);
            int[] parents = new int[parentNa.Length];
            parentNa.CopyTo(parents);

            var allBoxMainIslands = new SubBox[boxCount][][];
            var allBoxDebrisIslands = new SubBox[boxCount][][];

            for (int b = 0; b < boxCount; ++b)
            {
                int sCnt = counts[b];
                int sOfs = starts[b];
                int survivorCount = survivorCounts[b];
                bool isAnchored = boxArr[b].connectionState == ConnectionStateEnum.Anchored;

                if (sCnt == 0)
                {
                    allBoxMainIslands[b] = Array.Empty<SubBox[]>();
                    allBoxDebrisIslands[b] = Array.Empty<SubBox[]>();
                    continue;
                }

                // Map roots to dense group IDs
                var rootMap = new int[subsArr.Length];
                var localGroup = new int[sCnt];
                int nextGroup = 0;

                for (int i = 0; i < sCnt; ++i)
                {
                    int root = parents[sOfs + i];
                    if (rootMap[root] == 0 && root != 0)
                        rootMap[root] = ++nextGroup;
                }

                if (rootMap[0] == 0)
                {
                    for (int i = 0; i < sCnt; ++i)
                    {
                        if (parents[sOfs + i] == 0)
                        {
                            rootMap[0] = ++nextGroup;
                            break;
                        }
                    }
                }

                for (int i = 0; i < sCnt; ++i)
                    localGroup[i] = rootMap[parents[sOfs + i]] - 1;

                int totalGroups = nextGroup;
                if (totalGroups == 0)
                {
                    allBoxMainIslands[b] = Array.Empty<SubBox[]>();
                    allBoxDebrisIslands[b] = Array.Empty<SubBox[]>();
                    continue;
                }

                // Count subs per group
                var groupCounts = new int[totalGroups];
                for (int i = 0; i < sCnt; ++i)
                    groupCounts[localGroup[i]]++;

                // Check connectivity: a group is connected if it contains any survivor (index < survivorCount)
                var groupConnected = new bool[totalGroups];
                for (int i = 0; i < sCnt; ++i)
                {
                    if (i < survivorCount)
                        groupConnected[localGroup[i]] = true;
                }

                // Build prefix sums for contiguous layout
                var prefix = new int[totalGroups];
                for (int g = 1; g < totalGroups; ++g)
                    prefix[g] = prefix[g - 1] + groupCounts[g - 1];

                // Arrange subs contiguously per group
                var flatContig = new SubBox[sCnt];
                var cursor = (int[])prefix.Clone();
                for (int i = 0; i < sCnt; ++i)
                {
                    int g = localGroup[i];
                    flatContig[cursor[g]++] = subsArr[sOfs + i];
                }

                // Count connected and debris
                int connectedSubCount = 0, debrisGroupCount = 0;
                for (int g = 0; g < totalGroups; ++g)
                {
                    if (groupConnected[g])
                        connectedSubCount += groupCounts[g];
                    else
                        debrisGroupCount++;
                }

                // For anchored boxes: merge all connected islands into ONE main island
                if (isAnchored)
                {
                    if (connectedSubCount > 0)
                    {
                        var mergedMain = new SubBox[connectedSubCount];
                        int writeIdx = 0;
                        for (int g = 0; g < totalGroups; ++g)
                        {
                            if (groupConnected[g])
                            {
                                Array.Copy(flatContig, prefix[g], mergedMain, writeIdx, groupCounts[g]);
                                writeIdx += groupCounts[g];
                            }
                        }
                        allBoxMainIslands[b] = new[] { mergedMain };
                    }
                    else
                    {
                        allBoxMainIslands[b] = Array.Empty<SubBox[]>();
                    }

                    // Debris handling for anchored is same as non-anchored
                    if (canSpawnDebris && debrisGroupCount > 0)
                    {
                        var debrisIslands = new SubBox[debrisGroupCount][];
                        int ldi = 0;
                        for (int g = 0; g < totalGroups; ++g)
                        {
                            if (!groupConnected[g])
                            {
                                var arr = new SubBox[groupCounts[g]];
                                Array.Copy(flatContig, prefix[g], arr, 0, groupCounts[g]);
                                debrisIslands[ldi++] = arr;
                            }
                        }
                        allBoxDebrisIslands[b] = debrisIslands;
                    }
                    else
                    {
                        allBoxDebrisIslands[b] = Array.Empty<SubBox[]>();
                    }
                    continue;
                }

                // Non-anchored: each connected island stays separate
                int mainCount = totalGroups - debrisGroupCount;
                var mainIslands = new SubBox[mainCount][];
                var nonAnchoredDebris = canSpawnDebris ? new SubBox[debrisGroupCount][] : Array.Empty<SubBox[]>();

                int mi = 0, di = 0;
                for (int g = 0; g < totalGroups; ++g)
                {
                    int len = groupCounts[g];
                    var arr = new SubBox[len];
                    Array.Copy(flatContig, prefix[g], arr, 0, len);

                    if (groupConnected[g])
                    {
                        mainIslands[mi++] = arr;
                    }
                    else if (canSpawnDebris)
                    {
                        nonAnchoredDebris[di++] = arr;
                    }
                }

                allBoxMainIslands[b] = mainIslands;
                allBoxDebrisIslands[b] = nonAnchoredDebris;
            }

            return (allBoxMainIslands, allBoxDebrisIslands);
        }

        public static async Task<(NativeArray<int> kdOriginalIndicesGlobalNa, NativeArray<CellData> kdCellDataGlobalNa, NativeArray<int> kdCellOwnerNa, NativeArray<int2> leafCellCountPerBoxNa)> BuildKdTree(
            NativeArray<SubBox> subsNa,
            NativeArray<float> fillRatioNa,
            NativeArray<int> subStartsNa,
            NativeArray<int> subCountsNa,
            Allocator allocator,
            bool sync = false)
        {
            (NativeArray<int>, NativeArray<CellData>, NativeArray<int>, NativeArray<int2>) ReturnEmpty()
            {
                return (new NativeArray<int>(0, allocator), new NativeArray<CellData>(0, allocator), new NativeArray<int>(0, allocator), new NativeArray<int2>(0, allocator));   
            }
            
            int boxCount = subStartsNa.Length;
            int totalSubs = subsNa.Length;

            BoxCutterManager manager = BoxCutterManagerInstance;

            int[] subStartsArr = new int[subStartsNa.Length];
            subStartsNa.CopyTo(subStartsArr);
            int[] subCountsArr = new int[subCountsNa.Length];
            subCountsNa.CopyTo(subCountsArr);

            if (totalSubs == 0) return ReturnEmpty();

            var leafBases = new int[boxCount];
            var cellBases = new int[boxCount];
            for (int i = 1; i < boxCount; i++)
            {
                leafBases[i] = leafBases[i - 1] + subCountsArr[i - 1];
                cellBases[i] = cellBases[i - 1] + subCountsArr[i - 1];
            }

            int leafCap = totalSubs, cellCap = totalSubs;
            using var boxSubStartNa = new NativeArray<int>(subStartsArr, allocator);
            using var boxSubCountNa = new NativeArray<int>(subCountsArr, allocator);
            using var leafBaseNa = new NativeArray<int>(leafBases, allocator);
            using var cellBaseNa = new NativeArray<int>(cellBases, allocator);
            using var leafIndicesNa = new NativeArray<int>(leafCap, allocator);
            using var cellDataNa = new NativeArray<CellData>(cellCap, allocator);
            using var leafCountPerBoxNa = new NativeArray<int2>(boxCount, allocator);

            JobHandle handle = new BuildKdTree
                {
                    AllSubs = subsNa,
                    BoxSubStart = boxSubStartNa,
                    BoxSubCount = boxSubCountNa,
                    LeafBase = leafBaseNa,
                    CellBase = cellBaseNa,
                    FillThresholdNa = fillRatioNa,
                    MaxPerLeaf = BoxCutterManagerInstance.kdMaxSubInCell,
                    LeafIndicesFlat = leafIndicesNa,
                    CellSubsFlat = cellDataNa,
                    LeafCellCountPerBox = leafCountPerBoxNa
                }
                .Schedule(boxCount, 1);

            await WaitJobComplete(handle, sync);

            int2[] leafCountArr = new int2[boxCount];
            leafCountPerBoxNa.CopyTo(leafCountArr);
            int[] flatLeafIdxArr = new int[leafCap];
            leafIndicesNa.CopyTo(flatLeafIdxArr);
            CellData[] flatCellArr = new CellData[cellCap];
            cellDataNa.CopyTo(flatCellArr);

            int totalLeaves = 0, totalCells = 0;
            for (int i = 0; i < boxCount; i++)
            {
                totalLeaves += leafCountArr[i].x;
                totalCells += leafCountArr[i].y;
            }

            var origIdxArr = new int[totalLeaves];
            var cellDataArr = new CellData[totalCells];
            var cellOwnerArr = new int[totalCells];

            int leafWrite = 0, cellWrite = 0;
            for (int b = 0; b < boxCount; b++)
            {
                int leafStart = leafBases[b], leafCnt = leafCountArr[b].x;
                int subOff = subStartsArr[b];

                Array.Copy(flatLeafIdxArr, leafStart, origIdxArr, leafWrite, leafCnt);
                for (int k = 0; k < leafCnt; k++)
                    origIdxArr[leafWrite + k] += subOff;

                int cellStart = cellBases[b], cellCnt = leafCountArr[b].y;
                for (int k = 0; k < cellCnt; k++)
                {
                    var cd = flatCellArr[cellStart + k];
                    cd.rangeStart += leafWrite;
                    cd.rangeEnd += leafWrite;
                    cellDataArr[cellWrite + k] = cd;
                    cellOwnerArr[cellWrite + k] = b;
                }

                leafWrite += leafCnt;
                cellWrite += cellCnt;
            }
            
            return (new NativeArray<int>(origIdxArr, allocator), new NativeArray<CellData>(cellDataArr, allocator), new NativeArray<int>(cellOwnerArr, allocator), new NativeArray<int2>(leafCountPerBoxNa, allocator));
        }

        public static (NativeArray<int> kdOriginalIndicesGlobalNa, NativeArray<CellData> kdCellDataGlobalNa, NativeArray<int> kdCellOwnerNa, NativeArray<int2> leafCellCountPerBoxNa) BuildGrid(
            NativeArray<SubBox> subsNa,
            NativeArray<float> fillRatioNa,
            NativeArray<int> subStartsNa,
            NativeArray<int> subCountsNa,
            Allocator allocator,
            bool sync = false)
        {
            (NativeArray<int>, NativeArray<CellData>, NativeArray<int>, NativeArray<int2>) ReturnEmpty()
            {
                return (new NativeArray<int>(0, allocator), new NativeArray<CellData>(0, allocator), new NativeArray<int>(0, allocator), new NativeArray<int2>(0, allocator));
            }

            int boxCount = subStartsNa.Length;
            int totalSubs = subsNa.Length;

            if (totalSubs == 0) return ReturnEmpty();

            int[] subStartsArr = new int[subStartsNa.Length];
            subStartsNa.CopyTo(subStartsArr);

            // 1. Count exact capacity needed AND compute grid parameters (combined int2: x=leafCount, y=cellCount)
            using var countPerBoxNa = new NativeArray<int2>(boxCount, allocator);
            using var gridParamsPerBoxNa = new NativeArray<GridParameters>(boxCount, allocator); // NEW: Store grid parameters

            JobHandle countHandle = new CountLeafCapacity
                {
                    AllSubs = subsNa,
                    BoxSubStart = subStartsNa,
                    BoxSubCount = subCountsNa,
                    ResolutionTargetPerCell = math.max(1, BoxCutterManagerInstance.kdMaxSubInCell),
                    CountPerBox = countPerBoxNa,
                    GridParamsPerBox = gridParamsPerBoxNa // NEW: Output grid parameters
                }
                .Schedule(boxCount, 1);

            countHandle.Complete();

            // 2. Copy counts and compute base offsets
            int2[] countsArr = new int2[boxCount];
            countPerBoxNa.CopyTo(countsArr);

            int2[] basesArr = new int2[boxCount]; // x=leafBase, y=cellBase

            for (int i = 1; i < boxCount; i++)
            {
                basesArr[i].x = basesArr[i - 1].x + countsArr[i - 1].x;
                basesArr[i].y = basesArr[i - 1].y + countsArr[i - 1].y;
            }

            int leafCap = basesArr[boxCount - 1].x + countsArr[boxCount - 1].x;
            int cellCap = basesArr[boxCount - 1].y + countsArr[boxCount - 1].y;

            // Handle empty case
            if (leafCap == 0) return ReturnEmpty();

            // 3. Allocate exact sizes (combined int2: x=leafBase, y=cellBase)
            using var basePerBoxNa = new NativeArray<int2>(basesArr, allocator);

            using var leafIndicesNa = new NativeArray<int>(leafCap, allocator);
            using var cellDataNa = new NativeArray<CellData>(cellCap, allocator);
            var leafCellCountOutputNa = new NativeArray<int2>(boxCount, allocator);

            // 4. Schedule Grid Build Job (now with pre-computed grid parameters)
            JobHandle buildHandle = new BuildGrid
                {
                    AllSubs = subsNa,
                    BoxSubStart = subStartsNa,
                    BoxSubCount = subCountsNa,
                    BasePerBox = basePerBoxNa,
                    GridParamsPerBox = gridParamsPerBoxNa, // NEW: Pass pre-computed grid parameters
                    LeafIndicesFlat = leafIndicesNa,
                    CellSubsFlat = cellDataNa,
                    LeafCellCountPerBox = leafCellCountOutputNa
                }
                .Schedule(boxCount, 1);

            buildHandle.Complete();

            // 5. Serialize Output - Calculate totals and output base offsets
            int2[] actualCountsArr = new int2[boxCount];
            leafCellCountOutputNa.CopyTo(actualCountsArr);

            // Calculate total output sizes and per-box output bases
            int2[] outputBasesArr = new int2[boxCount]; // x=leafOutputBase, y=cellOutputBase
            int totalLeaves = 0, totalCells = 0;

            for (int i = 0; i < boxCount; i++)
            {
                outputBasesArr[i] = new int2(totalLeaves, totalCells);
                totalLeaves += actualCountsArr[i].x;
                totalCells += actualCountsArr[i].y;
            }

            // Allocate output arrays
            var outputLeafIndicesNa = new NativeArray<int>(totalLeaves, allocator, NativeArrayOptions.UninitializedMemory);
            var outputCellDataNa = new NativeArray<CellData>(totalCells, allocator, NativeArrayOptions.UninitializedMemory);
            var outputCellOwnerNa = new NativeArray<int>(totalCells, allocator, NativeArrayOptions.UninitializedMemory);
            using var outputBasesNa = new NativeArray<int2>(outputBasesArr, allocator);
            using var actualCountsNa = new NativeArray<int2>(actualCountsArr, allocator);

            // Run serialization job
            JobHandle serializeHandle = new SerializeGridOutput
            {
                FlatLeafIndices = leafIndicesNa,
                FlatCellData = cellDataNa,
                BasesPerBox = basePerBoxNa,
                CountsPerBox = actualCountsNa,
                SubStartsPerBox = subStartsNa,
                OutputBasesPerBox = outputBasesNa,
                OutputLeafIndices = outputLeafIndicesNa,
                OutputCellData = outputCellDataNa,
                OutputCellOwner = outputCellOwnerNa
            }.Schedule(boxCount, 1);

            serializeHandle.Complete();

            return (outputLeafIndicesNa, outputCellDataNa, outputCellOwnerNa, leafCellCountOutputNa);
        }
    }
}