using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static BoxCutter.BoxObj;

namespace BoxCutter
{
    /// <summary>
    /// Job for precise non-convex collision detection between two BoxObj instances.
    /// Uses KD-tree spatial acceleration and SubBox-level testing for maximum accuracy.
    /// </summary>
    [BurstCompile]
    public struct BoxIntersectWithBoxJob : IJobParallelFor
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Spatial Acceleration Data
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>KD-tree cell data for object A spatial partitioning</summary>
        [ReadOnly] public NativeArray<CellData> AKdCellData;

        /// <summary>KD-tree cell data for object B spatial partitioning</summary>
        [ReadOnly] public NativeArray<CellData> BKdCellData;

        /// <summary>SubBox fragments for object A organized by spatial structure</summary>
        [ReadOnly] public NativeArray<SubBox> AKdSubs;

        /// <summary>SubBox fragments for object B organized by spatial structure</summary>
        [ReadOnly] public NativeArray<SubBox> BKdSubs;

        /// <summary>Index mapping from spatial to original order for object A</summary>
        [ReadOnly] public NativeArray<int> AKdOriginalIndices;

        /// <summary>Index mapping from spatial to original order for object B</summary>
        [ReadOnly] public NativeArray<int> BKdOriginalIndices;

        /// <summary>Number of spatial cells in object A KD-tree</summary>
        public int AKdCellLength;

        /// <summary>Number of spatial cells in object B KD-tree</summary>
        public int BKdCellLength;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Transform Data
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Voxel size for object A used in fragment calculations</summary>
        public float AVoxelSize;

        /// <summary>Voxel size for object B used in fragment calculations</summary>
        public float BVoxelSize;

        // Object A rotation quaternion components
        public float ARotX, ARotY, ARotZ, ARotW;

        // Object B rotation quaternion components
        public float BRotX, BRotY, BRotZ, BRotW;

        // Object A world position
        public float AStartPosX, AStartPosY, AStartPosZ;

        // Object A local coordinate frame vectors
        public float APreLocalRightX, APreLocalRightY, APreLocalRightZ;
        public float APreLocalUpX, APreLocalUpY, APreLocalUpZ;
        public float APreLocalForwardX, APreLocalForwardY, APreLocalForwardZ;

        // Object B world position
        public float BStartPosX, BStartPosY, BStartPosZ;

        // Object B local coordinate frame vectors
        public float BPreLocalRightX, BPreLocalRightY, BPreLocalRightZ;
        public float BPreLocalUpX, BPreLocalUpY, BPreLocalUpZ;
        public float BPreLocalForwardX, BPreLocalForwardY, BPreLocalForwardZ;

        /// <summary>Collision detection accuracy offset for floating-point tolerance</summary>
        public float Offset;

        [NativeDisableParallelForRestriction] public NativeReference<bool> Result;

        public void Execute(int index)
        {
            if (Result.Value) return;
            
            // Iterate through all KD-tree cells in object A
            CellData aCell = AKdCellData[index];
            int aStartIndex = aCell.rangeStart;
            int aEndIndex = aCell.rangeEnd;

            float aSizeX = (aCell.maxX - aCell.minX) * AVoxelSize;
            float aSizeY = (aCell.maxY - aCell.minY) * AVoxelSize;
            float aSizeZ = (aCell.maxZ - aCell.minZ) * AVoxelSize;

            float aMidX = (aCell.maxX + aCell.minX) * 0.5f;
            float aMidY = (aCell.maxY + aCell.minY) * 0.5f;
            float aMidZ = (aCell.maxZ + aCell.minZ) * 0.5f;

            float aPosX = AStartPosX + APreLocalRightX * aMidX + APreLocalUpX * aMidY + APreLocalForwardX * aMidZ;
            float aPosY = AStartPosY + APreLocalRightY * aMidX + APreLocalUpY * aMidY + APreLocalForwardY * aMidZ;
            float aPosZ = AStartPosZ + APreLocalRightZ * aMidX + APreLocalUpZ * aMidY + APreLocalForwardZ * aMidZ;

            for (int v = 0; v < BKdCellLength; v++)
            {
                CellData bCell = BKdCellData[v];
                int bStartIndex = bCell.rangeStart;
                int bEndIndex = bCell.rangeEnd;

                float bSizeX = (bCell.maxX - bCell.minX) * BVoxelSize;
                float bSizeY = (bCell.maxY - bCell.minY) * BVoxelSize;
                float bSizeZ = (bCell.maxZ - bCell.minZ) * BVoxelSize;

                float bMidX = (bCell.maxX + bCell.minX) * 0.5f;
                float bMidY = (bCell.maxY + bCell.minY) * 0.5f;
                float bMidZ = (bCell.maxZ + bCell.minZ) * 0.5f;

                float bPosX = BStartPosX + BPreLocalRightX * bMidX + BPreLocalUpX * bMidY + BPreLocalForwardX * bMidZ;
                float bPosY = BStartPosY + BPreLocalRightY * bMidX + BPreLocalUpY * bMidY + BPreLocalForwardY * bMidZ;
                float bPosZ = BStartPosZ + BPreLocalRightZ * bMidX + BPreLocalUpZ * bMidY + BPreLocalForwardZ * bMidZ;

                if (BoxCollisionUtil.CheckSATOverlapRaw
                    (
                        aPosX, aPosY, aPosZ,
                        ARotX, ARotY, ARotZ, ARotW,
                        aSizeX + Offset, aSizeY + Offset, aSizeZ + Offset,
                        bPosX, bPosY, bPosZ,
                        BRotX, BRotY, BRotZ, BRotW,
                        bSizeX + Offset, bSizeY + Offset, bSizeZ + Offset)
                   )
                {
                    for (int aIndex = aStartIndex; aIndex < aEndIndex; aIndex++)
                    {
                        SubBox aSub = AKdSubs[AKdOriginalIndices[aIndex]];
                        int aMinX = aSub.minX;
                        int aMinY = aSub.minY;
                        int aMinZ = aSub.minZ;
                        int aMaxX = aSub.maxX;
                        int aMaxY = aSub.maxY;
                        int aMaxZ = aSub.maxZ;

                        float aSubSizeX = (aMaxX - aMinX) * AVoxelSize;
                        float aSubSizeY = (aMaxY - aMinY) * AVoxelSize;
                        float aSubSizeZ = (aMaxZ - aMinZ) * AVoxelSize;

                        float aSubMidX = (aMaxX + aMinX) * 0.5f;
                        float aSubMidY = (aMaxY + aMinY) * 0.5f;
                        float aSubMidZ = (aMaxZ + aMinZ) * 0.5f;

                        float aSubPosX = AStartPosX + APreLocalRightX * aSubMidX + APreLocalUpX * aSubMidY + APreLocalForwardX * aSubMidZ;
                        float aSubPosY = AStartPosY + APreLocalRightY * aSubMidX + APreLocalUpY * aSubMidY + APreLocalForwardY * aSubMidZ;
                        float aSubPosZ = AStartPosZ + APreLocalRightZ * aSubMidX + APreLocalUpZ * aSubMidY + APreLocalForwardZ * aSubMidZ;

                        for (int bIndex = bStartIndex; bIndex < bEndIndex; bIndex++)
                        {
                            SubBox bSub = BKdSubs[BKdOriginalIndices[bIndex]];
                            int bMinX = bSub.minX;
                            int bMinY = bSub.minY;
                            int bMinZ = bSub.minZ;
                            int bMaxX = bSub.maxX;
                            int bMaxY = bSub.maxY;
                            int bMaxZ = bSub.maxZ;

                            float bSubSizeX = (bMaxX - bMinX) * BVoxelSize;
                            float bSubSizeY = (bMaxY - bMinY) * BVoxelSize;
                            float bSubSizeZ = (bMaxZ - bMinZ) * BVoxelSize;

                            float bSubMidX = (bMaxX + bMinX) * 0.5f;
                            float bSubMidY = (bMaxY + bMinY) * 0.5f;
                            float bSubMidZ = (bMaxZ + bMinZ) * 0.5f;

                            float bSubPosX = BStartPosX + BPreLocalRightX * bSubMidX + BPreLocalUpX * bSubMidY + BPreLocalForwardX * bSubMidZ;
                            float bSubPosY = BStartPosY + BPreLocalRightY * bSubMidX + BPreLocalUpY * bSubMidY + BPreLocalForwardY * bSubMidZ;
                            float bSubPosZ = BStartPosZ + BPreLocalRightZ * bSubMidX + BPreLocalUpZ * bSubMidY + BPreLocalForwardZ * bSubMidZ;

                            if (BoxCollisionUtil.CheckSATOverlapRaw
                                (
                                    aSubPosX, aSubPosY, aSubPosZ,
                                    ARotX, ARotY, ARotZ, ARotW,
                                    aSubSizeX + Offset, aSubSizeY + Offset, aSubSizeZ + Offset,
                                    bSubPosX, bSubPosY, bSubPosZ,
                                    BRotX, BRotY, BRotZ, BRotW,
                                    bSubSizeX + Offset, bSubSizeY + Offset, bSubSizeZ + Offset)
                               )
                            {
                                Result.Value = true;
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
}