using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BoxCutter
{
    [BurstCompile(DisableSafetyChecks = true)]
    public struct EdgeGathering : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> subObjArr;
        [ReadOnly] public NativeArray<int> kdOriginalIndices;
        [ReadOnly] public NativeArray<BoxObj.CellData> kdCells;
        [ReadOnly] public NativeArray<int> kdCellOwner;
        [ReadOnly] public NativeArray<bool> filterDiagonalNa;

        public NativeStream.Writer streamWriter;

        public void Execute(int cellIndex)
        {
            streamWriter.BeginForEachIndex(cellIndex);

            int owner = kdCellOwner[cellIndex];
            bool filterDiagonal = filterDiagonalNa[owner];
            var cell = kdCells[cellIndex];
            int startA = cell.rangeStart, endA = cell.rangeEnd;

            for (int idxA = startA; idxA < endA; ++idxA)
            {
                int originalIndexA = kdOriginalIndices[idxA];
                var a = subObjArr[originalIndexA];

                int aMinX = a.minX, aMinY = a.minY, aMinZ = a.minZ;
                int aMaxX = a.maxX, aMaxY = a.maxY, aMaxZ = a.maxZ;

                for (int idxB = idxA + 1; idxB < endA; ++idxB)
                {
                    int originalIndexB = kdOriginalIndices[idxB];
                    if (originalIndexA == originalIndexB)
                        continue;

                    var b = subObjArr[originalIndexB];

                    if (AabbPass(aMinX, aMinY, aMinZ, aMaxX, aMaxY, aMaxZ,
                            b.minX, b.minY, b.minZ, b.maxX, b.maxY, b.maxZ,
                            filterDiagonal))
                    {
                        streamWriter.Write(new int2(originalIndexA, originalIndexB));
                    }
                }
            }

            streamWriter.EndForEachIndex();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AabbPass
        (
            int aMinX, int aMinY, int aMinZ, int aMaxX, int aMaxY, int aMaxZ,
            int bMinX, int bMinY, int bMinZ, int bMaxX, int bMaxY, int bMaxZ,
            bool filterDiag
        )
        {
            // Fast rejection
            if (aMinX > bMaxX | bMinX > aMaxX) return false;
            if (aMinY > bMaxY | bMinY > aMaxY) return false;
            if (aMinZ > bMaxZ | bMinZ > aMaxZ) return false;

            if (!filterDiag) return true;

            // Branchless diagonal count
            int adjacentCount = 
                math.select(0, 1, (aMaxX == bMinX) | (bMaxX == aMinX)) +
                math.select(0, 1, (aMaxY == bMinY) | (bMaxY == aMinY)) +
                math.select(0, 1, (aMaxZ == bMinZ) | (bMaxZ == aMinZ));
    
            return adjacentCount <= 1;
        }
    }

    [BurstCompile(DisableSafetyChecks = true)]
    public struct UnionSetIsland : IJob
    {
        public NativeStream.Reader streamReader;
        public NativeArray<int> parent;
        public NativeArray<int> rank;
        public int foreachCount;

        public void Execute()
        {
            int count = parent.Length;

            for (int i = 0; i < count; ++i)
            {
                parent[i] = i;
                rank[i] = 0;
            }

            // Read directly from stream - no intermediate array needed
            for (int i = 0; i < foreachCount; i++)
            {
                int edgeCount = streamReader.BeginForEachIndex(i);
                for (int j = 0; j < edgeCount; j++)
                {
                    int2 edge = streamReader.Read<int2>();
                    Union(edge.x, edge.y);
                }

                streamReader.EndForEachIndex();
            }

            for (int i = 0; i < count; ++i)
                parent[i] = Find(i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return;

            if (rank[ra] < rank[rb]) parent[ra] = rb;
            else if (rank[ra] > rank[rb]) parent[rb] = ra;
            else
            {
                parent[rb] = ra;
                rank[ra]++;
            }
        }
    }
}