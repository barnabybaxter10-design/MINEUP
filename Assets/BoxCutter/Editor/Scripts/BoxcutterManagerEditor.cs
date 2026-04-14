using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using static BoxCutter.BoxcutterEditorUtil;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxCutterManager))]
    public class BoxCutterManagerEditor : Editor
    {
        private Texture2D customIcon;

        private readonly AnimBool showDebugSettings = new AnimBool(true);
        private readonly AnimBool showMainSettings = new AnimBool(true);
        private readonly AnimBool showReturnSettings = new AnimBool(true);

        private AnimBool asyncDestructionAnim;
        private AnimBool asyncColliderAnim;
        private AnimBool asyncDebrisAnim;
        private AnimBool returnFallenIslandsAnim;
        private AnimBool returnDebrisAnim;
        private AnimBool maxActiveDebrisAnim;
        
        private SerializedProperty sp_asyncDestruction;
        private SerializedProperty sp_asyncDestructionMode;
        private SerializedProperty sp_asyncDestructionMaxFrames;
        private SerializedProperty sp_asyncDebris;
        private SerializedProperty sp_asyncDebrisMaxFrames;
        private SerializedProperty sp_processingIds;
        private SerializedProperty sp_triggerRequests;
        private SerializedProperty sp_freezeDataList;
        private SerializedProperty sp_defSetVoxelSize;
        private SerializedProperty sp_ignoreDiagonalsInIsland;
        private SerializedProperty sp_kdMaxSubInCell;
        private SerializedProperty sp_validCellFillRatio;
        private SerializedProperty sp_maxDebrisPerFrame;
        private SerializedProperty sp_connectionLeniencyDist;
        private SerializedProperty sp_canReturnFallenIslands;
        private SerializedProperty sp_returnFallenIslandDelay;
        private SerializedProperty sp_returnFallenIslandDura;
        private SerializedProperty sp_canReturnDebris;
        private SerializedProperty sp_returnDebrisDelay;
        private SerializedProperty sp_returnDebrisDura;
        private SerializedProperty sp_canMaxActiveDebrisAndIslands;
        private SerializedProperty sp_maxActiveDebrisAndIslands;
        private SerializedProperty sp_activeDebrisCount;
        
        #region Initialization

        private void OnEnable()
        {
            if (target == null || targets == null) return;
            customIcon = LoadLocalAsset("BoxcutterManagerIcon.png");
            ApplyIcon(target, customIcon);

            var animBools = new[] { showDebugSettings, showMainSettings, showReturnSettings };
            foreach (var ab in animBools)
                ab.valueChanged.AddListener(Repaint);

            var so = serializedObject;
            sp_asyncDestruction = so.FindProperty("asyncDestruction");
            sp_asyncDestructionMode = so.FindProperty("asyncDestructionMode");
            sp_asyncDestructionMaxFrames = so.FindProperty("asyncDestructionMaxFrames");
            sp_asyncDebris = so.FindProperty("asyncDebris");
            sp_asyncDebrisMaxFrames = so.FindProperty("asyncDebrisMaxFrames");
            sp_processingIds = so.FindProperty("processingIds");
            sp_triggerRequests = so.FindProperty("triggerRequests");
            sp_freezeDataList = so.FindProperty("freezeDataList");
            sp_defSetVoxelSize = so.FindProperty("defSetVoxelSize");
            sp_ignoreDiagonalsInIsland = so.FindProperty("ignoreDiagonalsInIsland");
            sp_validCellFillRatio = so.FindProperty("validCellFillRatio");
            sp_kdMaxSubInCell = so.FindProperty("kdMaxSubInCell");
            sp_maxDebrisPerFrame = so.FindProperty("maxDebrisPerFrame");
            sp_connectionLeniencyDist = so.FindProperty("connectionLeniencyDist");
            sp_canReturnFallenIslands = so.FindProperty("canReturnFallenIslands");
            sp_returnFallenIslandDelay = so.FindProperty("returnFallenIslandDelay");
            sp_returnFallenIslandDura = so.FindProperty("returnFallenIslandDura");
            sp_canReturnDebris = so.FindProperty("canReturnDebris");
            sp_returnDebrisDelay = so.FindProperty("returnDebrisDelay");
            sp_returnDebrisDura = so.FindProperty("returnDebrisDura");
            sp_canMaxActiveDebrisAndIslands = so.FindProperty("canMaxActiveDebrisAndIslands");
            sp_maxActiveDebrisAndIslands = so.FindProperty("maxActiveDebrisAndIslands");
            sp_activeDebrisCount = so.FindProperty("activeDebrisCount");

            asyncDestructionAnim = MakeAnimBool(so, "asyncDestruction");
            asyncDebrisAnim = MakeAnimBool(so, "asyncDebris");
            returnFallenIslandsAnim = MakeAnimBool(so, "canReturnFallenIslands");
            returnDebrisAnim = MakeAnimBool(so, "canReturnDebris");
            maxActiveDebrisAnim = MakeAnimBool(so, "canMaxActiveDebrisAndIslands");
        }

        private AnimBool MakeAnimBool(SerializedObject so, string boolProp)
        {
            var ab = new AnimBool(so.FindProperty(boolProp).boolValue);
            ab.valueChanged.AddListener(Repaint);
            return ab;
        }

        #endregion


        #region GUI

        public override void OnInspectorGUI()
        {
            if (target == null) return; // || sp_asyncDestruction == null
            serializedObject.Update();

            returnFallenIslandsAnim.target = sp_canReturnFallenIslands.boolValue;
            returnDebrisAnim.target = sp_canReturnDebris.boolValue;
            maxActiveDebrisAnim.target = sp_canMaxActiveDebrisAndIslands.boolValue;

            EditorGUILayout.BeginVertical(OuterContainerStyle);
            DrawTitle();
            DrawBanner(customIcon, "CORE MANAGER");
            DrawHorizontalDivider();

            DrawSeparator();

            DrawFoldoutSection("MAIN SETTINGS", showMainSettings, StartColor, () =>
            {
                EditorGUILayout.PropertyField(sp_defSetVoxelSize);
                EditorGUILayout.PropertyField(sp_ignoreDiagonalsInIsland);
                EditorGUILayout.PropertyField(sp_kdMaxSubInCell);
                EditorGUILayout.Slider(sp_validCellFillRatio, 0, 1, "Kd Cell Tightness");
                EditorGUILayout.PropertyField(sp_connectionLeniencyDist);
            });

            DrawSeparator();
            
            DrawFoldoutSection("ASYNC SETTINGS", showMainSettings, Color.Lerp(StartColor, EndColor, 0.5f), () =>
            {
                #if UNITY_WEBGL
                bool isWebGl = true;
                #else
                bool isWebGl = false;
                #endif
                if (isWebGl)
                {
                    GUI.enabled = false;
                    EditorGUILayout.HelpBox("WebGL does not support async processing.", MessageType.Warning);
                    //sp_asyncDestruction.boolValue = false;
                    sp_asyncDebris.boolValue = false;
                }
                EditorGUILayout.PropertyField(sp_asyncDestruction);
                asyncDestructionAnim.target = sp_asyncDestruction.boolValue;
                if (EditorGUILayout.BeginFadeGroup(asyncDestructionAnim.faded))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.HelpBox("\"Core\" asynchronously processes only the most resource intensive tasks for lower latency, while \"Full\" provides a fully asynchronous performance.", MessageType.Info);
                    EditorGUILayout.PropertyField(sp_asyncDestructionMode);
                    EditorGUILayout.IntSlider(sp_asyncDestructionMaxFrames, -1, 50, new GUIContent("Max Frames Per Destruction Job"));
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFadeGroup();
                EditorGUILayout.PropertyField(sp_asyncDebris);
                asyncDebrisAnim.target = sp_asyncDebris.boolValue;
                if (EditorGUILayout.BeginFadeGroup(asyncDebrisAnim.faded))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.IntSlider(sp_asyncDebrisMaxFrames, -1, 50, new GUIContent("Max Frames Per Debris Job"));
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFadeGroup();
                if (sp_maxDebrisPerFrame.intValue == 0)
                {
                    EditorGUILayout.HelpBox("Will spawn all debris in one frame", MessageType.Info);
                }
                EditorGUILayout.IntSlider(sp_maxDebrisPerFrame, 0, 500);
                /*GUI.enabled = false;
                EditorGUILayout.PropertyField(sp_processingIds);
                EditorGUILayout.PropertyField(sp_triggerRequests);
                EditorGUILayout.PropertyField(sp_freezeDataList);
                GUI.enabled = true;*/

                /*GUI.enabled = false;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("destroying"));
                //EditorGUILayout.PropertyField(serializedObject.FindProperty("currentQueueId"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("queueId"));
                GUI.enabled = true;*/
                
                if (isWebGl)
                {
                    GUI.enabled = true;
                }
            });

            DrawSeparator();

            DrawFoldoutSection("RETURN SETTINGS", showReturnSettings, EndColor, () =>
            {
                EditorGUILayout.PropertyField(sp_canReturnFallenIslands);
                if (EditorGUILayout.BeginFadeGroup(returnFallenIslandsAnim.faded))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.Slider(sp_returnFallenIslandDelay, 0f, 100f);
                    EditorGUILayout.Slider(sp_returnFallenIslandDura, 0f, 100f);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndFadeGroup();

                EditorGUILayout.PropertyField(sp_canReturnDebris);
                if (EditorGUILayout.BeginFadeGroup(returnDebrisAnim.faded))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.Slider(sp_returnDebrisDelay, 0f, 1000f);
                    EditorGUILayout.Slider(sp_returnDebrisDura, 0f, 1000f);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space();
                EditorGUILayout.EndFadeGroup();

                EditorGUILayout.PropertyField(sp_canMaxActiveDebrisAndIslands);
                if (EditorGUILayout.BeginFadeGroup(maxActiveDebrisAnim.faded))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.IntSlider(sp_maxActiveDebrisAndIslands, 1, 2500);
                    GUI.enabled = false;
                    EditorGUILayout.PropertyField(sp_activeDebrisCount);
                    GUI.enabled = true;
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndFadeGroup();
            });

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        public override bool RequiresConstantRepaint() => true;

        #endregion
    }
}