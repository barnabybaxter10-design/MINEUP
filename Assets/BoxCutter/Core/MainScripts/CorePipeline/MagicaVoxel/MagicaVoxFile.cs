using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
#endif

namespace BoxCutter
{

    [CreateAssetMenu(fileName = "New VoxAsset", menuName = "Vox/VoxAsset")]
    public class MagicaVoxFile : ScriptableObject
    {
        [HideInInspector] public byte[] rawData;

#if UNITY_EDITOR
        private void OnEnable()
        {
            var path = AssetDatabase.GetAssetPath(this);
            string iconName = path.EndsWith("bcvox") ? "BCMagicaVoxelIcon" : "MagicaVoxelIcon";
            if (!string.IsNullOrEmpty(path))
            {
                var icon = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().FirstOrDefault(t => t.name == iconName);
                if (icon != null)
                {
                    EditorGUIUtility.SetIconForObject(this, icon);
                    return;
                }
            }
            var iconGUIDs = AssetDatabase.FindAssets($"{iconName} t:Texture2D");
            foreach (var guid in iconGUIDs)
            {
                var iconPath = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(iconPath) == iconName)
                {
                    var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
                    if (icon != null)
                    {
                        EditorGUIUtility.SetIconForObject(this, icon);
                    }
                    break;
                }
            }
        }
#endif
    }

}