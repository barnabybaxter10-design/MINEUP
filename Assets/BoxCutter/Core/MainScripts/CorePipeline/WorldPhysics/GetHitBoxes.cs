using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BoxCutter
{
    /// <summary>
    /// Burst-compiled parallel job that tests each cell in the spatial grid for intersection
    /// with a caller's bounds (sphere or oriented box).
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct CellIntersectionJob : IJobParallelFor
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                                Grid Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        [ReadOnly] public float3 GridMin;
        [ReadOnly] public float CellSizeX;
        [ReadOnly] public float CellSizeY;
        [ReadOnly] public float CellSizeZ;
        [ReadOnly] public int CellAmountPerX;
        [ReadOnly] public int CellAmountPerY;
        [ReadOnly] public int CellAmountPerZ;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                                  Caller Data
        //─────────────────────────────────────────────────────────────────────────────────────
        [ReadOnly] public float CallerPosX;
        [ReadOnly] public float CallerPosY;
        [ReadOnly] public float CallerPosZ;
        [ReadOnly] public float CallerRadius;
        [ReadOnly] public float CallerBoxSizeX;
        [ReadOnly] public float CallerBoxSizeY;
        [ReadOnly] public float CallerBoxSizeZ;
        [ReadOnly] public float CallerBoxQuatX;
        [ReadOnly] public float CallerBoxQuatY;
        [ReadOnly] public float CallerBoxQuatZ;
        [ReadOnly] public float CallerBoxQuatW;
        [ReadOnly] public bool IsBoxMode;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                                     Output
        //─────────────────────────────────────────────────────────────────────────────────────
        [WriteOnly] public NativeArray<byte> IntersectionResults;

        public void Execute(int index)
        {
            // Convert flat index to 3D cell coordinates
            int strideYZ = CellAmountPerY * CellAmountPerZ;
            int x = index / strideYZ;
            int remainder = index % strideYZ;
            int y = remainder / CellAmountPerZ;
            int z = remainder % CellAmountPerZ;

            float cellCenterX = GridMin.x + (x + 0.5f) * CellSizeX;
            float cellCenterY = GridMin.y + (y + 0.5f) * CellSizeY;
            float cellCenterZ = GridMin.z + (z + 0.5f) * CellSizeZ;

            bool intersects = IsBoxMode
                ? BoxCollisionUtil.CheckSATOverlapRawBIdentity(
                    CallerPosX, CallerPosY, CallerPosZ, CallerBoxQuatX, CallerBoxQuatY, CallerBoxQuatZ, CallerBoxQuatW, CallerBoxSizeX, CallerBoxSizeY, CallerBoxSizeZ,
                    cellCenterX, cellCenterY, cellCenterZ, CellSizeX, CellSizeY, CellSizeZ)
                : BoxCollisionUtil.IsSphereIntersectingCuboidRawIdentity(CallerPosX, CallerPosY, CallerPosZ, CallerRadius, cellCenterX, cellCenterY, cellCenterZ, CellSizeX, CellSizeY, CellSizeZ);

            IntersectionResults[index] = intersects ? (byte)1 : (byte)0;
        }
    }

    [BurstCompile(DisableSafetyChecks = true)]
    public struct PreciseHitCheck : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> AllSubs;
        [ReadOnly] public NativeArray<int> SubBoxPointerToBox;
        [ReadOnly] public NativeArray<DestructionPipeline.BoxTrans> BoxData;
        [NativeDisableParallelForRestriction] public NativeArray<byte> BoxHit;

        [ReadOnly] public float HitPosX, HitPosY, HitPosZ, Radius;
        [ReadOnly] public bool BoxMode;
        [ReadOnly] public float BoxRotX, BoxRotY, BoxRotZ, BoxRotW;
        [ReadOnly] public float BoxSizeX, BoxSizeY, BoxSizeZ;

        public void Execute(int index)
        {
            int pointerToBox = SubBoxPointerToBox[index];
            if (BoxHit[pointerToBox] == 1) return;

            DestructionPipeline.BoxTrans boxTrans = BoxData[pointerToBox];
            SubBox subBox = AllSubs[index];

            float voxelSize = boxTrans.voxelSize;

            // Calculate SubBox geometry
            float subSizeX = (subBox.maxX - subBox.minX) * voxelSize;
            float subSizeY = (subBox.maxY - subBox.minY) * voxelSize;
            float subSizeZ = (subBox.maxZ - subBox.minZ) * voxelSize;

            float subMidX = (subBox.maxX + subBox.minX) * 0.5f;
            float subMidY = (subBox.maxY + subBox.minY) * 0.5f;
            float subMidZ = (subBox.maxZ + subBox.minZ) * 0.5f;

            // Transform to World Space
            float subPosX = boxTrans.startPosX + boxTrans.vRightX * subMidX + boxTrans.vUpX * subMidY + boxTrans.vFwdX * subMidZ;
            float subPosY = boxTrans.startPosY + boxTrans.vRightY * subMidX + boxTrans.vUpY * subMidY + boxTrans.vFwdY * subMidZ;
            float subPosZ = boxTrans.startPosZ + boxTrans.vRightZ * subMidX + boxTrans.vUpZ * subMidY + boxTrans.vFwdZ * subMidZ;

            bool isHit = BoxMode
                ? BoxCollisionUtil.CheckSATOverlapRaw(
                    HitPosX, HitPosY, HitPosZ, BoxRotX, BoxRotY, BoxRotZ, BoxRotW, BoxSizeX, BoxSizeY, BoxSizeZ,
                    subPosX, subPosY, subPosZ, boxTrans.rotX, boxTrans.rotY, boxTrans.rotZ, boxTrans.rotW, subSizeX, subSizeY, subSizeZ)
                : BoxCollisionUtil.IsSphereIntersectingCuboidRaw(
                    HitPosX, HitPosY, HitPosZ, Radius,
                    subPosX, subPosY, subPosZ, boxTrans.rotX, boxTrans.rotY, boxTrans.rotZ, boxTrans.rotW, subSizeX, subSizeY, subSizeZ);

            if (isHit) BoxHit[pointerToBox] = 1;
        }
    }

    [BurstCompile(DisableSafetyChecks = true)]
    public struct PreciseSingleHitCheck : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> AllSubs;
        [ReadOnly] public DestructionPipeline.BoxTrans BoxTrans;
        [NativeDisableParallelForRestriction] public NativeReference<bool> BoxHit;

        [ReadOnly] public float HitPosX, HitPosY, HitPosZ, Radius;
        [ReadOnly] public bool BoxMode;
        [ReadOnly] public float BoxRotX, BoxRotY, BoxRotZ, BoxRotW;
        [ReadOnly] public float BoxSizeX, BoxSizeY, BoxSizeZ;

        public void Execute(int index)
        {
            if (BoxHit.Value) return;
            SubBox subBox = AllSubs[index];

            float voxelSize = BoxTrans.voxelSize;

            // Calculate SubBox geometry
            float subSizeX = (subBox.maxX - subBox.minX) * voxelSize;
            float subSizeY = (subBox.maxY - subBox.minY) * voxelSize;
            float subSizeZ = (subBox.maxZ - subBox.minZ) * voxelSize;

            float subMidX = (subBox.maxX + subBox.minX) * 0.5f;
            float subMidY = (subBox.maxY + subBox.minY) * 0.5f;
            float subMidZ = (subBox.maxZ + subBox.minZ) * 0.5f;

            // Transform to World Space
            float subPosX = BoxTrans.startPosX + BoxTrans.vRightX * subMidX + BoxTrans.vUpX * subMidY + BoxTrans.vFwdX * subMidZ;
            float subPosY = BoxTrans.startPosY + BoxTrans.vRightY * subMidX + BoxTrans.vUpY * subMidY + BoxTrans.vFwdY * subMidZ;
            float subPosZ = BoxTrans.startPosZ + BoxTrans.vRightZ * subMidX + BoxTrans.vUpZ * subMidY + BoxTrans.vFwdZ * subMidZ;

            bool isHit = BoxMode
                ? BoxCollisionUtil.CheckSATOverlapRaw(
                    HitPosX, HitPosY, HitPosZ, BoxRotX, BoxRotY, BoxRotZ, BoxRotW, BoxSizeX, BoxSizeY, BoxSizeZ,
                    subPosX, subPosY, subPosZ, BoxTrans.rotX, BoxTrans.rotY, BoxTrans.rotZ, BoxTrans.rotW, subSizeX, subSizeY, subSizeZ)
                : BoxCollisionUtil.IsSphereIntersectingCuboidRaw(
                    HitPosX, HitPosY, HitPosZ, Radius,
                    subPosX, subPosY, subPosZ, BoxTrans.rotX, BoxTrans.rotY, BoxTrans.rotZ, BoxTrans.rotW, subSizeX, subSizeY, subSizeZ);

            if (isHit) BoxHit.Value = true;
        }
    }
}