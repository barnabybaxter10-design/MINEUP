using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;
using static BoxCutter.BoxcutterEditorUtil;
using System;

namespace BoxCutter
{
    [CustomEditor(typeof(FragSettings))]
    public class FragSettingEditor : Editor
    {
        private Texture2D customIcon;

        private readonly AnimBool showGeneralFoldout = new AnimBool(true);
        private readonly AnimBool showFalloffFoldout = new AnimBool(true);

        private readonly AnimBool showSlabSettingsFoldout = new AnimBool(true);
        private readonly AnimBool showRadialCrackFoldout = new AnimBool(true);
        private readonly AnimBool showRadialRingFoldout = new AnimBool(true);
        private readonly AnimBool showRadialPlaneFoldout = new AnimBool(true);

        private readonly AnimBool showSlabGroup = new AnimBool(false);
        private readonly AnimBool showRadialGroup = new AnimBool(false);

        private readonly AnimBool showClusterGroup = new AnimBool(false);
        private readonly AnimBool showClusterFoldout = new AnimBool(true);
        private readonly AnimBool clusterAnim = new AnimBool(false);

        private readonly AnimBool randomSizeAnim = new AnimBool(false);
        private readonly AnimBool customRadialPlaneAnim = new AnimBool(false);

        private readonly AnimBool showFixedFieldsAnim = new AnimBool(true);

        private void OnEnable()
        {
            if (target == null || targets == null) return;
            var animBools = new[]
            {
                showGeneralFoldout, showFalloffFoldout,
                showRadialCrackFoldout, showRadialRingFoldout, showRadialPlaneFoldout,
                showSlabGroup, showRadialGroup,
                showClusterGroup, showClusterFoldout, clusterAnim,
                randomSizeAnim, customRadialPlaneAnim,
                showFixedFieldsAnim
            };
            foreach (var ab in animBools)
                ab.valueChanged.AddListener(Repaint);

            customIcon = LoadLocalAsset("FragIcon.png");
            ApplyIcon(target, customIcon);

            SerializedProperty fragTypeProp = serializedObject.FindProperty("fragmentType");
            int fragTypeIndex = fragTypeProp.enumValueIndex;
            UpdateGroupTargets(fragTypeIndex);

            SerializedProperty fragType = serializedObject.FindProperty("fragmentType");
            bool isSplinter = fragType.enumValueIndex == (int)FragmentType.Splinter;
            string randomProp = isSplinter ? "canRandomSplinterAxis" : "canRandomSlabAxis";
            
            randomSizeAnim.value = serializedObject.FindProperty(randomProp).boolValue;
            showFixedFieldsAnim.value = !serializedObject.FindProperty(randomProp).boolValue;
            customRadialPlaneAnim.value = serializedObject.FindProperty("planeMode")
                .enumValueIndex == (int)RadialPlaneMode.Custom;

            SerializedProperty canClusterProp = serializedObject.FindProperty("canCluster");
            clusterAnim.value = canClusterProp.boolValue;
            showClusterFoldout.value = true;
        }

        public override void OnInspectorGUI()
        {
            if (target == null) return;
            serializedObject.Update();

            EditorGUILayout.BeginVertical(OuterContainerStyle);
            DrawTitle(
                "BoxCutter provides multiple fragmentation modes: Slab (fast, general-purpose), Splinter (realistic wood destruction), Radial (glass shattering).");
            DrawBanner(customIcon, "FRAG SETTINGS");
            DrawHorizontalDivider();
            DrawSeparator(1);

            SerializedProperty spFragmentType = serializedObject.FindProperty("fragmentType");
            UpdateGroupTargets(spFragmentType.enumValueIndex);

            DrawFoldoutSection(
                "GENERAL FRAGMENTATION",
                showGeneralFoldout,
                StartColor,
                () => { EditorGUILayout.PropertyField(spFragmentType); });

            int fragTypeIndex = spFragmentType.enumValueIndex;

            if (fragTypeIndex == (int)FragmentType.Standard)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "Standard fragmentation generates single voxels with no additional processing",
                    MessageType.Info
                );
                serializedObject.ApplyModifiedProperties();
            }

            bool isSplinter = fragTypeIndex == (int)FragmentType.Splinter;
            string slabTitle = isSplinter ? "SPLINTER SETTINGS" : "SLAB SETTINGS";

            DrawSlidingGroup(showSlabGroup, () =>
            {
                DrawSeparator();
                DrawFoldoutSection(
                    slabTitle,
                    showSlabSettingsFoldout,
                    Color.Lerp(StartColor, EndColor, 0.30f),
                    DrawSlabSettings);
            });

            DrawSlidingGroup(showClusterGroup, () =>
            {
                DrawSeparator();
                DrawFoldoutSection(
                    "CLUSTER SETTINGS",
                    showClusterFoldout,
                    Color.Lerp(StartColor, EndColor, 0.15f),
                    DrawClusterSettings);
            });

            DrawSlidingGroup(showRadialGroup, () =>
            {
                DrawSeparator();
                DrawFoldoutSection(
                    "RADIAL / LIGHTNING CRACKS",
                    showRadialCrackFoldout,
                    Color.Lerp(StartColor, EndColor, 0.60f),
                    DrawRadialCrackSettings);

                DrawSeparator();

                DrawFoldoutSection(
                    "RADIAL RINGS",
                    showRadialRingFoldout,
                    Color.Lerp(StartColor, EndColor, 0.70f),
                    DrawRadialRingSettings);

                DrawSeparator();

                DrawFoldoutSection(
                    "RADIAL PLANE MODE",
                    showRadialPlaneFoldout,
                    Color.Lerp(StartColor, EndColor, 0.80f),
                    DrawRadialPlaneSettings);
            });

            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }


        private void DrawSlabSettings()
        {
            EditorGUILayout.Slider(serializedObject.FindProperty("outRadiusScaleMul"), 0, 1);
            
            // Add splinter axis mode for splinter fragments
            SerializedProperty fragTypeProp = serializedObject.FindProperty("fragmentType");
            bool isSplinter = fragTypeProp.enumValueIndex == (int)FragmentType.Splinter;
            bool isSlab = fragTypeProp.enumValueIndex == (int)FragmentType.Slab;
            
            if (isSplinter)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("splinterAxisMode"));
                EditorGUILayout.Space();
            }
            
            string randomPropName = isSplinter ? "canRandomSplinterAxis" : "canRandomSlabAxis";
            SerializedProperty spRandom = serializedObject.FindProperty(randomPropName);
            
            if (isSlab)
            {
                EditorGUILayout.PropertyField(spRandom, new GUIContent("Random Size"));
            }
            else
            {
                EditorGUILayout.PropertyField(spRandom);
            }
            randomSizeAnim.target = spRandom.boolValue;
            showFixedFieldsAnim.target = !spRandom.boolValue;

            if (EditorGUILayout.BeginFadeGroup(randomSizeAnim.faded))
            {
                if (isSlab)
                {
                    DrawValidatedVector2FieldWithCustomLabel(serializedObject.FindProperty("slabRangePrimary"), "Size X");
                    DrawValidatedVector2FieldWithCustomLabel(serializedObject.FindProperty("slabRangeSecondary"), "Size Y");
                    DrawValidatedVector2FieldWithCustomLabel(serializedObject.FindProperty("slabRangeTertiary"), "Size Z");
                }
                else // splinter
                {
                    DrawValidatedVector2FieldWithCustomLabel(serializedObject.FindProperty("splinterRangePrimary"), "Primary Axis");
                    DrawValidatedVector2FieldWithCustomLabel(serializedObject.FindProperty("splinterRangeSecondary"), "Secondary Axis");
                    DrawValidatedVector2FieldWithCustomLabel(serializedObject.FindProperty("splinterRangeTertiary"), "Tertiary Axis");
                }
            }

            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(showFixedFieldsAnim.faded))
            {
                if (isSlab)
                {
                    DrawValidatedIntFieldWithCustomLabel(serializedObject.FindProperty("fixedSlabPrimaryAxis"), "Size X");
                    DrawValidatedIntFieldWithCustomLabel(serializedObject.FindProperty("fixedSlabSecondaryAxis"), "Size Y");
                    DrawValidatedIntFieldWithCustomLabel(serializedObject.FindProperty("fixedSlabTertiaryAxis"), "Size Z");
                }
                else // splinter
                {
                    DrawValidatedIntFieldWithCustomLabel(serializedObject.FindProperty("fixedSplinterPrimaryAxis"), "Primary Axis");
                    DrawValidatedIntFieldWithCustomLabel(serializedObject.FindProperty("fixedSplinterSecondaryAxis"), "Secondary Axis");
                    DrawValidatedIntFieldWithCustomLabel(serializedObject.FindProperty("fixedSplinterTertiaryAxis"), "Tertiary Axis");
                }
            }

            EditorGUILayout.EndFadeGroup();

            EditorGUILayout.Space();
        }

        private void DrawClusterSettings()
        {
            SerializedProperty canClusterProp = serializedObject.FindProperty("canCluster");
            EditorGUILayout.PropertyField(canClusterProp);

            clusterAnim.target = canClusterProp.boolValue;

            if (EditorGUILayout.BeginFadeGroup(clusterAnim.faded))
            {
                DrawValidatedIntField(serializedObject.FindProperty("maxClusterRadius"), 0);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useDiscMask"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("radialWeight"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("densityFallExp"));
            }

            EditorGUILayout.EndFadeGroup();
        }

        private void DrawRadialCrackSettings()
        {
            DrawValidatedIntField(serializedObject.FindProperty("radialSpokes"), 0);
            DrawValidatedFloatField(serializedObject.FindProperty("crackLengthMultiplier"), 0.1f);
            DrawValidatedFloatField(serializedObject.FindProperty("minSegmentLength"), 0.01f);
            DrawValidatedFloatField(serializedObject.FindProperty("maxSegmentLength"), 0.02f);
            DrawValidatedFloatField(serializedObject.FindProperty("maxZigZagAngleDeg"), 0);
            DrawValidatedIntField(serializedObject.FindProperty("crackThickness"), 0);
        }

        private void DrawRadialRingSettings()
        {
            // Core ring generation parameters
            DrawValidatedFloatField(serializedObject.FindProperty("ringDensity"), 0.1f);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ringCoverage"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ringGrowthExp"));
            
            // Ring appearance and falloff
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ringFalloff"));
            DrawValidatedIntField(serializedObject.FindProperty("ringThickness"), 0);
            
            EditorGUILayout.Space();
            
            // Ring generation type and noise
            EditorGUILayout.PropertyField(serializedObject.FindProperty("radialRingGenType"));
            DrawValidatedFloatField(serializedObject.FindProperty("ringNoiseAmplitude"), 0);
            DrawValidatedFloatField(serializedObject.FindProperty("ringNoiseFrequency"), 0);
        }

        private void DrawRadialPlaneSettings()
        {
            SerializedProperty spPlaneMode = serializedObject.FindProperty("planeMode");
            EditorGUILayout.PropertyField(spPlaneMode);

            customRadialPlaneAnim.target =
                spPlaneMode.enumValueIndex == (int)RadialPlaneMode.Custom;

            if (EditorGUILayout.BeginFadeGroup(customRadialPlaneAnim.faded))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("radialPlaneNormal"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("radialPlaneForward"));
            }

            EditorGUILayout.EndFadeGroup();
        }


        private void UpdateGroupTargets(int fragTypeIndex)
        {
            bool isSlab = fragTypeIndex == (int)FragmentType.Slab;
            bool isSplinter = fragTypeIndex == (int)FragmentType.Splinter;
            showSlabGroup.target = isSlab || isSplinter;

            showRadialGroup.target = fragTypeIndex == (int)FragmentType.Radial;
            showClusterGroup.target = isSlab; // Only show cluster settings for slab, not splinter
        }

        private static void DrawSlidingGroup(AnimBool animBool, Action content)
        {
            const float slideDistance = 20f;

            if (EditorGUILayout.BeginFadeGroup(animBool.faded))
            {
                float offset = (animBool.target
                    ? -(1 - animBool.faded)
                    : (1 - animBool.faded)) * slideDistance;
                using (new GUILayout.VerticalScope())
                {
                    var original = GUI.matrix;
                    GUI.matrix = Matrix4x4.Translate(new Vector3(0, offset, 0)) * original;
                    content.Invoke();
                    GUI.matrix = original;
                }
            }

            EditorGUILayout.EndFadeGroup();
        }

        private void DrawValidatedIntField(SerializedProperty property, int min = 1)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property);
            if (EditorGUI.EndChangeCheck())
            {
                if (property.intValue < min)
                {
                    property.intValue = min;
                }
            }
        }

        private void DrawValidatedFloatField(SerializedProperty property, float min = 1f)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property);
            if (EditorGUI.EndChangeCheck())
            {
                if (property.floatValue < min)
                {
                    property.floatValue = min;
                }
            }
        }

        private void DrawValidatedVector2Field(SerializedProperty property)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property);
            if (EditorGUI.EndChangeCheck())
            {
                // Handle int2 properties (Unity Mathematics)
                var xProp = property.FindPropertyRelative("x");
                var yProp = property.FindPropertyRelative("y");
                
                if (xProp != null && yProp != null)
                {
                    xProp.intValue = Mathf.Max(1, xProp.intValue);
                    yProp.intValue = Mathf.Max(1, yProp.intValue);
                }
            }
        }

        private void DrawValidatedVector2FieldWithCustomLabel(SerializedProperty property, string customLabel)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property, new GUIContent(customLabel));
            if (EditorGUI.EndChangeCheck())
            {
                // Handle int2 properties (Unity Mathematics)
                var xProp = property.FindPropertyRelative("x");
                var yProp = property.FindPropertyRelative("y");
                
                if (xProp != null && yProp != null)
                {
                    xProp.intValue = Mathf.Max(1, xProp.intValue);
                    yProp.intValue = Mathf.Max(1, yProp.intValue);
                }
            }
        }

        private void DrawValidatedIntFieldWithCustomLabel(SerializedProperty property, string customLabel, int min = 1)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property, new GUIContent(customLabel));
            if (EditorGUI.EndChangeCheck())
            {
                if (property.intValue < min)
                {
                    property.intValue = min;
                }
            }
        }
    }
}