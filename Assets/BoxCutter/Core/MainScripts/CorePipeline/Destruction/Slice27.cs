using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static BoxCutter.BoxObj;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    /// <summary>
    /// Advanced destruction algorithm that splits SubBox regions into 27 potential fragments.
    /// Handles complex collision geometry with rotation-aware bounding box calculations.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct Slice27 : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> InitialHitSubArr;
        [ReadOnly] public NativeArray<int2> InitialHitSubRangeArr;

        public float HitPosX;
        public float HitPosY;
        public float HitPosZ;

        public bool BoxMode;
        public float HitSizeX;
        public float HitSizeY;
        public float HitSizeZ;

        public float HitRotX;
        public float HitRotY;
        public float HitRotZ;
        public float HitRotW;

        public NativeArray<BoxTrans> BoxData;

        public NativeArray<int3> HitMinInterval;
        public NativeArray<int3> HitMaxInterval;

        public NativeList<SubObjOutputPair>.ParallelWriter HitSubObjList;
        public NativeList<SubObjOutputPair>.ParallelWriter CentralSubObjList;

        private const int IntMax = int.MaxValue;
        private const int IntNegMax = int.MinValue;

        /// <summary>
        /// Processes a single BoxObj to generate up to 27 fragment pieces from destruction impact.
        /// </summary>
        /// <param name="index">Index of the BoxObj to process for fragmentation</param>
        public void Execute(int index)
        {
            // Initialize bounds tracking for hit regions
            int tempHitMinX = IntMax;
            int tempHitMinY = IntMax;
            int tempHitMinZ = IntMax;
            int tempHitMaxX = IntNegMax;
            int tempHitMaxY = IntNegMax;
            int tempHitMaxZ = IntNegMax;

            BoxTrans boxTrans = BoxData[index];
            float voxelSize = boxTrans.voxelSize;
            float startPosX = boxTrans.startPosX;
            float startPosY = boxTrans.startPosY;
            float startPosZ = boxTrans.startPosZ;
            float localRightX = boxTrans.rightX;
            float localRightY = boxTrans.rightY;
            float localRightZ = boxTrans.rightZ;
            float localUpX = boxTrans.upX;
            float localUpY = boxTrans.upY;
            float localUpZ = boxTrans.upZ;
            float localForwardX = boxTrans.fwdX;
            float localForwardY = boxTrans.fwdY;
            float localForwardZ = boxTrans.fwdZ;
            float preLocalRightX = boxTrans.vRightX;
            float preLocalRightY = boxTrans.vRightY;
            float preLocalRightZ = boxTrans.vRightZ;
            float preLocalUpX = boxTrans.vUpX;
            float preLocalUpY = boxTrans.vUpY;
            float preLocalUpZ = boxTrans.vUpZ;
            float preLocalForwardX = boxTrans.vFwdX;
            float preLocalForwardY = boxTrans.vFwdY;
            float preLocalForwardZ = boxTrans.vFwdZ;
            float iqx = boxTrans.iqx;
            float iqy = boxTrans.iqy;
            float iqz = boxTrans.iqz;
            float iqw = boxTrans.iqw;

            int2 hitSubRange = InitialHitSubRangeArr[index];
            int startIndex = hitSubRange.x;
            int endIndex = hitSubRange.y;

            for (int i = startIndex; i < endIndex; i++)
            {
                SubBox initialHitSub = InitialHitSubArr[i];
                byte initialHitSubColor = initialHitSub.magicaIndex;

                float tempHitSizeX = HitSizeX;
                float tempHitSizeY = HitSizeY;
                float tempHitSizeZ = HitSizeZ;
                if (BoxMode)
                {
                    float lx = iqw * HitRotX + iqx * HitRotW + iqy * HitRotZ - iqz * HitRotY;
                    float ly = iqw * HitRotY - iqx * HitRotZ + iqy * HitRotW + iqz * HitRotX;
                    float lz = iqw * HitRotZ + iqx * HitRotY - iqy * HitRotX + iqz * HitRotW;
                    float lw = iqw * HitRotW - iqx * HitRotX - iqy * HitRotY - iqz * HitRotZ;

                    float xx = lx * lx, yy = ly * ly, zz = lz * lz;
                    float xy = lx * ly, xz = lx * lz, yz = ly * lz;
                    float wx = lw * lx, wy = lw * ly, wz = lw * lz;

                    float rcx = 1f - 2f * (yy + zz);
                    float rcy = 2f * (xy + wz);
                    float rcz = 2f * (xz - wy);

                    float ucx = 2f * (xy - wz);
                    float ucy = 1f - 2f * (xx + zz);
                    float ucz = 2f * (yz + wx);

                    float fcx = 2f * (xz + wy);
                    float fcy = 2f * (yz - wx);
                    float fcz = 1f - 2f * (xx + yy);

                    float hx2 = HitSizeX * 0.5f;
                    float hy2 = HitSizeY * 0.5f;
                    float hz2 = HitSizeZ * 0.5f;

                    tempHitSizeX = ((rcx < 0f ? -rcx : rcx) * hx2 + (ucx < 0f ? -ucx : ucx) * hy2 + (fcx < 0f ? -fcx : fcx) * hz2) * 2;
                    tempHitSizeY = ((rcy < 0f ? -rcy : rcy) * hx2 + (ucy < 0f ? -ucy : ucy) * hy2 + (fcy < 0f ? -fcy : fcy) * hz2) * 2;
                    tempHitSizeZ = ((rcz < 0f ? -rcz : rcz) * hx2 + (ucz < 0f ? -ucz : ucz) * hy2 + (fcz < 0f ? -fcz : fcz) * hz2) * 2;
                }

                float hitX = HitPosX;
                float hitY = HitPosY;
                float hitZ = HitPosZ;
                int minX = initialHitSub.minX;
                int minY = initialHitSub.minY;
                int minZ = initialHitSub.minZ;
                int maxX = initialHitSub.maxX;
                int maxY = initialHitSub.maxY;
                int maxZ = initialHitSub.maxZ;

                float subSizeX = (maxX - minX) * voxelSize;
                float subSizeY = (maxY - minY) * voxelSize;
                float subSizeZ = (maxZ - minZ) * voxelSize;

                float midX = (maxX + minX) * 0.5f;
                float midY = (maxY + minY) * 0.5f;
                float midZ = (maxZ + minZ) * 0.5f;

                float subPosX = startPosX + preLocalRightX * midX + preLocalUpX * midY + preLocalForwardX * midZ;
                float subPosY = startPosY + preLocalRightY * midX + preLocalUpY * midY + preLocalForwardY * midZ;
                float subPosZ = startPosZ + preLocalRightZ * midX + preLocalUpZ * midY + preLocalForwardZ * midZ;
                int tempIntervalMinX = 0;
                int tempIntervalMinY = 0;
                int tempIntervalMinZ = 0;

                int tempIntervalMaxX = (int)math.round(subSizeX / voxelSize);
                int tempIntervalMaxY = (int)math.round(subSizeY / voxelSize);
                int tempIntervalMaxZ = (int)math.round(subSizeZ / voxelSize);
                float halfSizeX = subSizeX / 2f;
                float halfSizeY = subSizeY / 2f;
                float halfSizeZ = subSizeZ / 2f;

                float tStartPosX = subPosX - localRightX * halfSizeX - localUpX * halfSizeY - localForwardX * halfSizeZ;
                float tStartPosY = subPosY - localRightY * halfSizeX - localUpY * halfSizeY - localForwardY * halfSizeZ;
                float tStartPosZ = subPosZ - localRightZ * halfSizeX - localUpZ * halfSizeY - localForwardZ * halfSizeZ;
                float hitSizeXTo = BoxMode ? tempHitSizeX * 0.5f : tempHitSizeX;
                float hitSizeYTo = BoxMode ? tempHitSizeY * 0.5f : tempHitSizeY;
                float hitSizeZTo = BoxMode ? tempHitSizeZ * 0.5f : tempHitSizeZ;

                float localRightHalfSizeX = localRightX * hitSizeXTo;
                float localRightHalfSizeY = localRightY * hitSizeXTo;
                float localRightHalfSizeZ = localRightZ * hitSizeXTo;

                float localUpHalfSizeX = localUpX * hitSizeYTo;
                float localUpHalfSizeY = localUpY * hitSizeYTo;
                float localUpHalfSizeZ = localUpZ * hitSizeYTo;

                float localForwardHalfSizeX = localForwardX * hitSizeZTo;
                float localForwardHalfSizeY = localForwardY * hitSizeZTo;
                float localForwardHalfSizeZ = localForwardZ * hitSizeZTo;

                float hitStartPosX = hitX - localRightHalfSizeX - localUpHalfSizeX - localForwardHalfSizeX;
                float hitStartPosY = hitY - localRightHalfSizeY - localUpHalfSizeY - localForwardHalfSizeY;
                float hitStartPosZ = hitZ - localRightHalfSizeZ - localUpHalfSizeZ - localForwardHalfSizeZ;

                float hitEndPosX = hitX + localRightHalfSizeX + localUpHalfSizeX + localForwardHalfSizeX;
                float hitEndPosY = hitY + localRightHalfSizeY + localUpHalfSizeY + localForwardHalfSizeY;
                float hitEndPosZ = hitZ + localRightHalfSizeZ + localUpHalfSizeZ + localForwardHalfSizeZ;
                float deltaStartPosX = hitStartPosX - tStartPosX;
                float deltaStartPosY = hitStartPosY - tStartPosY;
                float deltaStartPosZ = hitStartPosZ - tStartPosZ;

                float startLocalPosX, startLocalPosY, startLocalPosZ;
                {
                    float tX = 2f * (iqy * deltaStartPosZ - iqz * deltaStartPosY);
                    float tY = 2f * (iqz * deltaStartPosX - iqx * deltaStartPosZ);
                    float tZ = 2f * (iqx * deltaStartPosY - iqy * deltaStartPosX);

                    startLocalPosX = deltaStartPosX + iqw * tX + (iqy * tZ - iqz * tY);
                    startLocalPosY = deltaStartPosY + iqw * tY + (iqz * tX - iqx * tZ);
                    startLocalPosZ = deltaStartPosZ + iqw * tZ + (iqx * tY - iqy * tX);
                }

                int hitIntervalMinX = (int)math.round(startLocalPosX / voxelSize);
                int hitIntervalMinY = (int)math.round(startLocalPosY / voxelSize);
                int hitIntervalMinZ = (int)math.round(startLocalPosZ / voxelSize);
                float deltaEndPosX = hitEndPosX - tStartPosX;
                float deltaEndPosY = hitEndPosY - tStartPosY;
                float deltaEndPosZ = hitEndPosZ - tStartPosZ;

                float endLocalPosX, endLocalPosY, endLocalPosZ;
                {
                    float tX = 2f * (iqy * deltaEndPosZ - iqz * deltaEndPosY);
                    float tY = 2f * (iqz * deltaEndPosX - iqx * deltaEndPosZ);
                    float tZ = 2f * (iqx * deltaEndPosY - iqy * deltaEndPosX);

                    endLocalPosX = deltaEndPosX + iqw * tX + (iqy * tZ - iqz * tY);
                    endLocalPosY = deltaEndPosY + iqw * tY + (iqz * tX - iqx * tZ);
                    endLocalPosZ = deltaEndPosZ + iqw * tZ + (iqx * tY - iqy * tX);
                }

                int hitIntervalMaxX = (int)math.round(endLocalPosX / voxelSize);
                int hitIntervalMaxY = (int)math.round(endLocalPosY / voxelSize);
                int hitIntervalMaxZ = (int)math.round(endLocalPosZ / voxelSize);
                float deltaObjectPosX = tStartPosX - startPosX;
                float deltaObjectPosY = tStartPosY - startPosY;
                float deltaObjectPosZ = tStartPosZ - startPosZ;

                float objectStartLocalPosX, objectStartLocalPosY, objectStartLocalPosZ;
                {
                    float tX = 2f * (iqy * deltaObjectPosZ - iqz * deltaObjectPosY);
                    float tY = 2f * (iqz * deltaObjectPosX - iqx * deltaObjectPosZ);
                    float tZ = 2f * (iqx * deltaObjectPosY - iqy * deltaObjectPosX);

                    objectStartLocalPosX = deltaObjectPosX + iqw * tX + (iqy * tZ - iqz * tY);
                    objectStartLocalPosY = deltaObjectPosY + iqw * tY + (iqz * tX - iqx * tZ);
                    objectStartLocalPosZ = deltaObjectPosZ + iqw * tZ + (iqx * tY - iqy * tX);
                }

                float mult = Mathf.Pow(10.0f, 1.0f);

                int intervalOffsetX = (int)(math.round((objectStartLocalPosX / voxelSize) * mult) / mult);
                int intervalOffsetY = (int)(math.round((objectStartLocalPosY / voxelSize) * mult) / mult);
                int intervalOffsetZ = (int)(math.round((objectStartLocalPosZ / voxelSize) * mult) / mult);
                int xBoundMin = 0;
                int xBoundMax = 0;
                int yBoundMin = 0;
                int yBoundMax = 0;
                int zBoundMin = 0;
                int zBoundMax = 0;

                int range = 3;
                int totalCombinations = range * range * range;

                for (int combIndex = 0; combIndex < totalCombinations; combIndex++)
                {
                    int dx = (combIndex / (range * range)) - 1;
                    int dy = ((combIndex / range) % range) - 1;
                    int dz = (combIndex % range) - 1;

                    int clampedHitIntervalMinX = (hitIntervalMinX < tempIntervalMinX)
                        ? tempIntervalMinX
                        : (hitIntervalMinX > tempIntervalMaxX ? tempIntervalMaxX : hitIntervalMinX);
                    int clampedHitIntervalMaxX = (hitIntervalMaxX < tempIntervalMinX)
                        ? tempIntervalMinX
                        : (hitIntervalMaxX > tempIntervalMaxX ? tempIntervalMaxX : hitIntervalMaxX);
                    int clampedHitIntervalMinY = (hitIntervalMinY < tempIntervalMinY)
                        ? tempIntervalMinY
                        : (hitIntervalMinY > tempIntervalMaxY ? tempIntervalMaxY : hitIntervalMinY);
                    int clampedHitIntervalMaxY = (hitIntervalMaxY < tempIntervalMinY)
                        ? tempIntervalMinY
                        : (hitIntervalMaxY > tempIntervalMaxY ? tempIntervalMaxY : hitIntervalMaxY);
                    int clampedHitIntervalMinZ = (hitIntervalMinZ < tempIntervalMinZ)
                        ? tempIntervalMinZ
                        : (hitIntervalMinZ > tempIntervalMaxZ ? tempIntervalMaxZ : hitIntervalMinZ);
                    int clampedHitIntervalMaxZ = (hitIntervalMaxZ < tempIntervalMinZ)
                        ? tempIntervalMinZ
                        : (hitIntervalMaxZ > tempIntervalMaxZ ? tempIntervalMaxZ : hitIntervalMaxZ);

                    switch (dx)
                    {
                        case -1:
                            xBoundMin = tempIntervalMinX;
                            xBoundMax = clampedHitIntervalMinX;
                            break;
                        case 0:
                            xBoundMin = clampedHitIntervalMinX;
                            xBoundMax = clampedHitIntervalMaxX;
                            break;
                        case 1:
                            xBoundMin = clampedHitIntervalMaxX;
                            xBoundMax = tempIntervalMaxX;
                            break;
                    }

                    switch (dy)
                    {
                        case -1:
                            yBoundMin = tempIntervalMinY;
                            yBoundMax = clampedHitIntervalMinY;
                            break;
                        case 0:
                            yBoundMin = clampedHitIntervalMinY;
                            yBoundMax = clampedHitIntervalMaxY;
                            break;
                        case 1:
                            yBoundMin = clampedHitIntervalMaxY;
                            yBoundMax = tempIntervalMaxY;
                            break;
                    }

                    switch (dz)
                    {
                        case -1:
                            zBoundMin = tempIntervalMinZ;
                            zBoundMax = clampedHitIntervalMinZ;
                            break;
                        case 0:
                            zBoundMin = clampedHitIntervalMinZ;
                            zBoundMax = clampedHitIntervalMaxZ;
                            break;
                        case 1:
                            zBoundMin = clampedHitIntervalMaxZ;
                            zBoundMax = tempIntervalMaxZ;
                            break;
                    }

                    bool outOfBounds = (xBoundMin == xBoundMax || yBoundMin == yBoundMax || zBoundMin == zBoundMax);

                    if (!outOfBounds)
                    {
                        float xCenterInterval = (xBoundMin + xBoundMax) / 2.0f;
                        float yCenterInterval = (yBoundMin + yBoundMax) / 2.0f;
                        float zCenterInterval = (zBoundMin + zBoundMax) / 2.0f;

                        float nodePosX = tStartPosX;
                        float nodePosY = tStartPosY;
                        float nodePosZ = tStartPosZ;

                        nodePosX += preLocalRightX * xCenterInterval;
                        nodePosY += preLocalRightY * xCenterInterval;
                        nodePosZ += preLocalRightZ * xCenterInterval;

                        nodePosX += preLocalUpX * yCenterInterval;
                        nodePosY += preLocalUpY * yCenterInterval;
                        nodePosZ += preLocalUpZ * yCenterInterval;

                        nodePosX += preLocalForwardX * zCenterInterval;
                        nodePosY += preLocalForwardY * zCenterInterval;
                        nodePosZ += preLocalForwardZ * zCenterInterval;

                        if (dx == 0 && dy == 0 && dz == 0)
                        {
                            int boundIntervalX = xBoundMax - xBoundMin;
                            int boundIntervalY = yBoundMax - yBoundMin;
                            int boundIntervalZ = zBoundMax - zBoundMin;

                            float halfBoundX = boundIntervalX / 2.0f;
                            float halfBoundY = boundIntervalY / 2.0f;
                            float halfBoundZ = boundIntervalZ / 2.0f;
                            float preLocalRightHalfBoundX = preLocalRightX * halfBoundX;
                            float preLocalRightHalfBoundY = preLocalRightY * halfBoundX;
                            float preLocalRightHalfBoundZ = preLocalRightZ * halfBoundX;

                            float preLocalUpHalfBoundX = preLocalUpX * halfBoundY;
                            float preLocalUpHalfBoundY = preLocalUpY * halfBoundY;
                            float preLocalUpHalfBoundZ = preLocalUpZ * halfBoundY;

                            float preLocalForwardHalfBoundX = preLocalForwardX * halfBoundZ;
                            float preLocalForwardHalfBoundY = preLocalForwardY * halfBoundZ;
                            float preLocalForwardHalfBoundZ = preLocalForwardZ * halfBoundZ;

                            float trueNodeStartPosX = nodePosX - preLocalRightHalfBoundX - preLocalUpHalfBoundX - preLocalForwardHalfBoundX;
                            float trueNodeStartPosY = nodePosY - preLocalRightHalfBoundY - preLocalUpHalfBoundY - preLocalForwardHalfBoundY;
                            float trueNodeStartPosZ = nodePosZ - preLocalRightHalfBoundZ - preLocalUpHalfBoundZ - preLocalForwardHalfBoundZ;
                            float deltaX = trueNodeStartPosX - startPosX;
                            float deltaY = trueNodeStartPosY - startPosY;
                            float deltaZ = trueNodeStartPosZ - startPosZ;

                            float nodeStartLocalPosX, nodeStartLocalPosY, nodeStartLocalPosZ;
                            {
                                float tX = 2f * (iqy * deltaZ - iqz * deltaY);
                                float tY = 2f * (iqz * deltaX - iqx * deltaZ);
                                float tZ = 2f * (iqx * deltaY - iqy * deltaX);

                                nodeStartLocalPosX = deltaX + iqw * tX + (iqy * tZ - iqz * tY);
                                nodeStartLocalPosY = deltaY + iqw * tY + (iqz * tX - iqx * tZ);
                                nodeStartLocalPosZ = deltaZ + iqw * tZ + (iqx * tY - iqy * tX);
                            }

                            float centerMult = math.pow(10.0f, 1.0f);

                            int centerIntervalOffsetX = (int)(math.round((nodeStartLocalPosX / voxelSize) * centerMult) / centerMult);
                            int centerIntervalOffsetY = (int)(math.round((nodeStartLocalPosY / voxelSize) * centerMult) / centerMult);
                            int centerIntervalOffsetZ = (int)(math.round((nodeStartLocalPosZ / voxelSize) * centerMult) / centerMult);

                            if (centerIntervalOffsetX < tempHitMinX) tempHitMinX = centerIntervalOffsetX;
                            if (centerIntervalOffsetY < tempHitMinY) tempHitMinY = centerIntervalOffsetY;
                            if (centerIntervalOffsetZ < tempHitMinZ) tempHitMinZ = centerIntervalOffsetZ;

                            if (boundIntervalX + centerIntervalOffsetX > tempHitMaxX) tempHitMaxX = boundIntervalX + centerIntervalOffsetX;
                            if (boundIntervalY + centerIntervalOffsetY > tempHitMaxY) tempHitMaxY = boundIntervalY + centerIntervalOffsetY;
                            if (boundIntervalZ + centerIntervalOffsetZ > tempHitMaxZ) tempHitMaxZ = boundIntervalZ + centerIntervalOffsetZ;
                            HitSubObjList.AddNoResize(new SubObjOutputPair
                            {
                                SubBox = new SubBox
                                {
                                    minX = intervalOffsetX + xBoundMin,
                                    minY = intervalOffsetY + yBoundMin,
                                    minZ = intervalOffsetZ + zBoundMin,
                                    maxX = intervalOffsetX + xBoundMax,
                                    maxY = intervalOffsetY + yBoundMax,
                                    maxZ = intervalOffsetZ + zBoundMax,
                                    magicaIndex = initialHitSubColor
                                },
                                Index = index
                            });
                        }
                        else
                        {
                            CentralSubObjList.AddNoResize(new SubObjOutputPair
                            {
                                SubBox = new SubBox()
                                {
                                    minX = intervalOffsetX + xBoundMin,
                                    minY = intervalOffsetY + yBoundMin,
                                    minZ = intervalOffsetZ + zBoundMin,
                                    maxX = intervalOffsetX + xBoundMax,
                                    maxY = intervalOffsetY + yBoundMax,
                                    maxZ = intervalOffsetZ + zBoundMax,
                                    magicaIndex = initialHitSubColor
                                },
                                Index = index
                            });
                        }
                    }
                }
            }

            HitMinInterval[index] = new int3(tempHitMinX, tempHitMinY, tempHitMinZ);
            HitMaxInterval[index] = new int3(tempHitMaxX, tempHitMaxY, tempHitMaxZ);
        }
    }

}