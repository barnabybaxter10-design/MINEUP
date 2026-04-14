using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BoxCutter
{
    /// <summary>
    /// Fast parallel job for clearing buffer arrays with a specified value.
    /// Optimized with Burst compilation for high-performance memory operations.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, DisableSafetyChecks = true)]
    public struct ClearBufferJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<byte> Buffer;
        public byte Value;

        public void Execute(int i)
        {
            Buffer[i] = Value;
        }
    }

    /// <summary>
    /// Builds voxel grid indices from SubBox data for spatial partitioning.
    /// Transforms 3D SubBox regions into linear grid indices for efficient lookup.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct BuildGridIndexes : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> SubArr;
        [ReadOnly] public NativeArray<int> BoxIndexArr;

        [ReadOnly] public NativeArray<int> GridOffsetPerBox;
        [ReadOnly] public NativeArray<int3> GlobalSizePerBox;
        [ReadOnly] public NativeArray<int3> HitMinPerBox;

        [NativeDisableParallelForRestriction] public NativeArray<byte> Valid;

        /// <summary>
        /// Processes a single SubBox to populate the voxel grid with material indices.
        /// Converts 3D bounding box coordinates to linear grid positions efficiently.
        /// </summary>
        /// <param name="index">Index of the SubBox to process</param>
        public void Execute(int index)
        {
            int boxIdx = BoxIndexArr[index];
            int gridOffset = GridOffsetPerBox[boxIdx];
            int3 gSize = GlobalSizePerBox[boxIdx];
            int3 hitMin = HitMinPerBox[boxIdx];

            // Calculate strides for 3D to 1D index conversion
            int strideY = gSize.z;
            int strideX = gSize.y * strideY;

            var s = SubArr[index];
            // Convert to relative coordinates within the grid
            int relMinX = s.minX - hitMin.x;
            int relMinY = s.minY - hitMin.y;
            int relMinZ = s.minZ - hitMin.z;
            int relMaxX = s.maxX - hitMin.x;
            int relMaxY = s.maxY - hitMin.y;
            int relMaxZ = s.maxZ - hitMin.z;

            // Calculate dimensions of the SubBox region
            int sizeX = relMaxX - relMinX;
            int sizeY = relMaxY - relMinY;
            int sizeZ = relMaxZ - relMinZ;

            // Calculate base pointer for this SubBox region
            int basePtr = gridOffset + relMinX * strideX + relMinY * strideY + relMinZ;
            byte colour = s.magicaIndex;

            // Fill the 3D region with the material index
            for (int x = 0, ptrX = basePtr; x < sizeX; ++x, ptrX += strideX)
            {
                int ptrXY = ptrX;
                for (int y = 0; y < sizeY; ++y, ptrXY += strideY)
                {
                    int dst = ptrXY;
                    int end = dst + sizeZ;
                    for (; dst < end; ++dst)
                    {
                        Valid[dst] = colour;
                    }
                }
            }
        }
    }
}