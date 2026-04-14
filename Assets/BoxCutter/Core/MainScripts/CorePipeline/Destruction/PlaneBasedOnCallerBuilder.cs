using System.Collections;
using System.Collections.Generic;
using BoxCutter;
using Unity.Mathematics;
using UnityEngine;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.BoxCutterCaller;
using static BoxCutter.DestructionPipeline;

namespace BoxCutter
{
    public static class PlaneBasedOnCallerBuilder
    {
        public static void CalcPlane(CallerData callerData, BoxTrans boxTrans, out float3 overrideNormal, out float3 overrideForward)
        {
            // caller
            float callerX = callerData.pos.x;
            float callerY = callerData.pos.y;
            float callerZ = callerData.pos.z;

            // box center
            float boxCX = boxTrans.posX;
            float boxCY = boxTrans.posY;
            float boxCZ = boxTrans.posZ;

            // box axes
            float rightX = boxTrans.rightX;
            float rightY = boxTrans.rightY;
            float rightZ = boxTrans.rightZ;

            float upX = boxTrans.upX;
            float upY = boxTrans.upY;
            float upZ = boxTrans.upZ;

            float fwdX = boxTrans.fwdX;
            float fwdY = boxTrans.fwdY;
            float fwdZ = boxTrans.fwdZ;

            // half extents
            float hx = boxTrans.sizeX * 0.5f;
            float hy = boxTrans.sizeY * 0.5f;
            float hz = boxTrans.sizeZ * 0.5f;

            // pick the face with smallest |distance|
            float bestAbsDist = float.MaxValue;
            float bestNx = 0f, bestNy = 1f, bestNz = 0f;

            // +X
            {
                float planeX = boxCX + rightX * hx;
                float planeY = boxCY + rightY * hx;
                float planeZ = boxCZ + rightZ * hx;

                float dx = callerX - planeX;
                float dy = callerY - planeY;
                float dz = callerZ - planeZ;

                float d = dx * rightX + dy * rightY + dz * rightZ;
                float ad = d < 0f ? -d : d;
                if (ad < bestAbsDist)
                {
                    bestAbsDist = ad;
                    bestNx = rightX;
                    bestNy = rightY;
                    bestNz = rightZ;
                }
            }

            // -X
            {
                float planeX = boxCX - rightX * hx;
                float planeY = boxCY - rightY * hx;
                float planeZ = boxCZ - rightZ * hx;

                float dx = callerX - planeX;
                float dy = callerY - planeY;
                float dz = callerZ - planeZ;

                float nx = -rightX;
                float ny = -rightY;
                float nz = -rightZ;

                float d = dx * nx + dy * ny + dz * nz;
                float ad = d < 0f ? -d : d;
                if (ad < bestAbsDist)
                {
                    bestAbsDist = ad;
                    bestNx = nx;
                    bestNy = ny;
                    bestNz = nz;
                }
            }

            // +Y
            {
                float planeX = boxCX + upX * hy;
                float planeY = boxCY + upY * hy;
                float planeZ = boxCZ + upZ * hy;

                float dx = callerX - planeX;
                float dy = callerY - planeY;
                float dz = callerZ - planeZ;

                float d = dx * upX + dy * upY + dz * upZ;
                float ad = d < 0f ? -d : d;
                if (ad < bestAbsDist)
                {
                    bestAbsDist = ad;
                    bestNx = upX;
                    bestNy = upY;
                    bestNz = upZ;
                }
            }

            // -Y
            {
                float planeX = boxCX - upX * hy;
                float planeY = boxCY - upY * hy;
                float planeZ = boxCZ - upZ * hy;

                float dx = callerX - planeX;
                float dy = callerY - planeY;
                float dz = callerZ - planeZ;

                float nx = -upX;
                float ny = -upY;
                float nz = -upZ;

                float d = dx * nx + dy * ny + dz * nz;
                float ad = d < 0f ? -d : d;
                if (ad < bestAbsDist)
                {
                    bestAbsDist = ad;
                    bestNx = nx;
                    bestNy = ny;
                    bestNz = nz;
                }
            }

            // +Z
            {
                float planeX = boxCX + fwdX * hz;
                float planeY = boxCY + fwdY * hz;
                float planeZ = boxCZ + fwdZ * hz;

                float dx = callerX - planeX;
                float dy = callerY - planeY;
                float dz = callerZ - planeZ;

                float d = dx * fwdX + dy * fwdY + dz * fwdZ;
                float ad = d < 0f ? -d : d;
                if (ad < bestAbsDist)
                {
                    bestAbsDist = ad;
                    bestNx = fwdX;
                    bestNy = fwdY;
                    bestNz = fwdZ;
                }
            }

            // -Z
            {
                float planeX = boxCX - fwdX * hz;
                float planeY = boxCY - fwdY * hz;
                float planeZ = boxCZ - fwdZ * hz;

                float dx = callerX - planeX;
                float dy = callerY - planeY;
                float dz = callerZ - planeZ;

                float nx = -fwdX;
                float ny = -fwdY;
                float nz = -fwdZ;

                float d = dx * nx + dy * ny + dz * nz;
                float ad = d < 0f ? -d : d;
                if (ad < bestAbsDist)
                {
                    bestAbsDist = ad;
                    bestNx = nx;
                    bestNy = ny;
                    bestNz = nz;
                }
            }

            // dot(best, up)
            float dotNU = bestNx * upX + bestNy * upY + bestNz * upZ;
            float adNU = dotNU < 0f ? -dotNU : dotNU;

            float refX, refY, refZ;
            if (adNU < 0.999f)
            {
                refX = upX;
                refY = upY;
                refZ = upZ;
            }
            else
            {
                refX = rightX;
                refY = rightY;
                refZ = rightZ;
            }

            // tangent = cross(best, ref)
            float tanX = bestNy * refZ - bestNz * refY;
            float tanY = bestNz * refX - bestNx * refZ;
            float tanZ = bestNx * refY - bestNy * refX;

            float tanLenSq = tanX * tanX + tanY * tanY + tanZ * tanZ;
            if (tanLenSq < 1e-20f)
            {
                tanX = 1f;
                tanY = 0f;
                tanZ = 0f;
                tanLenSq = 1f;
            }

            float tanInvLen = math.rsqrt(tanLenSq);
            tanX *= tanInvLen;
            tanY *= tanInvLen;
            tanZ *= tanInvLen;

            // fwd = cross(tangent, best)
            float fwdNX = tanY * bestNz - tanZ * bestNy;
            float fwdNY = tanZ * bestNx - tanX * bestNz;
            float fwdNZ = tanX * bestNy - tanY * bestNx;

            float fwdLenSq = fwdNX * fwdNX + fwdNY * fwdNY + fwdNZ * fwdNZ;
            if (fwdLenSq > 1e-20f)
            {
                float fwdInvLen = math.rsqrt(fwdLenSq);
                fwdNX *= fwdInvLen;
                fwdNY *= fwdInvLen;
                fwdNZ *= fwdInvLen;
            }

            // write to your existing float3s
            overrideNormal.x = bestNx;
            overrideNormal.y = bestNy;
            overrideNormal.z = bestNz;

            overrideForward.x = fwdNX;
            overrideForward.y = fwdNY;
            overrideForward.z = fwdNZ;
        }
    }
}