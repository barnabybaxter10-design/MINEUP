using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace BoxCutter
{
    /// <summary>
    /// Data structure containing computed transform and rotation values for BoxObj instances.
    /// Stores both local directional vectors and voxel-scaled versions for efficient access.
    /// </summary>
    public struct BoxCutterUpdateResultData
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Local Directional Vectors
        //─────────────────────────────────────────────────────────────────────────────────────
        public float LocalRightX;
        public float LocalRightY;
        public float LocalRightZ;
        
        public float LocalUpX;
        public float LocalUpY;
        public float LocalUpZ;
        
        public float LocalForwardX;
        public float LocalForwardY;
        public float LocalForwardZ;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Voxel-Scaled Directional Vectors
        //─────────────────────────────────────────────────────────────────────────────────────
        public float PreLocalRightX;
        public float PreLocalRightY;
        public float PreLocalRightZ;
        
        public float PreLocalUpX;
        public float PreLocalUpY;
        public float PreLocalUpZ;
        
        public float PreLocalForwardX;
        public float PreLocalForwardY;
        public float PreLocalForwardZ;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Position & Rotation Data
        //─────────────────────────────────────────────────────────────────────────────────────
        public float3 Pos;
        
        public float Qx;
        public float Qy;
        public float Qz;
        public float Qw;
        
        public float IqX;
        public float IqY;
        public float IqZ;
        public float IqW;
    }

    /// <summary>
    /// Burst-compiled job for parallel computation of transform matrices and directional vectors.
    /// Processes multiple BoxObj transforms simultaneously for optimal performance.
    /// </summary>
    [BurstCompile]
    public struct BoxVarUpdateJob : IJobParallelForTransform
    {
        [WriteOnly] public NativeArray<BoxCutterUpdateResultData> Results;
        [ReadOnly] public NativeArray<float> VoxelSizeArr;

        /// <summary>
        /// Executes transform calculations for a single BoxObj instance.
        /// Computes rotation matrices, directional vectors, and voxel-scaled variants.
        /// </summary>
        /// <param name="index">Index of the transform to process</param>
        /// <param name="transform">Transform access for reading position and rotation</param>
        public void Execute(int index, TransformAccess transform)
        {
            Quaternion r = transform.rotation;
            float rx = r.x, ry = r.y, rz = r.z, rw = r.w;

            // Pre-compute quaternion component products for matrix calculations
            float xx = rx * rx;
            float yy = ry * ry;
            float zz = rz * rz;
            float xy = rx * ry;
            float xz = rx * rz;
            float yz = ry * rz;
            float wx = rw * rx;
            float wy = rw * ry;
            float wz = rw * rz;

            // Calculate local right vector (X-axis) from rotation matrix
            float localRightX = 1f - 2f * (yy + zz);
            float localRightY = 2f * (xy + wz);
            float localRightZ = 2f * (xz - wy);

            // Calculate local up vector (Y-axis) from rotation matrix
            float localUpX = 2f * (xy - wz);
            float localUpY = 1f - 2f * (xx + zz);
            float localUpZ = 2f * (yz + wx);

            // Calculate local forward vector (Z-axis) from rotation matrix
            float localForwardX = 2f * (xz + wy);
            float localForwardY = 2f * (yz - wx);
            float localForwardZ = 1f - 2f * (xx + yy);
            
            float3 pos = transform.position;

            // Calculate inverse quaternion for reverse transforms
            Quaternion inverse = math.inverse(r);
            float iqx = inverse.x;
            float iqy = inverse.y;
            float iqz = inverse.z;
            float iqw = inverse.w;
            
            float voxelSize = VoxelSizeArr[index];
            // Package all computed values into result structure
            Results[index] = new BoxCutterUpdateResultData
            {
                // Local directional vectors (unit length)
                LocalRightX = localRightX,
                LocalRightY = localRightY,
                LocalRightZ = localRightZ,
                LocalUpX = localUpX,
                LocalUpY = localUpY,
                LocalUpZ = localUpZ,
                LocalForwardX = localForwardX,
                LocalForwardY = localForwardY,
                LocalForwardZ = localForwardZ,
                
                // Voxel-scaled directional vectors for spatial calculations
                PreLocalRightX = localRightX * voxelSize,
                PreLocalRightY = localRightY * voxelSize,
                PreLocalRightZ = localRightZ * voxelSize,
                PreLocalUpX = localUpX * voxelSize,
                PreLocalUpY = localUpY * voxelSize,
                PreLocalUpZ = localUpZ * voxelSize,
                PreLocalForwardX = localForwardX * voxelSize,
                PreLocalForwardY = localForwardY * voxelSize,
                PreLocalForwardZ = localForwardZ * voxelSize,

                // Position and rotation data
                Pos = pos,
                Qx = rx,
                Qy = ry,
                Qz = rz,
                Qw = rw,
                IqX = iqx,
                IqY = iqy,
                IqZ = iqz,
                IqW = iqw
            };
        }
    }
}