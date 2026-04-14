using Unity.Jobs;
using Unity.Collections;
using UnityEngine;
using Unity.Burst;

namespace BoxCutter
{
    [BurstCompile(DisableSafetyChecks = true)]
    public struct BakeMultiMeshJob : IJobParallelFor
    {
#if UNITY_6000_3_OR_NEWER
        public NativeArray<EntityId> MeshId;
#else
        public NativeArray<int> MeshId;
#endif
        public bool Convex;
        public MeshColliderCookingOptions Options;

        public void Execute(int index)
        {
#if UNITY_6000_3_OR_NEWER
            var id = MeshId[index];
            if (id == EntityId.None) return;
            Physics.BakeMesh(id, Convex, Options);
#else
            int id = MeshId[index];
            if (id == -1) return;
            Physics.BakeMesh(id, Convex, Options);
#endif
        }
    }

    [BurstCompile(DisableSafetyChecks = true)]
    public struct BakeMultiMeshJobBatch : IJobParallelFor
    {
#if UNITY_6000_3_OR_NEWER
        public NativeArray<EntityId> MeshId;
#else
        public NativeArray<int> MeshId;
#endif
        public NativeArray<bool> Convex;
        public MeshColliderCookingOptions Options;

        public void Execute(int index)
        {
#if UNITY_6000_3_OR_NEWER
            var id = MeshId[index];
            if (id == EntityId.None) return;
            Physics.BakeMesh(id, Convex[index], Options);
#else
            int id = MeshId[index];
            if (id == -1) return;
            Physics.BakeMesh(id, Convex[index], Options);
#endif
        }
    }
}