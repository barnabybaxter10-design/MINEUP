using BoxCutter;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BoxCutter
{
    [BurstCompile(DisableSafetyChecks = true)]
    public struct CheckConnectivity : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> IslandSubs;
        [ReadOnly] public NativeArray<int2> IslandGroups;
        [ReadOnly] public NativeArray<int> GroupToBoxPointer;

        [ReadOnly] public NativeArray<SubBox> StaticSubs;
        [ReadOnly] public NativeArray<int> StaticStartsPerBox;
        [ReadOnly] public NativeArray<int> StaticCountsPerBox;

        // Spatial grid data for accelerated queries
        [ReadOnly] public NativeArray<int> StaticKdIndices;
        [ReadOnly] public NativeArray<BoxObj.CellData> StaticKdCells;
        [ReadOnly] public NativeArray<int> StaticKdCellOwner;

        [WriteOnly] public NativeArray<int> ConnectedFlags;
        public int IntInfinity;

        public void Execute(int groupIndex)
        {
            int2 groupRange = IslandGroups[groupIndex];
            int startIndex = groupRange.x;
            int endIndex = groupRange.y;

            if (startIndex > endIndex)
            {
                ConnectedFlags[groupIndex] = 0;
                return;
            }

            int boxIndex = GroupToBoxPointer[groupIndex];
            int staticStart = StaticStartsPerBox[boxIndex];
            int staticCount = StaticCountsPerBox[boxIndex];

            // If no static geometry in this box, it's disconnected by default
            if (staticCount == 0)
            {
                ConnectedFlags[groupIndex] = 0;
                return;
            }

            bool isConnected = false;

            // Use spatial grid to find candidate static SubBoxes for each island SubBox
            // This avoids checking all static SubBoxes and only checks nearby ones
            for (int s_idx = startIndex; s_idx <= endIndex && !isConnected; s_idx++)
            {
                SubBox islandSub = IslandSubs[s_idx];

                // Query grid cells that overlap this island SubBox
                for (int cellIdx = 0; cellIdx < StaticKdCells.Length && !isConnected; cellIdx++)
                {
                    // Skip cells from other boxes
                    if (StaticKdCellOwner[cellIdx] != boxIndex)
                        continue;

                    BoxObj.CellData cell = StaticKdCells[cellIdx];

                    // Check if this grid cell overlaps with the island SubBox (with adjacency)
                    bool ovX = islandSub.maxX > cell.minX && islandSub.minX < cell.maxX;
                    bool ovY = islandSub.maxY > cell.minY && islandSub.minY < cell.maxY;
                    bool ovZ = islandSub.maxZ > cell.minZ && islandSub.minZ < cell.maxZ;
                    bool adX = (islandSub.maxX == cell.minX || islandSub.minX == cell.maxX);
                    bool adY = (islandSub.maxY == cell.minY || islandSub.minY == cell.maxY);
                    bool adZ = (islandSub.maxZ == cell.minZ || islandSub.minZ == cell.maxZ);

                    if (!(ovX || adX) || !(ovY || adY) || !(ovZ || adZ))
                        continue;

                    // This cell overlaps, check all static SubBoxes in this cell
                    int rangeStart = cell.rangeStart;
                    int rangeEnd = cell.rangeEnd;

                    for (int i = rangeStart; i < rangeEnd; i++)
                    {
                        int staticSubIdx = StaticKdIndices[i];
                        SubBox staticSub = StaticSubs[staticSubIdx];

                        // Precise connectivity check
                        bool oX = islandSub.maxX > staticSub.minX && islandSub.minX < staticSub.maxX;
                        bool oY = islandSub.maxY > staticSub.minY && islandSub.minY < staticSub.maxY;
                        bool oZ = islandSub.maxZ > staticSub.minZ && islandSub.minZ < staticSub.maxZ;

                        bool aX = (islandSub.maxX == staticSub.minX || islandSub.minX == staticSub.maxX);
                        bool aY = (islandSub.maxY == staticSub.minY || islandSub.minY == staticSub.maxY);
                        bool aZ = (islandSub.maxZ == staticSub.minZ || islandSub.minZ == staticSub.maxZ);

                        // Face connection check: Touching on one axis and overlapping on the others
                        bool faceConnected = (aX && oY && oZ) || (oX && aY && oZ) || (oX && oY && aZ);
                        bool fullyInside = oX && oY && oZ;

                        if (faceConnected || fullyInside)
                        {
                            isConnected = true;
                            break;
                        }
                    }
                }
            }

            ConnectedFlags[groupIndex] = isConnected ? 1 : 0;
        }
    }
}