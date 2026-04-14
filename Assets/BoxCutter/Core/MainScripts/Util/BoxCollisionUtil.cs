using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Optimized Collision Detection Utilities
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// High-performance collision detection utilities for oriented bounding boxes and spheres.
    /// Implements Separating Axis Theorem (SAT) for precise OBB-OBB collision testing.
    /// Optimized for Burst compilation with manual vector operations for maximum performance.
    /// </summary>
    public static class BoxCollisionUtil
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Broad Phase Collision Detection
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Fast broad-phase collision check using bounding sphere approximation.
        /// Calculates diagonal radius of each box and tests sphere-sphere intersection.
        /// Used as early rejection test before expensive SAT collision detection.
        /// </summary>
        /// <param name="posA">Center position of first box</param>
        /// <param name="sizeA">Size dimensions of first box</param>
        /// <param name="posB">Center position of second box</param>
        /// <param name="sizeB">Size dimensions of second box</param>
        /// <returns>True if bounding spheres intersect, false if definitely not colliding</returns>
        public static bool BroadDiagRadiusCheck(in float3 posA, in float3 sizeA, in float3 posB, in float3 sizeB)
        {
            // Calculate diagonal radius for each box (half diagonal length)
            float lenSqA = sizeA.x * sizeA.x + sizeA.y * sizeA.y + sizeA.z * sizeA.z;
            float lenSqB = sizeB.x * sizeB.x + sizeB.y * sizeB.y + sizeB.z * sizeB.z;

            float radiusA = math.sqrt(lenSqA * 0.25f);
            float radiusB = math.sqrt(lenSqB * 0.25f);
            
            // Test sphere-sphere intersection
            float dx = posA.x - posB.x;
            float dy = posA.y - posB.y;
            float dz = posA.z - posB.z;
            float radiusSum = radiusA + radiusB;
            return dx * dx + dy * dy + dz * dz <= radiusSum * radiusSum;
        }
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               SAT (Separating Axis Theorem) Implementation
        //─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Small epsilon value to handle floating-point precision issues in SAT tests</summary>
        private const float epsilon = 1e-6f;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSATOverlap(
            in float3 posA, in Quaternion rotA, in float3 sizeA,
            in float3 posB, in Quaternion rotB, in float3 sizeB)
        {
            return CheckSATOverlapRaw(posA.x, posA.y, posA.z, rotA.x, rotA.y, rotA.z, rotA.w, sizeA.x, sizeA.y, sizeA.z, posB.x, posB.y, posB.z, rotB.x, rotB.y, rotB.z, rotB.w, sizeB.x, sizeB.y, sizeB.z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSATOverlap(
            in float3 posA, in Quaternion rotA, in float3 sizeA,
            in float3 posB, float rotBX, float rotBY, float rotBZ, float rotBW, in float3 sizeB)
        {
            return CheckSATOverlapRaw(posA.x, posA.y, posA.z, rotA.x, rotA.y, rotA.z, rotA.w, sizeA.x, sizeA.y, sizeA.z, posB.x, posB.y, posB.z, rotBX, rotBY, rotBZ, rotBW, sizeB.x, sizeB.y, sizeB.z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSATOverlap(
            in float3 posA, float rotAX, float rotAY, float rotAZ, float rotAW, in float3 sizeA,
            in float3 posB, float rotBX, float rotBY, float rotBZ, float rotBW, in float3 sizeB)
        {
            return CheckSATOverlapRaw(posA.x, posA.y, posA.z, rotAX, rotAY, rotAZ, rotAW, sizeA.x, sizeA.y, sizeA.z, posB.x, posB.y, posB.z, rotBX, rotBY, rotBZ, rotBW, sizeB.x, sizeB.y, sizeB.z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSATOverlap(
            in float3 posA, in float4 rotA, in float3 sizeA,
            in float3 posB, in float4 rotB, float sizeBX, float sizeBY, float sizeBZ)
        {
            return CheckSATOverlapRaw(posA.x, posA.y, posA.z, rotA.x, rotA.y, rotA.z, rotA.w, sizeA.x, sizeA.y, sizeA.z, posB.x, posB.y, posB.z, rotB.x, rotB.y, rotB.z, rotB.w, sizeBX, sizeBY, sizeBZ);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSATOverlap(
            in float3 posA, in float4 rotA, in float3 sizeA,
            in float3 posB, in float4 rotB, in float3 sizeB)
        {
            return CheckSATOverlapRaw(posA.x, posA.y, posA.z, rotA.x, rotA.y, rotA.z, rotA.w, sizeA.x, sizeA.y, sizeA.z, posB.x, posB.y, posB.z, rotB.x, rotB.y, rotB.z, rotB.w, sizeB.x, sizeB.y, sizeB.z);
        }

        /// <summary>
        /// Core SAT (Separating Axis Theorem) collision detection for oriented bounding boxes.
        /// Tests all 15 potential separating axes to determine if two OBBs intersect.
        /// Includes early sphere-based rejection for performance optimization.
        /// Hand-optimized for Burst compilation with manual quaternion-to-matrix conversion.
        /// </summary>
        /// <param name="pAx">Box A center position X</param>
        /// <param name="pAy">Box A center position Y</param>
        /// <param name="pAz">Box A center position Z</param>
        /// <param name="qAx">Box A rotation quaternion X</param>
        /// <param name="qAy">Box A rotation quaternion Y</param>
        /// <param name="qAz">Box A rotation quaternion Z</param>
        /// <param name="qAw">Box A rotation quaternion W</param>
        /// <param name="sAx">Box A size X</param>
        /// <param name="sAy">Box A size Y</param>
        /// <param name="sAz">Box A size Z</param>
        /// <param name="pBx">Box B center position X</param>
        /// <param name="pBy">Box B center position Y</param>
        /// <param name="pBz">Box B center position Z</param>
        /// <param name="qBx">Box B rotation quaternion X</param>
        /// <param name="qBy">Box B rotation quaternion Y</param>
        /// <param name="qBz">Box B rotation quaternion Z</param>
        /// <param name="qBw">Box B rotation quaternion W</param>
        /// <param name="sBx">Box B size X</param>
        /// <param name="sBy">Box B size Y</param>
        /// <param name="sBz">Box B size Z</param>
        /// <returns>True if the oriented bounding boxes intersect</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSATOverlapRaw(
            float pAx, float pAy, float pAz,
            float qAx, float qAy, float qAz, float qAw,
            float sAx, float sAy, float sAz,
            float pBx, float pBy, float pBz,
            float qBx, float qBy, float qBz, float qBw,
            float sBx, float sBy, float sBz)
        {
            // Calculate half extents for both boxes
            float halfSizeAx = sAx * 0.5f;
            float halfSizeAy = sAy * 0.5f;
            float halfSizeAz = sAz * 0.5f;

            float halfSizeBx = sBx * 0.5f;
            float halfSizeBy = sBy * 0.5f;
            float halfSizeBz = sBz * 0.5f;

            // Early rejection using bounding sphere test for performance
            float boxSphereRadius = math.sqrt(halfSizeAx * halfSizeAx + halfSizeAy * halfSizeAy + halfSizeAz * halfSizeAz);
            float subSphereRadius = math.sqrt(halfSizeBx * halfSizeBx + halfSizeBy * halfSizeBy + halfSizeBz * halfSizeBz);

            float dx = pBx - pAx;
            float dy = pBy - pAy;
            float dz = pBz - pAz;
            float distSq = dx * dx + dy * dy + dz * dz;

            float combinedRadius = boxSphereRadius + subSphereRadius;
            float combinedRadiusSq = combinedRadius * combinedRadius;

            // Reject if bounding spheres don't intersect
            if (distSq > combinedRadiusSq)
            {
                return false;
            }

            // Manual quaternion to rotation matrix conversion for Box A
            // Optimized to avoid Unity's quaternion operations for Burst compatibility
            float xxA = qAx * qAx;
            float yyA = qAy * qAy;
            float zzA = qAz * qAz;
            float xyA = qAx * qAy;
            float xzA = qAx * qAz;
            float yzA = qAy * qAz;
            float wxA = qAw * qAx;
            float wyA = qAw * qAy;
            float wzA = qAw * qAz;

            // Box A rotation matrix columns (right, up, forward vectors)
            float A0x = 1 - 2 * (yyA + zzA);
            float A0y = 2 * (xyA + wzA);
            float A0z = 2 * (xzA - wyA);

            float A1x = 2 * (xyA - wzA);
            float A1y = 1 - 2 * (xxA + zzA);
            float A1z = 2 * (yzA + wxA);

            float A2x = 2 * (xzA + wyA);
            float A2y = 2 * (yzA - wxA);
            float A2z = 1 - 2 * (xxA + yyA);

            float xxB = qBx * qBx;
            float yyB = qBy * qBy;
            float zzB = qBz * qBz;
            float xyB = qBx * qBy;
            float xzB = qBx * qBz;
            float yzB = qBy * qBz;
            float wxB = qBw * qBx;
            float wyB = qBw * qBy;
            float wzB = qBw * qBz;

            float B0x = 1 - 2 * (yyB + zzB);
            float B0y = 2 * (xyB + wzB);
            float B0z = 2 * (xzB - wyB);

            float B1x = 2 * (xyB - wzB);
            float B1y = 1 - 2 * (xxB + zzB);
            float B1z = 2 * (yzB + wxB);

            float B2x = 2 * (xzB + wyB);
            float B2y = 2 * (yzB - wxB);
            float B2z = 1 - 2 * (xxB + yyB);

            float R00 = A0x * B0x + A0y * B0y + A0z * B0z;
            float R01 = A0x * B1x + A0y * B1y + A0z * B1z;
            float R02 = A0x * B2x + A0y * B2y + A0z * B2z;
            float R10 = A1x * B0x + A1y * B0y + A1z * B0z;
            float R11 = A1x * B1x + A1y * B1y + A1z * B1z;
            float R12 = A1x * B2x + A1y * B2y + A1z * B2z;
            float R20 = A2x * B0x + A2y * B0y + A2z * B0z;
            float R21 = A2x * B1x + A2y * B1y + A2z * B1z;
            float R22 = A2x * B2x + A2y * B2y + A2z * B2z;

            float AbsR00 = (R00 >= 0 ? R00 : -R00) + epsilon;
            float AbsR01 = (R01 >= 0 ? R01 : -R01) + epsilon;
            float AbsR02 = (R02 >= 0 ? R02 : -R02) + epsilon;
            float AbsR10 = (R10 >= 0 ? R10 : -R10) + epsilon;
            float AbsR11 = (R11 >= 0 ? R11 : -R11) + epsilon;
            float AbsR12 = (R12 >= 0 ? R12 : -R12) + epsilon;
            float AbsR20 = (R20 >= 0 ? R20 : -R20) + epsilon;
            float AbsR21 = (R21 >= 0 ? R21 : -R21) + epsilon;
            float AbsR22 = (R22 >= 0 ? R22 : -R22) + epsilon;
            float tx = pBx - pAx;
            float ty = pBy - pAy;
            float tz = pBz - pAz;

            float tA0 = A0x * tx + A0y * ty + A0z * tz;
            float tA1 = A1x * tx + A1y * ty + A1z * tz;
            float tA2 = A2x * tx + A2y * ty + A2z * tz;

            float ra, rb;

            ra = halfSizeAx;
            rb = halfSizeBx * AbsR00 + halfSizeBy * AbsR01 + halfSizeBz * AbsR02;
            if ((tA0 >= 0 ? tA0 : -tA0) > ra + rb)
                return false;

            ra = halfSizeAy;
            rb = halfSizeBx * AbsR10 + halfSizeBy * AbsR11 + halfSizeBz * AbsR12;
            if ((tA1 >= 0 ? tA1 : -tA1) > ra + rb)
                return false;

            ra = halfSizeAz;
            rb = halfSizeBx * AbsR20 + halfSizeBy * AbsR21 + halfSizeBz * AbsR22;
            if ((tA2 >= 0 ? tA2 : -tA2) > ra + rb)
                return false;

            float tB0 = B0x * tx + B0y * ty + B0z * tz;
            float tB1 = B1x * tx + B1y * ty + B1z * tz;
            float tB2 = B2x * tx + B2y * ty + B2z * tz;

            ra = halfSizeAx * AbsR00 + halfSizeAy * AbsR10 + halfSizeAz * AbsR20;
            rb = halfSizeBx;
            if ((tB0 >= 0 ? tB0 : -tB0) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR01 + halfSizeAy * AbsR11 + halfSizeAz * AbsR21;
            rb = halfSizeBy;
            if ((tB1 >= 0 ? tB1 : -tB1) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR02 + halfSizeAy * AbsR12 + halfSizeAz * AbsR22;
            rb = halfSizeBz;
            if ((tB2 >= 0 ? tB2 : -tB2) > ra + rb)
                return false;

            float tVal;

            ra = halfSizeAy * AbsR20 + halfSizeAz * AbsR10;
            rb = halfSizeBy * AbsR02 + halfSizeBz * AbsR01;
            tVal = tA2 * R10 - tA1 * R20;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            ra = halfSizeAy * AbsR21 + halfSizeAz * AbsR11;
            rb = halfSizeBx * AbsR02 + halfSizeBz * AbsR00;
            tVal = tA2 * R11 - tA1 * R21;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            ra = halfSizeAy * AbsR22 + halfSizeAz * AbsR12;
            rb = halfSizeBx * AbsR01 + halfSizeBy * AbsR00;
            tVal = tA2 * R12 - tA1 * R22;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR20 + halfSizeAz * AbsR00;
            rb = halfSizeBy * AbsR12 + halfSizeBz * AbsR11;
            tVal = tA0 * R20 - tA2 * R00;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR21 + halfSizeAz * AbsR01;
            rb = halfSizeBx * AbsR12 + halfSizeBz * AbsR10;
            tVal = tA0 * R21 - tA2 * R01;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR22 + halfSizeAz * AbsR02;
            rb = halfSizeBx * AbsR11 + halfSizeBy * AbsR10;
            tVal = tA0 * R22 - tA2 * R02;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR10 + halfSizeAy * AbsR00;
            rb = halfSizeBy * AbsR22 + halfSizeBz * AbsR21;
            tVal = tA1 * R00 - tA0 * R10;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR11 + halfSizeAy * AbsR01;
            rb = halfSizeBx * AbsR22 + halfSizeBz * AbsR20;
            tVal = tA1 * R01 - tA0 * R11;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR12 + halfSizeAy * AbsR02;
            rb = halfSizeBx * AbsR21 + halfSizeBy * AbsR20;
            tVal = tA1 * R02 - tA0 * R12;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            return true;
        }
        
        /// <summary>
        /// Core SAT collision detection optimized for when Box B has identity rotation.
        /// Since Box B's rotation matrix is identity:
        /// - B0 = (1,0,0), B1 = (0,1,0), B2 = (0,0,1)
        /// - R matrix simplifies to just A's rotation matrix columns
        /// - Translation in B's frame equals world translation
        /// </summary>
        /// <param name="pAx">Box A center position X</param>
        /// <param name="pAy">Box A center position Y</param>
        /// <param name="pAz">Box A center position Z</param>
        /// <param name="qAx">Box A rotation quaternion X</param>
        /// <param name="qAy">Box A rotation quaternion Y</param>
        /// <param name="qAz">Box A rotation quaternion Z</param>
        /// <param name="qAw">Box A rotation quaternion W</param>
        /// <param name="sAx">Box A size X</param>
        /// <param name="sAy">Box A size Y</param>
        /// <param name="sAz">Box A size Z</param>
        /// <param name="pBx">Box B center position X</param>
        /// <param name="pBy">Box B center position Y</param>
        /// <param name="pBz">Box B center position Z</param>
        /// <param name="sBx">Box B size X (axis-aligned)</param>
        /// <param name="sBy">Box B size Y (axis-aligned)</param>
        /// <param name="sBz">Box B size Z (axis-aligned)</param>
        /// <returns>True if the oriented bounding boxes intersect</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckSATOverlapRawBIdentity(
            float pAx, float pAy, float pAz,
            float qAx, float qAy, float qAz, float qAw,
            float sAx, float sAy, float sAz,
            float pBx, float pBy, float pBz,
            float sBx, float sBy, float sBz)
        {
            // Calculate half extents for both boxes
            float halfSizeAx = sAx * 0.5f;
            float halfSizeAy = sAy * 0.5f;
            float halfSizeAz = sAz * 0.5f;

            float halfSizeBx = sBx * 0.5f;
            float halfSizeBy = sBy * 0.5f;
            float halfSizeBz = sBz * 0.5f;

            // Early rejection using bounding sphere test
            float boxSphereRadius = math.sqrt(halfSizeAx * halfSizeAx + halfSizeAy * halfSizeAy + halfSizeAz * halfSizeAz);
            float subSphereRadius = math.sqrt(halfSizeBx * halfSizeBx + halfSizeBy * halfSizeBy + halfSizeBz * halfSizeBz);

            float dx = pBx - pAx;
            float dy = pBy - pAy;
            float dz = pBz - pAz;
            float distSq = dx * dx + dy * dy + dz * dz;

            float combinedRadius = boxSphereRadius + subSphereRadius;
            if (distSq > combinedRadius * combinedRadius)
            {
                return false;
            }

            // Build rotation matrix for Box A only (Box B is identity)
            float xxA = qAx * qAx;
            float yyA = qAy * qAy;
            float zzA = qAz * qAz;
            float xyA = qAx * qAy;
            float xzA = qAx * qAz;
            float yzA = qAy * qAz;
            float wxA = qAw * qAx;
            float wyA = qAw * qAy;
            float wzA = qAw * qAz;

            // Box A rotation matrix columns
            float A0x = 1 - 2 * (yyA + zzA);
            float A0y = 2 * (xyA + wzA);
            float A0z = 2 * (xzA - wyA);

            float A1x = 2 * (xyA - wzA);
            float A1y = 1 - 2 * (xxA + zzA);
            float A1z = 2 * (yzA + wxA);

            float A2x = 2 * (xzA + wyA);
            float A2y = 2 * (yzA - wxA);
            float A2z = 1 - 2 * (xxA + yyA);

            // Since B's rotation is identity: B0=(1,0,0), B1=(0,1,0), B2=(0,0,1)
            // R = A^T * B simplifies to A^T (transposed A matrix)
            // R[i][j] = dot(A_row_i, B_col_j) = A[j][i] when B is identity
            float R00 = A0x;  // dot(A0, (1,0,0)) = A0x
            float R01 = A1x;  // dot(A0, (0,1,0)) = A0y -> but R is A^T*B, so R01 = A1x
            float R02 = A2x;
            float R10 = A0y;
            float R11 = A1y;
            float R12 = A2y;
            float R20 = A0z;
            float R21 = A1z;
            float R22 = A2z;

            float AbsR00 = (R00 >= 0 ? R00 : -R00) + epsilon;
            float AbsR01 = (R01 >= 0 ? R01 : -R01) + epsilon;
            float AbsR02 = (R02 >= 0 ? R02 : -R02) + epsilon;
            float AbsR10 = (R10 >= 0 ? R10 : -R10) + epsilon;
            float AbsR11 = (R11 >= 0 ? R11 : -R11) + epsilon;
            float AbsR12 = (R12 >= 0 ? R12 : -R12) + epsilon;
            float AbsR20 = (R20 >= 0 ? R20 : -R20) + epsilon;
            float AbsR21 = (R21 >= 0 ? R21 : -R21) + epsilon;
            float AbsR22 = (R22 >= 0 ? R22 : -R22) + epsilon;

            // Translation vector
            float tx = pBx - pAx;
            float ty = pBy - pAy;
            float tz = pBz - pAz;

            // Translation in A's coordinate frame
            float tA0 = A0x * tx + A0y * ty + A0z * tz;
            float tA1 = A1x * tx + A1y * ty + A1z * tz;
            float tA2 = A2x * tx + A2y * ty + A2z * tz;

            float ra, rb;

            // Test axes A0, A1, A2
            ra = halfSizeAx;
            rb = halfSizeBx * AbsR00 + halfSizeBy * AbsR01 + halfSizeBz * AbsR02;
            if ((tA0 >= 0 ? tA0 : -tA0) > ra + rb)
                return false;

            ra = halfSizeAy;
            rb = halfSizeBx * AbsR10 + halfSizeBy * AbsR11 + halfSizeBz * AbsR12;
            if ((tA1 >= 0 ? tA1 : -tA1) > ra + rb)
                return false;

            ra = halfSizeAz;
            rb = halfSizeBx * AbsR20 + halfSizeBy * AbsR21 + halfSizeBz * AbsR22;
            if ((tA2 >= 0 ? tA2 : -tA2) > ra + rb)
                return false;

            // Translation in B's coordinate frame (B is identity, so tB = t)
            float tB0 = tx;
            float tB1 = ty;
            float tB2 = tz;

            // Test axes B0, B1, B2
            ra = halfSizeAx * AbsR00 + halfSizeAy * AbsR10 + halfSizeAz * AbsR20;
            rb = halfSizeBx;
            if ((tB0 >= 0 ? tB0 : -tB0) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR01 + halfSizeAy * AbsR11 + halfSizeAz * AbsR21;
            rb = halfSizeBy;
            if ((tB1 >= 0 ? tB1 : -tB1) > ra + rb)
                return false;

            ra = halfSizeAx * AbsR02 + halfSizeAy * AbsR12 + halfSizeAz * AbsR22;
            rb = halfSizeBz;
            if ((tB2 >= 0 ? tB2 : -tB2) > ra + rb)
                return false;

            // Test cross product axes
            float tVal;

            // A0 x B0
            ra = halfSizeAy * AbsR20 + halfSizeAz * AbsR10;
            rb = halfSizeBy * AbsR02 + halfSizeBz * AbsR01;
            tVal = tA2 * R10 - tA1 * R20;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            // A0 x B1
            ra = halfSizeAy * AbsR21 + halfSizeAz * AbsR11;
            rb = halfSizeBx * AbsR02 + halfSizeBz * AbsR00;
            tVal = tA2 * R11 - tA1 * R21;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            // A0 x B2
            ra = halfSizeAy * AbsR22 + halfSizeAz * AbsR12;
            rb = halfSizeBx * AbsR01 + halfSizeBy * AbsR00;
            tVal = tA2 * R12 - tA1 * R22;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            // A1 x B0
            ra = halfSizeAx * AbsR20 + halfSizeAz * AbsR00;
            rb = halfSizeBy * AbsR12 + halfSizeBz * AbsR11;
            tVal = tA0 * R20 - tA2 * R00;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            // A1 x B1
            ra = halfSizeAx * AbsR21 + halfSizeAz * AbsR01;
            rb = halfSizeBx * AbsR12 + halfSizeBz * AbsR10;
            tVal = tA0 * R21 - tA2 * R01;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            // A1 x B2
            ra = halfSizeAx * AbsR22 + halfSizeAz * AbsR02;
            rb = halfSizeBx * AbsR11 + halfSizeBy * AbsR10;
            tVal = tA0 * R22 - tA2 * R02;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            // A2 x B0
            ra = halfSizeAx * AbsR10 + halfSizeAy * AbsR00;
            rb = halfSizeBy * AbsR22 + halfSizeBz * AbsR21;
            tVal = tA1 * R00 - tA0 * R10;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            // A2 x B1
            ra = halfSizeAx * AbsR11 + halfSizeAy * AbsR01;
            rb = halfSizeBx * AbsR22 + halfSizeBz * AbsR20;
            tVal = tA1 * R01 - tA0 * R11;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            // A2 x B2
            ra = halfSizeAx * AbsR12 + halfSizeAy * AbsR02;
            rb = halfSizeBx * AbsR21 + halfSizeBy * AbsR20;
            tVal = tA1 * R02 - tA0 * R12;
            if ((tVal >= 0 ? tVal : -tVal) > ra + rb)
                return false;

            return true;
        }

        /// <summary>
        /// Tests if cuboid A is completely contained within cuboid B.
        /// Checks all 8 corners of cuboid A to determine if they all lie within cuboid B's bounds.
        /// Uses inverse rotation transformation to convert world coordinates to local cuboid B space.
        /// </summary>
        /// <param name="posA">Center position of cuboid A</param>
        /// <param name="rotA">Rotation quaternion of cuboid A</param>
        /// <param name="sizeA">Size dimensions of cuboid A</param>
        /// <param name="posB">Center position of cuboid B (container)</param>
        /// <param name="rotB">Rotation quaternion of cuboid B (container)</param>
        /// <param name="sizeB">Size dimensions of cuboid B (container)</param>
        /// <returns>1 if cuboid A is fully contained within cuboid B, 0 otherwise</returns>
        static int IsCuboidFullyWithinCuboid(
            float3 posA, float4 rotA, float3 sizeA,
            float3 posB, float4 rotB, float3 sizeB)
        {
            sizeA *= 0.5f;
            sizeB *= 0.5f;

            float inv_xB = -rotB.x, inv_yB = -rotB.y, inv_zB = -rotB.z, inv_wB = rotB.w;

            float xA = rotA.x, yA = rotA.y, zA = rotA.z, wA = rotA.w;
            float xxA = xA * xA, yyA = yA * yA, zzA = zA * zA;
            float xyA = xA * yA, xzA = xA * zA, yzA = yA * zA;
            float wxA = wA * xA, wyA = wA * yA, wzA = wA * zA;

            float RA00 = 1f - 2f * (yyA + zzA);
            float RA01 = 2f * (xyA + wzA);
            float RA02 = 2f * (xzA - wyA);
            float RA10 = 2f * (xyA - wzA);
            float RA11 = 1f - 2f * (xxA + zzA);
            float RA12 = 2f * (yzA + wxA);
            float RA20 = 2f * (xzA + wyA);
            float RA21 = 2f * (yzA - wxA);
            float RA22 = 1f - 2f * (xxA + yyA);

            {
                float cx = -1f, cy = -1f, cz = -1f;

                float cornerAX = posA.x + (RA00 * cx * sizeA.x + RA01 * cy * sizeA.y + RA02 * cz * sizeA.z);
                float cornerAY = posA.y + (RA10 * cx * sizeA.x + RA11 * cy * sizeA.y + RA12 * cz * sizeA.z);
                float cornerAZ = posA.z + (RA20 * cx * sizeA.x + RA21 * cy * sizeA.y + RA22 * cz * sizeA.z);

                float relX = cornerAX - posB.x;
                float relY = cornerAY - posB.y;
                float relZ = cornerAZ - posB.z;

                float rotX = inv_wB * relX + inv_yB * relZ - inv_zB * relY;
                float rotY = inv_wB * relY + inv_zB * relX - inv_xB * relZ;
                float rotZ = inv_wB * relZ + inv_xB * relY - inv_yB * relX;

                float nx = rotX / sizeB.x;
                float ny = rotY / sizeB.y;
                float nz = rotZ / sizeB.z;

                if (nx < -1f || nx > 1f || ny < -1f || ny > 1f || nz < -1f || nz > 1f) return 0;
            }

            {
                float cx = 1f, cy = -1f, cz = -1f;

                float cornerAX = posA.x + (RA00 * cx * sizeA.x + RA01 * cy * sizeA.y + RA02 * cz * sizeA.z);
                float cornerAY = posA.y + (RA10 * cx * sizeA.x + RA11 * cy * sizeA.y + RA12 * cz * sizeA.z);
                float cornerAZ = posA.z + (RA20 * cx * sizeA.x + RA21 * cy * sizeA.y + RA22 * cz * sizeA.z);

                float relX = cornerAX - posB.x;
                float relY = cornerAY - posB.y;
                float relZ = cornerAZ - posB.z;

                float rotX = inv_wB * relX + inv_yB * relZ - inv_zB * relY;
                float rotY = inv_wB * relY + inv_zB * relX - inv_xB * relZ;
                float rotZ = inv_wB * relZ + inv_xB * relY - inv_yB * relX;

                float nx = rotX / sizeB.x;
                float ny = rotY / sizeB.y;
                float nz = rotZ / sizeB.z;

                if (nx < -1f || nx > 1f || ny < -1f || ny > 1f || nz < -1f || nz > 1f) return 0;
            }

            {
                float cx = -1f, cy = 1f, cz = -1f;

                float cornerAX = posA.x + (RA00 * cx * sizeA.x + RA01 * cy * sizeA.y + RA02 * cz * sizeA.z);
                float cornerAY = posA.y + (RA10 * cx * sizeA.x + RA11 * cy * sizeA.y + RA12 * cz * sizeA.z);
                float cornerAZ = posA.z + (RA20 * cx * sizeA.x + RA21 * cy * sizeA.y + RA22 * cz * sizeA.z);

                float relX = cornerAX - posB.x;
                float relY = cornerAY - posB.y;
                float relZ = cornerAZ - posB.z;

                float rotX = inv_wB * relX + inv_yB * relZ - inv_zB * relY;
                float rotY = inv_wB * relY + inv_zB * relX - inv_xB * relZ;
                float rotZ = inv_wB * relZ + inv_xB * relY - inv_yB * relX;

                float nx = rotX / sizeB.x;
                float ny = rotY / sizeB.y;
                float nz = rotZ / sizeB.z;

                if (nx < -1f || nx > 1f || ny < -1f || ny > 1f || nz < -1f || nz > 1f) return 0;
            }

            {
                float cx = 1f, cy = 1f, cz = -1f;

                float cornerAX = posA.x + (RA00 * cx * sizeA.x + RA01 * cy * sizeA.y + RA02 * cz * sizeA.z);
                float cornerAY = posA.y + (RA10 * cx * sizeA.x + RA11 * cy * sizeA.y + RA12 * cz * sizeA.z);
                float cornerAZ = posA.z + (RA20 * cx * sizeA.x + RA21 * cy * sizeA.y + RA22 * cz * sizeA.z);

                float relX = cornerAX - posB.x;
                float relY = cornerAY - posB.y;
                float relZ = cornerAZ - posB.z;

                float rotX = inv_wB * relX + inv_yB * relZ - inv_zB * relY;
                float rotY = inv_wB * relY + inv_zB * relX - inv_xB * relZ;
                float rotZ = inv_wB * relZ + inv_xB * relY - inv_yB * relX;

                float nx = rotX / sizeB.x;
                float ny = rotY / sizeB.y;
                float nz = rotZ / sizeB.z;

                if (nx < -1f || nx > 1f || ny < -1f || ny > 1f || nz < -1f || nz > 1f) return 0;
            }

            {
                float cx = -1f, cy = -1f, cz = 1f;

                float cornerAX = posA.x + (RA00 * cx * sizeA.x + RA01 * cy * sizeA.y + RA02 * cz * sizeA.z);
                float cornerAY = posA.y + (RA10 * cx * sizeA.x + RA11 * cy * sizeA.y + RA12 * cz * sizeA.z);
                float cornerAZ = posA.z + (RA20 * cx * sizeA.x + RA21 * cy * sizeA.y + RA22 * cz * sizeA.z);

                float relX = cornerAX - posB.x;
                float relY = cornerAY - posB.y;
                float relZ = cornerAZ - posB.z;

                float rotX = inv_wB * relX + inv_yB * relZ - inv_zB * relY;
                float rotY = inv_wB * relY + inv_zB * relX - inv_xB * relZ;
                float rotZ = inv_wB * relZ + inv_xB * relY - inv_yB * relX;

                float nx = rotX / sizeB.x;
                float ny = rotY / sizeB.y;
                float nz = rotZ / sizeB.z;

                if (nx < -1f || nx > 1f || ny < -1f || ny > 1f || nz < -1f || nz > 1f) return 0;
            }

            {
                float cx = 1f, cy = -1f, cz = 1f;

                float cornerAX = posA.x + (RA00 * cx * sizeA.x + RA01 * cy * sizeA.y + RA02 * cz * sizeA.z);
                float cornerAY = posA.y + (RA10 * cx * sizeA.x + RA11 * cy * sizeA.y + RA12 * cz * sizeA.z);
                float cornerAZ = posA.z + (RA20 * cx * sizeA.x + RA21 * cy * sizeA.y + RA22 * cz * sizeA.z);

                float relX = cornerAX - posB.x;
                float relY = cornerAY - posB.y;
                float relZ = cornerAZ - posB.z;

                float rotX = inv_wB * relX + inv_yB * relZ - inv_zB * relY;
                float rotY = inv_wB * relY + inv_zB * relX - inv_xB * relZ;
                float rotZ = inv_wB * relZ + inv_xB * relY - inv_yB * relX;

                float nx = rotX / sizeB.x;
                float ny = rotY / sizeB.y;
                float nz = rotZ / sizeB.z;

                if (nx < -1f || nx > 1f || ny < -1f || ny > 1f || nz < -1f || nz > 1f) return 0;
            }

            {
                float cx = -1f, cy = 1f, cz = 1f;

                float cornerAX = posA.x + (RA00 * cx * sizeA.x + RA01 * cy * sizeA.y + RA02 * cz * sizeA.z);
                float cornerAY = posA.y + (RA10 * cx * sizeA.x + RA11 * cy * sizeA.y + RA12 * cz * sizeA.z);
                float cornerAZ = posA.z + (RA20 * cx * sizeA.x + RA21 * cy * sizeA.y + RA22 * cz * sizeA.z);

                float relX = cornerAX - posB.x;
                float relY = cornerAY - posB.y;
                float relZ = cornerAZ - posB.z;

                float rotX = inv_wB * relX + inv_yB * relZ - inv_zB * relY;
                float rotY = inv_wB * relY + inv_zB * relX - inv_xB * relZ;
                float rotZ = inv_wB * relZ + inv_xB * relY - inv_yB * relX;

                float nx = rotX / sizeB.x;
                float ny = rotY / sizeB.y;
                float nz = rotZ / sizeB.z;

                if (nx < -1f || nx > 1f || ny < -1f || ny > 1f || nz < -1f || nz > 1f) return 0;
            }

            {
                float cx = 1f, cy = 1f, cz = 1f;

                float cornerAX = posA.x + (RA00 * cx * sizeA.x + RA01 * cy * sizeA.y + RA02 * cz * sizeA.z);
                float cornerAY = posA.y + (RA10 * cx * sizeA.x + RA11 * cy * sizeA.y + RA12 * cz * sizeA.z);
                float cornerAZ = posA.z + (RA20 * cx * sizeA.x + RA21 * cy * sizeA.y + RA22 * cz * sizeA.z);

                float relX = cornerAX - posB.x;
                float relY = cornerAY - posB.y;
                float relZ = cornerAZ - posB.z;

                float rotX = inv_wB * relX + inv_yB * relZ - inv_zB * relY;
                float rotY = inv_wB * relY + inv_zB * relX - inv_xB * relZ;
                float rotZ = inv_wB * relZ + inv_xB * relY - inv_yB * relX;

                float nx = rotX / sizeB.x;
                float ny = rotY / sizeB.y;
                float nz = rotZ / sizeB.z;

                if (nx < -1f || nx > 1f || ny < -1f || ny > 1f || nz < -1f || nz > 1f) return 0;
            }

            return 1;
        }

        /// <summary>
        /// Determines if a given point lies within the bounds of an oriented cuboid.
        /// Transforms the point to the cuboid's local coordinate system using inverse rotation,
        /// then performs axis-aligned bounding box test in normalized coordinates.
        /// </summary>
        /// <param name="point">The world space point to test</param>
        /// <param name="cuboidPos">Center position of the cuboid</param>
        /// <param name="cuboidRot">Rotation quaternion of the cuboid</param>
        /// <param name="cuboidScale">Scale dimensions of the cuboid</param>
        /// <returns>True if the point is inside the cuboid, false otherwise</returns>
        public static bool IsPointInsideCuboid(float3 point, float3 cuboidPos, float4 cuboidRot, float3 cuboidScale)
        {
            float localPoint_x = point.x - cuboidPos.x;
            float localPoint_y = point.y - cuboidPos.y;
            float localPoint_z = point.z - cuboidPos.z;

            float x = cuboidRot.x;
            float y = cuboidRot.y;
            float z = cuboidRot.z;
            float w = cuboidRot.w;

            float qx = -x;
            float qy = -y;
            float qz = -z;
            float qw = w;

            float ux = qx;
            float uy = qy;
            float uz = qz;
            float s = qw;

            float tx = 2f * (uy * localPoint_z - uz * localPoint_y);
            float ty = 2f * (uz * localPoint_x - ux * localPoint_z);
            float tz = 2f * (ux * localPoint_y - uy * localPoint_x);

            float cross_x = uy * tz - uz * ty;
            float cross_y = uz * tx - ux * tz;
            float cross_z = ux * ty - uy * tx;

            float rotatedPoint_x = localPoint_x + s * tx + cross_x;
            float rotatedPoint_y = localPoint_y + s * ty + cross_y;
            float rotatedPoint_z = localPoint_z + s * tz + cross_z;

            float halfScale_x = cuboidScale.x * 0.5f;
            float halfScale_y = cuboidScale.y * 0.5f;
            float halfScale_z = cuboidScale.z * 0.5f;

            float normalized_x = rotatedPoint_x / halfScale_x;
            float normalized_y = rotatedPoint_y / halfScale_y;
            float normalized_z = rotatedPoint_z / halfScale_z;

            float abs_x = normalized_x >= 0f ? normalized_x : -normalized_x;
            float abs_y = normalized_y >= 0f ? normalized_y : -normalized_y;
            float abs_z = normalized_z >= 0f ? normalized_z : -normalized_z;

            return abs_x <= 1f && abs_y <= 1f && abs_z <= 1f;
        }

        /// <summary>
        /// Tests if a ray (line segment) intersects with an oriented cuboid using ray-box intersection algorithm.
        /// Transforms the ray to the cuboid's local coordinate system and performs slab method intersection test.
        /// Uses parametric ray representation with t-values between 0 and 1 for the line segment.
        /// </summary>
        /// <param name="cuboidPos">Center position of the cuboid</param>
        /// <param name="cuboidRot">Rotation quaternion of the cuboid</param>
        /// <param name="cuboidSize">Size dimensions of the cuboid</param>
        /// <param name="rayStart">Start point of the ray in world space</param>
        /// <param name="rayEnd">End point of the ray in world space</param>
        /// <returns>True if the ray intersects the cuboid, false otherwise</returns>
        public static bool RayIntersectsCuboid(
            float3 cuboidPos, quaternion cuboidRot, float3 cuboidSize,
            float3 rayStart, float3 rayEnd)
        {
            quaternion invRotation = new quaternion(
                -cuboidRot.value.x,
                -cuboidRot.value.y,
                -cuboidRot.value.z,
                cuboidRot.value.w);

            float3 diffStart = new float3(
                rayStart.x - cuboidPos.x,
                rayStart.y - cuboidPos.y,
                rayStart.z - cuboidPos.z);

            float3 diffEnd = new float3(
                rayEnd.x - cuboidPos.x,
                rayEnd.y - cuboidPos.y,
                rayEnd.z - cuboidPos.z);

            float3 localRayStart = RotateVectorByQuaternion(diffStart, invRotation);
            float3 localRayEnd = RotateVectorByQuaternion(diffEnd, invRotation);

            float3 rayDir = new float3(
                localRayEnd.x - localRayStart.x,
                localRayEnd.y - localRayStart.y,
                localRayEnd.z - localRayStart.z);

            float3 min = new float3(
                -cuboidSize.x * 0.5f,
                -cuboidSize.y * 0.5f,
                -cuboidSize.z * 0.5f);

            float3 max = new float3(
                cuboidSize.x * 0.5f,
                cuboidSize.y * 0.5f,
                cuboidSize.z * 0.5f);

            float tMin = 0.0f;
            float tMax = 1.0f;

            for (int i = 0; i < 3; i++)
            {
                float dir = (i == 0) ? rayDir.x : (i == 1) ? rayDir.y : rayDir.z;
                float origin = (i == 0) ? localRayStart.x : (i == 1) ? localRayStart.y : localRayStart.z;

                float absDir = dir >= 0 ? dir : -dir;

                if (absDir < 1e-8f)
                {
                    float minVal = (i == 0) ? min.x : (i == 1) ? min.y : min.z;
                    float maxVal = (i == 0) ? max.x : (i == 1) ? max.y : max.z;

                    if (origin < minVal || origin > maxVal)
                        return false;
                }
                else
                {
                    float ood = 1.0f / dir;

                    float minVal = (i == 0) ? min.x : (i == 1) ? min.y : min.z;
                    float maxVal = (i == 0) ? max.x : (i == 1) ? max.y : max.z;

                    float t1 = (minVal - origin) * ood;
                    float t2 = (maxVal - origin) * ood;

                    if (t1 > t2)
                    {
                        float temp = t1;
                        t1 = t2;
                        t2 = temp;
                    }

                    tMin = t1 > tMin ? t1 : tMin;
                    tMax = t2 < tMax ? t2 : tMax;

                    if (tMin > tMax)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Rotates a 3D vector using quaternion rotation with manual calculation for Burst compatibility.
        /// Implements the standard quaternion rotation formula: v' = q * v * q^-1 using vector operations.
        /// Optimized with manual cross product calculations to avoid Unity's quaternion operations.
        /// </summary>
        /// <param name="v">The vector to rotate</param>
        /// <param name="q">The rotation quaternion</param>
        /// <returns>The rotated vector</returns>
        static float3 RotateVectorByQuaternion(float3 v, quaternion q)
        {
            float qx = q.value.x;
            float qy = q.value.y;
            float qz = q.value.z;
            float qw = q.value.w;

            float uvx = qy * v.z - qz * v.y;
            float uvy = qz * v.x - qx * v.z;
            float uvz = qx * v.y - qy * v.x;

            float uuvx = qy * uvz - qz * uvy;
            float uuvy = qz * uvx - qx * uvz;
            float uuvz = qx * uvy - qy * uvx;

            float3 rotatedV;
            rotatedV.x = v.x + 2.0f * (qw * uvx + uuvx);
            rotatedV.y = v.y + 2.0f * (qw * uvy + uuvy);
            rotatedV.z = v.z + 2.0f * (qw * uvz + uuvz);

            return rotatedV;
        }

        /// <summary>
        /// Tests if a sphere intersects with an oriented cuboid using closest point method.
        /// Overload that accepts rotation as individual quaternion components.
        /// </summary>
        /// <param name="sphereCenter">Center position of the sphere</param>
        /// <param name="sphereRadius">Radius of the sphere</param>
        /// <param name="cuboidPosition">Center position of the cuboid</param>
        /// <param name="cuboidRotationX">X component of cuboid rotation quaternion</param>
        /// <param name="cuboidRotationY">Y component of cuboid rotation quaternion</param>
        /// <param name="cuboidRotationZ">Z component of cuboid rotation quaternion</param>
        /// <param name="cuboidRotationW">W component of cuboid rotation quaternion</param>
        /// <param name="cuboidSize">Size dimensions of the cuboid</param>
        /// <returns>True if the sphere intersects the cuboid, false otherwise</returns>
        public static bool IsSphereIntersectingCuboid(in float3 sphereCenter, float sphereRadius, in float3 cuboidPosition, float cuboidRotationX, float cuboidRotationY, float cuboidRotationZ, float cuboidRotationW, in float3 cuboidSize)
        {
            return IsSphereIntersectingCuboidRaw(sphereCenter.x, sphereCenter.y, sphereCenter.z,
                sphereRadius,
                cuboidPosition.x, cuboidPosition.y, cuboidPosition.z,
                cuboidRotationX, cuboidRotationY, cuboidRotationZ, cuboidRotationW,
                cuboidSize.x, cuboidSize.y, cuboidSize.z);
        }

        /// <summary>
        /// Tests if a sphere intersects with an oriented cuboid using closest point method.
        /// Overload that accepts rotation as a float4 quaternion.
        /// </summary>
        /// <param name="sphereCenter">Center position of the sphere</param>
        /// <param name="sphereRadius">Radius of the sphere</param>
        /// <param name="cuboidPosition">Center position of the cuboid</param>
        /// <param name="cuboidRotation">Rotation quaternion of the cuboid as float4</param>
        /// <param name="cuboidSize">Size dimensions of the cuboid</param>
        /// <returns>True if the sphere intersects the cuboid, false otherwise</returns>
        public static bool IsSphereIntersectingCuboid(in float3 sphereCenter, float sphereRadius, in float3 cuboidPosition, in float4 cuboidRotation, in float3 cuboidSize)
        {
            return IsSphereIntersectingCuboidRaw(sphereCenter.x, sphereCenter.y, sphereCenter.z,
                sphereRadius,
                cuboidPosition.x, cuboidPosition.y, cuboidPosition.z,
                cuboidRotation.x, cuboidRotation.y, cuboidRotation.z, cuboidRotation.w,
                cuboidSize.x, cuboidSize.y, cuboidSize.z);
        }

        /// <summary>
        /// Core sphere-cuboid intersection test using closest point algorithm with raw float parameters.
        /// Transforms sphere center to cuboid's local coordinate system using inverse rotation,
        /// then finds the closest point on the cuboid to the sphere center and tests distance.
        /// Includes quaternion normalization and degenerate case handling for robustness.
        /// Optimized for Burst compilation with manual quaternion operations.
        /// </summary>
        /// <param name="sphereCenterX">X coordinate of sphere center</param>
        /// <param name="sphereCenterY">Y coordinate of sphere center</param>
        /// <param name="sphereCenterZ">Z coordinate of sphere center</param>
        /// <param name="sphereRadius">Radius of the sphere</param>
        /// <param name="cuboidPositionX">X coordinate of cuboid center</param>
        /// <param name="cuboidPositionY">Y coordinate of cuboid center</param>
        /// <param name="cuboidPositionZ">Z coordinate of cuboid center</param>
        /// <param name="cuboidRotationX">X component of cuboid rotation quaternion</param>
        /// <param name="cuboidRotationY">Y component of cuboid rotation quaternion</param>
        /// <param name="cuboidRotationZ">Z component of cuboid rotation quaternion</param>
        /// <param name="cuboidRotationW">W component of cuboid rotation quaternion</param>
        /// <param name="cuboidScaleX">X dimension of cuboid scale</param>
        /// <param name="cuboidScaleY">Y dimension of cuboid scale</param>
        /// <param name="cuboidScaleZ">Z dimension of cuboid scale</param>
        /// <returns>True if the sphere intersects the cuboid, false otherwise</returns>
        public static bool IsSphereIntersectingCuboidRaw
        (
            float sphereCenterX, float sphereCenterY, float sphereCenterZ,
            float sphereRadius,
            float cuboidPositionX, float cuboidPositionY, float cuboidPositionZ,
            float cuboidRotationX, float cuboidRotationY, float cuboidRotationZ, float cuboidRotationW,
            float cuboidScaleX, float cuboidScaleY, float cuboidScaleZ)
        {
            const float kEpsilon = 1e-6f;
            float lenSq = cuboidRotationX * cuboidRotationX +
                          cuboidRotationY * cuboidRotationY +
                          cuboidRotationZ * cuboidRotationZ +
                          cuboidRotationW * cuboidRotationW;
            if (lenSq < kEpsilon) return false;
            float invLen = 1.0f / math.sqrt(lenSq);
            float4 q = new float4(
                cuboidRotationX * invLen,
                cuboidRotationY * invLen,
                cuboidRotationZ * invLen,
                cuboidRotationW * invLen
            );

            float3 delta = new float3(sphereCenterX - cuboidPositionX, sphereCenterY - cuboidPositionY, sphereCenterZ - cuboidPositionZ);

            float4 iq = new float4(-q.x, -q.y, -q.z, q.w);
            float3 t = new float3(
                2.0f * (iq.y * delta.z - iq.z * delta.y),
                2.0f * (iq.z * delta.x - iq.x * delta.z),
                2.0f * (iq.x * delta.y - iq.y * delta.x)
            );
            float3 localCenter = new float3(
                delta.x + iq.w * t.x + (iq.y * t.z - iq.z * t.y),
                delta.y + iq.w * t.y + (iq.z * t.x - iq.x * t.z),
                delta.z + iq.w * t.z + (iq.x * t.y - iq.y * t.x)
            );

            if (cuboidScaleX <= 0f || cuboidScaleY <= 0f || cuboidScaleZ <= 0f)
                return false;
            float3 half = new float3(
                cuboidScaleX * 0.5f,
                cuboidScaleY * 0.5f,
                cuboidScaleZ * 0.5f
            );

            float cx = localCenter.x;
            float cy = localCenter.y;
            float cz = localCenter.z;
            float3 closest;
            closest.x = cx < -half.x ? -half.x : (cx > half.x ? half.x : cx);
            closest.y = cy < -half.y ? -half.y : (cy > half.y ? half.y : cy);
            closest.z = cz < -half.z ? -half.z : (cz > half.z ? half.z : cz);

            float dx = cx - closest.x;
            float dy = cy - closest.y;
            float dz = cz - closest.z;
            float distSq = dx * dx + dy * dy + dz * dz;

            return distSq <= sphereRadius * sphereRadius;
        }
        
        /// <summary>
        /// Core sphere-cuboid intersection test optimized for identity rotation (axis-aligned cuboid).
        /// Since the cuboid has no rotation, no coordinate transformation is needed.
        /// Simply finds the closest point on the AABB to the sphere center and tests distance.
        /// This eliminates all quaternion operations from the original function.
        /// </summary>
        /// <param name="sphereCenterX">X coordinate of sphere center</param>
        /// <param name="sphereCenterY">Y coordinate of sphere center</param>
        /// <param name="sphereCenterZ">Z coordinate of sphere center</param>
        /// <param name="sphereRadius">Radius of the sphere</param>
        /// <param name="cuboidPositionX">X coordinate of cuboid center</param>
        /// <param name="cuboidPositionY">Y coordinate of cuboid center</param>
        /// <param name="cuboidPositionZ">Z coordinate of cuboid center</param>
        /// <param name="cuboidScaleX">X dimension of cuboid scale</param>
        /// <param name="cuboidScaleY">Y dimension of cuboid scale</param>
        /// <param name="cuboidScaleZ">Z dimension of cuboid scale</param>
        /// <returns>True if the sphere intersects the axis-aligned cuboid, false otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSphereIntersectingCuboidRawIdentity(
            float sphereCenterX, float sphereCenterY, float sphereCenterZ,
            float sphereRadius,
            float cuboidPositionX, float cuboidPositionY, float cuboidPositionZ,
            float cuboidScaleX, float cuboidScaleY, float cuboidScaleZ)
        {
            // Early exit for degenerate cuboid
            if (cuboidScaleX <= 0f || cuboidScaleY <= 0f || cuboidScaleZ <= 0f)
                return false;

            // Calculate half extents
            float halfX = cuboidScaleX * 0.5f;
            float halfY = cuboidScaleY * 0.5f;
            float halfZ = cuboidScaleZ * 0.5f;

            // With identity rotation, local center is just the delta from cuboid position
            // No quaternion transformation needed
            float localCenterX = sphereCenterX - cuboidPositionX;
            float localCenterY = sphereCenterY - cuboidPositionY;
            float localCenterZ = sphereCenterZ - cuboidPositionZ;

            // Find closest point on AABB to sphere center (clamping approach)
            float closestX = localCenterX < -halfX ? -halfX : (localCenterX > halfX ? halfX : localCenterX);
            float closestY = localCenterY < -halfY ? -halfY : (localCenterY > halfY ? halfY : localCenterY);
            float closestZ = localCenterZ < -halfZ ? -halfZ : (localCenterZ > halfZ ? halfZ : localCenterZ);

            // Calculate squared distance from sphere center to closest point
            float dx = localCenterX - closestX;
            float dy = localCenterY - closestY;
            float dz = localCenterZ - closestZ;
            float distSq = dx * dx + dy * dy + dz * dz;

            // Compare with squared radius
            return distSq <= sphereRadius * sphereRadius;
        }
    }
}