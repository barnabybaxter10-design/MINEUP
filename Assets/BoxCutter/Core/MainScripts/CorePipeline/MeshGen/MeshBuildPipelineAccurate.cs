using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static BoxCutter.BoxCutterManager;

namespace BoxCutter
{
    public static partial class MeshBuildPipeline
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Accurate Mesh Generation
        //─────────────────────────────────────────────────────────────────────────────────────

        private struct BoxMeta
        {
            public int subStart, subCount;
            public int cellStart, cellCount;
            public int eQuadsStart;
            public int quadStart, quadCount; // output fields
        }

        /// <summary>
        /// Generates optimized meshes for BoxObj fragments using occlusion culling and greedy meshing.
        /// Performs face culling between adjacent SubBoxes and merges coplanar faces for efficiency.
        /// Uses KD-tree acceleration for fast neighbor queries and Burst-compiled jobs for performance.
        /// This is a convenience wrapper that extracts data from BoxObj instances and assigns results back.
        /// </summary>
        /// <param name="boxes">Array of BoxObj instances requiring accurate mesh generation</param>
        /// <param name="sync">Create the mesh in the same frame</param>
        /// <param name="maxFrames">Max amount of processing frames</param>
        /// <param name="nonDestructionCall">If the call is from outside the core destruction pipeline</param>
        /// <param name="canUseExistingQuads">If the system should ruse existing quads for performance</param>
        public static async Task BuildAccurate(BoxObj[] boxes, bool sync = false, int maxFrames = -1, bool nonDestructionCall = false, bool canUseExistingQuads = false)
        {
            int boxCount = boxes.Length;
            if (boxCount == 0) return;

            BoxCutterManager manager = BoxCutterManagerInstance;
            if (manager.CheckExit()) return;

            // Extract data from BoxObjs
            var allSubArrays = new SubBox[boxCount][];
            var kdCellDataArrays = new BoxObj.CellData[boxCount][];
            var kdOriginalIndicesArrays = new int[boxCount][];
            var boxTransforms = new DestructionPipeline.BoxTrans[boxCount];
            var existingQuads = new QuadRect[boxCount][];

            for (int i = 0; i < boxCount; i++)
            {
                BoxObj box = boxes[i];
                if (manager.CheckExit()) return;

                allSubArrays[i] = new SubBox[box.allSubList.Count];
                box.allSubList.CopyTo(allSubArrays[i]);

                kdCellDataArrays[i] = box.gridCellDataArr;
                kdOriginalIndicesArrays[i] = box.gridOriginalIndicesArr;

                box.UpdateLiveVars(true);
                boxTransforms[i] = box.CreateBoxData();
                existingQuads[i] = canUseExistingQuads ? box.quadRects : new QuadRect[]{};
            }

            // Call the modular BuildAccurate method
            var (meshes, updatedSubs, quadRects) = await BuildAccurate(
                allSubArrays,
                kdCellDataArrays,
                kdOriginalIndicesArrays,
                boxTransforms,
                existingQuads,
                sync,
                maxFrames,
                nonDestructionCall,
                canUseExistingQuads
            );

            // Assign results back to BoxObjs
            for (int b = 0; b < boxCount; ++b)
            {
                BoxObj box = boxes[b];
                if (box.CheckExit(1)) continue;

                box.meshFilter.sharedMesh = meshes[b];

                // Update SubBox list with quadStart/quadEnd values
                box.allSubList.Clear();
                box.allSubList.AddRange(updatedSubs[b]);

                // Update QuadRects array
                box.quadRects = quadRects[b];
            }
        }

        /// <summary>
        /// Generates optimized meshes using raw mesh generation data.
        /// Performs face culling between adjacent SubBoxes and merges coplanar faces for efficiency.
        /// Uses KD-tree acceleration for fast neighbor queries and Burst-compiled jobs for performance.
        /// </summary>
        /// <param name="allSubArrays">Array of SubBox arrays, one per box</param>
        /// <param name="kdCellDataArrays">Array of KD-tree cell data arrays, one per box</param>
        /// <param name="kdOriginalIndicesArrays">Array of KD-tree original indices arrays, one per box</param>
        /// <param name="boxTransforms">Array of box transform data</param>
        /// <param name="existingQuads">Existing quads of the BoxObj to reduce calculations</param>
        /// <param name="sync">Create the mesh in the same frame</param>
        /// <param name="maxFrames">Max amount of processing frames</param>
        /// <param name="nonDestructionCall">If the call is from outside the core destruction pipeline</param>
        /// <param name="canUseExistingQuads">If the system should ruse existing quads for performance</param>
        /// <returns>Tuple containing generated meshes, updated SubBox arrays with quad indices, and QuadRect arrays for each box</returns>
        public static async Task<(Mesh[], SubBox[][], QuadRect[][])> BuildAccurate(
            SubBox[][] allSubArrays,
            BoxObj.CellData[][] kdCellDataArrays,
            int[][] kdOriginalIndicesArrays,
            DestructionPipeline.BoxTrans[] boxTransforms,
            QuadRect[][] existingQuads,
            bool sync = false,
            int maxFrames = -1,
            bool nonDestructionCall = false, 
            bool canUseExistingQuads = false)
        {
            int boxCount = allSubArrays.Length;
            if (boxCount == 0) return (Array.Empty<Mesh>(), Array.Empty<SubBox[]>(), Array.Empty<QuadRect[]>());

            BoxCutterManager manager = BoxCutterManagerInstance;
            if (manager.CheckExit()) return (Array.Empty<Mesh>(), Array.Empty<SubBox[]>(), Array.Empty<QuadRect[]>());

            Allocator allocator = sync ? Allocator.TempJob : Allocator.Persistent;

            var boxMeta = new BoxMeta[boxCount];
            int totalSub = 0, totalKdCells = 0, totalKdIdx = 0, totalEQuads = 0;

            for (int b = 0; b < boxCount; ++b)
            {
                boxMeta[b].subStart = totalSub;
                boxMeta[b].subCount = allSubArrays[b].Length;
                boxMeta[b].cellStart = totalKdCells;
                boxMeta[b].cellCount = kdCellDataArrays[b].Length;
                boxMeta[b].eQuadsStart = totalEQuads;

                totalSub += boxMeta[b].subCount;
                totalKdCells += boxMeta[b].cellCount;
                totalKdIdx += kdOriginalIndicesArrays[b].Length;
                totalEQuads += existingQuads[b].Length;
            }

            var allSubs = new SubBox[totalSub];
            var subToBox = new int[totalSub];
            var allCells = new BoxObj.CellData[totalKdCells];
            var allIndices = new int[totalKdIdx];
            var allExistingQuads = new QuadRect[totalEQuads];

            int idxOff = 0;
            for (int b = 0; b < boxCount; ++b)
            {
                int subStart = boxMeta[b].subStart;
                int subCount = boxMeta[b].subCount;
                SubBox[] subBoxes = allSubArrays[b];
                for (int i = 0; i < subCount; ++i)
                {
                    allSubs[subStart + i] = subBoxes[i];
                    subToBox[subStart + i] = b;
                }

                int cellStart = boxMeta[b].cellStart;
                int cellCount = boxMeta[b].cellCount;
                var srcCells = kdCellDataArrays[b];
                for (int i = 0; i < cellCount; ++i)
                {
                    var c = srcCells[i];
                    c.rangeStart += idxOff;
                    c.rangeEnd += idxOff;
                    allCells[cellStart + i] = c;
                }

                int idxCount = kdOriginalIndicesArrays[b].Length;
                var srcIdx = kdOriginalIndicesArrays[b];
                for (int i = 0; i < idxCount; ++i)
                    allIndices[idxOff + i] = srcIdx[i] + subStart;
                idxOff += idxCount;

                int eQuadsStart = boxMeta[b].eQuadsStart;
                var srcQuads = existingQuads[b];
                int quadCount = srcQuads.Length;
                for (int i = 0; i < quadCount; i++)
                    allExistingQuads[eQuadsStart + i] = srcQuads[i];
            }

            using var subsNa = new NativeArray<SubBox>(allSubs, allocator);
            using var kdCellsNa = new NativeArray<BoxObj.CellData>(allCells, allocator);
            using var kdIdxNa = new NativeArray<int>(allIndices, allocator);
            using var subToBoxNa = new NativeArray<int>(subToBox, allocator);
            using var eQuadsNa = new NativeArray<QuadRect>(allExistingQuads, allocator);
            using var boxMetaNa = new NativeArray<BoxMeta>(boxMeta, allocator);
            
            if (manager.CheckExit()) return (Array.Empty<Mesh>(), Array.Empty<SubBox[]>(), Array.Empty<QuadRect[]>());

            using var boxTransNa = new NativeArray<DestructionPipeline.BoxTrans>(boxTransforms, allocator);

            var rectStream = new NativeStream(totalSub, allocator);
            var writer = rectStream.AsWriter();
            using var quadCountsNa = new NativeArray<int>(totalSub, allocator);

            JobHandle handle = new ClipCountAndWriteJob
            {
                Subs = subsNa,
                KdOriginalIndices = kdIdxNa,
                KdCells = kdCellsNa,
                SubToBox = subToBoxNa,
                BoxMeta = boxMetaNa,
                ExistingQuads = eQuadsNa,
                RectStream = writer,
                QuadCounts = quadCountsNa,
                CanUseExistingQuads = canUseExistingQuads,
            }.Schedule(totalSub, 64);

            await WaitJobComplete(handle, sync, maxFrames);
            if (manager.CheckExit()) return (Array.Empty<Mesh>(), Array.Empty<SubBox[]>(), Array.Empty<QuadRect[]>());

            // Build Stream
            using NativeArray<int> quadPrefixNa = new NativeArray<int>(totalSub, allocator);
            using NativeReference<int> totalQuadsRef = new NativeReference<int>(allocator);

            handle = new QuadPrefixSumJob
                {
                    Counts = quadCountsNa,
                    Prefix = quadPrefixNa,
                    Total = totalQuadsRef,
                    BoxMeta = boxMetaNa
                }
                .Schedule();

            await WaitJobComplete(handle, sync, maxFrames);

            using NativeArray<QuadRect> rectBufferNa = new NativeArray<QuadRect>(totalQuadsRef.Value, allocator);

            // Stream
            handle = new StreamFlattenJob
                {
                    Stream = rectStream,
                    Prefix = quadPrefixNa,
                    QuadCounts = quadCountsNa,
                    SubToBox = subToBoxNa,
                    BoxMeta = boxMetaNa,
                    Rects = rectBufferNa,
                    Subs = subsNa
                }
                .Schedule(totalSub, 64);

            await WaitJobComplete(handle, sync, maxFrames);

            rectStream.Dispose();

            var mda = Mesh.AllocateWritableMeshData(boxCount);
            var vLayout = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp);
            vLayout[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            vLayout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
            vLayout[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

            // Copy updated BoxMeta back (quadStart/quadCount were filled by QuadPrefixSumJob)
            boxMetaNa.CopyTo(boxMeta);

            for (int b = 0; b < boxCount; ++b)
            {
                int qc = boxMeta[b].quadCount;
                var md = mda[b];
                md.SetVertexBufferParams(qc * 4, vLayout);
                md.SetIndexBufferParams(qc * 6, IndexFormat.UInt32);
            }
            vLayout.Dispose();

            // 1. Emit Geometry
            handle = new EmitGeometryPerSubJob
            {
                QuadPrefix = quadPrefixNa,
                QuadCounts = quadCountsNa,
                Rects = rectBufferNa,
                Subs = subsNa,
                BoxTransArr = boxTransNa,
                SubToBox = subToBoxNa,
                BoxMeta = boxMetaNa,
                Mda = mda,
                NonDestructionCall = nonDestructionCall,
            }.Schedule(totalSub, 32);

            // 2. Finalize SubMeshes (Chained to handle 1)
            handle = new FinalizeSubMeshesJob
            {
                BoxMeta = boxMetaNa,
                Mda = mda
            }.Schedule(boxCount, 64, handle);

            // Wait for both to complete
            await WaitJobComplete(handle, sync, maxFrames);

            if (manager.CheckExit())
                return (Array.Empty<Mesh>(), Array.Empty<SubBox[]>(), Array.Empty<QuadRect[]>());

            // 3. Create and finalize meshes
            var meshArr = new Mesh[boxCount];
            for (int b = 0; b < boxCount; ++b)
                meshArr[b] = new Mesh { name = "BoxCutter Mesh", indexFormat = IndexFormat.UInt32 };

            Mesh.ApplyAndDisposeWritableMeshData(mda, meshArr);

            for (int b = 0; b < boxCount; ++b)
                meshArr[b].RecalculateBounds();

            // 4. Extract updated SubBoxes and QuadRects to managed arrays
            var updatedSubArrays = new SubBox[boxCount][];
            var quadRectsPerBox = new QuadRect[boxCount][];

            SubBox[] subsArr = new SubBox[totalSub];
            subsNa.CopyTo(subsArr);
            QuadRect[] rectBufferArr = new QuadRect[rectBufferNa.Length];
            rectBufferNa.CopyTo(rectBufferArr);

            for (int b = 0; b < boxCount; ++b)
            {
                int subStart = boxMeta[b].subStart;
                int subCount = boxMeta[b].subCount;

                // Extract updated SubBoxes for this box
                updatedSubArrays[b] = new SubBox[subCount];
                Array.Copy(subsArr, subStart, updatedSubArrays[b], 0, subCount);

                // Extract QuadRects for this box directly from the flattened global buffer
                int quadCount = boxMeta[b].quadCount;
                int quadOffset = boxMeta[b].quadStart;
                quadRectsPerBox[b] = new QuadRect[quadCount];

                if (quadCount > 0)
                    Array.Copy(rectBufferArr, quadOffset, quadRectsPerBox[b], 0, quadCount);
            }
            return (meshArr, updatedSubArrays, quadRectsPerBox);
        }

        [BurstCompile]
        private struct ClipCountAndWriteJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SubBox> Subs;
            [ReadOnly] public NativeArray<int> KdOriginalIndices;
            [ReadOnly] public NativeArray<BoxObj.CellData> KdCells;
            [ReadOnly] public NativeArray<int> SubToBox;
            [ReadOnly] public NativeArray<BoxMeta> BoxMeta;
            [ReadOnly] public NativeArray<QuadRect> ExistingQuads;

            public NativeStream.Writer RectStream;
            [NativeDisableParallelForRestriction] public NativeArray<int> QuadCounts;
            
            public bool CanUseExistingQuads;

            public void Execute(int self)
            {
                var me = Subs[self];
                int box = SubToBox[self];
                var meta = BoxMeta[box];

                if (CanUseExistingQuads && me.quadEnd != 0)
                {
                    int off = meta.eQuadsStart;
                    int startRange = me.quadStart + off;
                    int endRange = me.quadEnd + off;
                    RectStream.BeginForEachIndex(self);
                    for (int i = startRange; i < endRange; i++)
                        RectStream.Write(ExistingQuads[i]);

                    RectStream.EndForEachIndex();
                    QuadCounts[self] = endRange - startRange;

                    return;
                }

                int miX = me.minX, miY = me.minY, miZ = me.minZ;
                int maX = me.maxX, maY = me.maxY, maZ = me.maxZ;

                var visNX = new NativeList<QuadRect>(4, Allocator.Temp) { new QuadRect { dir = (byte)Dir.NegX, minU = miZ, maxU = maZ, minV = miY, maxV = maY } };
                var visPX = new NativeList<QuadRect>(4, Allocator.Temp) { new QuadRect { dir = (byte)Dir.PosX, minU = miZ, maxU = maZ, minV = miY, maxV = maY } };
                var visNY = new NativeList<QuadRect>(4, Allocator.Temp) { new QuadRect { dir = (byte)Dir.NegY, minU = miX, maxU = maX, minV = miZ, maxV = maZ } };
                var visPY = new NativeList<QuadRect>(4, Allocator.Temp) { new QuadRect { dir = (byte)Dir.PosY, minU = miX, maxU = maX, minV = miZ, maxV = maZ } };
                var visNZ = new NativeList<QuadRect>(4, Allocator.Temp) { new QuadRect { dir = (byte)Dir.NegZ, minU = miX, maxU = maX, minV = miY, maxV = maY } };
                var visPZ = new NativeList<QuadRect>(4, Allocator.Temp) { new QuadRect { dir = (byte)Dir.PosZ, minU = miX, maxU = maX, minV = miY, maxV = maY } };

                int nNX = 1, nPX = 1;
                int nNY = 1, nPY = 1;
                int nNZ = 1, nPZ = 1;

                int leafStart = meta.cellStart;
                int leafEnd = leafStart + meta.cellCount;

                for (int leaf = leafStart; leaf < leafEnd; ++leaf)
                {
                    var cell = KdCells[leaf];
                    if (cell.maxX < miX || cell.minX > maX ||
                        cell.maxY < miY || cell.minY > maY ||
                        cell.maxZ < miZ || cell.minZ > maZ)
                        continue;

                    for (int p = cell.rangeStart; p < cell.rangeEnd; ++p)
                    {
                        var n = Subs[KdOriginalIndices[p]];
                        int nMiX = n.minX, nMiY = n.minY, nMiZ = n.minZ;
                        int nMaX = n.maxX, nMaY = n.maxY, nMaZ = n.maxZ;

                        if (nMiX == miX && nMiY == miY && nMiZ == miZ &&
                            nMaX == maX && nMaY == maY && nMaZ == maZ)
                            continue;

                        bool posX = nMiX <= maX && nMaX > maX;
                        bool negX = nMaX >= miX && nMiX < miX;
                        bool posY = nMiY <= maY && nMaY > maY;
                        bool negY = nMaY >= miY && nMiY < miY;
                        bool posZ = nMiZ <= maZ && nMaZ > maZ;
                        bool negZ = nMaZ >= miZ && nMiZ < miZ;
                        if (!(posX | negX | posY | negY | posZ | negZ))
                            continue;

                        if (negX) ClipFace(ref visNX, (byte)Dir.NegX, ref nNX, nMiZ, nMaZ, nMiY, nMaY);
                        if (posX) ClipFace(ref visPX, (byte)Dir.PosX, ref nPX, nMiZ, nMaZ, nMiY, nMaY);
                        if (negY) ClipFace(ref visNY, (byte)Dir.NegY, ref nNY, nMiX, nMaX, nMiZ, nMaZ);
                        if (posY) ClipFace(ref visPY, (byte)Dir.PosY, ref nPY, nMiX, nMaX, nMiZ, nMaZ);
                        if (negZ) ClipFace(ref visNZ, (byte)Dir.NegZ, ref nNZ, nMiX, nMaX, nMiY, nMaY);
                        if (posZ) ClipFace(ref visPZ, (byte)Dir.PosZ, ref nPZ, nMiX, nMaX, nMiY, nMaY);

                        if ((nNX | nPX | nNY | nPY | nNZ | nPZ) == 0)
                            break;
                    }
                }

                QuadCounts[self] = nNX + nPX + nNY + nPY + nNZ + nPZ;

                RectStream.BeginForEachIndex(self);
                for (int i = 0; i < nNX; ++i) RectStream.Write(visNX[i]);
                for (int i = 0; i < nPX; ++i) RectStream.Write(visPX[i]);
                for (int i = 0; i < nNY; ++i) RectStream.Write(visNY[i]);
                for (int i = 0; i < nPY; ++i) RectStream.Write(visPY[i]);
                for (int i = 0; i < nNZ; ++i) RectStream.Write(visNZ[i]);
                for (int i = 0; i < nPZ; ++i) RectStream.Write(visPZ[i]);
                RectStream.EndForEachIndex();

                visNX.Dispose();
                visPX.Dispose();
                visNY.Dispose();
                visPY.Dispose();
                visNZ.Dispose();
                visPZ.Dispose();
            }
        }
        
        [BurstCompile]
        private struct QuadPrefixSumJob : IJob
        {
            [ReadOnly] public NativeArray<int> Counts;
            public NativeArray<int> Prefix;
            public NativeReference<int> Total;
            public NativeArray<BoxMeta> BoxMeta;

            public void Execute()
            {
                // 1. Calculate global quad prefix sum for flattening
                int run = 0;
                for (int i = 0; i < Counts.Length; i++)
                {
                    Prefix[i] = run;
                    run += Counts[i];
                }
                Total.Value = run;

                // 2. Calculate quads per box and the starting offset for each box
                int boxCount = BoxMeta.Length;
                int currentBoxOffset = 0;
                for (int b = 0; b < boxCount; b++)
                {
                    var meta = BoxMeta[b];
                    meta.quadStart = currentBoxOffset;

                    int start = meta.subStart;
                    int cnt = meta.subCount;
                    int sum = 0;
                    for (int i = 0; i < cnt; ++i)
                        sum += Counts[start + i];

                    meta.quadCount = sum;
                    BoxMeta[b] = meta;
                    currentBoxOffset += sum;
                }
            }
        }

        [BurstCompile(DisableSafetyChecks = true)]
        private struct StreamFlattenJob : IJobParallelFor
        {
            [ReadOnly] public NativeStream Stream;
            [ReadOnly] public NativeArray<int> Prefix;
            [ReadOnly] public NativeArray<int> QuadCounts;
            [ReadOnly] public NativeArray<int> SubToBox;
            [ReadOnly] public NativeArray<BoxMeta> BoxMeta;

            [NativeDisableParallelForRestriction] public NativeArray<QuadRect> Rects;
            [NativeDisableParallelForRestriction] public NativeArray<SubBox> Subs;

            public void Execute(int index)
            {
                // 1. Flatten the Stream into the Rects buffer
                var reader = Stream.AsReader();
                reader.BeginForEachIndex(index);
                int dst = Prefix[index];
                int count = reader.RemainingItemCount;

                while (reader.RemainingItemCount > 0)
                    Rects[dst++] = reader.Read<QuadRect>();
                reader.EndForEachIndex();

                // 2. Update the SubBox range values
                int boxIdx = SubToBox[index];
                int boxFirstSubIdx = BoxMeta[boxIdx].subStart;

                // Local quad offset within this specific box
                int localQuadOffset = Prefix[index] - Prefix[boxFirstSubIdx];

                var sub = Subs[index];
                sub.quadStart = localQuadOffset;
                sub.quadEnd = localQuadOffset + count;
                Subs[index] = sub;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ClipFace(
            ref NativeList<QuadRect> list, byte dir,
            ref int len,
            int u0, int u1, int v0, int v1)
        {
            int k = 0;
            while (k < len)
            {
                QuadRect cur = list[k];
                bool hit = cur.minU < u1 && cur.maxU > u0 && cur.minV < v1 && cur.maxV > v0;
                if (!hit)
                {
                    ++k;
                    continue;
                }

                list.RemoveAtSwapBack(k);
                len--;

                if (u0 > cur.minU)
                {
                    list.Add(new QuadRect { dir = dir, minU = cur.minU, maxU = u0, minV = cur.minV, maxV = cur.maxV });
                    len++;
                }

                if (u1 < cur.maxU)
                {
                    list.Add(new QuadRect { dir = dir, minU = u1, maxU = cur.maxU, minV = cur.minV, maxV = cur.maxV });
                    len++;
                }

                int uu0 = math.max(cur.minU, u0);
                int uu1 = math.min(cur.maxU, u1);

                if (v0 > cur.minV)
                {
                    list.Add(new QuadRect { dir = dir, minU = uu0, maxU = uu1, minV = cur.minV, maxV = v0 });
                    len++;
                }

                if (v1 < cur.maxV)
                {
                    list.Add(new QuadRect { dir = dir, minU = uu0, maxU = uu1, minV = v1, maxV = cur.maxV });
                    len++;
                }
            }
        }

        [BurstCompile(DisableSafetyChecks = true)]
        private struct EmitGeometryPerSubJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> QuadPrefix;
            [ReadOnly] public NativeArray<int> QuadCounts;
            [ReadOnly] public NativeArray<QuadRect> Rects;
            [ReadOnly] public NativeArray<SubBox> Subs;
            [ReadOnly] public NativeArray<DestructionPipeline.BoxTrans> BoxTransArr;
            [ReadOnly] public NativeArray<int> SubToBox;
            [ReadOnly] public NativeArray<BoxMeta> BoxMeta;

            [NativeDisableParallelForRestriction] public Mesh.MeshDataArray Mda;

            [ReadOnly] public bool NonDestructionCall;

            public void Execute(int subIdx)
            {
                int qc = QuadCounts[subIdx];
                if (qc <= 0) return;

                int boxIdx = SubToBox[subIdx];
                var bt = BoxTransArr[boxIdx];
                var me = Subs[subIdx];

                var md = Mda[boxIdx];
                var vBuf = md.GetVertexData<BoxVert>();
                var tBuf = md.GetIndexData<int>();

                int boxFirstSubIdx = BoxMeta[boxIdx].subStart;
                int startQuadInBox = QuadPrefix[subIdx] - QuadPrefix[boxFirstSubIdx];

                int vPtr = startQuadInBox * 4;
                int tPtr = startQuadInBox * 6;

                // Hoist BoxTrans variables to locals to assist Burst alias analysis
                float voxelSize = bt.voxelSize;
                float3 lossyScale = bt.lossyScale;
                float3 vStep = NonDestructionCall
                    ? new float3(voxelSize) / lossyScale
                    : new float3(voxelSize);

                float facePaddingX = vStep.x * FACE_PADDING;
                float facePaddingY = vStep.y * FACE_PADDING;
                float facePaddingZ = vStep.y * FACE_PADDING;
                
                // SubBox geometry bounds
                int x0 = me.minX, x1 = me.maxX;
                int y0 = me.minY, y1 = me.maxY;
                int z0 = me.minZ, z1 = me.maxZ;

                float3 halfSize = new float3(x1 - x0, y1 - y0, z1 - z0) * vStep * 0.5f;

                // Calculate Adjustment (Offset) once per SubBox
                float3 centerWS = new float3(bt.startPosX, bt.startPosY, bt.startPosZ) +
                                  new float3(bt.vRightX, bt.vRightY, bt.vRightZ) * ((x1 + x0) * 0.5f) +
                                  new float3(bt.vUpX, bt.vUpY, bt.vUpZ) * ((y1 + y0) * 0.5f) +
                                  new float3(bt.vFwdX, bt.vFwdY, bt.vFwdZ) * ((z1 + z0) * 0.5f);

                float3 pivotWS = new float3(bt.posX, bt.posY, bt.posZ);
                float3 adj = math.mul(new quaternion(bt.iqx, bt.iqy, bt.iqz, bt.iqw), centerWS - pivotWS);

                if (NonDestructionCall)
                {
                    adj = (adj + new float3(bt.magicaOffsetX, bt.magicaOffsetY, bt.magicaOffsetZ)) / lossyScale;
                }

                // UV is constant for all quads in this SubBox
                float uvX = (me.magicaIndex + 0.5f) * 0.00390625f; // 1/256
                float2 uv = new float2(uvX, 0.5f);

                int quadFirst = QuadPrefix[subIdx];

                for (int i = 0; i < qc; ++i)
                {
                    QuadRect q = Rects[quadFirst + i];
                    Dir dir = (Dir)q.dir;

                    float3 nrm = NormalOf(dir);
                    float3 c0, c1, c2, c3;

                    // Pre-calculate face-specific coordinate planes
                    // Inlined logic from AddQuad to remove extra method overhead and branching
                    float yLow = -halfSize.y + (q.minV - y0) * vStep.y - facePaddingY;
                    float yHigh = -halfSize.y + (q.maxV - y0) * vStep.y + facePaddingY;
                    float zLow = -halfSize.z + (q.minU - z0) * vStep.z - facePaddingZ;
                    float zHigh = -halfSize.z + (q.maxU - z0) * vStep.z + facePaddingZ;
                    float xLow = -halfSize.x + (q.minU - x0) * vStep.x - facePaddingX;
                    float xHigh = -halfSize.x + (q.maxU - x0) * vStep.x + facePaddingX;
                    float zAltL = -halfSize.z + (q.minV - z0) * vStep.z - facePaddingZ;
                    float zAltH = -halfSize.z + (q.maxV - z0) * vStep.z + facePaddingZ;

                    switch (dir)
                    {
                        case Dir.PosX:
                            c0 = new float3(halfSize.x, yLow, zLow);
                            c1 = new float3(halfSize.x, yLow, zHigh);
                            c2 = new float3(halfSize.x, yHigh, zHigh);
                            c3 = new float3(halfSize.x, yHigh, zLow);
                            break;
                        case Dir.NegX:
                            c0 = new float3(-halfSize.x, yLow, zHigh);
                            c1 = new float3(-halfSize.x, yLow, zLow);
                            c2 = new float3(-halfSize.x, yHigh, zLow);
                            c3 = new float3(-halfSize.x, yHigh, zHigh);
                            break;
                        case Dir.PosY:
                            c0 = new float3(xLow, halfSize.y, zAltL);
                            c1 = new float3(xHigh, halfSize.y, zAltL);
                            c2 = new float3(xHigh, halfSize.y, zAltH);
                            c3 = new float3(xLow, halfSize.y, zAltH);
                            break;
                        case Dir.NegY:
                            c0 = new float3(xLow, -halfSize.y, zAltH);
                            c1 = new float3(xHigh, -halfSize.y, zAltH);
                            c2 = new float3(xHigh, -halfSize.y, zAltL);
                            c3 = new float3(xLow, -halfSize.y, zAltL);
                            break;
                        case Dir.PosZ:
                            c0 = new float3(xHigh, yLow, halfSize.z);
                            c1 = new float3(xLow, yLow, halfSize.z);
                            c2 = new float3(xLow, yHigh, halfSize.z);
                            c3 = new float3(xHigh, yHigh, halfSize.z);
                            break;
                        default: // NegZ
                            c0 = new float3(xLow, yLow, -halfSize.z);
                            c1 = new float3(xHigh, yLow, -halfSize.z);
                            c2 = new float3(xHigh, yHigh, -halfSize.z);
                            c3 = new float3(xLow, yHigh, -halfSize.z);
                            break;
                    }

                    vBuf[vPtr + 0] = new BoxVert { pos = c0 + adj, nrm = nrm, uv = uv };
                    vBuf[vPtr + 1] = new BoxVert { pos = c1 + adj, nrm = nrm, uv = uv };
                    vBuf[vPtr + 2] = new BoxVert { pos = c2 + adj, nrm = nrm, uv = uv };
                    vBuf[vPtr + 3] = new BoxVert { pos = c3 + adj, nrm = nrm, uv = uv };

                    // We removed math.cross/dot check.
                    // The winding is now hardcoded for the CCW order defined in the switch.
                    tBuf[tPtr + 0] = vPtr;
                    tBuf[tPtr + 1] = vPtr + 2;
                    tBuf[tPtr + 2] = vPtr + 1;
                    tBuf[tPtr + 3] = vPtr;
                    tBuf[tPtr + 4] = vPtr + 3;
                    tBuf[tPtr + 5] = vPtr + 2;

                    vPtr += 4;
                    tPtr += 6;
                }
            }
        }

        [BurstCompile]
        private struct FinalizeSubMeshesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<BoxMeta> BoxMeta;
            public Mesh.MeshDataArray Mda;

            public void Execute(int i)
            {
                var md = Mda[i];
                md.subMeshCount = 1;
                md.SetSubMesh(0, new SubMeshDescriptor(0, BoxMeta[i].quadCount * 6));
            }
        }
    }
}