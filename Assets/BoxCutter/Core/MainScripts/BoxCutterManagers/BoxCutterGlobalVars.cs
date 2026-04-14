using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace BoxCutter
{
    /// <summary>
    /// Debug visualization helper for rendering boxes in the scene view.
    /// Used for development and debugging of destruction and fragmentation systems.
    /// </summary>
    [Serializable]
    public class BoxCutterDebugBox
    {
        public float3 pos;
        public Quaternion rot;
        public float3 size = new float3(1, 1, 1);
        public Color color = Color.green;
        public bool enhanceGizmos;
    }

    /// <summary>
    /// Central repository for commonly used constants, vectors, colors, and utility values.
    /// Provides cached instances to avoid repeated allocations and improve performance.
    /// </summary>
    public static class BoxCutterGlobalVars
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Common Math Constants
        //─────────────────────────────────────────────────────────────────────────────────────
        public static Quaternion quatZero3D = new Quaternion(0, 0, 0, 0);
        public static Quaternion quatZeroIdentity = new Quaternion(0, 0, 0, 1);
        public static Vector3 vecZero = Vector3.zero;
        public static Vector3 vecOne = new Vector3(1, 1, 1);
        public static Vector3Int vecZero3DInt = Vector3Int.zero;
        public static int2 int2Zero = new int2(0, 0);
        public static int3 int3Zero = new int3(0, 0, 0);
        public static float2 float2Zero = new float2(0, 0);
        public static float3 float3Zero = new float3(0, 0, 0);
        public static float4 float4Zero = new float4(0, 0, 0, 0);

        public const float Infinity = Mathf.Infinity;
        public const int IntInfinity = int.MaxValue;
        public const int NegIntInfinity = int.MinValue;
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Directional Vectors
        //─────────────────────────────────────────────────────────────────────────────────────
        public static Vector3 right = Vector3.right;
        public static Vector3 up = Vector3.up;
        public static Vector3 forward = Vector3.forward;
        public static Vector3 left = -Vector3.right;
        public static Vector3 down = -Vector3.up;
        public static Vector3 back = -Vector3.forward;

        public static float3 floatRight = new float3(1, 0, 0);
        public static float3 floatUp = new float3(0, 1, 0);
        public static float3 floatForward = new float3(0, 0, 1);
        public static float3 floatLeft = new float3(-1, 0, 0);
        public static float3 floatDown = new float3(0, -1, 0);
        public static float3 floatBack = new float3(0, 0, -1);
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Runtime State Management
        //─────────────────────────────────────────────────────────────────────────────────────
        private static bool isPlaying;
        /// <summary>
        /// Determines if the application is currently playing, with manual override capability.
        /// Used to handle both runtime and editor-time execution contexts.
        /// </summary>
        public static bool IsPlaying
        {
            get => Application.isPlaying || isPlaying;
            set => isPlaying = value;
        }

        public enum AxisEnumType
        {
            XAxis,
            YAxis,
            ZAxis,
        }
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Color Palette
        //─────────────────────────────────────────────────────────────────────────────────────
        public static Color green = Color.green;
        public static Color yellow = Color.yellow;
        public static Color red = Color.red;
        public static Color orange = new Color(1, 0.5f, 0f);
        public static Color cyan = Color.cyan;
        public static Color pink = Color.magenta;
        public static Color blue = Color.blue;
        public static Color black = Color.black;
        public static Color white = Color.white;
        public static Color blackBlue = new Color(0, 0, 0.26f, 1);
        public static Color blandBlue = new Color(0, 0.25f, 0.5f, 1);

        public static Color boxCutterPrimaryColor = new Color(0.42f, 0.32f, 0.7f, 1f);
        public static Color boxCutterSecColor = new Color(0.61f, 0.50f, 0.80f, 1f);
        public static Color boxCutterThirdColor = new Color(0f, 0.623f, 0.71f, 1f);

        /// <summary>
        /// Array of distinct colors used for debugging fragmentation and island detection.
        /// Provides visual differentiation between different fragments or regions.
        /// </summary>
        public static readonly Color[] fragDebugColors = new Color[]
        {
            Color.blue,
            Color.yellow,
            Color.cyan,
            Color.magenta,
            Color.gray,
            Color.white,
            Color.red,
            Color.green,
            new Color(1f, 0.5f, 0f),
            new Color(0.5f, 0f, 1f),
            new Color(1f, 0.2f, 0.2f),
            new Color(0.2f, 0.7f, 1f),
            new Color(0.2f, 1f, 0.2f),
            new Color(1f, 0.6f, 0.2f),
            new Color(1f, 0.85f, 0f),
        };
    }
}