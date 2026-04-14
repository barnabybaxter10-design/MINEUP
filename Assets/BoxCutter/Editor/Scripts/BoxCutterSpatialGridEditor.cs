using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using static BoxCutter.BoxcutterEditorUtil;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxCutterSpatialGrid)), CanEditMultipleObjects]
    public class BoxCutterSpatialGridEditor : Editor
    {
        private Texture2D customIcon;

        private readonly AnimBool showDebugSettings = new AnimBool(true);
        private readonly AnimBool showMainSettings  = new AnimBool(true);

        private AnimBool canShowGizmosAnim;

        private SerializedProperty sp_canDebug;
        private SerializedProperty sp_canShowGizmos;
        private SerializedProperty sp_showCells;
        private SerializedProperty sp_showIndex;
        private SerializedProperty sp_gridBounds;
        private SerializedProperty sp_resolution;

        private SerializedProperty sp_cellSizeX;
        private SerializedProperty sp_cellSizeY;
        private SerializedProperty sp_cellSizeZ;

        private SerializedProperty sp_cellsX;
        private SerializedProperty sp_cellsY;
        private SerializedProperty sp_cellsZ;
        private SerializedProperty sp_totalCells;

        private void OnEnable()
        {
            if (target == null || targets == null) return;
            customIcon = LoadLocalAsset("BoxcutterSpatialIcon.png");
            foreach (var obj in targets) ApplyIcon(obj, customIcon);

            showDebugSettings.valueChanged.AddListener(Repaint);
            showMainSettings.valueChanged.AddListener(Repaint);

            var so = serializedObject;
            sp_canDebug      = so.FindProperty("canDebug");
            sp_canShowGizmos = so.FindProperty("canShowGizmos");
            sp_showCells     = so.FindProperty("showCells");
            sp_showIndex     = so.FindProperty("showIndex");
            sp_gridBounds    = so.FindProperty("gridBounds");
            sp_resolution    = so.FindProperty("resolution");

            sp_cellSizeX     = so.FindProperty("cellSizeX");
            sp_cellSizeY     = so.FindProperty("cellSizeY");
            sp_cellSizeZ     = so.FindProperty("cellSizeZ");

            sp_cellsX        = so.FindProperty("cellAmountPerX");
            sp_cellsY        = so.FindProperty("cellAmountPerY");
            sp_cellsZ        = so.FindProperty("cellAmountPerZ");
            sp_totalCells    = so.FindProperty("totalCells");

            canShowGizmosAnim = MakeAnimBool(so, "canShowGizmos");
        }

        private AnimBool MakeAnimBool(SerializedObject so, string propName)
        {
            var ab = new AnimBool(so.FindProperty(propName).boolValue);
            ab.valueChanged.AddListener(Repaint);
            return ab;
        }

        public override void OnInspectorGUI()
        {
            if (target == null || sp_canShowGizmos == null) return;
            serializedObject.Update();

            canShowGizmosAnim.target = sp_canShowGizmos.boolValue;

            EditorGUILayout.BeginVertical(OuterContainerStyle);
            DrawTitle();
            DrawBanner(customIcon, "SPATIAL GRID");
            DrawHorizontalDivider();

            EditorGUILayout.HelpBox(
                "Rotation is locked. This spatial grid is always axis-aligned (0,0,0,1). Any rotation you apply will be reset automatically.",
                MessageType.Info);

            Color debugColor = Color.Lerp(StartColor, EndColor, 0f / 1f);
            Color mainColor  = Color.Lerp(StartColor, EndColor, 1f / 1f);

            DrawFoldoutSection("DEBUG SETTINGS", showDebugSettings, debugColor, () =>
            {
                EditorGUILayout.PropertyField(sp_canShowGizmos);

                if (EditorGUILayout.BeginFadeGroup(canShowGizmosAnim.faded))
                {
                    EditorGUI.indentLevel++;
                    int thresholdValue = serializedObject.FindProperty("threshold").intValue;
                    if (sp_totalCells.intValue > thresholdValue)
                        EditorGUILayout.HelpBox($"With over {thresholdValue} cells in this grid, enabling “Show All Cells” could cause the editor to freeze.", MessageType.Warning);

                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        EditorGUILayout.PropertyField(sp_showCells, new GUIContent("Show All Cells"));
                    }

                    EditorGUILayout.IntSlider(sp_showIndex, -1, Mathf.Max(0, sp_totalCells.intValue), new GUIContent("Highlight Cell Index"));
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFadeGroup();
            });

            DrawSeparator();

            DrawFoldoutSection("MAIN SETTINGS", showMainSettings, mainColor, () =>
            {
                bool isPlaying = Application.isPlaying;

                using (new EditorGUI.DisabledScope(isPlaying))
                {
                    EditorGUILayout.PropertyField(sp_gridBounds);

                    // CLAMP: Resolution never below 1
                    int current = sp_resolution.intValue;
                    int input   = EditorGUILayout.IntField(new GUIContent("Resolution"), Mathf.Max(1, current));
                    if (input < 1) input = 1;
                    if (input != current) sp_resolution.intValue = input;
                }

                EditorGUILayout.Space();

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(sp_cellSizeX, new GUIContent("Cell Size X"));
                    EditorGUILayout.PropertyField(sp_cellSizeY, new GUIContent("Cell Size Y"));
                    EditorGUILayout.PropertyField(sp_cellSizeZ, new GUIContent("Cell Size Z"));

                    EditorGUILayout.PropertyField(sp_cellsX, new GUIContent("Cells per X"));
                    EditorGUILayout.PropertyField(sp_cellsY, new GUIContent("Cells per Y"));
                    EditorGUILayout.PropertyField(sp_cellsZ, new GUIContent("Cells per Z"));
                    EditorGUILayout.PropertyField(sp_totalCells, new GUIContent("Total Cells"));
                }
            });

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        // OnSceneGUI must use single target
        private void OnSceneGUI()
        {
            var grid = target as BoxCutterSpatialGrid;
            if (grid == null) return;

            var t = grid.transform;
            if (t.rotation != Quaternion.identity)
            {
                Undo.RecordObject(t, "Lock BoxCutterSpatialGrid Rotation");
                t.rotation = Quaternion.identity;
                EditorUtility.SetDirty(t);
            }
        }
    }
}