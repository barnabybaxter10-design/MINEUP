using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    /// <summary>
    /// Pre-computed grid parameters shared between CountLeafCapacity and BuildGrid
    /// to avoid redundant calculations.
    /// </summary>
    public struct GridParameters
    {
        // Bounds
        public int minX, minY, minZ;
        public int maxX, maxY, maxZ;
        
        // Grid dimensions
        public int dimX, dimY, dimZ;
        public int totalCells;
        public int cellsXY;
        
        // Grid steps (inverse for performance)
        public float invStepX, invStepY, invStepZ;
        
        // Grid steps (forward for cell boundary calculations)
        public float stepX, stepY, stepZ;
        
        // Statistical data
        public float meanSubSize;
    }

    /// <summary>
    /// Counts the exact number of cell assignments needed for multi-cell sub assignment.
    /// This determines the precise leaf capacity before allocation.
    /// Uses mean sub size for better load balancing.
    /// NOW ALSO COMPUTES AND STORES GRID PARAMETERS FOR REUSE.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct CountLeafCapacity : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> AllSubs;
        [ReadOnly] public NativeArray<int> BoxSubStart;
        [ReadOnly] public NativeArray<int> BoxSubCount;

        public int ResolutionTargetPerCell;

        [NativeDisableParallelForRestriction] public NativeArray<int2> CountPerBox; // x = leafCount, y = cellCount
        [NativeDisableParallelForRestriction] public NativeArray<GridParameters> GridParamsPerBox; // NEW: Store computed grid parameters

        public void Execute(int boxIdx)
        {
            int subCount = BoxSubCount[boxIdx];
            if (subCount == 0)
            {
                CountPerBox[boxIdx] = int2.zero;
                GridParamsPerBox[boxIdx] = default;
                return;
            }

            int subStart = BoxSubStart[boxIdx];

            // 1. Calculate Bounds and accumulate sub sizes for mean calculation
            int minX = IntInfinity, minY = IntInfinity, minZ = IntInfinity;
            int maxX = NegIntInfinity, maxY = NegIntInfinity, maxZ = NegIntInfinity;
            
            float totalSubSize = 0f;

            for (int i = 0; i < subCount; i++)
            {
                var sb = AllSubs[subStart + i];
                minX = math.min(minX, sb.minX);
                minY = math.min(minY, sb.minY);
                minZ = math.min(minZ, sb.minZ);
                maxX = math.max(maxX, sb.maxX);
                maxY = math.max(maxY, sb.maxY);
                maxZ = math.max(maxZ, sb.maxZ);
                
                // Calculate diagonal size of this sub
                float dx = sb.maxX - sb.minX;
                float dy = sb.maxY - sb.minY;
                float dz = sb.maxZ - sb.minZ;
                totalSubSize += math.sqrt(dx * dx + dy * dy + dz * dz);
            }

            // 2. Calculate mean sub size for adaptive grid resolution
            float meanSubSize = totalSubSize / (float)subCount;

            // 3. Calculate Grid Resolution with mean-based adjustment
            float3 size = new float3((float)maxX - minX, (float)maxY - minY, (float)maxZ - minZ);

            float targetTotalCells = (float)subCount / (float)ResolutionTargetPerCell;
            float cubeRoot = math.pow(targetTotalCells, 1f / 3f);
            float avgLen = (size.x + size.y + size.z) / 3.0f + 1e-6f;

            // Adjust resolution based on mean sub size relative to domain
            float meanRatio = math.clamp(meanSubSize / avgLen, 0.1f, 2.0f);
            float adaptiveFactor = 1.0f / math.sqrt(meanRatio);

            int dimX = math.max(1, (int)(cubeRoot * (size.x / avgLen) * adaptiveFactor));
            int dimY = math.max(1, (int)(cubeRoot * (size.y / avgLen) * adaptiveFactor));
            int dimZ = math.max(1, (int)(cubeRoot * (size.z / avgLen) * adaptiveFactor));

            int totalCells = dimX * dimY * dimZ;
            int cellsXY = dimX * dimY;

            // Precompute inverse steps
            float invStepX = dimX / (size.x + 1e-6f);
            float invStepY = dimY / (size.y + 1e-6f);
            float invStepZ = dimZ / (size.z + 1e-6f);

            // Precompute forward steps
            float stepX = size.x / dimX;
            float stepY = size.y / dimY;
            float stepZ = size.z / dimZ;

            // Store grid parameters for reuse in BuildGrid
            GridParamsPerBox[boxIdx] = new GridParameters
            {
                minX = minX, minY = minY, minZ = minZ,
                maxX = maxX, maxY = maxY, maxZ = maxZ,
                dimX = dimX, dimY = dimY, dimZ = dimZ,
                totalCells = totalCells,
                cellsXY = cellsXY,
                invStepX = invStepX, invStepY = invStepY, invStepZ = invStepZ,
                stepX = stepX, stepY = stepY, stepZ = stepZ,
                meanSubSize = meanSubSize
            };

            // 4. Count exact assignments and track occupied cells
            int totalAssignments = 0;
            var cellOccupied = new NativeArray<bool>(totalCells, Allocator.Temp, NativeArrayOptions.ClearMemory);

            for (int i = 0; i < subCount; i++)
            {
                var sb = AllSubs[subStart + i];

                int ixMin = math.clamp((int)((sb.minX - minX) * invStepX), 0, dimX - 1);
                int iyMin = math.clamp((int)((sb.minY - minY) * invStepY), 0, dimY - 1);
                int izMin = math.clamp((int)((sb.minZ - minZ) * invStepZ), 0, dimZ - 1);

                int ixMax = math.clamp((int)((sb.maxX - minX) * invStepX), 0, dimX - 1);
                int iyMax = math.clamp((int)((sb.maxY - minY) * invStepY), 0, dimY - 1);
                int izMax = math.clamp((int)((sb.maxZ - minZ) * invStepZ), 0, dimZ - 1);

                for (int iz = izMin; iz <= izMax; iz++)
                {
                    int zOffset = iz * cellsXY;
                    for (int iy = iyMin; iy <= iyMax; iy++)
                    {
                        int yOffset = iy * dimX;
                        for (int ix = ixMin; ix <= ixMax; ix++)
                        {
                            int flatIdx = ix + yOffset + zOffset;
                            cellOccupied[flatIdx] = true;
                            totalAssignments++;
                        }
                    }
                }
            }

            // 5. Count active cells
            int activeCells = 0;
            for (int i = 0; i < totalCells; i++)
            {
                if (cellOccupied[i]) activeCells++;
            }

            CountPerBox[boxIdx] = new int2(totalAssignments, activeCells);

            cellOccupied.Dispose();
        }
    }


    [BurstCompile(DisableSafetyChecks = true)]
    public struct BuildGrid : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> AllSubs;
        [ReadOnly] public NativeArray<int> BoxSubStart;
        [ReadOnly] public NativeArray<int> BoxSubCount;
        [ReadOnly] public NativeArray<int2> BasePerBox;
        [ReadOnly] public NativeArray<GridParameters> GridParamsPerBox;

        [NativeDisableParallelForRestriction] public NativeArray<int> LeafIndicesFlat;
        [NativeDisableParallelForRestriction] public NativeArray<BoxObj.CellData> CellSubsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<int2> LeafCellCountPerBox;

        public void Execute(int boxIdx)
        {
            int subCount = BoxSubCount[boxIdx];
            if (subCount == 0)
            {
                LeafCellCountPerBox[boxIdx] = int2.zero;
                return;
            }

            int subStart = BoxSubStart[boxIdx];
            int2 baseOffsets = BasePerBox[boxIdx];
            int leafBase = baseOffsets.x;
            int cellBase = baseOffsets.y;

            GridParameters gp = GridParamsPerBox[boxIdx];

            int minX = gp.minX, minY = gp.minY, minZ = gp.minZ;
            int maxX = gp.maxX, maxY = gp.maxY, maxZ = gp.maxZ;
            int dimX = gp.dimX, dimY = gp.dimY, dimZ = gp.dimZ;
            int totalCells = gp.totalCells;
            int cellsXY = gp.cellsXY;
            float invStepX = gp.invStepX, invStepY = gp.invStepY, invStepZ = gp.invStepZ;
            float stepX = gp.stepX, stepY = gp.stepY, stepZ = gp.stepZ;

            var subCellAssignments = new NativeList<int>(subCount * 4, Allocator.Temp);
            var subAssignmentOffsets = new NativeArray<int>(subCount + 1, Allocator.Temp);

            int currentOffset = 0;
            for (int i = 0; i < subCount; i++)
            {
                subAssignmentOffsets[i] = currentOffset;
                var sb = AllSubs[subStart + i];

                int ixMin = math.clamp((int)((sb.minX - minX) * invStepX), 0, dimX - 1);
                int iyMin = math.clamp((int)((sb.minY - minY) * invStepY), 0, dimY - 1);
                int izMin = math.clamp((int)((sb.minZ - minZ) * invStepZ), 0, dimZ - 1);

                int ixMax = math.clamp((int)((sb.maxX - minX) * invStepX), 0, dimX - 1);
                int iyMax = math.clamp((int)((sb.maxY - minY) * invStepY), 0, dimY - 1);
                int izMax = math.clamp((int)((sb.maxZ - minZ) * invStepZ), 0, dimZ - 1);

                for (int iz = izMin; iz <= izMax; iz++)
                {
                    int zOffset = iz * cellsXY;
                    for (int iy = iyMin; iy <= iyMax; iy++)
                    {
                        int yOffset = iy * dimX;
                        for (int ix = ixMin; ix <= ixMax; ix++)
                        {
                            subCellAssignments.Add(ix + yOffset + zOffset);
                            currentOffset++;
                        }
                    }
                }
            }

            subAssignmentOffsets[subCount] = currentOffset;

            var cellCounts = new NativeArray<int>(totalCells, Allocator.Temp, NativeArrayOptions.ClearMemory);

            for (int i = 0; i < subCellAssignments.Length; i++)
            {
                cellCounts[subCellAssignments[i]]++;
            }

            var cellOffsets = new NativeArray<int>(totalCells + 1, Allocator.Temp);
            int runningTotal = 0;
            for (int k = 0; k < totalCells; k++)
            {
                cellOffsets[k] = runningTotal;
                runningTotal += cellCounts[k];
            }

            cellOffsets[totalCells] = runningTotal;

            var currentOffsets = new NativeArray<int>(totalCells, Allocator.Temp);
            NativeArray<int>.Copy(cellOffsets, currentOffsets, totalCells);

            for (int i = 0; i < subCount; i++)
            {
                int assignStart = subAssignmentOffsets[i];
                int assignEnd = subAssignmentOffsets[i + 1];

                for (int j = assignStart; j < assignEnd; j++)
                {
                    int cellIdx = subCellAssignments[j];
                    int writePos = currentOffsets[cellIdx]++;
                    LeafIndicesFlat[leafBase + writePos] = i;
                }
            }

            int cellsWritten = 0;

            for (int iz = 0, flatIdx = 0; iz < dimZ; iz++)
            {
                int lMinZ = minZ + (int)(iz * stepZ);
                int lMaxZ = (iz == dimZ - 1) ? maxZ : minZ + (int)((iz + 1) * stepZ);

                for (int iy = 0; iy < dimY; iy++)
                {
                    int lMinY = minY + (int)(iy * stepY);
                    int lMaxY = (iy == dimY - 1) ? maxY : minY + (int)((iy + 1) * stepY);

                    for (int ix = 0; ix < dimX; ix++, flatIdx++)
                    {
                        int count = cellCounts[flatIdx];
                        if (count == 0) continue;

                        int lMinX = minX + (int)(ix * stepX);
                        int lMaxX = (ix == dimX - 1) ? maxX : minX + (int)((ix + 1) * stepX);

                        int startOffset = cellOffsets[flatIdx];

                        CellSubsFlat[cellBase + cellsWritten] = new BoxObj.CellData
                        {
                            minX = lMinX, minY = lMinY, minZ = lMinZ,
                            maxX = lMaxX, maxY = lMaxY, maxZ = lMaxZ,
                            rangeStart = startOffset,
                            rangeEnd = startOffset + count
                        };
                        cellsWritten++;
                    }
                }
            }

            LeafCellCountPerBox[boxIdx] = new int2(runningTotal, cellsWritten);

            // Cleanup
            subCellAssignments.Dispose();
            subAssignmentOffsets.Dispose();
            cellCounts.Dispose();
            cellOffsets.Dispose();
            currentOffsets.Dispose();
        }
    }

    /// <summary>
    /// Serializes grid build output by adjusting indices and cell ranges to global space.
    /// Processes per-box local data into globally-indexed arrays for final output.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct SerializeGridOutput : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> FlatLeafIndices;
        [ReadOnly] public NativeArray<BoxObj.CellData> FlatCellData;
        [ReadOnly] public NativeArray<int2> BasesPerBox;
        [ReadOnly] public NativeArray<int2> CountsPerBox;
        [ReadOnly] public NativeArray<int> SubStartsPerBox;
        [ReadOnly] public NativeArray<int2> OutputBasesPerBox;

        [NativeDisableParallelForRestriction] public NativeArray<int> OutputLeafIndices;
        [NativeDisableParallelForRestriction] public NativeArray<BoxObj.CellData> OutputCellData;
        [NativeDisableParallelForRestriction] public NativeArray<int> OutputCellOwner;

        public void Execute(int boxIdx)
        {
            int2 bases = BasesPerBox[boxIdx];
            int2 counts = CountsPerBox[boxIdx];
            int2 outBases = OutputBasesPerBox[boxIdx];
            int subOffset = SubStartsPerBox[boxIdx];

            int leafStart = bases.x;
            int leafCount = counts.x;
            int leafOutBase = outBases.x;

            for (int i = 0; i < leafCount; i++)
            {
                int localIdx = FlatLeafIndices[leafStart + i];
                OutputLeafIndices[leafOutBase + i] = localIdx + subOffset;
            }

            int cellStart = bases.y;
            int cellCount = counts.y;
            int cellOutBase = outBases.y;

            for (int i = 0; i < cellCount; i++)
            {
                var cd = FlatCellData[cellStart + i];

                cd.rangeStart += leafOutBase;
                cd.rangeEnd += leafOutBase;

                OutputCellData[cellOutBase + i] = cd;
                OutputCellOwner[cellOutBase + i] = boxIdx;
            }
        }
    }
}