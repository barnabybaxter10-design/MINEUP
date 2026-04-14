using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    /// <summary>
    /// Parameter structure containing all data needed for radial crack pattern generation.
    /// Encapsulates crack geometry, ring parameters, and voxel grid information.
    /// </summary>
    [BurstCompile]
    public struct RadialParam
    {
        // Crack line parameters
        public float crackThicknessSq; // Squared thickness for distance testing
        public float hitRadiusSq; // Squared radius of impact zone

        // Plane orientation vectors
        public float3 planeForward; // Forward direction in crack plane
        public float3 planeRight; // Right direction in crack plane

        // Radial spoke configuration
        public int radialSpokes; // Number of crack lines radiating from center
        public int segmentStride; // Stride between segment arrays
        public int segBase; // Base index for segment data
        public int segCountBase; // Base index for segment count data

        // Circular ring configuration
        public int ringBase; // Base index for ring radius data
        public int circularRingCount; // Number of concentric rings
        public float ringNoiseAmplitude; // Amplitude of ring noise variation
        public float ringNoiseFrequency; // Frequency of ring noise variation
        public float ringThickness; // Thickness of ring boundaries
        public int ringGenMode; // Ring generation mode (perfect/square/sawtooth)

        // Voxel grid parameters
        public int voxelBase; // Base offset in voxel array
        public int yz; // Y*Z stride for 3D indexing
        public int boundYTo; // Y dimension bound
        public int boundZTo; // Z dimension bound
    }

    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Radial Fragmentation Job
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Parallel job for generating radial crack patterns with lightning-style spokes and rings.
    /// Creates realistic destruction patterns emanating from impact points.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct FragmentRadial : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> VoxelData;
        [WriteOnly] public NativeArray<byte> OutVisited;
        public NativeArray<byte> NewVoxelData;
        [WriteOnly] public NativeArray<byte> OobFlagsNa;

        /// <summary>Mask for which boxes have at least one voxel hit (set to 1 when a voxel passes corner check)</summary>
        [NativeDisableParallelForRestriction] public NativeArray<byte> HitBox;

        [ReadOnly] public NativeArray<RadialParam> BoxParams;
        [ReadOnly] public NativeArray<int> VoxelOffsetPerBox;
        /// <summary>Pre-computed owner box index for each voxel in this batch</summary>
        [ReadOnly] public NativeArray<int> BoxOwnerNa;
        [ReadOnly] public NativeArray<int3> HitMinIntervalNa;

        [ReadOnly] public NativeArray<float4> AllSegments;
        [ReadOnly] public NativeArray<int> SegmentCounts;
        [ReadOnly] public NativeArray<float> RingRadii;

        [ReadOnly] public NativeArray<BoxTrans> BoxDataArr;

        public float3 HitPos;
        [ReadOnly] public int GridBase;
        [ReadOnly] public int GlobalStartBoxIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsVoxelCenterFarOutside(float centerX, float centerY, float centerZ,
            float hitPosX, float hitPosY, float hitPosZ,
            float radius, float voxelSize)
        {
            float dx = centerX - hitPosX;
            float dy = centerY - hitPosY;
            float dz = centerZ - hitPosZ;

            float distSq = dx * dx + dy * dy + dz * dz;
            float voxelHalfDiag = voxelSize * 0.8660254f; // sqrt(3)/2 for voxel half-diagonal
            float expandedRadius = radius + voxelHalfDiag;
            return distSq > expandedRadius * expandedRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsVoxelCenterFullyInside(float centerX, float centerY, float centerZ,
            float hitPosX, float hitPosY, float hitPosZ,
            float radius, float voxelSize)
        {
            float dx = centerX - hitPosX;
            float dy = centerY - hitPosY;
            float dz = centerZ - hitPosZ;

            float distSq = dx * dx + dy * dy + dz * dz;
            float voxelHalfDiag = voxelSize * 0.8660254f; // sqrt(3)/2 for voxel half-diagonal
            float innerRadius = radius - voxelHalfDiag;
            return innerRadius > 0f && distSq <= innerRadius * innerRadius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool AreAllVoxelCornersInside(float voxelCenterX, float voxelCenterY, float voxelCenterZ,
            BoxTrans boxTrans, float3 hitMin, float voxelSize,
            float hitPosX, float hitPosY, float hitPosZ, float radius)
        {
            // Use index-space half voxel; world scaling happens via vRight/Up/Fwd
            float halfVoxelIdx = 0.5f;
            float radiusSq = radius * radius;

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

                        if (dx * dx + dy * dy + dz * dz > radiusSq)
                            return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Processes a single voxel to determine if it should be removed by radial cracks.
        /// Uses 8-corner checking for accurate boundary detection with optimized broad checks.
        /// Tests against both lightning-style spokes and concentric ring patterns.
        /// </summary>
        /// <param name="i">Index of the voxel to process</param>
        public void Execute(int i)
        {
            // Initialize output values - 255 represents empty space
            OobFlagsNa[i] = 0;
            NewVoxelData[i] = 255;

            // Get which BoxObj this voxel belongs to (pre-computed)
            int globalIdx = i + GridBase;
            int boxIdx = BoxOwnerNa[i];
            int boxIdxGlobal = boxIdx + GlobalStartBoxIndex;

            RadialParam bp = BoxParams[boxIdx];
            int localIdx = globalIdx - bp.voxelBase;
            if (VoxelData[i] == 255) return; // Skip empty voxels

            // Convert linear voxel index to 3D coordinates
            int3 hitMin = HitMinIntervalNa[boxIdxGlobal];
            int x = localIdx / bp.yz;
            int yz = localIdx - x * bp.yz;
            int y = yz / bp.boundZTo;
            int z = yz - y * bp.boundZTo;

            // Convert to voxel center coordinates
            float fx = x + hitMin.x + 0.5f;
            float fy = y + hitMin.y + 0.5f;
            float fz = z + hitMin.z + 0.5f;

            // Transform voxel position to world space
            BoxTrans bd = BoxDataArr[boxIdxGlobal];
            float3 v = new float3(
                bd.startPosX + fx * bd.vRightX + fy * bd.vUpX + fz * bd.vFwdX,
                bd.startPosY + fx * bd.vRightY + fy * bd.vUpY + fz * bd.vFwdY,
                bd.startPosZ + fx * bd.vRightZ + fy * bd.vUpZ + fz * bd.vFwdZ);

            float radius = math.sqrt(bp.hitRadiusSq);

            // Tier 1: Fast check if voxel center is far outside - mark as out-of-bounds immediately
            if (IsVoxelCenterFarOutside(v.x, v.y, v.z, HitPos.x, HitPos.y, HitPos.z, radius, bd.voxelSize))
            {
                OutVisited[i] = 0;
                NewVoxelData[i] = VoxelData[i];
                OobFlagsNa[i] = 1; // Mark as out of bounds
                return;
            }

            // Tier 2: Fast check if voxel center is fully inside - proceed to crack testing
            bool allCornersInside;
            if (IsVoxelCenterFullyInside(v.x, v.y, v.z, HitPos.x, HitPos.y, HitPos.z, radius, bd.voxelSize))
            {
                allCornersInside = true;
            }
            else
            {
                // Tier 3: Precise 8-corner check when voxel intersects boundary
                allCornersInside = AreAllVoxelCornersInside(fx, fy, fz, bd, hitMin, bd.voxelSize,
                    HitPos.x, HitPos.y, HitPos.z, radius);
            }

            if (!allCornersInside)
            {
                OutVisited[i] = 0;
                NewVoxelData[i] = VoxelData[i];
                OobFlagsNa[i] = 1; // Mark as out of bounds
                return;
            }

            // Only write if not already set - avoids redundant writes and cache invalidation
            if (HitBox[boxIdxGlobal] == 0)
                HitBox[boxIdxGlobal] = 1;

            // Project to 2D crack plane coordinates for crack testing
            float3 toV = v - HitPos;
            float px = math.dot(toV, bp.planeForward);
            float py = math.dot(toV, bp.planeRight);
            float r2Dsq = px * px + py * py;

            // Test proximity to lightning-style crack spokes
            bool nearLightning = false;
            int segCountPtr = bp.segCountBase;
            int segStride = bp.segmentStride;

            for (int s = 0; s < bp.radialSpokes && !nearLightning; ++s)
            {
                int cntPts = SegmentCounts[segCountPtr + s];
                if (cntPts <= 1) continue; // Skip spokes with insufficient segments

                int baseSeg = bp.segBase + s * segStride;
                for (int seg = 0; seg < cntPts - 1; ++seg)
                {
                    int idx = baseSeg + seg;
                    if (idx >= AllSegments.Length) break;

                    // Get segment endpoints
                    float4 ab = AllSegments[idx];
                    float aX = ab.x, aY = ab.y;
                    float bX = ab.z, bY = ab.w;

                    // Calculate segment vector and length
                    float abvX = bX - aX;
                    float abvY = bY - aY;
                    float l2 = abvX * abvX + abvY * abvY;

                    // Project point onto line segment
                    float t;
                    if (l2 > 1e-10f) // Avoid division by zero
                    {
                        float dotPA = (px - aX) * abvX + (py - aY) * abvY;
                        float invL2 = 1.0f / l2;
                        float rawT = dotPA * invL2;
                        t = rawT;
                        // Clamp to segment bounds
                        if (t < 0f) t = 0f;
                        else if (t > 1f) t = 1f;
                    }
                    else
                    {
                        t = 0f; // Degenerate segment
                    }

                    // Calculate closest point on segment
                    float projX = aX + abvX * t;
                    float projY = aY + abvY * t;

                    // Test distance to crack line
                    float dX = px - projX;
                    float dY = py - projY;
                    if (dX * dX + dY * dY <= bp.crackThicknessSq)
                    {
                        nearLightning = true;
                        break;
                    }
                }
            }

            // Test proximity to concentric ring patterns
            bool nearRing = false;
            if (!nearLightning && bp.circularRingCount > 0)
            {
                float rVal = (r2Dsq > 1e-12f) ? math.sqrt(r2Dsq) : 0f;
                float angle = math.atan2(py, px);
                int mode = bp.ringGenMode;

                for (int rr = 0; rr < bp.circularRingCount && !nearRing; ++rr)
                {
                    float baseRing = RingRadii[bp.ringBase + rr];
                    float noisyRadius = baseRing;

                    // Apply noise variation based on ring generation mode
                    if (mode == 0) // Perfect (smooth sine wave)
                    {
                        float wave = bp.ringNoiseAmplitude *
                                     math.sin(bp.ringNoiseFrequency * angle + rr * 1.2345f);
                        noisyRadius += wave;
                    }
                    else if (mode == 1) // Square (sharp edges)
                    {
                        float wave = bp.ringNoiseAmplitude *
                                     math.sign(math.sin(bp.ringNoiseFrequency * angle + rr * 1.2345f));
                        noisyRadius += wave;
                    }
                    else // Sawtooth (linear ramps)
                    {
                        float phase = (bp.ringNoiseFrequency * angle) / (2f * math.PI);
                        phase = math.frac(phase);
                        float wave = bp.ringNoiseAmplitude * (2f * phase - 1f);
                        noisyRadius += wave;
                    }

                    // Test if voxel is within ring thickness
                    if (math.abs(rVal - noisyRadius) <= bp.ringThickness)
                    {
                        nearRing = true;
                    }
                }
            }

            // Determine final voxel state based on proximity tests
            if (nearLightning | nearRing)
            {
                OutVisited[i] = 1;
                NewVoxelData[i] = 255; // Remove voxel (create crack)
            }
            else
            {
                OutVisited[i] = 0;
                NewVoxelData[i] = VoxelData[i]; // Preserve original material
            }
        }
    }

    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Radial Data Generation Helpers
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Snapshot of fragmentation settings optimized for Burst compilation.
    /// Contains all parameters needed for radial crack generation without managed references.
    /// </summary>
    [BurstCompile]
    public struct FragSettingsSnap
    {
        // Core radial parameters
        public int planeMode; // Plane orientation mode
        public int radialSpokes; // Number of crack spokes
        public float maxCrackLength; // Calculated dynamic crack propagation distance
        public float minSegmentLength; // Minimum crack segment length
        public float maxSegmentLength; // Maximum crack segment length
        public float maxZigZagAngleDeg; // Maximum zigzag deviation angle
        public float crackThickness; // Thickness of crack lines

        // Ring parameters (procedurally calculated)
        public int circularRingCount; // Calculated number of concentric rings
        public float ringSpacing; // Calculated distance between rings
        public float ringGrowthExp; // Growth factor for ring spacing
        public float ringNoiseAmplitude; // Amplitude of ring noise
        public float ringNoiseFrequency; // Frequency of ring noise
        public float ringThickness; // Thickness of ring boundaries
        public int radialRingGenType; // Ring generation type

        // Impact parameters
        public float hitRadius; // Radius of destruction impact
        public uint randomSeed; // Random seed for crack generation

        // Data layout parameters
        public int segmentStride; // Stride between segment arrays
        public int segBase; // Base index for segment data
        public int cntBase; // Base index for count data
        public int ringBase; // Base index for ring data

        // Custom plane orientation (when planeMode = Custom)
        public float3 radialPlaneNormal; // Custom plane normal vector
        public float3 radialPlaneForward; // Custom plane forward vector
    }

    /// <summary>
    /// Parallel job for building radial crack data including spokes and rings.
    /// Generates procedural crack patterns with realistic zigzag propagation.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct BuildRadialData : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FragSettingsSnap> FragSettingsArr;
        [ReadOnly] public NativeArray<BoxTrans> BoxDataArr;
        public uint GlobalSeed;

        public int BoxStartGlobal;

        [NativeDisableParallelForRestriction] public NativeArray<float4> Segments;
        [NativeDisableParallelForRestriction] public NativeArray<int> SegmentCounts;
        [NativeDisableParallelForRestriction] public NativeArray<float> RingRadii;
        public NativeArray<RadialParam> Params;

        public void Execute(int boxIdx)
        {
            int globalIdx = BoxStartGlobal + boxIdx;
            FragSettingsSnap fs = FragSettingsArr[boxIdx];

            // ─────────────────────────────────────────────────────
            // 1. Pick plane axes
            // ─────────────────────────────────────────────────────
            float3 n, fwd;
            switch (fs.planeMode)
            {
                case 0:
                {
                    BoxTrans bd = BoxDataArr[globalIdx];

                    float3 axX = new float3(bd.vRightX, bd.vRightY, bd.vRightZ);
                    float3 axY = new float3(bd.vUpX, bd.vUpY, bd.vUpZ);
                    float3 axZ = new float3(bd.vFwdX, bd.vFwdY, bd.vFwdZ);

                    float sx = bd.sizeX;
                    float sy = bd.sizeY;
                    float sz = bd.sizeZ;

                    if (sx <= sy && sx <= sz)
                    {
                        n = axX;
                        fwd = (sy >= sz) ? axY : axZ;
                    }
                    else if (sy <= sx && sy <= sz)
                    {
                        n = axY;
                        fwd = (sx >= sz) ? axX : axZ;
                    }
                    else
                    {
                        n = axZ;
                        fwd = (sx >= sy) ? axX : axY;
                    }
                }
                    break;

                case 1:
                {
                    BoxTrans bd = BoxDataArr[globalIdx];
                    n = new float3(bd.vRightX, bd.vRightY, bd.vRightZ);
                    fwd = new float3(bd.vUpX, bd.vUpY, bd.vUpZ);
                }
                    break;

                case 2:
                {
                    BoxTrans bd = BoxDataArr[globalIdx];
                    n = new float3(bd.vUpX, bd.vUpY, bd.vUpZ);
                    fwd = new float3(bd.vFwdX, bd.vFwdY, bd.vFwdZ);
                }
                    break;

                case 3:
                {
                    BoxTrans bd = BoxDataArr[globalIdx];
                    n = new float3(bd.vFwdX, bd.vFwdY, bd.vFwdZ);
                    fwd = new float3(bd.vRightX, bd.vRightY, bd.vRightZ);
                }
                    break;

                default:
                    n = fs.radialPlaneNormal;
                    fwd = fs.radialPlaneForward;
                    break;
            }

            n = math.normalize(n);
            fwd = math.normalize(fwd);
            if (math.abs(math.dot(n, fwd)) > 0.9999f)
            {
                fwd = math.normalize(math.cross(n, new float3(1, 0, 0)));
                if (math.lengthsq(fwd) < 1e-6f)
                    fwd = math.normalize(math.cross(n, new float3(0, 1, 0)));
            }

            float3 right = math.normalize(math.cross(n, fwd));

            // ─────────────────────────────────────────────────────
            // 2. Precompute layout
            // ─────────────────────────────────────────────────────
            int stride = fs.segmentStride;
            int maxSegPerSpoke = stride + 1;

            int segBase = fs.segBase;
            int cntBase = fs.cntBase;

            uint boxSeed = fs.randomSeed ^ (uint)BoxDataArr[globalIdx].id;
            var rng = new Unity.Mathematics.Random(boxSeed);

            Span<float2> tmp = stackalloc float2[maxSegPerSpoke];

            // ─────────────────────────────────────────────────────
            // 3. Figure out the biggest allowed 2D radius
            // ─────────────────────────────────────────────────────
            float safety = fs.ringNoiseAmplitude + fs.ringThickness;
            float maxPlaneRadius = math.max(0f, fs.hitRadius - safety);

            float spokeMaxRadius = maxPlaneRadius;
            float spokeMaxLength = math.min(fs.maxCrackLength, spokeMaxRadius);
            // ─────────────────────────────────────────────────────
            // 4. Build spokes
            // ─────────────────────────────────────────────────────
            for (int s = 0; s < fs.radialSpokes; ++s)
            {
                int cntPtr = cntBase + s;
                int segPtr = segBase + s * stride;

                float baseFr = rng.NextFloat(-.2f, .2f);
                float angle = 2f * math.PI * s / fs.radialSpokes + baseFr;

                float2 cur = float2.zero;
                tmp[0] = cur;
                int pts = 1;
                float travelled = 0f;
                float curAng = angle;
                float sign = 1f;

                while (travelled < spokeMaxLength && pts < maxSegPerSpoke)
                {
                    uint rv = rng.NextUInt();

                    // base segment length
                    float segLen = math.lerp(fs.minSegmentLength, fs.maxSegmentLength,
                                       (rv & 0xFFFF) * (1f / 65535f))
                                   * math.pow(1.05f, pts - 1);

                    if (travelled + segLen > spokeMaxLength)
                        segLen = spokeMaxLength - travelled;

                    // angle zigzag
                    float angD = ((rv >> 16) & 0xFFFF) * (1f / 65535f) * fs.maxZigZagAngleDeg;
                    curAng += sign * math.radians(angD);
                    sign = -sign;

                    float2 dir = new float2(math.cos(curAng), math.sin(curAng));
                    float2 next = cur + dir * segLen;

                    // clamp to circle so the spoke never exits
                    float nextLenSq = math.lengthsq(next);
                    if (nextLenSq > spokeMaxRadius * spokeMaxRadius)
                    {
                        float len = math.sqrt(nextLenSq);
                        if (len > 1e-6f)
                            next = next * (spokeMaxRadius / len);
                        tmp[pts++] = next;
                        travelled = spokeMaxLength;
                        break;
                    }

                    cur = next;
                    tmp[pts++] = cur;
                    travelled += segLen;
                }

                // write back
                SegmentCounts[cntPtr] = pts;
                for (int p = 0; p < pts - 1; ++p)
                {
                    float2 a = tmp[p], b = tmp[p + 1];
                    Segments[segPtr + p] = new float4(a.x, a.y, b.x, b.y);
                }
            }

            // ─────────────────────────────────────────────────────
            // 5. Build rings
            // ─────────────────────────────────────────────────────
            int ringBase = fs.ringBase;

            int wantedCnt = fs.circularRingCount + (int)(fs.circularRingCount * .33f);

            int actualRingCount = 0;
            for (int r = 0; r < wantedCnt; ++r)
            {
                float baseR = fs.ringSpacing * math.pow(fs.ringGrowthExp, r);

                // if this base ring is already outside, we are done
                if (baseR > maxPlaneRadius)
                    break;

                RingRadii[ringBase + actualRingCount] = baseR;
                actualRingCount++;
            }

            // ─────────────────────────────────────────────────────
            // 6. Store params
            // ─────────────────────────────────────────────────────
            Params[boxIdx] = new RadialParam
            {
                crackThicknessSq = fs.crackThickness * fs.crackThickness,
                hitRadiusSq = fs.hitRadius * fs.hitRadius,
                planeForward = fwd,
                planeRight = right,
                radialSpokes = fs.radialSpokes,
                segmentStride = stride,
                segBase = segBase,
                segCountBase = cntBase,
                ringBase = ringBase,
                circularRingCount = actualRingCount,
                ringNoiseAmplitude = fs.ringNoiseAmplitude,
                ringNoiseFrequency = fs.ringNoiseFrequency,
                ringThickness = fs.ringThickness,
                ringGenMode = fs.radialRingGenType,

                voxelBase = 0,
                yz = 0,
                boundYTo = 0,
                boundZTo = 0
            };
        }
    }
}