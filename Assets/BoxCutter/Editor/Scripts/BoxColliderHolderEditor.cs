using System.Reflection;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using static BoxCutter.BoxcutterEditorUtil;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxColliderHolder))]
    public class BoxColliderHolderEditor : Editor
    {

        #region Fields & Init
        
        private Texture2D customIcon;
        private readonly AnimBool showSettings = new AnimBool(true);
        private SerializedProperty boxcutterPrefabEnum;
        private SerializedProperty boxColliders;

        private void OnEnable()
        {
            if (target == null) return;
            
            customIcon = LoadLocalAsset("BoxcutterColliderHolderIcon.png");
            ApplyIcon(target, customIcon);
            
            boxcutterPrefabEnum = serializedObject.FindProperty("boxcutterPrefabEnum");
            boxColliders = serializedObject.FindProperty("boxColObjArr");

            showSettings.valueChanged.AddListener(Repaint);
        }

        #endregion
        
        #region GUI

        public override void OnInspectorGUI()
        {
            if (target == null) return;
            
            serializedObject.Update();

            boxcutterPrefabEnum = serializedObject.FindProperty("boxCutterPrefabEnum");

            var selectedPrefab = (BoxCutterObjectPool.BoxCutterPrefabEnum)boxcutterPrefabEnum.intValue;
            EditorGUILayout.BeginVertical(OuterContainerStyle);

            DrawTitle("This script's acts a data container for all box or mesh colliders associated with it.");
            DrawBanner(customIcon, "COLLIDER HOLDER");
            DrawHorizontalDivider();
            bool invalidPrefab = false;

            if (!IsPlaying)
            {
                if (selectedPrefab == BoxCutterObjectPool.BoxCutterPrefabEnum.MeshCollider)
                {
                    ((BoxColliderHolder)target).Generate();
                }
                else if (boxColliderCapLookup.TryGetValue(selectedPrefab, out int instanceAmount))
                {
                    int boxColliderLength = boxColliders.arraySize;

                    if (boxColliderLength != instanceAmount)
                    {
                        ((BoxColliderHolder)target).Generate();
                    }
                }
                else
                {
                    invalidPrefab = true;
                }
            }

            EditorGUILayout.Space();
            DrawFoldoutSection("MAIN SETTINGS", showSettings, StartColor, () =>
            {
                GUI.enabled = false;
                EditorGUILayout.PropertyField(boxColliders, true);
                GUI.enabled = true;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("isMeshCollider"));
                EditorGUILayout.PropertyField(boxcutterPrefabEnum);
                if (invalidPrefab) EditorGUILayout.HelpBox($"Invalid Prefab Enum for: {selectedPrefab}.", MessageType.Warning);
                GUI.enabled = false;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("boxObj"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("instanceAmount"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("pooled"));
                GUI.enabled = true;
            });

            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }

        public override bool RequiresConstantRepaint() => true;

        #endregion
    }
}