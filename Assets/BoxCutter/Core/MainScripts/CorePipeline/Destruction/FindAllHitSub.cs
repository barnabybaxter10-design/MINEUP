using BoxCutter;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace BoxCutter
{
    [BurstCompile(DisableSafetyChecks = true)]
    public struct FindAllHitSub : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> AllSubs;
        [ReadOnly] public NativeArray<int> SubBoxPointerToBox;
        [WriteOnly] public NativeList<int>.ParallelWriter HitIndices;
        [ReadOnly] public NativeArray<DestructionPipeline.BoxTrans> BoxData;

        [ReadOnly] public float HitPosX, HitPosY, HitPosZ, Radius;
        [ReadOnly] public bool BoxMode;
        [ReadOnly] public float BoxRotX, BoxRotY, BoxRotZ, BoxRotW;
        [ReadOnly] public float BoxSizeX, BoxSizeY, BoxSizeZ;

        public void Execute(int index)
        {
            int pointerToBox = SubBoxPointerToBox[index];
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

            if (isHit)
            {
                HitIndices.AddNoResize(index);
            }
        }
    }
}