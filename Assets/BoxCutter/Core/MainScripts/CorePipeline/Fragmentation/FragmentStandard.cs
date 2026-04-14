using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Standard Fragmentation Parallel Job
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Parallel job for standard probability-based fragmentation at individual voxel level.
    /// Applies distance-based exponential falloff to determine voxel removal probability.
    /// Supports both spherical and box-shaped impact areas with deterministic random distribution.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct FragmentStandard : IJobParallelFor
    {
        /// <summary>Voxel data array - 255 for empty, other values for material</summary>
        public NativeArray<byte> VoxelDataNa;
        /// <summary>Out-of-bounds flags - 1 if voxel is outside impact area</summary>
        [NativeDisableParallelForRestriction] public NativeArray<byte> OobFlagsNa;

        /// <summary>Grid offset indices for each BoxObj in the batch</summary>
        [ReadOnly] public NativeArray<int> GridOffsetPerBox;
        /// <summary>Pre-computed owner box index for each voxel in this batch</summary>
        [ReadOnly] public NativeArray<int> BoxOwnerNa;
        /// <summary>Base index offset for this processing batch</summary>
        [ReadOnly] public int GridBase;

        /// <summary>Mask for which boxes have at least one voxel hit (set to 1 when a voxel passes corner check)</summary>
        [NativeDisableParallelForRestriction] public NativeArray<byte> HitBox;
        /// <summary>Transform data array for all BoxObj instances</summary>
        [ReadOnly] public NativeArray<BoxTrans> BoxDataArr;
        /// <summary>Minimum voxel bounds for each BoxObj</summary>
        [ReadOnly] public NativeArray<int3> HitMinIntervalNa;
        /// <summary>Maximum voxel bounds for each BoxObj</summary>
        [ReadOnly] public NativeArray<int3> HitMaxIntervalNa;
        
        /// <summary>Exponential falloff factors for each BoxObj for distance-based probability</summary>
        [ReadOnly] public NativeArray<float> FallOffFactors;
        /// <summary>Random seed for deterministic probability distribution</summary>
        [ReadOnly] public uint Seed;

        /// <summary>Impact position X coordinate in world space</summary>
        [ReadOnly] public float HitPosX;
        /// <summary>Impact position Y coordinate in world space</summary>
        [ReadOnly] public float HitPosY;
        /// <summary>Impact position Z coordinate in world space</summary>
        [ReadOnly] public float HitPosZ;

        /// <summary>Squared radius of spherical impact area</summary>
        [ReadOnly] public float Radius2;

        /// <summary>True for box-shaped impact, false for spherical impact</summary>
        [ReadOnly] public bool BoxMode;

        /// <summary>Inverse quaternion X component for box mode rotation</summary>
        [ReadOnly] public float Iqx;
        /// <summary>Inverse quaternion Y component for box mode rotation</summary>
        [ReadOnly] public float Iqy;
        /// <summary>Inverse quaternion Z component for box mode rotation</summary>
        [ReadOnly] public float Iqz;
        /// <summary>Inverse quaternion W component for box mode rotation</summary>
        [ReadOnly] public float Iqw;

        /// <summary>Half-width of box impact area in X axis</summary>
        [ReadOnly] public float HalfBoxBoundX;
        /// <summary>Half-width of box impact area in Y axis</summary>
        [ReadOnly] public float HalfBoxBoundY;
        /// <summary>Half-width of box impact area in Z axis</summary>
        [ReadOnly] public float HalfBoxBoundZ;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsVoxelCenterFarOutside(float centerX, float centerY, float centerZ,
            float hitPosX, float hitPosY, float hitPosZ,
            float radius, float voxelSize, bool isBoxMode,
            float halfBoxX, float halfBoxY, float halfBoxZ,
            float iqx, float iqy, float iqz, float iqw)
        {
            float dx = centerX - hitPosX;
            float dy = centerY - hitPosY;
            float dz = centerZ - hitPosZ;

            float voxelHalfDiag = voxelSize * 0.8660254f; // sqrt(3)/2 for voxel half-diagonal

            if (isBoxMode)
            {
                float tx = iqy * 2f * dz - iqz * 2f * dy;
                float ty = iqz * 2f * dx - iqx * 2f * dz;
                float tz = iqx * 2f * dy - iqy * 2f * dx;

                float lx = dx + iqw * tx + (iqy * tz - iqz * ty);
                float ly = dy + iqw * ty + (iqz * tx - iqx * tz);
                float lz = dz + iqw * tz + (iqx * ty - iqy * tx);

                float nx = lx / halfBoxX;
                float ny = ly / halfBoxY;
                float nz = lz / halfBoxZ;

                // per-axis, dimensionless expansion
                float ex = voxelHalfDiag / halfBoxX;
                float ey = voxelHalfDiag / halfBoxY;
                float ez = voxelHalfDiag / halfBoxZ;

                return nx > 1f + ex || nx < -1f - ex ||
                       ny > 1f + ey || ny < -1f - ey ||
                       nz > 1f + ez || nz < -1f - ez;
            }

            float distSq = dx * dx + dy * dy + dz * dz;
            float expandedRadius = radius + voxelHalfDiag;
            return distSq > expandedRadius * expandedRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsVoxelCenterFullyInside(float centerX, float centerY, float centerZ,
            float hitPosX, float hitPosY, float hitPosZ,
            float radius, float voxelSize, bool isBoxMode,
            float halfBoxX, float halfBoxY, float halfBoxZ,
            float iqx, float iqy, float iqz, float iqw)
        {
            float dx = centerX - hitPosX;
            float dy = centerY - hitPosY;
            float dz = centerZ - hitPosZ;

            float voxelHalfDiag = voxelSize * 0.8660254f; // sqrt(3)/2 for voxel half-diagonal

            if (isBoxMode)
            {
                float tx = iqy * 2f * dz - iqz * 2f * dy;
                float ty = iqz * 2f * dx - iqx * 2f * dz;
                float tz = iqx * 2f * dy - iqy * 2f * dx;

                float lx = dx + iqw * tx + (iqy * tz - iqz * ty);
                float ly = dy + iqw * ty + (iqz * tx - iqx * tz);
                float lz = dz + iqw * tz + (iqx * ty - iqy * tx);

                float nx = lx / halfBoxX;
                float ny = ly / halfBoxY;
                float nz = lz / halfBoxZ;

                // per-axis, dimensionless shrink
                float ex = voxelHalfDiag / halfBoxX;
                float ey = voxelHalfDiag / halfBoxY;
                float ez = voxelHalfDiag / halfBoxZ;

                return nx <= 1f - ex && nx >= -1f + ex &&
                       ny <= 1f - ey && ny >= -1f + ey &&
                       nz <= 1f - ez && nz >= -1f + ez;
            }

            float distSq = dx * dx + dy * dy + dz * dz;
            float innerRadius = radius - voxelHalfDiag;
            return innerRadius > 0f && distSq <= innerRadius * innerRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool AreAllVoxelCornersInside(float voxelCenterX, float voxelCenterY, float voxelCenterZ,
                                           BoxTrans boxTrans,
                                           float hitPosX, float hitPosY, float hitPosZ,
                                           bool isBoxMode, float radius, 
                                           float halfBoxX, float halfBoxY, float halfBoxZ,
                                           float iqx, float iqy, float iqz, float iqw)
        {
            float halfVoxelIdx = 0.5f;

            // Cache quaternion calculations for box mode
            float iqx2 = isBoxMode ? 2f * iqx : 0f;
            float iqy2 = isBoxMode ? 2f * iqy : 0f;
            float iqz2 = isBoxMode ? 2f * iqz : 0f;
            float invHalfX = isBoxMode ? 1f / halfBoxX : 0f;
            float invHalfY = isBoxMode ? 1f / halfBoxY : 0f;
            float invHalfZ = isBoxMode ? 1f / halfBoxZ : 0f;
            float radiusSq = isBoxMode ? 0f : radius * radius;

            // Check all 8 corners
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        float cornerOffsetX = (x == 0 ? -halfVoxelIdx : halfVoxelIdx);
                        float cornerOffsetY = (y == 0 ? -halfVoxelIdx : halfVoxelIdx);
                        float cornerOffsetZ = (z == 0 ? -halfVoxelIdx : halfVoxelIdx);

                        float cornerX = voxelCenterX + cornerOffsetX;
                        float cornerY = voxelCenterY + cornerOffsetY;
                        float cornerZ = voxelCenterZ + cornerOffsetZ;

                        // Transform corner to world space
                        float worldX = boxTrans.startPosX + boxTrans.vRightX * cornerX + 
                                      boxTrans.vUpX * cornerY + boxTrans.vFwdX * cornerZ;
                        float worldY = boxTrans.startPosY + boxTrans.vRightY * cornerX + 
                                      boxTrans.vUpY * cornerY + boxTrans.vFwdY * cornerZ;
                        float worldZ = boxTrans.startPosZ + boxTrans.vRightZ * cornerX + 
                                      boxTrans.vUpZ * cornerY + boxTrans.vFwdZ * cornerZ;

                        // Calculate distance to hit position
                        float dx = worldX - hitPosX;
                        float dy = worldY - hitPosY;
                        float dz = worldZ - hitPosZ;

                        if (isBoxMode)
                        {
                            // 2*cross(q.xyz, v) with q = inverse quaternion
                            float tx = iqy2 * dz - iqz2 * dy;
                            float ty = iqz2 * dx - iqx2 * dz;
                            float tz = iqx2 * dy - iqy2 * dx;
                            
                            float lx = dx + iqw * tx + (iqy * tz - iqz * ty);
                            float ly = dy + iqw * ty + (iqz * tx - iqx * tz);
                            float lz = dz + iqw * tz + (iqx * ty - iqy * tx);
                            
                            float nx = lx * invHalfX;
                            float ny = ly * invHalfY;
                            float nz = lz * invHalfZ;
                            
                            if (!(nx >= -1f && nx <= 1f && ny >= -1f && ny <= 1f && nz >= -1f && nz <= 1f))
                                return false;
                        }
                        else
                        {
                            if (dx * dx + dy * dy + dz * dz > radiusSq)
                                return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Processes a single voxel to determine fragmentation based on 8-corner testing and probability.
        /// Uses optimized broad checks followed by precise 8-corner verification for accurate boundary detection.
        /// Marks voxels as destroyed or out-of-bounds based on impact area and probability distribution.
        /// </summary>
        /// <param name="i">Index of the voxel to process for fragmentation</param>
        public void Execute(int i)
        {
            OobFlagsNa[i] = 0; // Initialize as in-bounds

            // Skip already empty voxels
            if (VoxelDataNa[i] == 255)
                return;

            // Get which BoxObj owns this voxel (pre-computed) and get local coordinates
            int globalIdx = i + GridBase;
            int pointer = BoxOwnerNa[i];
            int localIdx = globalIdx - GridOffsetPerBox[pointer];

            BoxTrans boxTrans = BoxDataArr[pointer];

            // Get voxel bounds and calculate 3D grid dimensions
            int3 hitMax = HitMaxIntervalNa[pointer];
            int3 hitMin = HitMinIntervalNa[pointer];

            int boundIntervalY = hitMax.y - hitMin.y;
            int boundIntervalZ = hitMax.z - hitMin.z;
            int boundIntervalYZ = boundIntervalY * boundIntervalZ;

            // Convert 1D index to 3D voxel coordinates (center of voxel)
            float centerX = (localIdx / boundIntervalYZ) + hitMin.x + 0.5f;
            float centerY = ((localIdx / boundIntervalZ) % boundIntervalY) + hitMin.y + 0.5f;
            float centerZ = (localIdx % boundIntervalZ) + hitMin.z + 0.5f;

            // Transform voxel center to world position
            float centerWorldX = boxTrans.startPosX + boxTrans.vRightX * centerX + boxTrans.vUpX * centerY + boxTrans.vFwdX * centerZ;
            float centerWorldY = boxTrans.startPosY + boxTrans.vRightY * centerX + boxTrans.vUpY * centerY + boxTrans.vFwdY * centerZ;
            float centerWorldZ = boxTrans.startPosZ + boxTrans.vRightZ * centerX + boxTrans.vUpZ * centerY + boxTrans.vFwdZ * centerZ;

            float radius = BoxMode ? 0f : math.sqrt(Radius2);

            // Tier 1: Fast check if voxel center is far outside - mark as out-of-bounds immediately
            if (IsVoxelCenterFarOutside(centerWorldX, centerWorldY, centerWorldZ, 
                                       HitPosX, HitPosY, HitPosZ, radius, boxTrans.voxelSize, 
                                       BoxMode, HalfBoxBoundX, HalfBoxBoundY, HalfBoxBoundZ,
                                       Iqx, Iqy, Iqz, Iqw))
            {
                OobFlagsNa[i] = 1;
                return;
            }

            // Tier 2: Fast check if voxel center is fully inside - proceed to probability calculation
            bool allCornersInside;
            if (IsVoxelCenterFullyInside(centerWorldX, centerWorldY, centerWorldZ,
                                        HitPosX, HitPosY, HitPosZ, radius, boxTrans.voxelSize,
                                        BoxMode, HalfBoxBoundX, HalfBoxBoundY, HalfBoxBoundZ,
                                        Iqx, Iqy, Iqz, Iqw))
            {
                allCornersInside = true;
            }
            else
            {
                // Tier 3: Precise 8-corner check when voxel intersects boundary
                allCornersInside = AreAllVoxelCornersInside(centerX, centerY, centerZ,
                                                          boxTrans,
                                                          HitPosX, HitPosY, HitPosZ, BoxMode, radius,
                                                          HalfBoxBoundX, HalfBoxBoundY, HalfBoxBoundZ,
                                                          Iqx, Iqy, Iqz, Iqw);
            }

            if (!allCornersInside)
            {
                OobFlagsNa[i] = 1;
            }
            else
            {
                // Only write if not already set - avoids redundant writes and cache invalidation
                if (HitBox[pointer] == 0)
                    HitBox[pointer] = 1;

                // Use already calculated center position and distance for probabilistic removal
                float dx = centerWorldX - HitPosX;
                float dy = centerWorldY - HitPosY;
                float dz = centerWorldZ - HitPosZ;
                float distSq = dx * dx + dy * dy + dz * dz;

                uint h = (uint)i * 0x9E3779B9u ^ (Seed * (uint)pointer);
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;

                float randVal = (h & 0xFFFFFF) / 16777216f;

                float distNormalized;
                if (BoxMode)
                {
                    // normalize by circumscribed-sphere radius of the box
                    float rBox = math.sqrt(HalfBoxBoundX * HalfBoxBoundX +
                                           HalfBoxBoundY * HalfBoxBoundY +
                                           HalfBoxBoundZ * HalfBoxBoundZ);
                    distNormalized = distSq / (rBox * rBox);
                }
                else
                {
                    distNormalized = distSq / Radius2;
                }

                float fallOffFac = FallOffFactors[pointer];
                float ratio = math.exp(-fallOffFac * distNormalized);

                if (randVal < ratio)
                {
                    VoxelDataNa[i] = 255; // Destroy voxel
                }
            }
        }
    }

}