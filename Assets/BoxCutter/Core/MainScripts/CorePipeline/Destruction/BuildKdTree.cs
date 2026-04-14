using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BoxCutter
{
    [BurstCompile(DisableSafetyChecks = true)]
    public struct BuildKdTree : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SubBox> AllSubs;
        [ReadOnly] public NativeArray<int> BoxSubStart;
        [ReadOnly] public NativeArray<int> BoxSubCount;
        [ReadOnly] public NativeArray<int> LeafBase;
        [ReadOnly] public NativeArray<int> CellBase;

        public int MaxPerLeaf;

        [ReadOnly] public NativeArray<float> FillThresholdNa;

        [NativeDisableParallelForRestriction] public NativeArray<int> LeafIndicesFlat;
        [NativeDisableParallelForRestriction] public NativeArray<BoxObj.CellData> CellSubsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<int2> LeafCellCountPerBox;

        struct WorkingSub
        {
            public int originalIndex;
            public SubBox sub;
        }

        struct WorkItem
        {
            public int3 Min, Max;
            public int Start, Count;
        }

        const int INT_MAX = int.MaxValue;
        const int INT_MIN = int.MinValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long VolumeInclusive(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
            long dx = (long)maxX - minX + 1;
            long dy = (long)maxY - minY + 1;
            long dz = (long)maxZ - minZ + 1;
            return dx * dy * dz;
        }

        static void ComputeTightBoundsAndVolumes(
            NativeArray<WorkingSub> workingSubs,
            int start, int count,
            out int minX, out int minY, out int minZ,
            out int maxX, out int maxY, out int maxZ,
            out float ratio)
        {
            minX = INT_MAX;
            minY = INT_MAX;
            minZ = INT_MAX;
            maxX = INT_MIN;
            maxY = INT_MIN;
            maxZ = INT_MIN;

            long subsVolume = 0;
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                var sb = workingSubs[i].sub;

                if (sb.minX < minX) minX = sb.minX;
                if (sb.minY < minY) minY = sb.minY;
                if (sb.minZ < minZ) minZ = sb.minZ;
                if (sb.maxX > maxX) maxX = sb.maxX;
                if (sb.maxY > maxY) maxY = sb.maxY;
                if (sb.maxZ > maxZ) maxZ = sb.maxZ;

                subsVolume += VolumeInclusive(sb.minX, sb.minY, sb.minZ, sb.maxX, sb.maxY, sb.maxZ);
            }

            long boundVolume = VolumeInclusive(minX, minY, minZ, maxX, maxY, maxZ);
            ratio = (boundVolume > 0) ? (float)((double)subsVolume / (double)boundVolume) : 1f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Overlap1D(int aMin, int aMax, int bMin, int bMax) => (aMax >= bMin) && (bMax >= aMin);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Touch1D(int aMin, int aMax, int bMin, int bMax) => (aMax + 1 >= bMin) && (bMax + 1 >= aMin);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool BoxesTouch(in SubBox a, in SubBox b)
        {
            bool ox = Overlap1D(a.minX, a.maxX, b.minX, b.maxX);
            bool oy = Overlap1D(a.minY, a.maxY, b.minY, b.maxY);
            bool oz = Overlap1D(a.minZ, a.maxZ, b.minZ, b.maxZ);

            if (ox && oy && oz) return true;

            bool tx = Touch1D(a.minX, a.maxX, b.minX, b.maxX);
            bool ty = Touch1D(a.minY, a.maxY, b.minY, b.maxY);
            bool tz = Touch1D(a.minZ, a.maxZ, b.minZ, b.maxZ);

            if (!ox && tx && oy && oz) return true;
            if (!oy && ty && ox && oz) return true;
            if (!oz && tz && ox && oy) return true;
            return false;
        }

        static bool IsSingleConnectedCluster(
            NativeArray<WorkingSub> workingSubs,
            int start, int count)
        {
            if (count <= 1) return true;

            var visited = new NativeArray<byte>(count, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var queue = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            int qh = 0, qt = 0, seen = 0;

            visited[0] = 1;
            queue[qt++] = 0;
            seen = 1;

            while (qh < qt)
            {
                int iLoc = queue[qh++];
                var a = workingSubs[start + iLoc].sub;

                for (int jLoc = 0; jLoc < count; ++jLoc)
                {
                    if (visited[jLoc] != 0) continue;

                    var b = workingSubs[start + jLoc].sub;
                    if (BoxesTouch(a, b))
                    {
                        visited[jLoc] = 1;
                        queue[qt++] = jLoc;
                        if (++seen == count)
                        {
                            queue.Dispose();
                            visited.Dispose();
                            return true;
                        }
                    }
                }
            }

            queue.Dispose();
            visited.Dispose();
            return false;
        }

        public void Execute(int boxIdx)
        {
            var allSubsLocal = AllSubs;
            var leafIndicesFlatLocal = LeafIndicesFlat;
            var cellSubsFlatLocal = CellSubsFlat;

            int subStart = BoxSubStart[boxIdx];
            int subCount = BoxSubCount[boxIdx];
            if (subCount == 0)
            {
                LeafCellCountPerBox[boxIdx] = default;
                return;
            }

            int leafBase = LeafBase[boxIdx];
            int leafWrite = leafBase;
            int cellBase = CellBase[boxIdx];
            int cellWrite = cellBase;

            float fillThreshold = FillThresholdNa[boxIdx];

            // Copy once upfront - eliminates all double-indexing in hot loops
            var workingSubs = new NativeArray<WorkingSub>(subCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < subCount; ++i)
            {
                workingSubs[i] = new WorkingSub
                {
                    originalIndex = i,
                    sub = allSubsLocal[subStart + i]
                };
            }

            var stack = new NativeArray<WorkItem>(128, Allocator.Temp, NativeArrayOptions.ClearMemory);
            int sp = 0;

            var first = workingSubs[0].sub;
            int rootMinX = first.minX, rootMinY = first.minY, rootMinZ = first.minZ;
            int rootMaxX = first.maxX, rootMaxY = first.maxY, rootMaxZ = first.maxZ;
            for (int i = 1; i < subCount; ++i)
            {
                var sb = workingSubs[i].sub;
                rootMinX = math.min(rootMinX, sb.minX);
                rootMinY = math.min(rootMinY, sb.minY);
                rootMinZ = math.min(rootMinZ, sb.minZ);
                rootMaxX = math.max(rootMaxX, sb.maxX);
                rootMaxY = math.max(rootMaxY, sb.maxY);
                rootMaxZ = math.max(rootMaxZ, sb.maxZ);
            }

            stack[sp++] = new WorkItem
            {
                Min = new int3(rootMinX, rootMinY, rootMinZ),
                Max = new int3(rootMaxX, rootMaxY, rootMaxZ),
                Start = 0,
                Count = subCount
            };

            while (sp > 0)
            {
                var wi = stack[--sp];
                int start = wi.Start;
                int count = wi.Count;

                // Degenerate quick-outs
                if (count <= 1)
                {
                    WriteLeaf(wi);
                    continue;
                }

                bool minEqMax = math.all(wi.Min == wi.Max);
                // ─────────────────────────────────────────────────────────────
                // Fill ratio heuristic: sum of sub volumes / true tight bounds
                bool forceSplit = false;
                if (!minEqMax && fillThreshold > 0f)
                {
                    ComputeTightBoundsAndVolumes(
                        workingSubs, start, count,
                        out int tMinX, out int tMinY, out int tMinZ, out int tMaxX, out int tMaxY, out int tMaxZ,
                        out float fillRatio);

                    // If we're small and filled, stop, otherwise possibly force split.
                    if (count <= MaxPerLeaf && fillRatio >= fillThreshold)
                    {
                        if (IsSingleConnectedCluster(workingSubs, start, count))
                        {
                            WriteLeaf(wi);
                            continue;
                        }

                        forceSplit = true; // fall through to split path
                    }
                    else
                    {
                        // If sparsely filled, try to split even if under MaxPerLeaf
                        forceSplit = forceSplit || (fillRatio < fillThreshold);
                    }
                }

                if (!forceSplit && (count <= MaxPerLeaf || minEqMax))
                {
                    if (IsSingleConnectedCluster(workingSubs, start, count))
                    {
                        WriteLeaf(wi);
                        continue;
                    }
                    else
                    {
                        forceSplit = true;
                    }
                }

                // ─────────────────────────────────────────────────────────────
                // Partition
                float3 mean = 0, m2 = 0;
                int end = start + count;
                for (int i = start; i < end; ++i)
                {
                    var sb = workingSubs[i].sub;
                    float3 c = 0.5f * new float3(sb.minX + sb.maxX, sb.minY + sb.maxY, sb.minZ + sb.maxZ);
                    float3 d = c - mean;
                    mean += d / (i - start + 1);
                    m2 += d * (c - mean);
                }

                float3 var = m2 / count;

                int axis = (var.x >= var.y && var.x >= var.z) ? 0 :
                    (var.y >= var.z) ? 1 : 2;

                float pivot = mean[axis];

                int L = start, R = start + count - 1;
                while (L <= R)
                {
                    var sbL = workingSubs[L].sub;
                    float cL = (axis == 0 ? sbL.minX + sbL.maxX :
                        axis == 1 ? sbL.minY + sbL.maxY :
                        sbL.minZ + sbL.maxZ) * 0.5f;

                    if (cL <= pivot) ++L;
                    else
                    {
                        var tmp = workingSubs[L];
                        workingSubs[L] = workingSubs[R];
                        workingSubs[R--] = tmp;
                    }
                }

                int leftCnt = L - start;
                int rightCnt = count - leftCnt;

                if (leftCnt == 0 || rightCnt == 0)
                {
                    // Degenerate split. If it is already a connected cluster, accept, else do a fallback split.
                    if (IsSingleConnectedCluster(workingSubs, start, count))
                    {
                        WriteLeaf(wi);
                        continue;
                    }

                    // Fallback: split the range in half along the longest box extent to keep searching.
                    int fallbackLeftCnt = math.max(1, count >> 1);
                    int fallbackRightCnt = count - fallbackLeftCnt;

                    // Choose axis by longest extent of current WI bounds.
                    int3 extent = wi.Max - wi.Min;
                    int axis2 = (extent.x >= extent.y && extent.x >= extent.z) ? 0 :
                        (extent.y >= extent.z) ? 1 : 2;

                    int3 leftMax = wi.Max;
                    int3 rightMin = wi.Min;
                    int pivotInt;
                    switch (axis2)
                    {
                        case 0:
                            pivotInt = (wi.Min.x + wi.Max.x) >> 1;
                            leftMax.x = pivotInt;
                            rightMin.x = pivotInt;
                            break;
                        case 1:
                            pivotInt = (wi.Min.y + wi.Max.y) >> 1;
                            leftMax.y = pivotInt;
                            rightMin.y = pivotInt;
                            break;
                        default:
                            pivotInt = (wi.Min.z + wi.Max.z) >> 1;
                            leftMax.z = pivotInt;
                            rightMin.z = pivotInt;
                            break;
                    }

                    stack[sp++] = new WorkItem { Min = wi.Min, Max = leftMax, Start = start, Count = fallbackLeftCnt };
                    stack[sp++] = new WorkItem { Min = rightMin, Max = wi.Max, Start = start + fallbackLeftCnt, Count = fallbackRightCnt };
                    continue;
                }

                int3 leftMax2 = wi.Max;
                int3 rightMin2 = wi.Min;
                switch (axis)
                {
                    case 0:
                        leftMax2.x = (int)pivot;
                        rightMin2.x = (int)pivot;
                        break;
                    case 1:
                        leftMax2.y = (int)pivot;
                        rightMin2.y = (int)pivot;
                        break;
                    default:
                        leftMax2.z = (int)pivot;
                        rightMin2.z = (int)pivot;
                        break;
                }

                stack[sp++] = new WorkItem { Min = wi.Min, Max = leftMax2, Start = start, Count = leftCnt };
                stack[sp++] = new WorkItem { Min = rightMin2, Max = wi.Max, Start = start + leftCnt, Count = rightCnt };
            }

            void WriteLeaf(in WorkItem wi)
            {
                int rangeStart = leafWrite - leafBase;
                int end = wi.Start + wi.Count;
                for (int i = wi.Start; i < end; ++i)
                    leafIndicesFlatLocal[leafWrite++] = workingSubs[i].originalIndex;
                int rangeEnd = leafWrite - leafBase;

                // Tight bounds over subs in this leaf
                ComputeTightBoundsAndVolumes(
                    workingSubs, wi.Start, wi.Count,
                    out int minX, out int minY, out int minZ, out int maxX, out int maxY, out int maxZ,
                    out float ratio);

                cellSubsFlatLocal[cellWrite++] = new BoxObj.CellData
                {
                    minX = minX, minY = minY, minZ = minZ,
                    maxX = maxX, maxY = maxY, maxZ = maxZ,
                    rangeStart = rangeStart,
                    rangeEnd = rangeEnd
                };
            }

            LeafCellCountPerBox[boxIdx] = new int2(leafWrite - leafBase, cellWrite - cellBase);

            workingSubs.Dispose();
            stack.Dispose();
        }
    }
}