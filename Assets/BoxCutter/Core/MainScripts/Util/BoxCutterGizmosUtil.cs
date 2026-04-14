using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering; // for CompareFunction
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               Editor Gizmo Drawing Utilities
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Static utility class providing standardized gizmo drawing methods for BoxCutter editor visualization.
    /// Offers consistent sphere and box drawing with proper handle state management and camera-relative rendering.
    /// Designed for scene view debugging and visual feedback during development and testing.
    /// </summary>
    public static class BoxCutterGizmosUtil
    {
        /// <summary>
        /// Draws a sphere gizmo in the scene view with filled and wireframe components.
        /// Uses camera-relative orientation for consistent visual appearance across view angles.
        /// </summary>
        /// <param name="color">Base color for the sphere visualization</param>
        /// <param name="position">World space position of the sphere center</param>
        /// <param name="radius">Radius of the sphere in world units</param>
        /// <param name="alwaysOnTop">If true, disables depth testing so it renders on top</param>
        public static void DrawSphere(Color color, Vector3 position, float radius, bool alwaysOnTop = true)
        {
#if UNITY_EDITOR
            if (!Camera.current) return; // Skip drawing if no active camera

            // Save state
            var ogColor = Handles.color;
            var ogZTest = Handles.zTest;

            // Force overlay when requested
            Handles.zTest = alwaysOnTop ? CompareFunction.Always : CompareFunction.LessEqual;

            // Draw filled disc with reduced alpha for interior visualization
            Handles.color = color.WithA(0.2f);
            Handles.DrawSolidDisc(position, Camera.current.transform.forward, radius);

            // Draw wireframe outline for clear boundary definition
            Handles.color = color;
            Handles.DrawWireDisc(position, Camera.current.transform.forward, radius);

            // Restore
            Handles.color = ogColor;
            Handles.zTest = ogZTest;
#endif
        }

        /// <summary>
        /// Draws a box gizmo in the scene view with filled and wireframe components.
        /// Supports arbitrary position, rotation, and scaling with proper transform matrix management.
        /// </summary>
        /// <param name="color">Base color for the box visualization</param>
        /// <param name="center">World space position of the box center</param>
        /// <param name="rotation">Rotation quaternion for box orientation</param>
        /// <param name="size">Box dimensions in local space</param>
        /// <param name="alwaysOnTop">If true, disables depth testing so it renders on top</param>
        /// <param name="mode">0 - Regular, 1 draw only outline</param>
        public static void DrawBox(Color color, Vector3 center, Quaternion rotation, Vector3 size, bool alwaysOnTop = false, byte mode = 0)
        {
#if UNITY_EDITOR
            if (alwaysOnTop)
            {
                var ogColor = Handles.color;
                var ogMatrix = Handles.matrix;
                var ogZTest = Handles.zTest;

                Handles.zTest = CompareFunction.Always;
                
                Handles.matrix = Matrix4x4.TRS(center, rotation, size);

                // Main
                Handles.color = color.WithA(0.4f);
                Handles.CubeHandleCap(
                    controlID: 0,
                    position: Vector3.zero,
                    rotation: Quaternion.identity,
                    size: 1f,
                    eventType: EventType.Repaint);

                // Outline
                Handles.color = color.WithA(0.7f);
                Handles.DrawWireCube(Vector3.zero, Vector3.one);

                // restore
                Handles.zTest = ogZTest;
                Handles.matrix = ogMatrix;
                Handles.color = ogColor;
            }
            else
            {
                Color ogColor = Gizmos.color;
                Matrix4x4 ogMatrix = Gizmos.matrix;

                // Main
                if (mode == 0)
                {
                    Gizmos.color = color.WithA(0.4f);
                    Gizmos.matrix = Matrix4x4.TRS(center, rotation, vecOne);
                    Gizmos.DrawCube(vecZero, size);
                }

                // Outline
                Gizmos.color = color.WithA(0.7f);
                if (mode != 1) Gizmos.matrix = Matrix4x4.TRS(center, rotation, vecOne);
                Gizmos.DrawWireCube(vecZero, size);

                Gizmos.color = ogColor;
                Gizmos.matrix = ogMatrix;
            }
#endif
        }
    }
}