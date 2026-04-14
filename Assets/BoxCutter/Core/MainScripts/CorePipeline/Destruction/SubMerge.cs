using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static BoxCutter.BoxObj;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                                SubBox Merging Job
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// SubBox merging job that produces minimal merged rectangles.
    /// Uses a plane-sweep algorithm to find maximal rectangles efficiently.
    /// </summary>
    [BurstCompile(DisableSafetyChecks = true)]
    public struct SubMerge : IJob
    {
        /// <summary>All SubBoxes from the BoxObj's allSubList</summary>
        [ReadOnly] public NativeArray<SubBox> AllSubBoxes;

        /// <summary>Output collection for merged SubBox results</summary>
        public NativeList<SubBox> MergedOutput;

        /// <summary>
        /// Optimized SubBox merging using maximal rectangle finding.
        /// Processes each Z-plane separately for better performance.
        /// </summary>
        public void Execute()
        {
            int subCount = AllSubBoxes.Length;
            if (subCount == 0) return;

            // Group SubBoxes by Z planes for efficient processing
            var zPlanes = new NativeHashMap<int2, NativeList<SubBox>>(32, Allocator.Temp);

            // Collect unique Z ranges
            for (int i = 0; i < subCount; i++)
            {
                var box = AllSubBoxes[i];
                var zKey = new int2(box.minZ, box.maxZ);

                if (!zPlanes.ContainsKey(zKey))
                {
                    zPlanes[zKey] = new NativeList<SubBox>(Allocator.Temp);
                }

                zPlanes[zKey].Add(box);
            }

            // Process each Z plane separately
            var keys = zPlanes.GetKeyArray(Allocator.Temp);
            for (int k = 0; k < keys.Length; k++)
            {
                var zKey = keys[k];
                var planeBoxes = zPlanes[zKey];

                // Find maximal rectangles in this Z plane
                ProcessZPlane(planeBoxes, zKey.x, zKey.y);

                planeBoxes.Dispose();
            }

            // Cleanup
            keys.Dispose();
            zPlanes.Dispose();
        }

        /// <summary>
        /// Process a single Z plane to find maximal merged rectangles
        /// </summary>
        private void ProcessZPlane(NativeList<SubBox> planeBoxes, int minZ, int maxZ)
        {
            if (planeBoxes.Length == 0) return;

            // Create a 2D grid representation for this Z plane
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            // Find bounds
            for (int i = 0; i < planeBoxes.Length; i++)
            {
                var box = planeBoxes[i];
                minX = math.min(minX, box.minX);
                maxX = math.max(maxX, box.maxX);
                minY = math.min(minY, box.minY);
                maxY = math.max(maxY, box.maxY);
            }

            int width = maxX - minX;
            int height = maxY - minY;

            // Create occupancy grid
            var grid = new NativeArray<bool>(width * height, Allocator.Temp);

            // Mark occupied cells
            for (int i = 0; i < planeBoxes.Length; i++)
            {
                var box = planeBoxes[i];
                for (int y = box.minY; y < box.maxY; y++)
                {
                    for (int x = box.minX; x < box.maxX; x++)
                    {
                        int idx = (y - minY) * width + (x - minX);
                        grid[idx] = true;
                    }
                }
            }

            // Find maximal rectangles using dynamic programming
            var processed = new NativeArray<bool>(width * height, Allocator.Temp);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    if (!grid[idx] || processed[idx]) continue;

                    // Find the largest rectangle starting at this position
                    var rect = FindMaximalRectangle(grid, processed, x, y, width, height);

                    if (rect.z > 0 && rect.w > 0) // Valid rectangle found
                    {
                        // Mark all cells in this rectangle as processed
                        for (int dy = 0; dy < rect.w; dy++)
                        {
                            for (int dx = 0; dx < rect.z; dx++)
                            {
                                processed[(y + dy) * width + (x + dx)] = true;
                            }
                        }

                        // Create merged SubBox
                        var merged = new SubBox
                        {
                            minX = minX + x,
                            maxX = minX + x + rect.z,
                            minY = minY + y,
                            maxY = minY + y + rect.w,
                            minZ = minZ,
                            maxZ = maxZ,
                            magicaIndex = 0
                        };
                        MergedOutput.Add(merged);
                    }
                }
            }

            grid.Dispose();
            processed.Dispose();
        }

        /// <summary>
        /// Find the maximal rectangle starting at (x, y) in the grid
        /// Returns int4(x, y, width, height) of the rectangle
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int4 FindMaximalRectangle(NativeArray<bool> grid, NativeArray<bool> processed,
            int startX, int startY, int gridWidth, int gridHeight)
        {
            if (!grid[startY * gridWidth + startX] || processed[startY * gridWidth + startX])
                return int4.zero;

            int maxWidth = gridWidth - startX;
            int maxHeight = gridHeight - startY;

            // Find maximum width for first row
            int width = 0;
            for (int x = startX; x < startX + maxWidth; x++)
            {
                if (!grid[startY * gridWidth + x] || processed[startY * gridWidth + x])
                    break;
                width++;
            }

            if (width == 0) return int4.zero;

            // Now find maximum height maintaining this width
            int height = 1;
            for (int y = startY + 1; y < startY + maxHeight; y++)
            {
                // Check if entire row at this height is valid
                bool validRow = true;
                for (int x = startX; x < startX + width; x++)
                {
                    if (!grid[y * gridWidth + x] || processed[y * gridWidth + x])
                    {
                        validRow = false;
                        break;
                    }
                }

                if (!validRow) break;
                height++;
            }

            return new int4(startX, startY, width, height);
        }
    }
}