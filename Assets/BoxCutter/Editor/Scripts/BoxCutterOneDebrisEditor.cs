using System.Reflection;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using static BoxCutter.BoxcutterEditorUtil;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxCutterOneDebris))]
    [CanEditMultipleObjects]
    public class BoxCutterOneDebrisEditor : Editor
    {

        #region Fields & Init


        private Texture2D customIcon;
        private readonly AnimBool showSettings = new AnimBool(true);
        private SerializedProperty objProp;
        private SerializedProperty gameObjProp;
        private SerializedProperty rbProp;
        private SerializedProperty meshRendProp;

        private SerializedProperty uniqueIdProp;
        private SerializedProperty pooledProp;
        private void OnEnable()
        {
            if (target == null || targets == null) return;
            customIcon = LoadLocalAsset("BoxCutterOneDebrisIcon.png");

            foreach (var obj in targets)
            {
                if (obj == null) continue;
                ApplyIcon(obj, customIcon);
            }
                
            objProp = serializedObject.FindProperty("obj");
            gameObjProp = serializedObject.FindProperty("gameObj");
            rbProp = serializedObject.FindProperty("rb");
            meshRendProp = serializedObject.FindProperty("meshRend");

            uniqueIdProp = serializedObject.FindProperty("uniqueId");
            pooledProp = serializedObject.FindProperty("pooled");

            showSettings.valueChanged.AddListener(Repaint);
        }

        #endregion


        #region GUI


        public override void OnInspectorGUI()
        {
            if (target == null || objProp == null) return;
            
            serializedObject.Update();

            EditorGUILayout.BeginVertical(OuterContainerStyle);
            DrawTitle();
            DrawBanner(customIcon, "SINGLE DEBRIS");
            DrawHorizontalDivider();

            DrawFoldoutSection("MAIN SETTINGS", showSettings, StartColor, () =>
            {
                GUI.enabled = false;
                EditorGUILayout.PropertyField(objProp, true);
                EditorGUILayout.PropertyField(gameObjProp, true);
                EditorGUILayout.PropertyField(rbProp, true);
                EditorGUILayout.PropertyField(meshRendProp, true);
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(uniqueIdProp);
                EditorGUILayout.PropertyField(pooledProp);
                GUI.enabled = true;
            });

            EditorGUILayout.EndVertical();

            if (!IsPlaying) ((BoxCutterOneDebris)target).Init();

            serializedObject.ApplyModifiedProperties();
        }

        public override bool RequiresConstantRepaint() => true;

        #endregion
    }
}