using UnityEngine;
using UnityEngine.Rendering;

namespace BoxCutter
{
    /// <summary>
    /// Extension methods providing additional functionality to built-in Unity types.
    /// Focused on color manipulation and utility operations for the BoxCutter system.
    /// </summary>
    public static class Extend
    {
        public enum RenderPipelineType
        {
            HDRP,
            URP,
            BuiltIn
        }
        
        /// <summary>
        /// Creates a new Color with the specified alpha multiplier applied.
        /// Preserves RGB values while adjusting transparency for visual effects.
        /// </summary>
        /// <param name="color">Base color to modify</param>
        /// <param name="a">Alpha multiplier (0-1 range)</param>
        /// <returns>New Color with adjusted alpha</returns>
        public static Color WithA(this Color color, float a)
        {
            return new Color(color.r, color.g, color.b, color.a * a);
        }
        
        /// <summary>
        /// Get the Unity project's render pipeline
        /// </summary>
        /// <returns></returns>
        public static RenderPipelineType GetCurrentRenderPipeline()
        {
            var currentRP = GraphicsSettings.currentRenderPipeline;
            if (currentRP != null)
            {
                string rpTypeName = currentRP.GetType().Name;
                if (rpTypeName.Contains("Universal") || rpTypeName.Contains("URP")) return RenderPipelineType.URP;
                if (rpTypeName.Contains("HDRenderPipeline") || rpTypeName.Contains("HDRP")) return RenderPipelineType.HDRP;
            }

            if (System.Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime") != null)
            {
                return RenderPipelineType.URP;
            }

            if (System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime") != null)
            {
                return RenderPipelineType.HDRP;
            }

            // If no pipeline is active and no packages are found, it's truly Built in.
            return RenderPipelineType.BuiltIn;
        }
        
        /// <summary>
        /// Converts a World Space rotation to Local Space (relative to this transform).
        /// Analogue to transform.InverseTransformPoint().
        /// </summary>
        public static Quaternion InverseTransformRotation(this Transform trans, Quaternion worldRotation)
        {
            return Quaternion.Inverse(trans.rotation) * worldRotation;
        }

        /// <summary>
        /// Converts a Local Space rotation to World Space (relative to this transform).
        /// Analogue to transform.TransformPoint().
        /// </summary>
        public static Quaternion TransformRotation(this Transform trans, Quaternion localRotation)
        {
            return trans.rotation * localRotation;
        }

        /// <summary>
        /// Refreshes the Box's basic variables in editor
        /// </summary>
        /// <param name="box"></param>
        public static void RefreshBoxObjInEditor(BoxObj box)
        {
            int allSubObjCount = box.allSubList.Count;
            
            box.GetComps();
            box.GetVoxelSize();
            if (allSubObjCount == 0)
            {
                box.sizeX = 0;
                box.sizeY = 0;
                box.sizeZ = 0;
            }

            box.GetStartingSize(box.magicaVoxelData == null);

            box.RefreshPosRot();
            box.RefreshLocalDirs();
            box.RefreshTrueStartPos();

            if (allSubObjCount == 0)
            {
                box.AddStartingSubObj();
            }
        }
    }
}