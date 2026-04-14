using System;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static BoxCutter.BoxObj;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    public static partial class MeshBuildPipeline
    {
        /// <summary>
        /// Accurate mesh build that returns one mesh per KD cell for a single BoxObj.
        /// Thin wrapper that forwards to the batched builder.
        /// </summary>
        public static async Task<Mesh[]> BuildAccuratePerKdCell(BoxObj box, bool sync = false, int maxFrames = -1)
        {
            if (box == null) return new Mesh[] { };

            var (allMeshes, cellOwners, cellStartPerBox) = await BuildAccuratePerKdCell(new[] { box }, sync, maxFrames);

            int start = cellStartPerBox[0];
            int count = math.max(0, box.kdCellLength);

            if (start == 0 && allMeshes.Length == count)
                return allMeshes;

            var meshes = new Mesh[count];
            Array.Copy(allMeshes, start, meshes, 0, count);
            return meshes;
        }

        /// <summary>
        /// Accurate mesh build that returns one mesh per KD cell.
        /// This is a wrapper around BuildAccurate that redistributes quads by cell.
        /// </summary>
        public static async Task<(Mesh[] meshes, int[] cellOwners, int[] cellStartPerBox)> BuildAccuratePerKdCell(BoxObj[] boxes, bool sync, int maxFrames)
        {
            (Mesh[] meshes, int[] cellOwners, int[] cellStartPerBox) ReturnEmpty()
            {
                return (new Mesh[] { }, new int[] {}, new int[] {});
            }
            
            int boxCount = boxes?.Length ?? 0;
            if (boxCount == 0) return ReturnEmpty();

            BoxCutterManager manager = BoxCutterManagerInstance;
            if (manager.CheckExit()) return ReturnEmpty();

            // Step 1: Use the accurate pipeline to generate quads for all boxes
            var allSubArrays = new SubBox[boxCount][];
            var kdCellDataArrays = new CellData[boxCount][];
            var kdOriginalIndicesArrays = new int[boxCount][];
            var boxTransforms = new BoxTrans[boxCount];
            var existingQuads = new QuadRect[boxCount][];

            for (int i = 0; i < boxCount; i++)
            {
                BoxObj box = boxes[i];
                allSubArrays[i] = new SubBox[box.allSubList.Count];
                box.allSubList.CopyTo(allSubArrays[i]);
                
                kdCellDataArrays[i] = box.kdCellDataArr;
                kdOriginalIndicesArrays[i] = box.kdOriginalIndicesArr;
                
                box.UpdateLiveVars(true);
                boxTransforms[i] = box.CreateBoxData();
                existingQuads[i] = box.quadRects ?? new QuadRect[0];
            }

            // Call the accurate pipeline to generate all quads
            var (_, updatedSubArrays, quadRectsPerBox) = await BuildAccurate(
                allSubArrays,
                kdCellDataArrays,
                kdOriginalIndicesArrays,
                boxTransforms,
                existingQuads,
                sync,
                maxFrames,
                false
            );

            if (manager.CheckExit()) return ReturnEmpty();

            // Step 2: Count total cells and prepare metadata
            int totalCells = 0;
            var cellCounts = new int[boxCount];
            var cellStartPerBox = new int[boxCount];

            for (int b = 0; b < boxCount; b++)
            {
                cellCounts[b] = math.max(0, boxes[b].kdCellLength);
                cellStartPerBox[b] = totalCells;
                totalCells += cellCounts[b];
            }

            if (totalCells == 0) return ReturnEmpty();

            // Step 3: Prepare data for per-cell mesh creation
            Allocator allocator = sync ? Allocator.TempJob : Allocator.Persistent;

            // Flatten all data with proper indexing
            int totalSubs = 0, totalKdCells = 0, totalKdLeaves = 0, totalQuads = 0;
            for (int b = 0; b < boxCount; b++)
            {
                totalSubs += updatedSubArrays[b].Length;
                totalKdCells += cellCounts[b];
                totalKdLeaves += kdOriginalIndicesArrays[b].Length;
                totalQuads += quadRectsPerBox[b].Length;
            }

            var subsFlat = new SubBox[totalSubs];
            var subToBox = new int[totalSubs];
            var kdCellsFlat = new CellData[totalKdCells];
            var kdOrigFlat = new int[totalKdLeaves];
            var cellOwner = new int[totalKdCells];
            var allQuadsFlat = new QuadRect[totalQuads];

            int subBase = 0, cellBase = 0, leafBase = 0, quadBase = 0;

            for (int b = 0; b < boxCount; b++)
            {
                // Copy updated subs
                var subs = updatedSubArrays[b];
                for (int i = 0; i < subs.Length; i++)
                {
                    var sub = subs[i];
                    // Adjust quad indices to global space
                    sub.quadStart += quadBase;
                    sub.quadEnd += quadBase;
                    subsFlat[subBase + i] = sub;
                    subToBox[subBase + i] = b;
                }

                // Copy KD cells with adjusted leaf indices
                var cells = kdCellDataArrays[b];
                for (int i = 0; i < cellCounts[b]; i++)
                {
                    var cell = cells[i];
                    cell.rangeStart += leafBase;
                    cell.rangeEnd += leafBase;
                    kdCellsFlat[cellBase + i] = cell;
                    cellOwner[cellBase + i] = b;
                }

                // Copy KD leaves with adjusted sub indices
                var leaves = kdOriginalIndicesArrays[b];
                for (int i = 0; i < leaves.Length; i++)
                    kdOrigFlat[leafBase + i] = leaves[i] + subBase;

                // Copy quads
                var quads = quadRectsPerBox[b];
                Array.Copy(quads, 0, allQuadsFlat, quadBase, quads.Length);

                subBase += subs.Length;
                cellBase += cellCounts[b];
                leafBase += leaves.Length;
                quadBase += quads.Length;
            }

            using var subsNa = new NativeArray<SubBox>(subsFlat, allocator);
            using var kdCellsNa = new NativeArray<CellData>(kdCellsFlat, allocator);
            using var kdOrigNa = new NativeArray<int>(kdOrigFlat, allocator);
            using var cellOwnerNa = new NativeArray<int>(cellOwner, allocator);
            using var allQuadsNa = new NativeArray<QuadRect>(allQuadsFlat, allocator);
            using var boxTransNa = new NativeArray<BoxTrans>(boxTransforms, allocator);

            // Step 4: Calculate quads per cell
            using var quadsPerCellNa = new NativeArray<int>(totalCells, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            new QuadSumPerCellJob
            {
                KdCells = kdCellsNa,
                KdOriginalIndices = kdOrigNa,
                Subs = subsNa,
                QuadsPerCell = quadsPerCellNa
            }.Schedule(totalCells, 64).Complete();

            int[] quadsPerCell = new int[totalCells];
            quadsPerCellNa.CopyTo(quadsPerCell);

            // Step 5: Allocate mesh data for all cells
            var mda = Mesh.AllocateWritableMeshData(totalCells);
            var vLayout = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp);
            vLayout[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            vLayout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
            vLayout[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);

            for (int i = 0; i < totalCells; i++)
            {
                int qc = quadsPerCell[i];
                var md = mda[i];
                md.SetVertexBufferParams(qc * 4, vLayout);
                md.SetIndexBufferParams(qc * 6, IndexFormat.UInt32);
            }

            vLayout.Dispose();

            // Prepare per-box data
            var pivots = new float3[boxCount];
            var invLossy = new float3[boxCount];

            for (int b = 0; b < boxCount; b++)
            {
                var box = boxes[b];
                pivots[b] = box.pos;
                var lossy = box.obj.lossyScale;
                invLossy[b] = new float3(
                    lossy.x != 0 ? 1f / lossy.x : 0f,
                    lossy.y != 0 ? 1f / lossy.y : 0f,
                    lossy.z != 0 ? 1f / lossy.z : 0f
                );
            }

            using var pivotsNa = new NativeArray<float3>(pivots, allocator);
            using var invLossyNa = new NativeArray<float3>(invLossy, allocator);

            // Step 6: Emit geometry per cell
            var selectedCellArr = new int[totalCells];
            for (int i = 0; i < totalCells; i++) selectedCellArr[i] = i;
            using var selectedCellsNa = new NativeArray<int>(selectedCellArr, allocator);

            JobHandle handle = new EmitGeometryPerCellBatchJob
            {
                Quads = allQuadsNa,
                Subs = subsNa,
                KdCells = kdCellsNa,
                KdOriginalIndices = kdOrigNa,
                BoxTransNa = boxTransNa,
                BoxPivots = pivotsNa,
                InvLossies = invLossyNa,
                CellOwner = cellOwnerNa,
                SelectedCells = selectedCellsNa,
                EmitLocalSpace = true,
                Mda = mda
            }.Schedule(totalCells, 1);

            await WaitJobComplete(handle, sync, maxFrames);

            if (manager.CheckExit()) return ReturnEmpty();

            // Step 7: Finalize submeshes
            for (int i = 0; i < totalCells; i++)
            {
                int qc = quadsPerCell[i];
                var md = mda[i];
                md.subMeshCount = 1;
                md.SetSubMesh(0, new SubMeshDescriptor(0, qc * 6)
                {
                    topology = MeshTopology.Triangles,
                    indexStart = 0,
                    indexCount = qc * 6
                });
            }

            // Step 8: Create and apply meshes
            var meshes = new Mesh[totalCells];
            for (int i = 0; i < totalCells; i++)
                meshes[i] = new Mesh { name = "BoxCutter Cell Mesh", indexFormat = IndexFormat.UInt32 };

            Mesh.ApplyAndDisposeWritableMeshData(mda, meshes);
            return (meshes, cellOwner, cellStartPerBox);
        }

        [BurstCompile]
        private struct QuadSumPerCellJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CellData> KdCells;
            [ReadOnly] public NativeArray<int> KdOriginalIndices;
            [ReadOnly] public NativeArray<SubBox> Subs;
            [WriteOnly] public NativeArray<int> QuadsPerCell;

            public void Execute(int cellIdx)
            {
                var cell = KdCells[cellIdx];
                int sum = 0;
                for (int p = cell.rangeStart; p < cell.rangeEnd; p++)
                {
                    int subIdx = KdOriginalIndices[p];
                    var sub = Subs[subIdx];
                    sum += sub.quadEnd - sub.quadStart;
                }
                QuadsPerCell[cellIdx] = sum;
            }
        }

        [BurstCompile]
        private struct EmitGeometryPerCellBatchJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<QuadRect> Quads;
            [ReadOnly] public NativeArray<SubBox> Subs;
            [ReadOnly] public NativeArray<CellData> KdCells;
            [ReadOnly] public NativeArray<int> KdOriginalIndices;
            [ReadOnly] public NativeArray<BoxTrans> BoxTransNa;
            [ReadOnly] public NativeArray<float3> BoxPivots;
            [ReadOnly] public NativeArray<float3> InvLossies;
            [ReadOnly] public NativeArray<int> SelectedCells;
            [ReadOnly] public NativeArray<int> CellOwner;
            [ReadOnly] public bool EmitLocalSpace;

            [NativeDisableParallelForRestriction] public Mesh.MeshDataArray Mda;

            public void Execute(int i)
            {
                int cellIdx = SelectedCells[i];
                int boxIdx = CellOwner[cellIdx];
                var cell = KdCells[cellIdx];

                var md = Mda[i];
                
                // Empty cell guard
                if (cell.rangeStart >= cell.rangeEnd)
                {
                    md.subMeshCount = 1;
                    md.SetSubMesh(0, new SubMeshDescriptor(0, 0)
                    {
                        topology = MeshTopology.Triangles,
                        indexStart = 0,
                        indexCount = 0
                    });
                    return;
                }

                var vBuf = md.GetVertexData<BoxVert>();
                var tBuf = md.GetIndexData<int>();

                int vPtr = 0, tPtr = 0;

                var bt = BoxTransNa[boxIdx];
                var invRot = new quaternion(bt.iqx, bt.iqy, bt.iqz, bt.iqw);
                float3 pivotWS = BoxPivots[boxIdx];
                float3 invS = EmitLocalSpace ? InvLossies[boxIdx] : new float3(1f, 1f, 1f);

                for (int p = cell.rangeStart; p < cell.rangeEnd; p++)
                {
                    int subIdx = KdOriginalIndices[p];
                    var me = Subs[subIdx];

                    int quadFirst = me.quadStart;
                    int quadLast = me.quadEnd;
                    if (quadFirst >= quadLast) continue;

                    int minX = me.minX, minY = me.minY, minZ = me.minZ;
                    int maxX = me.maxX, maxY = me.maxY, maxZ = me.maxZ;

                    // Size in local space
                    float3 size = new float3(
                        (maxX - minX) * bt.voxelSize * invS.x,
                        (maxY - minY) * bt.voxelSize * invS.y,
                        (maxZ - minZ) * bt.voxelSize * invS.z
                    );

                    float3 centreWS =
                        new float3(bt.startPosX, bt.startPosY, bt.startPosZ) +
                        new float3(bt.vRightX, bt.vRightY, bt.vRightZ) * ((maxX + minX) * 0.5f) +
                        new float3(bt.vUpX, bt.vUpY, bt.vUpZ) * ((maxY + minY) * 0.5f) +
                        new float3(bt.vFwdX, bt.vFwdY, bt.vFwdZ) * ((maxZ + minZ) * 0.5f);

                    float3 adj = math.mul(invRot, centreWS - pivotWS);
                    if (EmitLocalSpace) adj *= invS;

                    for (int qi = quadFirst; qi < quadLast; qi++)
                    {
                        QuadRect q = Quads[qi];
                        AddQuad(
                            (Dir)q.dir, q.minU, q.maxU, q.minV, q.maxV,
                            ref vPtr, ref tPtr,
                            minX, minY, minZ, size, adj,
                            bt.voxelSize, me.magicaIndex,
                            vBuf, tBuf,
                            EmitLocalSpace, invS
                        );
                    }
                }

                md.subMeshCount = 1;
                md.SetSubMesh(0, new SubMeshDescriptor(0, tPtr)
                {
                    topology = MeshTopology.Triangles,
                    indexStart = 0,
                    indexCount = tPtr
                });
            }
        }
    }
}