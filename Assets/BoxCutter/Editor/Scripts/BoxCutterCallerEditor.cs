using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using static BoxCutter.BoxcutterEditorUtil;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxCutterCaller)), CanEditMultipleObjects]
    public class BoxCutterCallerEditor : Editor
    {
        private Texture2D customIcon;

        private readonly AnimBool showDebugSettings = new AnimBool(true);
        private readonly AnimBool showMainSettings = new AnimBool(true);
        private readonly AnimBool showAdditionalSettings = new AnimBool(true);
        private readonly AnimBool showFragmentationSettings = new AnimBool(true);

        private AnimBool canShowGizmosAnim;
        private AnimBool visualizeDestructionAnim;
        private AnimBool boxModeAnim;
        private AnimBool canSpawnDebrisAnim;
        private AnimBool seedVisibilityAnim;
        private AnimBool canForceOffset;
        private AnimBool useWhiteListAnim;
        private AnimBool whiteListVisibilityAnim;
        private AnimBool blackListVisibilityAnim;
        
        private SerializedProperty sp_canShowGizmos;
        private SerializedProperty sp_boxMode;
        private SerializedProperty sp_canSpawnDebris;
        private SerializedProperty sp_canRandomSeed;
        private SerializedProperty sp_canForceOffset;
        private SerializedProperty sp_useWhiteList;

        #region Initialization

        private void OnEnable()
        {
            if (target == null || targets == null) return;
            customIcon = LoadLocalAsset("BoxcutterCallerIcon.png");

            foreach (var obj in targets)
                ApplyIcon(obj, customIcon);

            var animBools = new[] { showDebugSettings, showMainSettings, showAdditionalSettings, showFragmentationSettings };
            foreach (var ab in animBools)
                ab.valueChanged.AddListener(Repaint);

            var so = serializedObject;
            sp_canShowGizmos = so.FindProperty("canShowGizmos");
            sp_boxMode = so.FindProperty("boxMode");
            sp_canSpawnDebris = so.FindProperty("canSpawnDebris");
            sp_canRandomSeed = so.FindProperty("canRandomSeed");
            sp_canForceOffset = so.FindProperty("canForceOffset");
            sp_useWhiteList = so.FindProperty("useWhiteList");
            
            canShowGizmosAnim = MakeAnimBool(so, "canShowGizmos");
            visualizeDestructionAnim = new AnimBool(so.FindProperty("visualizeDestruction").boolValue);
            boxModeAnim = MakeAnimBool(so, "boxMode");
            canSpawnDebrisAnim = MakeAnimBool(so, "canSpawnDebris");
            seedVisibilityAnim = new AnimBool(!sp_canRandomSeed.boolValue);
            seedVisibilityAnim.valueChanged.AddListener(Repaint);
            canForceOffset = MakeAnimBool(so, "canForceOffset");
            useWhiteListAnim = MakeAnimBool(so, "useWhiteList");
            whiteListVisibilityAnim = new AnimBool(sp_useWhiteList.boolValue);
            whiteListVisibilityAnim.valueChanged.AddListener(Repaint);
            blackListVisibilityAnim = new AnimBool(!sp_useWhiteList.boolValue);
            blackListVisibilityAnim.valueChanged.AddListener(Repaint);
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
            if (target == null || sp_canShowGizmos == null) return;
            serializedObject.Update();

            canShowGizmosAnim.target = sp_canShowGizmos.boolValue;
            boxModeAnim.target = sp_boxMode.boolValue;
            canSpawnDebrisAnim.target = sp_canSpawnDebris.boolValue;
            seedVisibilityAnim.target = !sp_canRandomSeed.boolValue;
            canForceOffset.target = sp_canForceOffset.boolValue;
            useWhiteListAnim.target = sp_useWhiteList.boolValue;
            whiteListVisibilityAnim.target = sp_useWhiteList.boolValue;
            blackListVisibilityAnim.target = !sp_useWhiteList.boolValue;

            EditorGUILayout.BeginVertical(OuterContainerStyle);
            DrawTitle("This script is the public entry point for initiating the voxel destruction and is great for nearly any use case.");
            DrawBanner(customIcon, "CUTTER CALLER");
            DrawHorizontalDivider();

            Color debugHeaderColor = Color.Lerp(StartColor, EndColor, 0f / 3f);
            Color mainHeaderColor = Color.Lerp(StartColor, EndColor, 1f / 3f);
            Color additionalHeaderColor = Color.Lerp(StartColor, EndColor, 2f / 3f);
            Color fragmentationHeaderColor = Color.Lerp(StartColor, EndColor, 3f / 3f);

            DrawFoldoutSection("DEBUG SETTINGS", showDebugSettings, debugHeaderColor, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("canPrintMs"));
                EditorGUILayout.PropertyField(sp_canShowGizmos);

                var visualizeDestructionProp = serializedObject.FindProperty("visualizeDestruction");
                visualizeDestructionAnim.target = visualizeDestructionProp.boolValue;

                EditorGUILayout.PropertyField(visualizeDestructionProp);
                if (EditorGUILayout.BeginFadeGroup(visualizeDestructionAnim.faded))
                {
                    if (Application.isPlaying)
                    {
                        EditorGUILayout.HelpBox("The destruction preview can only be shown in the editor.", MessageType.Info);
                    }
                    var debugCallerProp = serializedObject.FindProperty("visualizeBoxArr");
                    
                    /*if (debugCallerProp.arraySize == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "visualizeBoxArr is empty. Please assign BoxObj's.",
                            MessageType.Warning
                        );
                    }*/
                    EditorGUILayout.PropertyField(debugCallerProp);
                }

                EditorGUILayout.EndFadeGroup();

                if (EditorGUILayout.BeginFadeGroup(canShowGizmosAnim.faded))
                {
                    EditorGUILayout.Slider(serializedObject.FindProperty("gizmosAlpha"), 0f, 1f);
                }

                EditorGUILayout.EndFadeGroup();
            });

            DrawSeparator();

            DrawFoldoutSection("MAIN SETTINGS", showMainSettings, mainHeaderColor, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("posOffset"));
                EditorGUILayout.PropertyField(sp_boxMode);

                if (EditorGUILayout.BeginFadeGroup(1f - boxModeAnim.faded))
                    EditorGUILayout.Slider(serializedObject.FindProperty("radius"), 0f, 150f);
                EditorGUILayout.EndFadeGroup();

                if (EditorGUILayout.BeginFadeGroup(boxModeAnim.faded))
                {
                    var boxBoundsFromTrans = serializedObject.FindProperty("boxBoundsFromTrans");
                    bool inheritingBoxBounds = boxBoundsFromTrans.boolValue;
                    EditorGUILayout.PropertyField(boxBoundsFromTrans);

                    var rotFromTrans = serializedObject.FindProperty("rotFromTrans");
                    bool inheritingRot = rotFromTrans.boolValue;
                    EditorGUILayout.PropertyField(rotFromTrans);

                    if (inheritingBoxBounds) GUI.enabled = false;
                    var boxBoundsProp = serializedObject.FindProperty("boxBounds");
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(boxBoundsProp);
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Ensure box bounds are never negative (handling float3 as individual components)
                        var xProp = boxBoundsProp.FindPropertyRelative("x");
                        var yProp = boxBoundsProp.FindPropertyRelative("y");
                        var zProp = boxBoundsProp.FindPropertyRelative("z");
                        
                        if (xProp != null) xProp.floatValue = Mathf.Max(0f, xProp.floatValue);
                        if (yProp != null) yProp.floatValue = Mathf.Max(0f, yProp.floatValue);
                        if (zProp != null) zProp.floatValue = Mathf.Max(0f, zProp.floatValue);
                    }
                    if (inheritingBoxBounds) GUI.enabled = true;

                    if (inheritingRot) GUI.enabled = false;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("boxRot"));
                    if (inheritingRot) GUI.enabled = true;
                }

                EditorGUILayout.EndFadeGroup();
            });

            DrawSeparator();

            DrawFoldoutSection("ADDITIONAL SETTINGS", showAdditionalSettings, additionalHeaderColor, () =>
            {
                EditorGUILayout.PropertyField(sp_canSpawnDebris);

                EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnForce"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("forceRange"));

                EditorGUILayout.PropertyField(sp_canForceOffset);
                if (EditorGUILayout.BeginFadeGroup(canForceOffset.faded))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("worldForceOffset"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("forceOffset"));
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndFadeGroup();

                EditorGUILayout.PropertyField(sp_useWhiteList);

                if (Application.isPlaying)
                {
                    if (EditorGUILayout.BeginFadeGroup(whiteListVisibilityAnim.faded))
                    {
                        var whiteIds = serializedObject.FindProperty("whiteListedIds");
                        if (whiteIds.arraySize != 0)
                            EditorGUILayout.HelpBox("To modify the white listed objects in runtime, use whiteListedIds instead and add the BoxObjs inheritableId to the list.", MessageType.Info);
                        EditorGUILayout.PropertyField(whiteIds);
                    }
                    EditorGUILayout.EndFadeGroup();

                    if (EditorGUILayout.BeginFadeGroup(blackListVisibilityAnim.faded))
                    {
                        var blackIds = serializedObject.FindProperty("blackListedIds");
                        if (blackIds.arraySize != 0)
                            EditorGUILayout.HelpBox("To modify the black listed objects in runtime, use blackListedIds instead and add the BoxObjs inheritableId to the list.", MessageType.Info);
                        EditorGUILayout.PropertyField(blackIds);
                    }
                    EditorGUILayout.EndFadeGroup();
                }
                else
                {
                    if (EditorGUILayout.BeginFadeGroup(whiteListVisibilityAnim.faded))
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("whiteListedObjs"));
                    EditorGUILayout.EndFadeGroup();

                    if (EditorGUILayout.BeginFadeGroup(blackListVisibilityAnim.faded))
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("blackListedObjs"));
                    EditorGUILayout.EndFadeGroup();
                }
            });

            DrawSeparator();

            DrawFoldoutSection("FRAGMENTATION SETTINGS", showFragmentationSettings, fragmentationHeaderColor, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("turnOffStrengthNormalizer"));
                EditorGUILayout.Slider(serializedObject.FindProperty("strength"), 0, 1);

                EditorGUILayout.PropertyField(sp_canRandomSeed);

                if (EditorGUILayout.BeginFadeGroup(seedVisibilityAnim.faded))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("seed"));
                EditorGUILayout.EndFadeGroup();
            });

            DrawSeparator(2);
            if (DrawGradientButton("EXPLODE"))
            {
                foreach (var obj in targets)
                    ((BoxCutterCaller)obj).Explode();
            }

            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }

        public override bool RequiresConstantRepaint() => true;

        #endregion
    }
}