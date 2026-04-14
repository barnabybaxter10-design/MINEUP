using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Greedy Voxel Merging Job
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Parallel job for greedy voxel merging optimization.
    /// Combines adjacent voxels of the same type into larger SubBox primitives to reduce fragment count.
    /// Uses a three-dimensional greedy approach extending along Z, Y, then X axes for maximum efficiency.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct OnesGreedy : IJobParallelFor
    {
        /// <summary>3D grid marking voxel presence and type</summary>
        [NativeDisableParallelForRestriction] public NativeArray<byte> HasArr;
        /// <summary>Flags marking voxels that are out-of-bounds for structural integrity</summary>
        [ReadOnly] public NativeArray<byte> OobFlagArr;

        /// <summary>Starting grid offset for each BoxObj's voxel data</summary>
        [ReadOnly] public NativeArray<int> GridOffsetPerBox;
        /// <summary>3D dimensions of each BoxObj's voxel grid</summary>
        [ReadOnly] public NativeArray<int3> SizePerBox;
        /// <summary>Minimum hit coordinates for each BoxObj in voxel space</summary>
        [ReadOnly] public NativeArray<int3> HitMinPerBox;

        /// <summary>Thread-safe output writer for generated SubBox results</summary>
        public NativeList<GreedyOut>.ParallelWriter Out;

        /// <summary>If true, merge all non-empty voxels regardless of color/type</summary>
        public bool IgnoreColor;

        /// <summary>
        /// Executes greedy voxel merging for a single BoxObj instance.
        /// Processes the 3D grid to find and merge adjacent voxels into larger SubBox regions.
        /// </summary>
        /// <param name="boxIdx">Index of the BoxObj to process</param>
        public void Execute(int boxIdx)
        {
            int3 size = SizePerBox[boxIdx];
            int sizeX = size.x;
            int sizeY = size.y;
            int sizeZ = size.z;

            int yz = sizeY * sizeZ;  // Y-Z plane stride for linear indexing
            int baseOf = GridOffsetPerBox[boxIdx];
            int3 hitMin = HitMinPerBox[boxIdx];

            // Iterate through 3D grid in X-Y-Z order for greedy merging
            int xBase = baseOf;
            for (int x = 0; x < sizeX; ++x, xBase += yz)
            {
                int yBase = xBase;
                for (int y = 0; y < sizeY; ++y, yBase += sizeZ)
                {
                    int idx = yBase;
                    for (int z = 0; z < sizeZ; ++z, ++idx)
                    {
                        byte v = HasArr[idx];
                        if (v == 255) continue;  // Skip already processed voxels

                        // Phase 1: Extend along Z-axis as far as possible
                        int bz = z;
                        int endIdx = idx;
                        while (true)
                        {
                            int nz = bz + 1;
                            if (nz >= sizeZ) break;
                            int nIdx = endIdx + 1;

                            // Stop if voxel type doesn't match or is already processed
                            if (IgnoreColor ? (HasArr[nIdx] == 255) : (HasArr[nIdx] != v)) break;

                            bz = nz;
                            endIdx = nIdx;
                        }

                        // Phase 2: Extend along Y-axis while maintaining Z-range
                        int by = y;
                        int lineStartIdx = yBase;
                        while (true)
                        {
                            int ny = by + 1;
                            if (ny >= sizeY) break;

                            int nextLine = lineStartIdx + sizeZ;
                            int checkIdx = nextLine + z;
                            bool ok = true;

                            // Check if entire Z-range can be extended to next Y-line
                            for (int zi = z; zi <= bz; ++zi, ++checkIdx)
                            {
                                if (IgnoreColor ? (HasArr[checkIdx] == 255) : (HasArr[checkIdx] != v))
                                {
                                    ok = false;
                                    break;
                                }
                            }

                            if (!ok) break;
                            by = ny;
                            lineStartIdx = nextLine;
                        }

                        // Phase 3: Extend along X-axis while maintaining Y-Z rectangle
                        int bx = x;
                        int slabBase = xBase;
                        while (true)
                        {
                            int nx = bx + 1;
                            if (nx >= sizeX) break;

                            int nextSlab = slabBase + yz;
                            bool ok = true;

                            // Check if entire Y-Z rectangle can be extended to next X-slab
                            int yLineIdx = nextSlab + y * sizeZ + z;
                            for (int yi = y; yi <= by && ok; ++yi, yLineIdx += sizeZ)
                            {
                                int voxelIdx = yLineIdx;
                                for (int zi = z; zi <= bz; ++zi, ++voxelIdx)
                                {
                                    if (IgnoreColor ? (HasArr[voxelIdx] == 255) : (HasArr[voxelIdx] != v))
                                    {
                                        ok = false;
                                        break;
                                    }
                                }
                            }

                            if (!ok) break;
                            bx = nx;
                            slabBase = nextSlab;
                        }

                        // Phase 4: Mark all voxels in merged region as processed and check OOB status
                        byte oob = 0;
                        int xiBase = xBase;
                        for (int xi = x; xi <= bx; ++xi, xiBase += yz)
                        {
                            int yiBase = xiBase + y * sizeZ;
                            for (int yi = y; yi <= by; ++yi, yiBase += sizeZ)
                            {
                                int ziIdx = yiBase + z;
                                for (int zi = z; zi <= bz; ++zi, ++ziIdx)
                                {
                                    // Check if any voxel in merged region is out-of-bounds
                                    if (oob == 0 && OobFlagArr[ziIdx] == 1) oob = 1;
                                    // Mark voxel as processed to prevent reprocessing
                                    HasArr[ziIdx] = 255;
                                }
                            }
                        }

                        // Output the merged SubBox result
                        Out.AddNoResize(new GreedyOut
                        {
                            BoxIdx = boxIdx,
                            OobFlag = oob,
                            Sub = new SubBox
                            {
                                minX = hitMin.x + x,
                                minY = hitMin.y + y,
                                minZ = hitMin.z + z,
                                maxX = hitMin.x + bx + 1,
                                maxY = hitMin.y + by + 1,
                                maxZ = hitMin.z + bz + 1,
                                magicaIndex = v
                            }
                        });

                        // Skip to end of merged Z-range to avoid reprocessing
                        z = bz;
                        idx = yBase + z;
                    }
                }
            }
        }
    }

}