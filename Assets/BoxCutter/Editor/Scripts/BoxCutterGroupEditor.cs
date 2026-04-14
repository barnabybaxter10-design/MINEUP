using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using static BoxCutter.BoxcutterEditorUtil;
using static BoxCutter.BoxCutterManager;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    //─────────────────────────────────────────────────────────────────────────────────────
    //                               BoxCutter Group Editor Component
    //─────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Custom Unity Editor for BoxCutterGroup components providing advanced hierarchy visualization.
    /// Features tree-based bone relationship visualization, animated UI sections, and real-time
    /// statistics for complex rig setups. Supports multi-parent bone structures and required connections
    /// for realistic limb dismemberment behavior in destruction systems.
    /// </summary>
    [CustomEditor(typeof(BoxCutterGroup))]
    public class BoxCutterGroupEditor : Editor
    {
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Fields & Initialization
        //─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Custom icon texture for BoxCutter group folder display</summary>
        private Texture2D customIcon;
        /// <summary>Animated boolean for main settings section visibility</summary>
        private readonly AnimBool showSettings = new AnimBool(true);
        /// <summary>Serialized property reference for automatic setup toggle</summary>
        private SerializedProperty autoSetUpProp;
        /// <summary>Serialized property reference for fallback ID assignment</summary>
        private SerializedProperty fallIdTo;

        /// <summary>
        /// Unity OnEnable callback for editor initialization.
        /// Sets up custom icon display, caches serialized properties, and configures
        /// animated UI sections with smooth transition animations.
        /// </summary>
        private void OnEnable()
        {
            if (target == null || targets == null) return;
            // Load and apply custom folder icon for visual identification
            customIcon = LoadLocalAsset("BoxCutterFolderIcon.png");
            ApplyIcon(target, customIcon);
            
            // Cache serialized property references for performance
            autoSetUpProp = serializedObject.FindProperty("autoSetUp");
            fallIdTo = serializedObject.FindProperty("fallIdTo");

            // Setup animated section event handlers
            showSettings.valueChanged.AddListener(Repaint);
        }

        /// <summary>
        /// Unity OnDisable callback for proper cleanup.
        /// Removes event listeners to prevent memory leaks when editor is destroyed.
        /// </summary>
        private void OnDisable()
        {
            // Clean up animated section listeners to prevent memory leaks
            if (showSettings != null)
                showSettings.valueChanged.RemoveListener(Repaint);
        }
        
        //─────────────────────────────────────────────────────────────────────────────────────
        //                               Unity Inspector GUI
        //─────────────────────────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Main Unity Inspector GUI rendering method with enhanced visualization features.
        /// Provides animated UI sections, real-time bone hierarchy visualization, and comprehensive
        /// settings management for BoxCutter group behavior and rig support configuration.
        /// </summary>
        public override void OnInspectorGUI()
        {
            if (target == null || autoSetUpProp == null) return;
            serializedObject.Update();
            EditorGUILayout.BeginVertical(OuterContainerStyle);

            DrawTitle("This module lets you group BoxCutter objects so the attachment detection only considers items within the same group. It improves performance for complex models and rigs and allows for more customizable behavior.");

            DrawBanner(customIcon, "CUSTOM BOX GROUP");
            DrawHorizontalDivider();
            EditorGUILayout.Space();
            DrawFoldoutSection("MAIN SETTINGS", showSettings, StartColor, () =>
            {
                bool autoSetUp = autoSetUpProp.boolValue;
                EditorGUILayout.PropertyField(autoSetUpProp);
                GUI.enabled = false;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("boxGroupIdTo"), true);
                int idTo = fallIdTo.intValue;
                if (idTo != -1) EditorGUILayout.PropertyField(fallIdTo, true);
                GUI.enabled = true;
                if (autoSetUp) GUI.enabled = false;
                var boxListProp = serializedObject.FindProperty("boxList");
                
                int boxListCount = boxListProp.arraySize;
                ConnectionStateEnum connectionState = ConnectionStateEnum.Disconnected;
                bool diffState = false;
                int startIndex = boxListCount - 1;
                for (int i = startIndex; i >= 0; i--)
                {
                    var elem = boxListProp.GetArrayElementAtIndex(i);
                    var box = elem.objectReferenceValue as BoxObj;
                    
                    if (box == null)
                    {
                        boxListProp.DeleteArrayElementAtIndex(i);
                        continue;
                    }

                    if (i == startIndex)
                    {
                        connectionState = box.connectionState;
                    }
                    
                    if (connectionState != box.connectionState)
                    {
                        diffState = true;
                        break;
                    }
                }

                if (diffState)
                {
                    EditorGUILayout.HelpBox("The BoxObjs connection state must all be the same.", MessageType.Warning);
                }
                
                EditorGUILayout.PropertyField(boxListProp);
                if (autoSetUp) GUI.enabled = true;
                
                EditorGUILayout.Space();
                EditorGUILayout.Space();
                
                if (DrawGradientButton("AUTO SET UP WHITELISTS"))
                {
                    foreach (var obj in targets)
                        ((BoxCutterGroup)obj).SetUpWl();
                }
            });

            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Indicates this editor requires constant repainting for smooth animated sections.
        /// Enables real-time updates for animated UI transitions and tree visualization.
        /// </summary>
        /// <returns>True to enable constant editor repainting</returns>
        public override bool RequiresConstantRepaint() => true;
    }
}