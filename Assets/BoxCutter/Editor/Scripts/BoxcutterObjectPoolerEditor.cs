using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using UnityEditorInternal;
using static BoxCutter.BoxcutterEditorUtil;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxCutterObjectPool))]
    public class BoxcutterObjectPoolerEditor : Editor
    {
        private Texture2D customIcon;
        private readonly AnimBool showPoolSettings = new AnimBool(true);
        private ReorderableList poolList;
        private SerializedProperty listProp;

        private void OnEnable()
        {
            if (target == null || targets == null) return;
            customIcon = LoadLocalAsset("BoxcutterOPIcon.png");
            ApplyIcon(target, customIcon);

            showPoolSettings.valueChanged.AddListener(Repaint);

            listProp = serializedObject.FindProperty("objectPoolList");
            poolList = new ReorderableList(serializedObject, listProp, true, true, true, true);

            poolList.drawHeaderCallback = r =>
                EditorGUI.LabelField(r, "Object Pool Settings");

            poolList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = listProp.GetArrayElementAtIndex(index);
                rect.y += 2;
                EditorGUI.PropertyField(rect, element, true);
            };

            poolList.elementHeightCallback = index =>
            {
                var element = listProp.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(element, true) + 4;
            };
        }

        public override void OnInspectorGUI()
        {
            if (target == null || listProp == null) return;
            serializedObject.Update();

            Color poolHeaderColor = Color.Lerp(StartColor, EndColor, 0.5f);

            EditorGUILayout.BeginVertical(OuterContainerStyle);

            DrawTitle();
            DrawBanner(customIcon, "OBJECT POOLER");
            DrawHorizontalDivider();
            EditorGUILayout.Space();
            DrawFoldoutSection("POOL SETTINGS", showPoolSettings, poolHeaderColor, () =>
            {
                poolList.DoLayoutList();
            });
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        public override bool RequiresConstantRepaint() => true;
    }
}