using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Slab Fragmentation Configuration
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Configuration parameters for slab-based fragmentation pattern generation.
    /// Defines size constraints, randomization settings, and clustering behavior for slab fragments.
    /// Supports both fixed-size and randomized fragment generation with distance-based scaling.
    /// </summary>
    public struct SlabParam
    {
        /// <summary>1 if fragment sizes should be randomized, 0 for fixed sizes</summary>
        public int CanRandomize;

        /// <summary>Fixed fragment size for primary axis when randomization is disabled</summary>
        public int FixedPrimary;
        /// <summary>Fixed fragment size for secondary axis when randomization is disabled</summary>
        public int FixedSecondary;
        /// <summary>Fixed fragment size for tertiary axis when randomization is disabled</summary>
        public int FixedTertiary;

        /// <summary>Minimum random fragment size for primary axis</summary>
        public int PrimaryMin;
        /// <summary>Maximum random fragment size for primary axis</summary>
        public int PrimaryMax;
        /// <summary>Minimum random fragment size for secondary axis</summary>
        public int SecondaryMin;
        /// <summary>Maximum random fragment size for secondary axis</summary>
        public int SecondaryMax;
        /// <summary>Minimum random fragment size for tertiary axis</summary>
        public int TertiaryMin;
        /// <summary>Maximum random fragment size for tertiary axis</summary>
        public int TertiaryMax;

        /// <summary>Axis selection mode for splinter alignment: 0=Auto, 1=X, 2=Y, 3=Z</summary>
        public int SplinterAxisModeValue;

        /// <summary>Scaling multiplier for fragments outside impact radius</summary>
        public float OutRadiusScaleMul;

        /// <summary>Enable clustering mode for additional fragment scattering</summary>
        public bool ClusterMode;
        /// <summary>Maximum radius for cluster fragment generation</summary>
        public int MaxClusterRadius;

        /// <summary>Use disc-shaped mask for cluster generation</summary>
        [ReadOnly] public bool UseDiscMask;
        /// <summary>Apply radial weight distribution to cluster fragments</summary>
        [ReadOnly] public bool UseRadialWeight;
        /// <summary>Exponential falloff for density-based fragment distribution</summary>
        [ReadOnly] public float DensityFallExp;

        /// <summary>Enable splinter mode for elongated fragment generation</summary>
        [ReadOnly] public bool SplinterMode;
    }
    

    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Slab Fragmentation Parallel Job
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Parallel job for generating slab-based fragmentation patterns with configurable clustering.
    /// Creates rectangular fragments with distance-based probability and optional splinter alignment.
    /// Supports both fixed and randomized fragment sizes with collision detection avoidance.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct FragmentSlab : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<byte> Visited;
        [NativeDisableParallelForRestriction] public NativeArray<byte> VoxelData;
        [NativeDisableParallelForRestriction] public NativeArray<byte> OobFlagsNa;

        /// <summary>Mask for which boxes have at least one fragment hit (set to 1 when a fragment passes corner check)</summary>
        [NativeDisableParallelForRestriction] public NativeArray<byte> HitBox;

        [ReadOnly] public NativeArray<int> SliceOffsetPerBox;
        [ReadOnly] public NativeArray<int> VoxelOffsetPerBox;
        /// <summary>Pre-computed owner box index for each slice in this batch</summary>
        [ReadOnly] public NativeArray<int> SliceOwnerNa;

        [ReadOnly] public int VoxelSubBase;

        [ReadOnly] public int GridBase;

        [ReadOnly] public NativeArray<int3> HitMinIntervalNa;
        [ReadOnly] public NativeArray<int3> HitMaxIntervalNa;

        [ReadOnly] public NativeArray<BoxTrans> BoxDataArr;
        [ReadOnly] public NativeArray<SlabParam> SlabParams;

        public Unity.Mathematics.Random MathRandom;

        public float HitPosX;
        public float HitPosY;
        public float HitPosZ;

        public float HitRadiusSq;

        /// <summary>Exponential falloff factors for each BoxObj for distance-based probability</summary>
        [ReadOnly] public NativeArray<float> FallOffFactors;

        [ReadOnly] public bool BoxMode;

        [ReadOnly] public float Iqx;
        [ReadOnly] public float Iqy;
        [ReadOnly] public float Iqz;
        [ReadOnly] public float Iqw;

        [ReadOnly] public float HalfBoxBoundX;
        [ReadOnly] public float HalfBoxBoundY;
        [ReadOnly] public float HalfBoxBoundZ;

        const int IntInfinity = 999999999;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // fragmentHalfDiag is the exact world-space half-diagonal of the fragment
        static bool IsFragmentCenterFarOutside(float centerX, float centerY, float centerZ,
            float hitPosX, float hitPosY, float hitPosZ,
            float radius, float fragmentHalfDiag, bool isBoxMode,
            float halfBoxX, float halfBoxY, float halfBoxZ,
            float iqx, float iqy, float iqz, float iqw)
        {
            float dx = centerX - hitPosX;
            float dy = centerY - hitPosY;
            float dz = centerZ - hitPosZ;

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
                float ex = fragmentHalfDiag / halfBoxX;
                float ey = fragmentHalfDiag / halfBoxY;
                float ez = fragmentHalfDiag / halfBoxZ;

                return nx > 1f + ex || nx < -1f - ex ||
                       ny > 1f + ey || ny < -1f - ey ||
                       nz > 1f + ez || nz < -1f - ez;
            }

            float distSq = dx * dx + dy * dy + dz * dz;
            float expandedRadius = radius + fragmentHalfDiag;
            return distSq > expandedRadius * expandedRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsFragmentCenterFullyInside(float centerX, float centerY, float centerZ,
            float hitPosX, float hitPosY, float hitPosZ,
            float radius, float fragmentHalfDiag, bool isBoxMode,
            float halfBoxX, float halfBoxY, float halfBoxZ,
            float iqx, float iqy, float iqz, float iqw)
        {
            float dx = centerX - hitPosX;
            float dy = centerY - hitPosY;
            float dz = centerZ - hitPosZ;

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
                float ex = fragmentHalfDiag / halfBoxX;
                float ey = fragmentHalfDiag / halfBoxY;
                float ez = fragmentHalfDiag / halfBoxZ;

                return nx <= 1f - ex && nx >= -1f + ex &&
                       ny <= 1f - ey && ny >= -1f + ey &&
                       nz <= 1f - ez && nz >= -1f + ez;
            }

            float distSq = dx * dx + dy * dy + dz * dz;
            float innerRadius = radius - fragmentHalfDiag;
            return innerRadius > 0f && distSq <= innerRadius * innerRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool AreAllFragmentCornersInside(float fragmentCenterX, float fragmentCenterY, float fragmentCenterZ,
            int fragmentSizeX, int fragmentSizeY, int fragmentSizeZ,
            BoxTrans boxTrans, float3 hitMin, float voxelSize,
            float hitPosX, float hitPosY, float hitPosZ,
            bool isBoxMode, float radius,
            float halfBoxX, float halfBoxY, float halfBoxZ,
            float iqx, float iqy, float iqz, float iqw)
        {
            // Use index-space half sizes; world scaling happens via vRight/Up/Fwd
            float halfSizeXIdx = 0.5f * fragmentSizeX;
            float halfSizeYIdx = 0.5f * fragmentSizeY;
            float halfSizeZIdx = 0.5f * fragmentSizeZ;

            // Cache quaternion calculations for box mode
            float iqx2 = isBoxMode ? 2f * iqx : 0f;
            float iqy2 = isBoxMode ? 2f * iqy : 0f;
            float iqz2 = isBoxMode ? 2f * iqz : 0f;
            float invHalfX = isBoxMode ? 1f / halfBoxX : 0f;
            float invHalfY = isBoxMode ? 1f / halfBoxY : 0f;
            float invHalfZ = isBoxMode ? 1f / halfBoxZ : 0f;
            float radiusSq = isBoxMode ? 0f : radius * radius;

            // Check all 8 corners of the fragment
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        float cornerOffsetX = (x == 0 ? -halfSizeXIdx : halfSizeXIdx);
                        float cornerOffsetY = (y == 0 ? -halfSizeYIdx : halfSizeYIdx);
                        float cornerOffsetZ = (z == 0 ? -halfSizeZIdx : halfSizeZIdx);

                        float cornerX = fragmentCenterX + cornerOffsetX;
                        float cornerY = fragmentCenterY + cornerOffsetY;
                        float cornerZ = fragmentCenterZ + cornerOffsetZ;

                        // Transform corner to world space
                        float worldX = boxTrans.startPosX + boxTrans.vRightX * (hitMin.x + cornerX) +
                                       boxTrans.vUpX * (hitMin.y + cornerY) + boxTrans.vFwdX * (hitMin.z + cornerZ);
                        float worldY = boxTrans.startPosY + boxTrans.vRightY * (hitMin.x + cornerX) +
                                       boxTrans.vUpY * (hitMin.y + cornerY) + boxTrans.vFwdY * (hitMin.z + cornerZ);
                        float worldZ = boxTrans.startPosZ + boxTrans.vRightZ * (hitMin.x + cornerX) +
                                       boxTrans.vUpZ * (hitMin.y + cornerY) + boxTrans.vFwdZ * (hitMin.z + cornerZ);

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
        /// Processes a single X slice to generate slab fragments within the voxel grid.
        /// Implements fragment size randomization, distance-based probability, and clustering.
        /// Handles collision avoidance and out-of-bounds detection for realistic destruction.
        /// </summary>
        /// <param name="sliceIdx">Index of the X slice to process for fragmentation</param>
        public void Execute(int sliceIdx)
        {
            // Get which BoxObj owns this slice (pre-computed) and get its parameters
            int globalSlice = GridBase + sliceIdx;
            int boxId = SliceOwnerNa[sliceIdx];
            int sliceBoxStart = SliceOffsetPerBox[boxId];
            int3 hitMin = HitMinIntervalNa[boxId];
            int3 hitMax = HitMaxIntervalNa[boxId];
            int3 gSize = hitMax - hitMin;
            int yz = gSize.y * gSize.z;

            int baseX = globalSlice - sliceBoxStart;
            if (baseX >= gSize.x) return; // Skip if outside valid range

            // Initialize all voxels in this slice as empty
            int voxelBoxBase = VoxelOffsetPerBox[boxId] - VoxelSubBase;
            int xPart = baseX * yz;

            BoxTrans bd = BoxDataArr[boxId];
            SlabParam sp = SlabParams[boxId];

            int boundXTo = gSize.x;
            int boundYTo = gSize.y;
            int boundZTo = gSize.z;

            int canRandomize;
            int fixedX, fixedY, fixedZ;
            int xMin, yMin, zMin;
            int xMax, yMax, zMax;
            
            int splinterAxisMode = sp.SplinterAxisModeValue;

            float outRadiusScaleMul = sp.OutRadiusScaleMul;
            bool clusterMode = sp.ClusterMode;
            float maxClusterRadius = sp.MaxClusterRadius;
            bool useDiscMask = sp.UseDiscMask;
            bool useRadialWeight = sp.UseRadialWeight;
            float densityFallExp = sp.DensityFallExp;
            bool splinterMode = sp.SplinterMode;

            if (splinterMode)
            {
                canRandomize = sp.CanRandomize;
                fixedX = sp.FixedPrimary;
                fixedY = sp.FixedSecondary;
                fixedZ = sp.FixedTertiary;
                xMin = sp.PrimaryMin;
                yMin = sp.SecondaryMin;
                zMin = sp.TertiaryMin;
                xMax = sp.PrimaryMax;
                yMax = sp.SecondaryMax;
                zMax = sp.TertiaryMax;
            }
            else
            {
                canRandomize = sp.CanRandomize;
                fixedX = sp.FixedPrimary;
                fixedY = sp.FixedSecondary;
                fixedZ = sp.FixedTertiary;
                xMin = sp.PrimaryMin;
                yMin = sp.SecondaryMin;
                zMin = sp.TertiaryMin;
                xMax = sp.PrimaryMax;
                yMax = sp.SecondaryMax;
                zMax = sp.TertiaryMax;
            }
            
            float startPosX = bd.startPosX;
            float startPosY = bd.startPosY;
            float startPosZ = bd.startPosZ;
            float preLocalRightX = bd.vRightX;
            float preLocalRightY = bd.vRightY;
            float preLocalRightZ = bd.vRightZ;
            float preLocalUpX = bd.vUpX;
            float preLocalUpY = bd.vUpY;
            float preLocalUpZ = bd.vUpZ;
            float preLocalForwardX = bd.vFwdX;
            float preLocalForwardY = bd.vFwdY;
            float preLocalForwardZ = bd.vFwdZ;

            if (splinterMode)
            {
                int boundAxis;
                
                // Determine the target axis based on splinter mode
                if (splinterAxisMode == 0) // Auto
                {
                    // Use automatic detection - find longest axis based on BoxObj size, not hit bounds
                    boundAxis = 0;
                    float maxSize = bd.sizeX;
                    if (bd.sizeY > maxSize)
                    {
                        boundAxis = 1;
                        maxSize = bd.sizeY;
                    }
                    if (bd.sizeZ > maxSize)
                    {
                        boundAxis = 2;
                    }
                }
                else
                {
                    // Use forced axis (1=X, 2=Y, 3=Z) - convert to 0-based
                    boundAxis = splinterAxisMode - 1;
                }

                // Find which axis has the largest fixed value
                int fixedAxis = 0;
                int maxFixed = fixedX;
                if (fixedY > maxFixed)
                {
                    fixedAxis = 1;
                    maxFixed = fixedY;
                }
                if (fixedZ > maxFixed)
                {
                    fixedAxis = 2;
                }

                // Swap dimensions so the target boundAxis gets the largest fixed value
                if (boundAxis != fixedAxis)
                {
                    if ((boundAxis == 0 && fixedAxis == 1) ||
                        (boundAxis == 1 && fixedAxis == 0))
                    {
                        int tmp = fixedX;
                        fixedX = fixedY;
                        fixedY = tmp;
                        tmp = xMin;
                        xMin = yMin;
                        yMin = tmp;
                        tmp = xMax;
                        xMax = yMax;
                        yMax = tmp;
                    }
                    else if ((boundAxis == 0 && fixedAxis == 2) ||
                             (boundAxis == 2 && fixedAxis == 0))
                    {
                        int tmp = fixedX;
                        fixedX = fixedZ;
                        fixedZ = tmp;
                        tmp = xMin;
                        xMin = zMin;
                        zMin = tmp;
                        tmp = xMax;
                        xMax = zMax;
                        zMax = tmp;
                    }
                    else
                    {
                        int tmp = fixedY;
                        fixedY = fixedZ;
                        fixedZ = tmp;
                        tmp = yMin;
                        yMin = zMin;
                        zMin = tmp;
                        tmp = yMax;
                        yMax = zMax;
                        zMax = tmp;
                    }
                }
            }

            uint sliceSeed = MathRandom.NextUInt() ^ (uint)baseX * 0x9E3779B9u;
            var rnd = new Unity.Mathematics.Random(sliceSeed);
            
            for (int baseY = 0; baseY < boundYTo; baseY++)
            {
                int xyPart = xPart + baseY * boundZTo;

                for (int baseZ = 0; baseZ < boundZTo; baseZ++)
                {
                    int index1D = voxelBoxBase + xyPart + baseZ;

                    if (Visited[index1D] == 1)
                        continue;
                    if (VoxelData[index1D] == 255)
                        continue;

                    int xSize, ySize, zSize;
                    if (canRandomize == 1)
                    {
                        xSize = (xMax > xMin) ? rnd.NextInt(xMin, xMax) : xMin;
                        ySize = (yMax > yMin) ? rnd.NextInt(yMin, yMax) : yMin;
                        zSize = (zMax > zMin) ? rnd.NextInt(zMin, zMax) : zMin;
                    }
                    else
                    {
                        xSize = fixedX;
                        ySize = fixedY;
                        zSize = fixedZ;
                    }

                    xSize = math.min(xSize, boundXTo - baseX);
                    ySize = math.min(ySize, boundYTo - baseY);
                    zSize = math.min(zSize, boundZTo - baseZ);

                    if (xSize <= 0 || ySize <= 0 || zSize <= 0)
                        continue;

                    int maxX = baseX + xSize;
                    int maxY = baseY + ySize;
                    int maxZ = baseZ + zSize;

                    float centerX = (baseX + maxX) * 0.5f;
                    float centerY = (baseY + maxY) * 0.5f;
                    float centerZ = (baseZ + maxZ) * 0.5f;

                    float centerWorldX = startPosX + preLocalRightX * (hitMin.x + centerX) + preLocalUpX * (hitMin.y + centerY) + preLocalForwardX * (hitMin.z + centerZ);
                    float centerWorldY = startPosY + preLocalRightY * (hitMin.x + centerX) + preLocalUpY * (hitMin.y + centerY) + preLocalForwardY * (hitMin.z + centerZ);
                    float centerWorldZ = startPosZ + preLocalRightZ * (hitMin.x + centerX) + preLocalUpZ * (hitMin.y + centerY) + preLocalForwardZ * (hitMin.z + centerZ);

                    float cdx = centerWorldX - HitPosX;
                    float cdy = centerWorldY - HitPosY;
                    float cdz = centerWorldZ - HitPosZ;
                    float distSq = cdx * cdx + cdy * cdy + cdz * cdz;

                    float radius = BoxMode ? 0f : math.sqrt(HitRadiusSq);

                    // exact world-space half-diagonal of this fragment (for broad-phase)
                    float sx = (maxX - baseX) * bd.voxelSize;
                    float sy = (maxY - baseY) * bd.voxelSize;
                    float sz = (maxZ - baseZ) * bd.voxelSize;
                    float fragmentHalfDiag = 0.5f * math.sqrt(sx * sx + sy * sy + sz * sz);

                    bool canAddToOccupied = false;
                    int outBounds = 0;

                    // Fast check if fragment center is far outside - mark as out-of-bounds immediately
                    if (IsFragmentCenterFarOutside(centerWorldX, centerWorldY, centerWorldZ,
                            HitPosX, HitPosY, HitPosZ, radius, fragmentHalfDiag,
                            BoxMode, HalfBoxBoundX, HalfBoxBoundY, HalfBoxBoundZ,
                            Iqx, Iqy, Iqz, Iqw))
                    {
                        canAddToOccupied = true;
                        outBounds = 1;
                    }
                    else
                    {
                        // Fast check if fragment center is fully inside - proceed without corner check
                        bool allCornersInside;
                        if (IsFragmentCenterFullyInside(centerWorldX, centerWorldY, centerWorldZ,
                                HitPosX, HitPosY, HitPosZ, radius, fragmentHalfDiag,
                                BoxMode, HalfBoxBoundX, HalfBoxBoundY, HalfBoxBoundZ,
                                Iqx, Iqy, Iqz, Iqw))
                        {
                            allCornersInside = true;
                        }
                        else
                        {
                            // Precise 8-corner check when fragment intersects boundary
                            allCornersInside = AreAllFragmentCornersInside(centerX, centerY, centerZ,
                                                                         maxX - baseX, maxY - baseY, maxZ - baseZ,
                                                                         bd, hitMin, bd.voxelSize,
                                                                         HitPosX, HitPosY, HitPosZ, BoxMode, radius,
                                                                         HalfBoxBoundX, HalfBoxBoundY, HalfBoxBoundZ,
                                                                         Iqx, Iqy, Iqz, Iqw);
                        }

                        if (!allCornersInside)
                        {
                            canAddToOccupied = true;
                            outBounds = 1;
                        }
                        else
                        {
                            // Only write if not already set - avoids redundant writes and cache invalidation
                            if (HitBox[boxId] == 0)
                                HitBox[boxId] = 1;

                            // Apply probability calculation for fragments inside the hit area
                            uint fracBits = rnd.NextUInt() & 0xFFFFFF;
                            float randVal = fracBits / 16777216f;

                            float distNorm;
                            if (BoxMode)
                            {
                                // normalize by circumscribed-sphere radius of the box
                                float rBox = math.sqrt(HalfBoxBoundX * HalfBoxBoundX +
                                                       HalfBoxBoundY * HalfBoxBoundY +
                                                       HalfBoxBoundZ * HalfBoxBoundZ);
                                distNorm = distSq / (rBox * rBox);
                            }
                            else
                            {
                                distNorm = distSq / HitRadiusSq;
                            }

                            float fallOffFac = FallOffFactors[boxId];
                            float ratio = math.exp(-fallOffFac * distNorm);

                            if (randVal > ratio)
                            {
                                float scaleF = math.lerp(distNorm, 1, outRadiusScaleMul);

                                int scaledX = math.max(1, (int)math.ceil((maxX - baseX) * scaleF));
                                int scaledY = math.max(1, (int)math.ceil((maxY - baseY) * scaleF));
                                int scaledZ = math.max(1, (int)math.ceil((maxZ - baseZ) * scaleF));

                                maxX = baseX + scaledX;
                                maxY = baseY + scaledY;
                                maxZ = baseZ + scaledZ;
                                canAddToOccupied = true;
                            }
                        }
                    }

                    byte obbFlagTo = outBounds == 1 ? (byte)1 : (byte)0;

                    // Process main fragment voxels
                    for (int x = baseX; x < maxX; x++)
                    {
                        int xOffset = x * yz;
                        for (int y = baseY; y < maxY; y++)
                        {
                            int xyOffset = xOffset + y * boundZTo;
                            for (int z = baseZ; z < maxZ; z++)
                            {
                                int idx = xyOffset + z + voxelBoxBase;
                                Visited[idx] = 1;
                                if (canAddToOccupied)
                                {
                                    OobFlagsNa[idx] = obbFlagTo;
                                }
                                else
                                {
                                    VoxelData[idx] = 255;
                                }

                                // Cluster mode processing (on-demand generation)
                                if (clusterMode && canAddToOccupied)
                                {
                                    // compute radius
                                    float normC = math.saturate(distSq / math.max(HitRadiusSq, 1e-6f));
                                    int r = (int)math.lerp(maxClusterRadius, 1, normC);
                                    float rSq = r * r;
                                    
                                    // hoist once
                                    bool applyDisc = useDiscMask;
                                    bool applyWeight = useRadialWeight;
                                    float invRSq = 1f / rSq;
                                    float fallE = densityFallExp;
                                    
                                    int processed = 0;
                                    int maxProcessed = r * 2;
                                    
                                    // Generate offsets on-demand within cluster radius
                                    for (int dx = -r; dx <= r && processed < maxProcessed; dx++)
                                    {
                                        for (int dy = -r; dy <= r && processed < maxProcessed; dy++)
                                        {
                                            for (int dz = -r; dz <= r && processed < maxProcessed; dz++)
                                            {
                                                if (dx == 0 && dy == 0 && dz == 0) continue;
                                                
                                                float distSq3D = dx * dx + dy * dy + dz * dz;
                                                if (distSq3D > rSq) continue;
                                                
                                                // Apply disc mask (2D distance check)
                                                if (applyDisc)
                                                {
                                                    float distSq2D = dx * dx + dy * dy;
                                                    if (distSq2D > rSq) continue;
                                                }
                                                
                                                int nx = x + dx;
                                                int ny = y + dy;
                                                int nz = z + dz;
                                                if (nx < 0 || nx >= boundXTo || ny < 0 || ny >= boundYTo || nz < 0 || nz >= boundZTo)
                                                    continue;
                                                
                                                // Apply radial weight
                                                if (applyWeight)
                                                {
                                                    float rn = distSq3D * invRSq;
                                                    float w = math.exp(math.log(1f - rn) * fallE);
                                                    if (rnd.NextFloat() > w) continue;
                                                }
                                                
                                                int tgt = voxelBoxBase + nx * yz + ny * boundZTo + nz;
                                                if (Visited[tgt] == 0)
                                                {
                                                    Visited[tgt] = 1;
                                                    OobFlagsNa[tgt] = obbFlagTo;
                                                    processed++;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}