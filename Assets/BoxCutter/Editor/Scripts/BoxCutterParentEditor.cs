using System.Reflection;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using static BoxCutter.BoxcutterEditorUtil;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxCutterParent))]
    public class BoxCutterParentEditor : Editor
    {

        #region Fields & Init


        private Texture2D customIcon;
        private readonly AnimBool showSettings = new AnimBool(true);
        private SerializedProperty rbProp;
        private SerializedProperty objProp;
        private SerializedProperty childrenProp;

        private void OnEnable()
        {
            customIcon = LoadLocalAsset("BoxCutterParentIcon.png");
            ApplyIcon(target, customIcon);
            
            rbProp = serializedObject.FindProperty("rb");
            objProp = serializedObject.FindProperty("obj");
            childrenProp = serializedObject.FindProperty("childrenBoxes");

            showSettings.valueChanged.AddListener(Repaint);
        }

        #endregion


        #region GUI


        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.BeginVertical(OuterContainerStyle);

            DrawTitle();

            DrawBanner(customIcon, "PARENT CONTAINER");
            DrawHorizontalDivider();
            EditorGUILayout.Space();
            DrawFoldoutSection("MAIN SETTINGS", showSettings, StartColor, () =>
            {
                GUI.enabled = false;
                EditorGUILayout.PropertyField(rbProp);
                EditorGUILayout.PropertyField(objProp);
                EditorGUILayout.PropertyField(childrenProp);
            });

            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }

        public override bool RequiresConstantRepaint() => true;

        #endregion
    }
}