#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VoxReader;
using VoxReader.Interfaces;
using Vector3 = UnityEngine.Vector3;
using static BoxCutter.BoxCutterGlobalVars;
using UnityEditor.SceneManagement;

namespace BoxCutter
{
    /// <summary>
    /// Utility class for loading and processing MagicaVoxel .vox files into BoxCutter SubBox data.
    /// Handles voxel data conversion, coordinate transformation, and greedy meshing optimization.
    /// </summary>
    public static class MagicaBuilder
    {
        public static void LoadVoxFile(BoxObj boxObj)
        {
            IVoxFile voxFileData = GetVoxFile(boxObj.magicaVoxelData);
            if (voxFileData == null) return;
            boxObj.lastMagicaVoxelData = boxObj.magicaVoxelData;
            boxObj.lastCusVoxelSize = boxObj.cusVoxelSize;
            boxObj.lastLossyScale = boxObj.transform.lossyScale;
            boxObj.lastCanCusVoxelSize = boxObj.canCusVoxelSize;
            boxObj.lastVoxelResolution = boxObj.voxelResolution;
            boxObj.GetComps();
            BuildSubObjFromMagica(boxObj, voxFileData, boxObj.modelIndex);
            boxObj.RefreshLocalDirs();
            boxObj.RefreshPosRot();
            boxObj.RefreshTrueStartPos();
            Undo.RecordObject(boxObj, "Rebuild Magica Voxels");
            EditorUtility.SetDirty(boxObj);
            EditorSceneManager.MarkSceneDirty(boxObj.gameObject.scene);
        }

        public static IVoxFile GetVoxFile(MagicaVoxFile magicaVoxFile)
        {
            string path = AssetDatabase.GetAssetPath(magicaVoxFile);
            string ext = Path.GetExtension(path);
            if (ext != ".vox" && ext != ".bcvox")
            {
                Debug.LogError("[BoxCutter] Please assign a MagicaVoxel file");
                return null;
            }

            IVoxFile voxFileData = null;

            try
            {
                voxFileData = VoxReader.VoxReader.Read(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BoxCutter] Failed to read file at '{path}': {e.Message}");
                return null;
            }

            return voxFileData;
        }

        [BurstCompile]
        public struct SubObjMinXComparer : IComparer<SubBox>
        {
            public int Compare(SubBox x, SubBox y)
            {
                int cmp = x.minX.CompareTo(y.minX);
                if (cmp != 0) return cmp;

                cmp = x.minY.CompareTo(y.minY);
                if (cmp != 0) return cmp;

                return x.minZ.CompareTo(y.minZ);
            }
        }

        private static void GetBaseSizeX(BoxObj boxObj, IModel[] models, out float baseSizeX, out int minX, out int minY, out int minZ, int modelIndex = -1)
        {
            Bounds meshBounds = boxObj.meshFilter.sharedMesh.bounds;
            Vector3 boundSize = meshBounds.size;
            Vector3 lossyScale = boxObj.transform.lossyScale;
            boundSize.Scale(lossyScale);

            minX = IntInfinity;
            minY = IntInfinity; 
            minZ = IntInfinity;

            baseSizeX = 0;

            int rawMinX = int.MaxValue;
            int rawMaxX = int.MinValue;
            
            bool cusModelIndex = modelIndex != -1;

            int modelLength = models.Length;
            for (int i = 0; i < modelLength; i++)
            {
                if (cusModelIndex)
                    i = modelIndex;
                IModel model = models[i];

                int voxelCount = model.Voxels.Length;
                for (int v = 0; v < voxelCount; v++)
                {
                    Voxel voxel = model.Voxels[v];
                    var p = voxel.GlobalPosition;
                    minX = math.min(minX, p.X);
                    minY = math.min(minY, p.Y);
                    minZ = math.min(minZ, p.Z);

                    rawMinX = math.min(rawMinX, p.X);
                    rawMaxX = math.max(rawMaxX, p.X);
                }

                if (cusModelIndex) break;
            }

            minX = -minX;
            minY = -minY;
            minZ = -minZ;

            int maxModelSizeX = rawMaxX - rawMinX + 1;
            baseSizeX = boundSize.x / maxModelSizeX;
        }

        public static void UpdateResolution(BoxObj box)
        {
            //─────────────────────────────────────────────────────────────────────────────────────
            IVoxFile voxFileData = GetVoxFile(box.magicaVoxelData);
            if (voxFileData == null) return;
            IModel[] models = voxFileData.Models;
            if (models == null || models.Length == 0) return;
            //─────────────────────────────────────────────────────────────────────────────────────
            int lastVoxelResolution = box.lastVoxelResolution;
            int allSubCount = box.allSubList.Count;
            
            for (int i = 0; i < allSubCount; i++)
            {
                SubBox subBox = box.allSubList[i];
                int ogMinX = subBox.minX / lastVoxelResolution;
                int ogMinY = subBox.minY / lastVoxelResolution;
                int ogMinZ = subBox.minZ / lastVoxelResolution;
                int ogMaxX = subBox.maxX / lastVoxelResolution;
                int ogMaxY = subBox.maxY / lastVoxelResolution;
                int ogMaxZ = subBox.maxZ / lastVoxelResolution;

                box.allSubList[i] = new SubBox
                {
                    minX = ogMinX * box.voxelResolution,
                    minY = ogMinY * box.voxelResolution,
                    minZ = ogMinZ * box.voxelResolution,
                    maxX = ogMaxX * box.voxelResolution,
                    maxY = ogMaxY * box.voxelResolution,
                    maxZ = ogMaxZ * box.voxelResolution,
                    magicaIndex = subBox.magicaIndex,
                };
            }
            
            float resolutionDivisor = (float)box.voxelResolution;
            GetBaseSizeX(box, models, out float baseSizeX, out int minX, out int minY, out int minZ, modelIndex: box.modelIndex);
            box.voxelSize = baseSizeX / resolutionDivisor;
            //─────────────────────────────────────────────────────────────────────────────────────
            box.lastVoxelResolution = box.voxelResolution;
            
            box.RefreshLocalDirs();
            box.RefreshPosRot();
            box.RefreshTrueStartPos();
            Undo.RecordObject(box, "Rebuild Magica Voxels");
            EditorUtility.SetDirty(box);
            EditorSceneManager.MarkSceneDirty(box.gameObject.scene);
            //─────────────────────────────────────────────────────────────────────────────────────
        }

        private static async void BuildSubObjFromMagica(BoxObj boxObj, IVoxFile voxFileData, int modelIndex = -1)
        {
            var models = voxFileData.Models;
            if (models == null || models.Length == 0)
                return;
            Bounds meshBounds = boxObj.meshFilter.sharedMesh.bounds;
            Vector3 boundSize = meshBounds.size;
            Vector3 lossyScale = boxObj.transform.lossyScale;
            boundSize.Scale(lossyScale);

            boxObj.sizeX = boundSize.x;
            boxObj.sizeY = boundSize.y;
            boxObj.sizeZ = boundSize.z;

            Vector3 localOffset = meshBounds.center;
            Vector3 scaled = Vector3.Scale(localOffset, lossyScale);

            boxObj.magicaLocalPosOffsetX = scaled.x;
            boxObj.magicaLocalPosOffsetY = scaled.y;
            boxObj.magicaLocalPosOffsetZ = scaled.z;
            
            GetBaseSizeX(boxObj, models, out float baseSizeX, out int minX, out int minY, out int minZ, modelIndex);
            
            int modelLength = models.Length;
            bool cusModelIndex = modelIndex != -1;
            
            // Apply voxel resolution to divide the base voxel size for finer detail
            float resolutionDivisor = (float)boxObj.voxelResolution;
            boxObj.voxelSize = baseSizeX / resolutionDivisor;
            
            boxObj.allSubList.Clear();

            int intervalX = Mathf.RoundToInt(boundSize.x / boxObj.voxelSize);
            int intervalY = Mathf.RoundToInt(boundSize.y / boxObj.voxelSize);
            int intervalZ = Mathf.RoundToInt(boundSize.z / boxObj.voxelSize);
            float sizeRatio = resolutionDivisor;
            int boundMul = Mathf.RoundToInt(sizeRatio);

            int sliceYZ = intervalY * intervalZ;
            int maxIdxX = intervalX - 1;
            int maxIdxZ = intervalZ - 1;

            int totalVoxels = intervalX * intervalY * intervalZ;

            var voxelDataGrid = new byte[totalVoxels];
            for (int i = 0; i < voxelDataGrid.Length; i++)
            {
                voxelDataGrid[i] = 255;
            }

            HashSet<byte> colorSet = new HashSet<byte>();

            for (int i = 0; i < modelLength; i++)
            {
                if (cusModelIndex)
                    i = modelIndex;
                IModel model = models[i];
                int voxelCount = model.Voxels.Length;
                for (int v = 0; v < voxelCount; v++)
                {
                    Voxel voxel = model.Voxels[v];
                    byte colorIndex = (byte)voxel.ColorIndex;
                    var p = voxel.GlobalPosition;

                    int x = maxIdxX - (p.X + minX);
                    int y = p.Z + minZ;
                    int z = maxIdxZ - (p.Y + minY);

                    int gridIndex = x * sliceYZ + y * intervalZ + z;
                    if (gridIndex >= 0 && gridIndex < voxelDataGrid.Length)
                    {
                        voxelDataGrid[gridIndex] = colorIndex;
                    }

                    colorSet.Add(colorIndex);
                }

                if (cusModelIndex) break;
            }

            boxObj.totalColors = colorSet.Count;
            using var voxelDataNa = new NativeArray<byte>(voxelDataGrid, Allocator.TempJob);
            using var oobFlagsNa = new NativeArray<byte>(voxelDataGrid.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            using var hitMinIntervalNa = new NativeArray<int3>(new[] { new int3(0, 0, 0) }, Allocator.TempJob);

            int[] gridOffsetPerBox = { 0 };
            int3[] globalSizePerBox = { new int3(intervalX, intervalY, intervalZ) };

            (NativeArray<SubBox> inSubsNa, NativeArray<int> inSubStartsNa, NativeArray<int> inSubCountsNa) greedyData = await DestructionPipeline.Greedy(
                new BoxObj[] { boxObj },
                voxelDataNa,
                oobFlagsNa,
                gridOffsetPerBox,
                globalSizePerBox,
                hitMinIntervalNa,
                Allocator.TempJob,
                sync: true
            );

            SubBox[] subObjs = greedyData.inSubsNa.ToArray();
            greedyData.inSubsNa.Dispose();
            greedyData.inSubStartsNa.Dispose();
            greedyData.inSubCountsNa.Dispose();

            boxObj.allSubList.AddRange(subObjs);

            int count = boxObj.allSubList.Count;
            if (count > 1)
            {
                var indices = new int[count];
                for (int i = 0; i < count; i++) indices[i] = i;

                Array.Sort(indices, (a, b) =>
                    new SubObjMinXComparer().Compare(boxObj.allSubList[a], boxObj.allSubList[b])
                );

                var sortedSubs = new List<SubBox>(count);
                for (int i = 0; i < count; i++)
                {
                    int k = indices[i];
                    sortedSubs.Add(boxObj.allSubList[k]);
                }

                boxObj.allSubList = sortedSubs;
            }

            int globalMinX = int.MaxValue, globalMinY = int.MaxValue, globalMinZ = int.MaxValue;
            foreach (var s in boxObj.allSubList)
            {
                globalMinX = math.min(globalMinX, s.minX);
                globalMinY = math.min(globalMinY, s.minY);
                globalMinZ = math.min(globalMinZ, s.minZ);
            }

            int subCount = boxObj.allSubList.Count;
            for (int i = 0; i < subCount; i++)
            {
                var s = boxObj.allSubList[i];
                boxObj.allSubList[i] = new SubBox
                {
                    minX = s.minX - globalMinX,
                    minY = s.minY - globalMinY,
                    minZ = s.minZ - globalMinZ,
                    maxX = s.maxX - globalMinX,
                    maxY = s.maxY - globalMinY,
                    maxZ = s.maxZ - globalMinZ,
                    magicaIndex = s.magicaIndex
                };
            }

            if (boundMul != 1)
            {
                for (int i = 0; i < subCount; i++)
                {
                    var s = boxObj.allSubList[i];
                    boxObj.allSubList[i] = new SubBox
                    {
                        minX = s.minX * boundMul,
                        minY = s.minY * boundMul,
                        minZ = s.minZ * boundMul,
                        maxX = s.maxX * boundMul,
                        maxY = s.maxY * boundMul,
                        maxZ = s.maxZ * boundMul,
                        magicaIndex = s.magicaIndex
                    };
                }
            }
        }
    }
}
#endif