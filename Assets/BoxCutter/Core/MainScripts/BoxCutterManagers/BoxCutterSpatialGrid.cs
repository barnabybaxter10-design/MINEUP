using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    /// <summary>
    /// Axis-aligned spatial grid for BoxObj instances using a 3D grid system.
    /// - Uses per-axis cell sizes (no averaging).
    /// - Correct inclusive index range: floor(min/size) .. ceil(max/size)-1.
    /// - Grid is forced to world rotation (0,0,0,0) in editor and play mode.
    /// </summary>
    [ExecuteAlways]
    public class BoxCutterSpatialGrid : MonoBehaviour
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Debug Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        [SerializeField] private bool canDebug;
        [SerializeField] private bool canShowGizmos;
        [SerializeField] private bool showCells;
        [SerializeField] private int showIndex = -1;

        [HideInInspector] private int previousTotalCells = 0;
        [HideInInspector] private bool canAutoDisable = true;
        [HideInInspector] private bool savedShowCellsState;
        [HideInInspector] public int threshold = 50_000;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Grid Configuration
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Size of the spatial grid in world units (X, Y, Z dimensions)")]
        public Vector3 gridBounds = new Vector3(100f, 100f, 100f);

        [Tooltip("Number of cells per shortest axis - higher values create smaller cells for better precision")]
        public int resolution = 10;

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Runtime Grid Data
        //─────────────────────────────────────────────────────────────────────────────────────
        [Tooltip("Per-axis cell size (computed)")]
        [SerializeField] [HideInInspector] public float cellSizeX, cellSizeY, cellSizeZ;

        [SerializeField] [HideInInspector] public int cellAmountPerX;
        [SerializeField] [HideInInspector] public int cellAmountPerY;
        [SerializeField] [HideInInspector] public int cellAmountPerZ;
        [SerializeField] [HideInInspector] public int totalCells;

        public float3 gridMin;                     // world-space min corner of the grid AABB
        public float3 preFloatRight, preFloatUp, preFloatForward; // world increments per cell
        public float3 cell3D;                      // per-axis cell size as float3
        public int total;

        [HideInInspector] public WorldCellData[] gridData;
        private int3[] visualizeCellArr;

        /// <summary>Represents a single cell in the spatial grid containing BoxObj references.</summary>
        [Serializable]
        public class WorldCellData
        {
            public int x, y, z;
            public List<BoxObj> boxObjList = new List<BoxObj>();
        }

        public static BoxCutterSpatialGrid BoxCutterSpatialGridInstance { get; private set; }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Lifecycle
        //─────────────────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Application.isPlaying)
            {
                if (BoxCutterSpatialGridInstance != null)
                {
                    Destroy(gameObject);
                    return;
                }

                visualizeCellArr = Array.Empty<int3>();
                BuildGrid();
                BoxCutterSpatialGridInstance = this;
            }
        }

        private void OnValidate()
        {
            // Rebuild previews when values change in editor
            if (!Application.isPlaying)
            {
                BuildGridVisualization();
            }
        }

        private void LateUpdate()
        {
            // Continually enforce zero world rotation during play or edit update loops
            ForceZeroWorldRotation();
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Helpers
        //─────────────────────────────────────────────────────────────────────────────────────

        private void ForceZeroWorldRotation()
        {
            if (transform.rotation != Quaternion.identity)
                transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// Calculates grid parameters and sets up spatial partitioning variables.
        /// </summary>
        private void SetupVars()
        {
            // Base cell size derived from shortest axis to maintain overall density
            float shortestAxis = Mathf.Min(gridBounds.x, gridBounds.y, gridBounds.z);
            float baseCellSize = shortestAxis / Mathf.Max(1, resolution);

            // Compute counts on each axis by rounding to keep roughly cubic cells
            cellAmountPerX = Mathf.Max(1, Mathf.RoundToInt(gridBounds.x / baseCellSize));
            cellAmountPerY = Mathf.Max(1, Mathf.RoundToInt(gridBounds.y / baseCellSize));
            cellAmountPerZ = Mathf.Max(1, Mathf.RoundToInt(gridBounds.z / baseCellSize));

            // True per-axis sizes (no averaging!)
            cellSizeX = gridBounds.x / cellAmountPerX;
            cellSizeY = gridBounds.y / cellAmountPerY;
            cellSizeZ = gridBounds.z / cellAmountPerZ;

            // Precomputed increments for drawing
            preFloatRight   = new float3(1, 0, 0) * cellSizeX;
            preFloatUp      = new float3(0, 1, 0) * cellSizeY;
            preFloatForward = new float3(0, 0, 1) * cellSizeZ;
            cell3D = new float3(cellSizeX, cellSizeY, cellSizeZ);

            // Grid min in world-space: centered on transform.position and axis-aligned
            gridMin = (float3)transform.position
                      - new float3(gridBounds.x, gridBounds.y, gridBounds.z) * 0.5f;

            int yz = cellAmountPerY * cellAmountPerZ;
            total = cellAmountPerX * yz;
            totalCells = total;

            // Auto-toggle visualization if too many cells
            if (previousTotalCells < threshold && totalCells > threshold && canAutoDisable)
            {
                savedShowCellsState = showCells;
                showCells = false;
                canAutoDisable = false;
            }

            if (totalCells < threshold && !canAutoDisable)
            {
                showCells = savedShowCellsState;
                canAutoDisable = true;
            }

            previousTotalCells = totalCells;
        }

        private void BuildGridVisualization()
        {
            SetupVars();

            visualizeCellArr = new int3[total];
            int idx = 0;
            for (int x = 0; x < cellAmountPerX; x++)
                for (int y = 0; y < cellAmountPerY; y++)
                    for (int z = 0; z < cellAmountPerZ; z++)
                        visualizeCellArr[idx++] = new int3(x, y, z);
        }

        private void BuildGrid()
        {
            SetupVars();

            gridData = new WorldCellData[total];
            int idx = 0;
            for (int x = 0; x < cellAmountPerX; x++)
                for (int y = 0; y < cellAmountPerY; y++)
                    for (int z = 0; z < cellAmountPerZ; z++)
                        gridData[idx++] = new WorldCellData { x = x, y = y, z = z };
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Core API
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Adds a BoxObj to all spatial grid cells that overlap with its world-space AABB.
        /// Expects low/high per axis in world space. The grid is axis-aligned.
        /// </summary>
        public List<WorldCellData> AddBoxObj(
            BoxObj box,
            float lowX, float lowY, float lowZ,
            float highX, float highY, float highZ)
        {
            var impacted = new List<WorldCellData>();

            // Convert world coordinates to distances from gridMin
            float uMin = lowX  - gridMin.x;
            float vMin = lowY  - gridMin.y;
            float wMin = lowZ  - gridMin.z;

            float uMax = highX - gridMin.x;
            float vMax = highY - gridMin.y;
            float wMax = highZ - gridMin.z;

            // Inclusive cell index ranges: floor(min/size)->ceil(max/size) - 1
            int minX = Mathf.FloorToInt(uMin / cellSizeX);
            int maxX = Mathf.CeilToInt (uMax / cellSizeX) - 1;

            int minY = Mathf.FloorToInt(vMin / cellSizeY);
            int maxY = Mathf.CeilToInt (vMax / cellSizeY) - 1;

            int minZ = Mathf.FloorToInt(wMin / cellSizeZ);
            int maxZ = Mathf.CeilToInt (wMax / cellSizeZ) - 1;

            // Clamp to valid ranges
            minX = Mathf.Clamp(minX, 0, cellAmountPerX - 1);
            maxX = Mathf.Clamp(maxX, 0, cellAmountPerX - 1);

            minY = Mathf.Clamp(minY, 0, cellAmountPerY - 1);
            maxY = Mathf.Clamp(maxY, 0, cellAmountPerY - 1);

            minZ = Mathf.Clamp(minZ, 0, cellAmountPerZ - 1);
            maxZ = Mathf.Clamp(maxZ, 0, cellAmountPerZ - 1);

            // Early out if no overlap after clamping
            if (minX > maxX || minY > maxY || minZ > maxZ)
                return impacted;

            // Strides for 3D->1D index conversion
            int strideY = cellAmountPerZ;
            int strideX = cellAmountPerY * strideY;

            for (int x = minX; x <= maxX; x++)
            {
                int baseX = x * strideX;
                for (int y = minY; y <= maxY; y++)
                {
                    int baseY = baseX + y * strideY;
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        int idx = baseY + z;
                        if ((uint)idx < (uint)total) // bounds check
                        {
                            var cell = gridData[idx];
                            cell.boxObjList.Add(box);
                            impacted.Add(cell);
                        }
                    }
                }
            }

            return impacted;
        }

        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Gizmos / Visualization
        //─────────────────────────────────────────────────────────────────────────────────────

        public void DrawCells(WorldCellData[] cellList, bool canShowBoxInPartition = false)
        {
            if (!canShowGizmos) return;

            // Always draw grid/cells in world space (no lingering TRS)
            Color ogColor = Gizmos.color;
            Matrix4x4 ogMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.identity;

            float3 halfOffset = (preFloatRight + preFloatUp + preFloatForward) * 0.5f;

            // Full grid wireframe (primary color)
            if (showCells)
            {
                Gizmos.color = boxCutterPrimaryColor;
                int visualizedCellCount = visualizeCellArr.Length;
                for (int i = 0; i < visualizedCellCount; i++)
                {
                    int3 c = visualizeCellArr[i];
                    float3 cellMin = gridMin + preFloatRight * c.x + preFloatUp * c.y + preFloatForward * c.z;
                    Gizmos.DrawWireCube(cellMin + halfOffset, cell3D);
                }
            }

            // Highlighted/impacted cells + boxes (third color)
            Gizmos.color = boxCutterThirdColor;
            int cellCount = cellList.Length;

            for (int i = 0; i < cellCount; i++)
            {
                var cell = cellList[i];

                // Only draw specific cells when requested
                if (!canShowBoxInPartition && !(showIndex != -1 && i == showIndex))
                    continue;

                // Draw the cell itself in world space
                Gizmos.matrix = Matrix4x4.identity;
                float3 cellMin = gridMin + preFloatRight * cell.x + preFloatUp * cell.y + preFloatForward * cell.z;
                Gizmos.DrawWireCube(cellMin + halfOffset, cell3D);

                // Draw boxes contained in that cell, each under its own TRS,
                // then reset back to world for the next thing we draw.
                foreach (var box in cell.boxObjList)
                {
                    Gizmos.matrix = Matrix4x4.TRS(
                        new Vector3(box.centerPos.x, box.centerPos.y, box.centerPos.z),
                        new Quaternion(box.qx, box.qy, box.qz, box.qw),
                        Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(box.sizeX, box.sizeY, box.sizeZ));
                    Gizmos.matrix = Matrix4x4.identity; // IMPORTANT: reset for next draw
                }
            }

            // Restore original gizmo state
            Gizmos.color = ogColor;
            Gizmos.matrix = ogMatrix;
        }

        private void OnDrawGizmosSelected()
        {
            if (!canShowGizmos) return;
            
            if (gridData == null)
                BuildGridVisualization();

            Color ogColor = Gizmos.color;
            Matrix4x4 ogMatrix = Gizmos.matrix;

            if (!IsPlaying) BuildGridVisualization();
            DrawCells(gridData);

            Gizmos.color = boxCutterPrimaryColor;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireCube(transform.position, gridBounds);

            Gizmos.color = ogColor;
            Gizmos.matrix = ogMatrix;
        }
    }
}