using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using static BoxCutter.BoxObj;

namespace BoxCutter
{
    /// <summary>
    /// Job for detecting if a child transform's bounds touch a box's SubBox's.
    /// If True then that child should be re-parented and have its reference be removed from the source's list.
    /// If not then do not anything.
    /// </summary>
    [BurstCompile]
    public struct ChildTransConnection : IJobParallelFor
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Spatial Acceleration Data
        //─────────────────────────────────────────────────────────────────────────────────────
        
        /// <summary>KD-tree cell data for spatial partitioning</summary>
        [ReadOnly] public NativeArray<CellData> KdCellData;

        /// <summary>SubBox fragments organized by spatial structure</summary>
        [ReadOnly] public NativeArray<SubBox> Subs;

        /// <summary>Index mapping from spatial to original order</summary>
        [ReadOnly] public NativeArray<int> KdOriginalIndices;

        /// <summary>Number of spatial cells in object A KD-tree</summary>
        [ReadOnly] public int KdCellLength;
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Transform Data
        //─────────────────────────────────────────────────────────────────────────────────────
        public float VoxelSize;

        public float RotX, RotY, RotZ, RotW;
        public float StartPosX, StartPosY, StartPosZ;
        public float PreLocalRightX, PreLocalRightY, PreLocalRightZ;
        public float PreLocalUpX, PreLocalUpY, PreLocalUpZ;
        public float PreLocalForwardX, PreLocalForwardY, PreLocalForwardZ;

        public float Offset;
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Transform Data
        //─────────────────────────────────────────────────────────────────────────────────────

        [ReadOnly] public NativeArray<Vector3> ChildPos;
        [ReadOnly] public NativeArray<Quaternion> ChildRots;
        [ReadOnly] public NativeArray<Vector3> ChildSizes;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Output
        //─────────────────────────────────────────────────────────────────────────────────────
        
        public NativeArray<bool> Results;
        
        public void Execute(int index)
        {
            Vector3 childPos = ChildPos[index];
            Quaternion childRot = ChildRots[index];
            Vector3 childSize = ChildSizes[index];

            float childPosX = childPos.x;
            float childPosY = childPos.y;
            float childPosZ = childPos.z;

            float childRotX = childRot.x;
            float childRotY = childRot.y;
            float childRotZ = childRot.z;
            float childRotW = childRot.w;

            float childSizeX = childSize.x;
            float childSizeY = childSize.y;
            float childSizeZ = childSize.z;

            for (int i = 0; i < KdCellLength; i++)
            {
                CellData cell = KdCellData[i];
                int startIndex = cell.rangeStart;
                int endIndex = cell.rangeEnd;

                float sizeX = (cell.maxX - cell.minX) * VoxelSize;
                float sizeY = (cell.maxY - cell.minY) * VoxelSize;
                float sizeZ = (cell.maxZ - cell.minZ) * VoxelSize;

                float midX = (cell.maxX + cell.minX) * 0.5f;
                float midY = (cell.maxY + cell.minY) * 0.5f;
                float midZ = (cell.maxZ + cell.minZ) * 0.5f;

                float posX = StartPosX + PreLocalRightX * midX + PreLocalUpX * midY + PreLocalForwardX * midZ;
                float posY = StartPosY + PreLocalRightY * midX + PreLocalUpY * midY + PreLocalForwardY * midZ;
                float posZ = StartPosZ + PreLocalRightZ * midX + PreLocalUpZ * midY + PreLocalForwardZ * midZ;

                if (BoxCollisionUtil.CheckSATOverlapRaw
                    (
                        posX, posY, posZ,
                        RotX, RotY, RotZ, RotW,
                        sizeX + Offset, sizeY + Offset, sizeZ + Offset,
                        childPosX, childPosY, childPosZ,
                        childRotX, childRotY, childRotZ, childRotW,
                        childSizeX + Offset, childSizeY + Offset, childSizeZ + Offset)
                   )
                {
                    for (int aIndex = startIndex; aIndex < endIndex; aIndex++)
                    {
                        SubBox sub = Subs[KdOriginalIndices[aIndex]];
                        int minX = sub.minX;
                        int minY = sub.minY;
                        int minZ = sub.minZ;
                        int maxX = sub.maxX;
                        int maxY = sub.maxY;
                        int maxZ = sub.maxZ;

                        float subSizeX = (maxX - minX) * VoxelSize;
                        float subSizeY = (maxY - minY) * VoxelSize;
                        float subSizeZ = (maxZ - minZ) * VoxelSize;

                        float subMidX = (maxX + minX) * 0.5f;
                        float subMidY = (maxY + minY) * 0.5f;
                        float subMidZ = (maxZ + minZ) * 0.5f;

                        float subPosX = StartPosX + PreLocalRightX * subMidX + PreLocalUpX * subMidY + PreLocalForwardX * subMidZ;
                        float subPosY = StartPosY + PreLocalRightY * subMidX + PreLocalUpY * subMidY + PreLocalForwardY * subMidZ;
                        float subPosZ = StartPosZ + PreLocalRightZ * subMidX + PreLocalUpZ * subMidY + PreLocalForwardZ * subMidZ;
                        
                        if (BoxCollisionUtil.CheckSATOverlapRaw
                            (
                                subPosX, subPosY, subPosZ,
                                RotX, RotY, RotZ, RotW,
                                subSizeX + Offset, subSizeY + Offset, subSizeZ + Offset,
                                childPosX, childPosY, childPosZ,
                                childRotX, childRotY, childRotZ, childRotW,
                                childSizeX + Offset, childSizeY + Offset, childSizeZ + Offset)
                           )
                        {
                            Results[index] = true;
                            return;
                        }
                    }
                }
            }
        }
    }
}