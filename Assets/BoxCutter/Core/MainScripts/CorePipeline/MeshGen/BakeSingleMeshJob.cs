using Unity.Jobs;
using UnityEngine;

namespace BoxCutter
{
    public struct BakeSingleMeshJob : IJob
    {
#if UNITY_6000_3_OR_NEWER
        public EntityId meshId;
#else
        public int meshId;
#endif
        public bool convex;
        public MeshColliderCookingOptions options;

        public void Execute()
        {
#if UNITY_6000_3_OR_NEWER
            if (meshId == EntityId.None) return;
            Physics.BakeMesh(meshId, convex, options);
#else
            if (meshId == -1) return;
            Physics.BakeMesh(meshId, convex, options);
#endif
        }
    }
}