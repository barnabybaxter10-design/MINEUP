using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;
using System.IO;

namespace BoxCutter
{
    public abstract class MagicaVoxelImporterBase : ScriptedImporter
    {
        protected void ImportVoxAsset(AssetImportContext ctx, bool bc = false)
        {
            var bytes = File.ReadAllBytes(ctx.assetPath);

            var voxAsset = ScriptableObject.CreateInstance<MagicaVoxFile>();
            voxAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            voxAsset.rawData = bytes;

            ctx.AddObjectToAsset("voxData", voxAsset);
            ctx.SetMainObject(voxAsset);

            string iconName = bc ? "BCMagicaVoxelIcon" : "MagicaVoxelIcon";
            var iconGUIDs = AssetDatabase.FindAssets($"{iconName} t:Texture2D");

            string iconPath = null;
            foreach (var guid in iconGUIDs)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(assetPath) == iconName)
                {
                    iconPath = assetPath;
                    break;
                }
            }

            if (iconPath == null)
            {
                Debug.LogError($"Could not find {iconName}.png in the project.");
                return;
            }

            var iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);

            var iconCopy = Object.Instantiate(iconTex);
            iconCopy.name = iconName;
            ctx.AddObjectToAsset("icon", iconCopy);

            EditorGUIUtility.SetIconForObject(voxAsset, iconTex);
        }
    }
}