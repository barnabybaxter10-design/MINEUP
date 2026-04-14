using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static BoxCutter.BoxObj;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               BoxCutter Mesh Build Pipeline System
    //─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// High-performance mesh generation pipeline for BoxCutter voxel fragments.
    /// Provides both accurate occlusion-culled meshes and simple cube meshes for fragments.
    /// Uses Burst-compiled jobs and Unity's native mesh data arrays for optimal performance.
    /// </summary>
    public static partial class MeshBuildPipeline
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Data Structures & Constants
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Vertex structure for mesh generation with position, normal, and UV coordinates.
        /// Uses sequential layout for optimal memory access in Burst-compiled jobs.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct BoxVert
        {
            /// <summary>Vertex position in world space</summary>
            public Vector3 pos;

            /// <summary>Surface normal vector for lighting</summary>
            public Vector3 nrm;

            /// <summary>Texture coordinates for material mapping</summary>
            public Vector2 uv;
        }

        /// <summary>
        /// Directional enumeration for cube face identification.
        /// Used for normal calculation and face culling operations.
        /// </summary>
        public enum Dir : byte
        {
            /// <summary>Negative X direction (-1, 0, 0)</summary>
            NegX = 0,

            /// <summary>Positive X direction (1, 0, 0)</summary>
            PosX = 1,

            /// <summary>Negative Y direction (0, -1, 0)</summary>
            NegY = 2,

            /// <summary>Positive Y direction (0, 1, 0)</summary>
            PosY = 3,

            /// <summary>Negative Z direction (0, 0, -1)</summary>
            NegZ = 4,

            /// <summary>Positive Z direction (0, 0, 1)</summary>
            PosZ = 5
        }

        /// <summary>
        /// Converts directional enum to corresponding normal vector.
        /// Used for calculating surface normals for lighting and rendering.
        /// </summary>
        /// <param name="d">Direction enum value</param>
        /// <returns>Unit normal vector corresponding to the direction</returns>
        private static float3 NormalOf(Dir d) => d switch
        {
            Dir.NegX => new float3(-1, 0, 0), // Left face
            Dir.PosX => new float3(1, 0, 0), // Right face
            Dir.NegY => new float3(0, -1, 0), // Bottom face
            Dir.PosY => new float3(0, 1, 0), // Top face
            Dir.NegZ => new float3(0, 0, -1), // Back face
            _ => new float3(0, 0, 1), // Front face (PosZ)
        };

        /// <summary>
        /// Rectangle structure representing a quad face on a voxel surface.
        /// Contains direction information and 2D rectangle bounds for mesh generation.
        /// </summary>
        [Serializable]
        public struct QuadRect
        {
            // Face direction as Dir enum cast to byte
            public byte dir;

            // Rectangle bounds: (minU, maxU, minV, maxV) in face-local coordinates
            public int minU;
            public int maxU;
            public int minV;
            public int maxV;
        }

        public static readonly Vector3[] k_CubePos = new Vector3[24]
        {
            new(-.5f, -.5f, -.5f), new(-.5f, -.5f, .5f),
            new(-.5f, .5f, .5f), new(-.5f, .5f, -.5f),
            new(.5f, -.5f, -.5f), new(.5f, .5f, -.5f),
            new(.5f, .5f, .5f), new(.5f, -.5f, .5f),
            new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f),
            new(.5f, -.5f, .5f), new(-.5f, -.5f, .5f),
            new(-.5f, .5f, -.5f), new(-.5f, .5f, .5f),
            new(.5f, .5f, .5f), new(.5f, .5f, -.5f),
            new(-.5f, -.5f, -.5f), new(-.5f, .5f, -.5f),
            new(.5f, .5f, -.5f), new(.5f, -.5f, -.5f),
            new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f),
            new(.5f, .5f, .5f), new(-.5f, .5f, .5f)
        };

        public static readonly Vector3[] k_CubeNrm = new Vector3[24]
        {
            new(-1, 0, 0), new(-1, 0, 0), new(-1, 0, 0), new(-1, 0, 0),
            new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0),
            new(0, -1, 0), new(0, -1, 0), new(0, -1, 0), new(0, -1, 0),
            new(0, 1, 0), new(0, 1, 0), new(0, 1, 0), new(0, 1, 0),
            new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1),
            new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1)
        };

        public static readonly int[] k_CubeTri = new int[36]
        {
            0, 1, 2, 0, 2, 3,
            4, 5, 6, 4, 6, 7,
            8, 9, 10, 8, 10, 11,
            12, 13, 14, 12, 14, 15,
            16, 17, 18, 16, 18, 19,
            20, 21, 22, 20, 22, 23
        };

        /// <summary>
        /// Small padding value to prevent light leaks between adjacent faces.
        /// Faces are expanded slightly to ensure overlap and eliminate gaps.
        /// </summary>
        private const float FACE_PADDING = 1e-4f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddQuad(
            Dir dir, in int rX, in int rY, in int rZ, in int rW,
            ref int vPtr, ref int tPtr,
            int minX, int minY, int minZ, float3 size, float3 adj,
            float voxelSize, byte colorIdx,
            NativeArray<BoxVert> vBuf, NativeArray<int> tBuf,
            bool localSpace = false, float3 invLossyScale = default)
        {
            float3 nrm = NormalOf(dir);
            float3 c0, c1, c2, c3;

            float3 halfSize = size * 0.5f;
            // Use a conditional select to avoid branching for vStep calculation
            float3 vStep = localSpace ? (new float3(voxelSize) * invLossyScale) : new float3(voxelSize);
            
            float facePaddingX = vStep.x * FACE_PADDING;
            float facePaddingY = vStep.y * FACE_PADDING;
            float facePaddingZ = vStep.y * FACE_PADDING;

            // Pre-calculate coordinate planes to reduce redundant math inside the switch
            float yLow = -halfSize.y + (rZ - minY) * vStep.y - facePaddingY;
            float yHigh = -halfSize.y + (rW - minY) * vStep.y + facePaddingY;

            float zLow = -halfSize.z + (rX - minZ) * vStep.z - facePaddingZ;
            float zHigh = -halfSize.z + (rY - minZ) * vStep.z + facePaddingZ;

            float xLow = -halfSize.x + (rX - minX) * vStep.x - facePaddingX;
            float xHigh = -halfSize.x + (rY - minX) * vStep.x + facePaddingX;

            float zAltLow = -halfSize.z + (rZ - minZ) * vStep.z - facePaddingZ;
            float zAltHigh = -halfSize.z + (rW - minZ) * vStep.z + facePaddingZ;

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
                    c0 = new float3(xLow, halfSize.y, zAltLow);
                    c1 = new float3(xHigh, halfSize.y, zAltLow);
                    c2 = new float3(xHigh, halfSize.y, zAltHigh);
                    c3 = new float3(xLow, halfSize.y, zAltHigh);
                    break;
                case Dir.NegY:
                    c0 = new float3(xLow, -halfSize.y, zAltHigh);
                    c1 = new float3(xHigh, -halfSize.y, zAltHigh);
                    c2 = new float3(xHigh, -halfSize.y, zAltLow);
                    c3 = new float3(xLow, -halfSize.y, zAltLow);
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

            // Multiply by 1/256 instead of dividing
            float uvX = (colorIdx + 0.5f) * 0.00390625f;
            float2 uv = new float2(uvX, 0.5f);

            // Apply translation to all vertices
            c0 += adj;
            c1 += adj;
            c2 += adj;
            c3 += adj;

            // Sequential writes allow Burst to optimize bounds checks (Hoisting)
            int v = vPtr;
            vBuf[v + 0] = new BoxVert { pos = c0, nrm = nrm, uv = uv };
            vBuf[v + 1] = new BoxVert { pos = c1, nrm = nrm, uv = uv };
            vBuf[v + 2] = new BoxVert { pos = c2, nrm = nrm, uv = uv };
            vBuf[v + 3] = new BoxVert { pos = c3, nrm = nrm, uv = uv };

            float3 cp = math.cross(c1 - c0, c2 - c0);
            int t = tPtr;

            // Deterministic winding to prevent branching inside quad generation
            if (math.dot(cp, nrm) >= 0f)
            {
                tBuf[t + 0] = v;
                tBuf[t + 1] = v + 1;
                tBuf[t + 2] = v + 2;
                tBuf[t + 3] = v;
                tBuf[t + 4] = v + 2;
                tBuf[t + 5] = v + 3;
            }
            else
            {
                tBuf[t + 0] = v;
                tBuf[t + 1] = v + 2;
                tBuf[t + 2] = v + 1;
                tBuf[t + 3] = v;
                tBuf[t + 4] = v + 3;
                tBuf[t + 5] = v + 2;
            }

            vPtr += 4;
            tPtr += 6;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PositionKey(in BoxVert v)
        {
            float3 p = new float3(v.pos.x, v.pos.y, v.pos.z);
            int3 q = (int3)math.round(p / 1e-5f);
            return math.hash(q);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool VertEqual(in BoxVert a, in BoxVert b)
        {
            float3 ap = new float3(a.pos.x, a.pos.y, a.pos.z);
            float3 bp = new float3(b.pos.x, b.pos.y, b.pos.z);
            float3 an = new float3(a.nrm.x, a.nrm.y, a.nrm.z);
            float3 bn = new float3(b.nrm.x, b.nrm.y, b.nrm.z);
            float2 au = new float2(a.uv.x, a.uv.y);
            float2 bu = new float2(b.uv.x, b.uv.y);

            return math.all(math.abs(ap - bp) <= new float3(1e-5f)) &&
                   math.all(math.abs(an - bn) <= new float3(1e-5f)) &&
                   math.all(math.abs(au - bu) <= new float2(1e-5f));
        }

        /// <summary>
        /// Welds vertices by remapping triangle indices so that identical verts (same position/normal/uv within given epsilons) share one vertex.
        /// </summary>
        [BurstCompile]
        private struct WeldIndicesPerBoxJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public Mesh.MeshDataArray Mda;

            public void Execute(int boxIdx)
            {
                var md = Mda[boxIdx];

                var vBuf = md.GetVertexData<BoxVert>();
                var iBuf = md.GetIndexData<int>();

                var sm = md.GetSubMesh(0);
                int iCount = sm.indexCount;

                var bucket = new NativeParallelMultiHashMap<uint, int>(vBuf.Length, Allocator.Temp);
                var remap  = new NativeArray<int>(vBuf.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < remap.Length; ++i) remap[i] = -1;

                for (int t = 0; t < iCount; ++t)
                {
                    int vi = iBuf[t];

                    int ri = remap[vi];
                    if (ri != -1)
                    {
                        iBuf[t] = ri;
                        continue;
                    }

                    BoxVert v = vBuf[vi];
                    uint key = PositionKey(in v);

                    NativeParallelMultiHashMapIterator<uint> it;
                    int candidate;
                    bool found = bucket.TryGetFirstValue(key, out candidate, out it);
                    bool matched = false;

                    while (found)
                    {
                        if (VertEqual(in v, vBuf[candidate]))
                        {
                            remap[vi] = candidate;
                            iBuf[t] = candidate;
                            matched = true;
                            break;
                        }
                        found = bucket.TryGetNextValue(out candidate, ref it);
                    }

                    if (!matched)
                    {
                        bucket.Add(key, vi);
                        remap[vi] = vi;
                        iBuf[t] = vi;
                    }
                }

                bucket.Dispose();
                remap.Dispose();

                // submesh indexCount is unchanged; vertices are now welded by index remap.
                // md.SetSubMesh(0, new SubMeshDescriptor(0, iCount) { topology = MeshTopology.Triangles });
            }
        }
    }
}