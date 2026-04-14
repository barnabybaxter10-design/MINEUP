using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static BoxCutter.BoxObj;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    /// <summary>
    /// Job for precise non-convex collision detection between two BoxObj instances.
    /// Uses KD-tree spatial acceleration and SubBox-level testing for maximum accuracy.
    /// </summary>
    [BurstCompile]
    public struct BoxIntersectWithManyBoxesJob : IJobParallelFor
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Spatial Acceleration Data
        //─────────────────────────────────────────────────────────────────────────────────────
        [ReadOnly] public NativeArray<SubBox> ASubs;
        [ReadOnly] public NativeArray<CellData> ACellData;
        [ReadOnly] public NativeArray<int> AOriginalIndices;
        
        [ReadOnly] public NativeArray<SubBox> BSubs;
        [ReadOnly] public NativeArray<CellData> BCellData;
        [ReadOnly] public NativeArray<int> BOriginalIndices;
        [ReadOnly] public NativeArray<int> BCellToBoxPointer;
        
        [ReadOnly] public NativeArray<BoxTrans> BBoxTrans;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Transform Data
        //─────────────────────────────────────────────────────────────────────────────────────
        public float AVoxelSize;
        public float ARotX, ARotY, ARotZ, ARotW;
        public float AStartPosX, AStartPosY, AStartPosZ;
        public float APreLocalRightX, APreLocalRightY, APreLocalRightZ;
        public float APreLocalUpX, APreLocalUpY, APreLocalUpZ;
        public float APreLocalForwardX, APreLocalForwardY, APreLocalForwardZ;

        public float Offset;

        [NativeDisableParallelForRestriction] public NativeArray<byte> Result;

        public void Execute(int index)
        {
            //─────────────────────────────────────────────────────────────────────────────────────
            int boxIdx = BCellToBoxPointer[index];
            if (Result[boxIdx] == 1) return;
            //─────────────────────────────────────────────────────────────────────────────────────
            // Iterate through all KD-tree cells in object A
            CellData bCell = BCellData[index];
            //─────────────────────────────────────────────────────────────────────────────────────
            BoxTrans bBoxTrans = BBoxTrans[boxIdx];
            
            float bStartPosX = bBoxTrans.startPosX;
            float bStartPosY = bBoxTrans.startPosY;
            float bStartPosZ = bBoxTrans.startPosZ;
            float bVRightX = bBoxTrans.vRightX;
            float bVRightY = bBoxTrans.vRightY;
            float bVRightZ = bBoxTrans.vRightZ;
            float bVUpX = bBoxTrans.vUpX;
            float bVUpY = bBoxTrans.vUpY;
            float bVUpZ = bBoxTrans.vUpZ;
            float bVFwdX = bBoxTrans.vFwdX;
            float bVFwdY = bBoxTrans.vFwdY;
            float bVFwdZ = bBoxTrans.vFwdZ;

            float bVoxelSize = bBoxTrans.voxelSize;
            float bRotX = bBoxTrans.rotX;
            float bRotY = bBoxTrans.rotY;
            float bRotZ = bBoxTrans.rotZ;
            float bRotW = bBoxTrans.rotW;
            //─────────────────────────────────────────────────────────────────────────────────────
            int bStartIndex = bCell.rangeStart;
            int bEndIndex = bCell.rangeEnd;

            float bSizeX = (bCell.maxX - bCell.minX) * bVoxelSize;
            float bSizeY = (bCell.maxY - bCell.minY) * bVoxelSize;
            float bSizeZ = (bCell.maxZ - bCell.minZ) * bVoxelSize;

            float bMidX = (bCell.maxX + bCell.minX) * 0.5f;
            float bMidY = (bCell.maxY + bCell.minY) * 0.5f;
            float bMidZ = (bCell.maxZ + bCell.minZ) * 0.5f;

            float bPosX = bStartPosX + bVRightX * bMidX + bVUpX * bMidY + bVFwdX * bMidZ;
            float bPosY = bStartPosY + bVRightY * bMidX + bVUpY * bMidY + bVFwdY * bMidZ;
            float bPosZ = bStartPosZ + bVRightZ * bMidX + bVUpZ * bMidY + bVFwdZ * bMidZ;

            int aCellLength = ACellData.Length;

            for (int v = 0; v < aCellLength; v++)
            {
                //─────────────────────────────────────────────────────────────────────────────────
                CellData aCell = ACellData[v];
                
                int aStartIndex = aCell.rangeStart;
                
                int aEndIndex = aCell.rangeEnd;
                float aCellSizeX = (aCell.maxX - aCell.minX) * AVoxelSize;
                float aCellSizeY = (aCell.maxY - aCell.minY) * AVoxelSize;
                float aCellSizeZ = (aCell.maxZ - aCell.minZ) * AVoxelSize;
                float aMidX = (aCell.maxX + aCell.minX) * 0.5f;
                float aMidY = (aCell.maxY + aCell.minY) * 0.5f;
                float aMidZ = (aCell.maxZ + aCell.minZ) * 0.5f;
                float aCellPosX = AStartPosX + APreLocalRightX * aMidX + APreLocalUpX * aMidY + APreLocalForwardX * aMidZ;
                float aCellPosY = AStartPosY + APreLocalRightY * aMidX + APreLocalUpY * aMidY + APreLocalForwardY * aMidZ;
                float aCellPosZ = AStartPosZ + APreLocalRightZ * aMidX + APreLocalUpZ * aMidY + APreLocalForwardZ * aMidZ;
                
                if (BoxCollisionUtil.CheckSATOverlapRaw
                    (
                        aCellPosX, aCellPosY, aCellPosZ,
                        ARotX, ARotY, ARotZ, ARotW,
                        aCellSizeX + Offset, aCellSizeY + Offset, aCellSizeZ + Offset,
                        bPosX, bPosY, bPosZ,
                        bRotX, bRotY, bRotZ, bRotW,
                        bSizeX + Offset, bSizeY + Offset, bSizeZ + Offset)
                   )
                {
                    for (int aIndex = aStartIndex; aIndex < aEndIndex; aIndex++)
                    {
                        SubBox aSub = ASubs[AOriginalIndices[aIndex]];
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
                            SubBox bSub = BSubs[BOriginalIndices[bIndex]];
                            int bMinX = bSub.minX;
                            int bMinY = bSub.minY;
                            int bMinZ = bSub.minZ;
                            int bMaxX = bSub.maxX;
                            int bMaxY = bSub.maxY;
                            int bMaxZ = bSub.maxZ;

                            float bSubSizeX = (bMaxX - bMinX) * bVoxelSize;
                            float bSubSizeY = (bMaxY - bMinY) * bVoxelSize;
                            float bSubSizeZ = (bMaxZ - bMinZ) * bVoxelSize;

                            float bSubMidX = (bMaxX + bMinX) * 0.5f;
                            float bSubMidY = (bMaxY + bMinY) * 0.5f;
                            float bSubMidZ = (bMaxZ + bMinZ) * 0.5f;

                            float bSubPosX = bStartPosX + bVRightX * bSubMidX + bVUpX * bSubMidY + bVFwdX * bSubMidZ;
                            float bSubPosY = bStartPosY + bVRightY * bSubMidX + bVUpY * bSubMidY + bVFwdY * bSubMidZ;
                            float bSubPosZ = bStartPosZ + bVRightZ * bSubMidX + bVUpZ * bSubMidY + bVFwdZ * bSubMidZ;

                            if (BoxCollisionUtil.CheckSATOverlapRaw
                                (
                                    aSubPosX, aSubPosY, aSubPosZ,
                                    ARotX, ARotY, ARotZ, ARotW,
                                    aSubSizeX + Offset, aSubSizeY + Offset, aSubSizeZ + Offset,
                                    bSubPosX, bSubPosY, bSubPosZ,
                                    bRotX, bRotY, bRotZ, bRotW,
                                    bSubSizeX + Offset, bSubSizeY + Offset, bSubSizeZ + Offset)
                               )
                            {
                                Result[boxIdx] = 1;
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
}