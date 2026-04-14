using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VoxReader.Interfaces;
using static BoxCutter.BoxCutterGlobalVars;
using static BoxCutter.BoxcutterEditorUtil;
using Debug = UnityEngine.Debug;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxObj)), CanEditMultipleObjects]
    public class BoxObjEditor : Editor
    {
        private Texture2D customIcon;

        private AnimBool showDebugSettings;
        private AnimBool showMainSettings;
        private AnimBool showFragSettings;
        private AnimBool showMagicaVars;
        private AnimBool showAdvancedSettings;

        private AnimBool showGizmosAnim;
        private AnimBool showGridCellsAnim;
        private AnimBool canDebugAnim;
        private AnimBool magicaVoxelDataAnim;
        private AnimBool canCusVoxelSizeAnim;
        private AnimBool voxelResolutionAnim;
        private AnimBool hasMagicaVoxelDataAnim;
        private AnimBool noMagicaVoxelDataAnim;
        private AnimBool canFragAnim;
        private AnimBool nonAnchoredStateAnim;
        private AnimBool anchoredStateAnim;

        private AnimBool canWlAttachAnim;
        private AnimBool canCusRequireLogicAnim;
        private AnimBool canRequireBoxesAnim;
        private AnimBool useRequireAsAnchorAnim;

        private AnimBool hasModelsAnim;

        private AnimBool showModelIndexHelpAnim;
        private AnimBool showRebuildHelpAnim;
        private AnimBool showRebuildButtonAnim;

        private AnimBool canInheritChildTransAnim;

        private AnimBool canOverrideDiagInIslandAnim;
        private AnimBool canOverrideValidCellFillRatioAnim;

        private AnimBool canCustomDataAnim;

        private bool didAutoSetDynamicThisFrame = false;

        private SerializedProperty gizmosProp;
        private SerializedProperty showGridCellsProp;
        private SerializedProperty canDebugProp;
        private SerializedProperty magicaDataProp;
        private SerializedProperty canCusVoxelSizeProp;
        private SerializedProperty cusVoxelSizeProp;
        private SerializedProperty voxelResolutionProp;
        private SerializedProperty canAdvancedFragProp;
        private SerializedProperty connectionStateProp;
        private SerializedProperty canWlAttachProp;
        private SerializedProperty canCusRequireLogicProp;
        private SerializedProperty canRequireBoxesProp;
        private SerializedProperty cusRequireLogicProp;
        private SerializedProperty useRequireAsAnchorProp;
        private SerializedProperty hasModelsProp;
        private SerializedProperty modelIndexProp;
        private SerializedProperty parentDynamicProp;
        private SerializedProperty dynamicProp;
        private SerializedProperty autoRefreshMagicaDataProp;
        private SerializedProperty callerStrengthMulProp;
        private SerializedProperty canInheritChildTransProp;
        private SerializedProperty canCustomDataProp;
        private SerializedProperty customDataListProp;

        // Cached properties for DrawReadOnlyFields
        private SerializedProperty readyProp;
        private SerializedProperty pooledProp;
        private SerializedProperty hasReturnDelayProp;
        private SerializedProperty returnDelayProp;
        private SerializedProperty voxelSizeProp;
        private SerializedProperty voxelSize3DProp;
        private SerializedProperty gameObjProp;
        private SerializedProperty objProp;
        private SerializedProperty meshRendProp;
        private SerializedProperty matsProp;
        private SerializedProperty meshFilterProp;
        private SerializedProperty existingCollidersProp;
        private SerializedProperty fallUpdateAlreadyProp;
        private SerializedProperty fallVisitedProp;
        private SerializedProperty startPosXProp;
        private SerializedProperty startPosYProp;
        private SerializedProperty startPosZProp;
        private SerializedProperty posProp;
        private SerializedProperty centerPosProp;
        private SerializedProperty sizeXProp;
        private SerializedProperty sizeYProp;
        private SerializedProperty sizeZProp;
        private SerializedProperty qxProp;
        private SerializedProperty qyProp;
        private SerializedProperty qzProp;
        private SerializedProperty qwProp;
        private SerializedProperty iqxProp;
        private SerializedProperty iqyProp;
        private SerializedProperty iqzProp;
        private SerializedProperty iqwProp;
        private SerializedProperty magicaLocalPosOffsetXProp;
        private SerializedProperty magicaLocalPosOffsetYProp;
        private SerializedProperty magicaLocalPosOffsetZProp;
        private SerializedProperty lastLossyScaleProp;
        private SerializedProperty rightXProp;
        private SerializedProperty rightYProp;
        private SerializedProperty rightZProp;
        private SerializedProperty upXProp;
        private SerializedProperty upYProp;
        private SerializedProperty upZProp;
        private SerializedProperty fwdXProp;
        private SerializedProperty fwdYProp;
        private SerializedProperty fwdZProp;
        private SerializedProperty vRightXProp;
        private SerializedProperty vRightYProp;
        private SerializedProperty vRightZProp;
        private SerializedProperty vUpXProp;
        private SerializedProperty vUpYProp;
        private SerializedProperty vUpZProp;
        private SerializedProperty vFwdXProp;
        private SerializedProperty vFwdYProp;
        private SerializedProperty vFwdZProp;
        private SerializedProperty uniqueIdProp;
        private SerializedProperty inheritableIdProp;
        private SerializedProperty cusGroupIdProp;
        private SerializedProperty fellInTurnProp;
        private SerializedProperty parentHolderProp;
        private SerializedProperty allSubListProp;
        private SerializedProperty quadRectsProp;
        private SerializedProperty minXOffsetProp;
        private SerializedProperty minYOffsetProp;
        private SerializedProperty minZOffsetProp;
        private SerializedProperty boxColliderHolderListProp;
        private SerializedProperty cellDataListProp;
        private SerializedProperty groupProp;

        private SerializedProperty canOverrideDiagInIslandProp;
        private SerializedProperty diagInIslandToProp;
        private SerializedProperty canOverrideKdCellTightnessProp;
        private SerializedProperty kdCellTightnessToProp;
        
        //private SerializedProperty destroyingProp;
        private SerializedProperty queuedCallersProp;
        /*private SerializedProperty pendingParentBoxProp;
        private SerializedProperty pendingBoxesProp;
        private SerializedProperty pendingDebrisBoxesProp;
        private SerializedProperty pendingDebrisOnesProp;
        private SerializedProperty createdFromCallerDataProp;*/
        //private SerializedProperty pendingIslandsProp;

        private void OnEnable()
        {
            if (target == null || targets == null) return;
            customIcon = LoadLocalAsset("BoxcutterIcon.png");
            if (customIcon != null)
                ApplyIcon(target, customIcon);

            showMainSettings = new AnimBool(true);
            showMainSettings.valueChanged.AddListener(Repaint);

            showDebugSettings = new AnimBool(true);
            showDebugSettings.valueChanged.AddListener(Repaint);

            showFragSettings = new AnimBool(true);
            showFragSettings.valueChanged.AddListener(Repaint);

            showMagicaVars = new AnimBool(true);
            showMagicaVars.valueChanged.AddListener(Repaint);

            showAdvancedSettings = new AnimBool(true);
            showAdvancedSettings.valueChanged.AddListener(Repaint);

            var so = serializedObject;

            gizmosProp = so.FindProperty("canShowGizmos");
            showGridCellsProp = so.FindProperty("showGridCells");
            canDebugProp = so.FindProperty("canDebug");
            magicaDataProp = so.FindProperty("magicaVoxelData");
            canCusVoxelSizeProp = so.FindProperty("canCusVoxelSize");
            cusVoxelSizeProp = so.FindProperty("cusVoxelSize");
            voxelResolutionProp = so.FindProperty("voxelResolution");
            canAdvancedFragProp = so.FindProperty("canAdvancedFrag");
            connectionStateProp = so.FindProperty("connectionState");
            canWlAttachProp = so.FindProperty("canWlAttach");
            canCusRequireLogicProp = so.FindProperty("canCusRequireLogic");
            canRequireBoxesProp = so.FindProperty("canRequireBoxes");
            cusRequireLogicProp = so.FindProperty("cusRequireLogic");
            useRequireAsAnchorProp = so.FindProperty("useRequireAsAnchor");

            hasModelsProp = so.FindProperty("hasModels");
            modelIndexProp = so.FindProperty("modelIndex");
            parentDynamicProp = so.FindProperty("parentIsDynamic");
            dynamicProp = so.FindProperty("isDynamic");
            autoRefreshMagicaDataProp = so.FindProperty("autoRefreshMagicaData");
            callerStrengthMulProp = so.FindProperty("callerStrengthMul");

            canInheritChildTransProp = so.FindProperty("canInheritChildTrans");
            canCustomDataProp = so.FindProperty("canCustomData");
            customDataListProp = so.FindProperty("customDataList");

            // Cache read-only field properties
            readyProp = so.FindProperty("ready");
            pooledProp = so.FindProperty("pooled");
            hasReturnDelayProp = so.FindProperty("hasReturnDelay");
            returnDelayProp = so.FindProperty("returnDelay");
            voxelSizeProp = so.FindProperty("voxelSize");
            voxelSize3DProp = so.FindProperty("voxelSize3D");
            gameObjProp = so.FindProperty("gameObj");
            objProp = so.FindProperty("obj");
            meshRendProp = so.FindProperty("meshRend");
            matsProp = so.FindProperty("mats");
            meshFilterProp = so.FindProperty("meshFilter");
            existingCollidersProp = so.FindProperty("existingColliders");
            fallUpdateAlreadyProp = so.FindProperty("fallUpdateAlready");
            fallVisitedProp = so.FindProperty("fallVisited");
            startPosXProp = so.FindProperty("startPosX");
            startPosYProp = so.FindProperty("startPosY");
            startPosZProp = so.FindProperty("startPosZ");
            posProp = so.FindProperty("pos");
            centerPosProp = so.FindProperty("centerPos");
            sizeXProp = so.FindProperty("sizeX");
            sizeYProp = so.FindProperty("sizeY");
            sizeZProp = so.FindProperty("sizeZ");
            qxProp = so.FindProperty("qx");
            qyProp = so.FindProperty("qy");
            qzProp = so.FindProperty("qz");
            qwProp = so.FindProperty("qw");
            iqxProp = so.FindProperty("iqx");
            iqyProp = so.FindProperty("iqy");
            iqzProp = so.FindProperty("iqz");
            iqwProp = so.FindProperty("iqw");
            magicaLocalPosOffsetXProp = so.FindProperty("magicaLocalPosOffsetX");
            magicaLocalPosOffsetYProp = so.FindProperty("magicaLocalPosOffsetY");
            magicaLocalPosOffsetZProp = so.FindProperty("magicaLocalPosOffsetZ");
            lastLossyScaleProp = so.FindProperty("lastLossyScale");
            rightXProp = so.FindProperty("rightX");
            rightYProp = so.FindProperty("rightY");
            rightZProp = so.FindProperty("rightZ");
            upXProp = so.FindProperty("upX");
            upYProp = so.FindProperty("upY");
            upZProp = so.FindProperty("upZ");
            fwdXProp = so.FindProperty("fwdX");
            fwdYProp = so.FindProperty("fwdY");
            fwdZProp = so.FindProperty("fwdZ");
            vRightXProp = so.FindProperty("vRightX");
            vRightYProp = so.FindProperty("vRightY");
            vRightZProp = so.FindProperty("vRightZ");
            vUpXProp = so.FindProperty("vUpX");
            vUpYProp = so.FindProperty("vUpY");
            vUpZProp = so.FindProperty("vUpZ");
            vFwdXProp = so.FindProperty("vFwdX");
            vFwdYProp = so.FindProperty("vFwdY");
            vFwdZProp = so.FindProperty("vFwdZ");
            uniqueIdProp = so.FindProperty("uniqueId");
            inheritableIdProp = so.FindProperty("inheritableId");
            cusGroupIdProp = so.FindProperty("cusGroupId");
            fellInTurnProp = so.FindProperty("fellInTurn");
            parentHolderProp = so.FindProperty("parentHolder");
            allSubListProp = so.FindProperty("allSubList");
            quadRectsProp = so.FindProperty("quadRects");
            minXOffsetProp = so.FindProperty("minXOffset");
            minYOffsetProp = so.FindProperty("minYOffset");
            minZOffsetProp = so.FindProperty("minZOffset");
            boxColliderHolderListProp = so.FindProperty("boxColliderHolderList");
            cellDataListProp = so.FindProperty("cellDataList");
            groupProp = so.FindProperty("group");

            canOverrideDiagInIslandProp = so.FindProperty("canOverrideDiagInIsland");
            diagInIslandToProp = so.FindProperty("diagInIslandTo");
            canOverrideKdCellTightnessProp = so.FindProperty("canOverrideKdCellTightness");
            kdCellTightnessToProp = so.FindProperty("kdCellTightnessTo");

            //destroyingProp = so.FindProperty("destroying");
            queuedCallersProp = so.FindProperty("queuedCallers");
            /*pendingParentBoxProp = so.FindProperty("pendingParentBox");
            pendingBoxesProp = so.FindProperty("pendingBoxes");
            pendingDebrisBoxesProp = so.FindProperty("pendingDebrisBoxes");
            pendingDebrisOnesProp = so.FindProperty("pendingDebrisOnes");
            createdFromCallerDataProp = so.FindProperty("createdFromCallerData");*/
            //pendingIslandsProp = so.FindProperty("pendingIslands");

            var detailAnimBools = new[]
            {
                showGizmosAnim = new AnimBool(gizmosProp.boolValue),
                showGridCellsAnim = new AnimBool(showGridCellsProp.boolValue),
                canDebugAnim = new AnimBool(canDebugProp.boolValue),
                magicaVoxelDataAnim = new AnimBool(magicaDataProp.objectReferenceValue != null),
                canCusVoxelSizeAnim = new AnimBool(canCusVoxelSizeProp.boolValue),
                voxelResolutionAnim = new AnimBool(magicaDataProp.objectReferenceValue != null),
                hasMagicaVoxelDataAnim = new AnimBool(magicaDataProp.objectReferenceValue != null),
                noMagicaVoxelDataAnim = new AnimBool(magicaDataProp.objectReferenceValue == null),
                canFragAnim = new AnimBool(canAdvancedFragProp.boolValue),
                nonAnchoredStateAnim = new AnimBool(connectionStateProp.enumValueIndex != (int)BoxCutterManager.ConnectionStateEnum.Anchored),
                anchoredStateAnim = new AnimBool(connectionStateProp.enumValueIndex == (int)BoxCutterManager.ConnectionStateEnum.Anchored),
                canWlAttachAnim = new AnimBool(canWlAttachProp.boolValue),
                canCusRequireLogicAnim = new AnimBool(canCusRequireLogicProp.boolValue),
                canRequireBoxesAnim = new AnimBool(canRequireBoxesProp.boolValue),
                useRequireAsAnchorAnim = new AnimBool(useRequireAsAnchorProp.boolValue),
                hasModelsAnim = new AnimBool(hasModelsProp.boolValue),
                showModelIndexHelpAnim = new AnimBool(modelIndexProp.intValue == -1),
                showRebuildHelpAnim = new AnimBool(false),
                showRebuildButtonAnim = new AnimBool(false),
                canInheritChildTransAnim = new AnimBool(canInheritChildTransProp.boolValue),
                canOverrideDiagInIslandAnim = new AnimBool(canOverrideDiagInIslandProp.boolValue),
                canOverrideValidCellFillRatioAnim = new AnimBool(canOverrideKdCellTightnessProp.boolValue),
                canCustomDataAnim = new AnimBool(canCustomDataProp.boolValue),
            };

            foreach (var ab in detailAnimBools)
                ab.valueChanged.AddListener(Repaint);
        }

        public override void OnInspectorGUI()
        {
            if (target == null || gizmosProp == null) return;

            serializedObject.Update();

            bool setDynamicDueToRb = false;
            if (!Application.isPlaying)
            {
                var allBoxes = targets.Cast<BoxObj>();
                if (allBoxes.Any(bc =>
                    {
                        var rb = bc.GetComponentInParent<Rigidbody>();
                        return rb != null
                               && (!rb.isKinematic || rb.constraints != RigidbodyConstraints.FreezeAll);
                    }))
                {
                    serializedObject.FindProperty("isDynamic").boolValue = true;
                    setDynamicDueToRb = true;
                }
            }

            didAutoSetDynamicThisFrame = setDynamicDueToRb;

            EnsureGradients(StartColor, EndColor);

            EditorGUILayout.BeginVertical(OuterContainerStyle);

            DrawTitle("This script automatically configures BoxCutter to recognize objects as destructible. Compatible with MagicaVoxel models and Unity's default cube for rapid prototyping.");

            DrawBanner(customIcon, "BOXCUTTER OBJ");
            DrawHorizontalDivider();
            DrawSeparator(1);

            DrawFoldoutSection(
                "DEBUG SETTINGS",
                showDebugSettings,
                StartColor,
                () =>
                {
                    EditorGUILayout.PropertyField(gizmosProp);

                    showGizmosAnim.target = gizmosProp.boolValue;
                    if (EditorGUILayout.BeginFadeGroup(showGizmosAnim.faded))
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("canShowPartitions"));
                        
                        showGridCellsAnim.target = showGridCellsProp.boolValue;
                        
                        EditorGUILayout.PropertyField(showGridCellsProp);

                        EditorGUI.indentLevel++;
                        if (showGridCellsProp.boolValue)
                        {
                            int rangeLength = serializedObject.FindProperty("gridCellLength").intValue;
                            if (rangeLength > 0) EditorGUILayout.IntSlider(serializedObject.FindProperty("gridCellIndex"), -1, rangeLength - 1);
                        }
                        else
                        {
                            int rangeLength = serializedObject.FindProperty("kdCellLength").intValue;
                            if (rangeLength > 0) EditorGUILayout.IntSlider(serializedObject.FindProperty("kdCellIndex"), -1, rangeLength - 1);
                        }
                        EditorGUI.indentLevel--;

                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndFadeGroup();

                    EditorGUILayout.PropertyField(canDebugProp);

                    canDebugAnim.target = canDebugProp.boolValue;
                    if (EditorGUILayout.BeginFadeGroup(canDebugAnim.faded))
                    {
                        EditorGUI.indentLevel++;
                        DrawSeparator();
                        EditorGUILayout.LabelField(
                            "DEBUG VARIABLES",
                            CenteredHeaderStyle
                        );
                        DrawHorizontalDivider();
                        EditorGUILayout.Space();
                        DrawReadOnlyFields();
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndFadeGroup();
                }
            );

            DrawSeparator();


            DrawFoldoutSection(
                "MAIN SETTINGS",
                showMainSettings,
                Color.Lerp(StartColor, EndColor, 0.25f),
                () =>
                {
                    if (didAutoSetDynamicThisFrame)
                    {
                        EditorGUILayout.HelpBox(
                            "“Is Dynamic” was automatically enabled because a movable Rigidbody\n" +
                            "was found on this object or one of its parents.",
                            MessageType.Info
                        );
                    }

                    if (Application.isPlaying) GUI.enabled = false;

                    var bcObj = (BoxObj)target;

                    // Update AnimBool targets based on MagicaVoxel data presence
                    hasMagicaVoxelDataAnim.target = magicaDataProp.objectReferenceValue != null;
                    noMagicaVoxelDataAnim.target = magicaDataProp.objectReferenceValue == null;

                    // MagicaVoxel data present - show voxel resolution slider with smooth animation
                    if (EditorGUILayout.BeginFadeGroup(hasMagicaVoxelDataAnim.faded))
                    {
                        EditorGUILayout.IntSlider(voxelResolutionProp, 1, 4, new GUIContent("Voxel Resolution", "Higher values create finer voxel detail by dividing the base voxel size"));
                    }

                    EditorGUILayout.EndFadeGroup();

                    // No MagicaVoxel data - show original custom voxel size controls with smooth animation
                    if (EditorGUILayout.BeginFadeGroup(noMagicaVoxelDataAnim.faded))
                    {
                        EditorGUILayout.PropertyField(canCusVoxelSizeProp);
                        canCusVoxelSizeAnim.target = canCusVoxelSizeProp.boolValue;

                        if (EditorGUILayout.BeginFadeGroup(canCusVoxelSizeAnim.faded))
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.Slider(cusVoxelSizeProp, 0.01f, 2.0f);
                            EditorGUI.indentLevel--;
                        }

                        EditorGUILayout.EndFadeGroup();

                        // Show compatibility warning for non-MagicaVoxel objects only
                        float defVoxelSize = BoxCutterManager.defVoxelSize;
                        Vector3 worldScale = bcObj.transform.lossyScale;
                        float lowestDim = Mathf.Min(worldScale.x, Mathf.Min(worldScale.y, worldScale.z));
                        float voxelSize = lowestDim < defVoxelSize ? lowestDim : defVoxelSize;
                        float sizeFactor = bcObj.canCusVoxelSize ? bcObj.cusVoxelSize : voxelSize;
                        float effectiveVoxelSize = sizeFactor;

                        const float epsilon = 0.0001f;
                        float round = Mathf.Pow(10f, 5);

                        worldScale.x = Mathf.Round(worldScale.x * round) / round;
                        worldScale.y = Mathf.Round(worldScale.y * round) / round;
                        worldScale.z = Mathf.Round(worldScale.z * round) / round;

                        float modX = Mathf.Abs(worldScale.x % effectiveVoxelSize);
                        float modY = Mathf.Abs(worldScale.y % effectiveVoxelSize);
                        float modZ = Mathf.Abs(worldScale.z % effectiveVoxelSize);

                        bool xBad = modX > epsilon && Mathf.Abs(modX - effectiveVoxelSize) > epsilon;
                        bool yBad = modY > epsilon && Mathf.Abs(modY - effectiveVoxelSize) > epsilon;
                        bool zBad = modZ > epsilon && Mathf.Abs(modZ - effectiveVoxelSize) > epsilon;

                        if ((xBad || yBad || zBad) && !Application.isPlaying)
                        {
                            EditorGUILayout.HelpBox(
                                "Custom Voxel size is incompatible with this object's scale.\n" +
                                $"World Scale: {worldScale}\n" +
                                $"New Voxel Size: {effectiveVoxelSize:F3}\n" +
                                "Each axis must divide evenly into the voxel grid.",
                                MessageType.Warning
                            );
                        }
                    }

                    EditorGUILayout.EndFadeGroup();

                    Vector3 localScale = bcObj.transform.localScale;
                    if (localScale.x < 0 || localScale.y < 0 || localScale.z < 0)
                    {
                        EditorGUILayout.HelpBox(
                            $"Negative local scale detected: {localScale}\n" +
                            "Negative scale values can cause unexpected behavior with BoxCutter.",
                            MessageType.Warning
                        );
                    }

                    const float scaleEpsilon = 0.001f;
                    bool isUniform = Mathf.Abs(localScale.x - localScale.y) < scaleEpsilon &&
                                     Mathf.Abs(localScale.y - localScale.z) < scaleEpsilon &&
                                     Mathf.Abs(localScale.x - localScale.z) < scaleEpsilon;

                    if (!isUniform && magicaDataProp.objectReferenceValue != null)
                    {
                        EditorGUILayout.HelpBox(
                            $"Non-uniform local scale detected: {localScale}\n" +
                            "Non-uniform scaling can cause issues with BoxCutter's voxel calculations.",
                            MessageType.Warning
                        );
                    }

                    /*if (!Application.isPlaying && anchoredStateProp.enumValueIndex == (int)BoxCutterManager.AnchoredStateEnum.Connected)
                        EditorGUILayout.HelpBox("Setting the Anchored State to Connected in the Editor is not recommended, use at your own risk.", MessageType.Warning);*/

                    EditorGUILayout.PropertyField(connectionStateProp, new GUIContent("Connection State"));
                    nonAnchoredStateAnim.target = (connectionStateProp.enumValueIndex != (int)BoxCutterManager.ConnectionStateEnum.Anchored);
                    anchoredStateAnim.target = (connectionStateProp.enumValueIndex == (int)BoxCutterManager.ConnectionStateEnum.Anchored);

                    if (anchoredStateAnim.target && dynamicProp.boolValue)
                    {
                        dynamicProp.boolValue = false;
                    }

                    EditorGUI.indentLevel++;

                    canWlAttachAnim.target = canWlAttachProp.boolValue;
                    canRequireBoxesAnim.target = canRequireBoxesProp.boolValue;
                    canCusRequireLogicAnim.target = canCusRequireLogicProp.boolValue;
                    useRequireAsAnchorAnim.target = useRequireAsAnchorProp.boolValue;

                    if (EditorGUILayout.BeginFadeGroup(nonAnchoredStateAnim.faded))
                    {
                        DrawDynamicParentFields();
                        EditorGUILayout.PropertyField(canWlAttachProp);

                        if (EditorGUILayout.BeginFadeGroup(canWlAttachAnim.faded))
                        {
                            EditorGUI.indentLevel++;
                            var wlProp = serializedObject.FindProperty("wlAttachedBox");
                            var wlInheirtId = serializedObject.FindProperty("wlAttachedBoxInheritId");

                            if (!Application.isPlaying)
                            {
                                EditorGUILayout.PropertyField(wlProp, new GUIContent("Whitelisted Attached Boxes"));
                            }
                            else
                            {
                                int arraySize = wlProp.arraySize;

                                if (arraySize > 0)
                                {
                                    EditorGUILayout.HelpBox("Use \"wlAttachedBoxInheritId.\" for runtime assignment.", MessageType.Info);
                                }

                                if (EditorGUILayout.BeginFadeGroup(canDebugAnim.faded))
                                {
                                    EditorGUILayout.PropertyField(wlProp, new GUIContent("Whitelisted Attached Boxes"));
                                }

                                EditorGUILayout.EndFadeGroup();
                                EditorGUILayout.PropertyField(wlInheirtId, new GUIContent("Whitelisted Attached Inheritable ID"));
                            }

                            EditorGUI.indentLevel--;
                        }

                        EditorGUILayout.EndFadeGroup();

                        EditorGUILayout.PropertyField(canRequireBoxesProp);

                        if (EditorGUILayout.BeginFadeGroup(canRequireBoxesAnim.faded))
                        {
                            EditorGUI.indentLevel++;
                            var reqProp = serializedObject.FindProperty("requiredBoxes");
                            var reqInheritId = serializedObject.FindProperty("requiredBoxesInheritId");

                            if (!Application.isPlaying)
                            {
                                EditorGUILayout.PropertyField(reqProp, new GUIContent("Required Boxes"));
                            }
                            else
                            {
                                int arraySize = reqProp.arraySize;

                                if (arraySize > 0)
                                {
                                    EditorGUILayout.HelpBox("Use \"requiredBoxesInheritId.\" for runtime assignment.", MessageType.Info);
                                }

                                if (EditorGUILayout.BeginFadeGroup(canDebugAnim.faded))
                                {
                                    EditorGUILayout.PropertyField(reqProp, new GUIContent("Required Boxes"));
                                }

                                EditorGUILayout.EndFadeGroup();
                                EditorGUILayout.PropertyField(reqInheritId, new GUIContent("Required Boxes Inheritable ID"));
                            }

                            EditorGUILayout.PropertyField(useRequireAsAnchorProp);
                            if (EditorGUILayout.BeginFadeGroup(useRequireAsAnchorAnim.faded))
                            {
                                EditorGUI.indentLevel++;

                                EditorGUILayout.PropertyField(canCusRequireLogicProp);

                                if (EditorGUILayout.BeginFadeGroup(canCusRequireLogicAnim.faded))
                                {
                                    EditorGUI.indentLevel++;

                                    IRequireLogic cusLogic = cusRequireLogicProp.objectReferenceValue as IRequireLogic;

                                    if (cusRequireLogicProp.objectReferenceValue == null)
                                    {
                                        EditorGUILayout.HelpBox("Refer to the documentation how to inject your own code. Due to the Unity's limitations make sure your created script is attached to a game object and assigned to this field.", MessageType.Info);
                                    }
                                    else if (cusLogic == null)
                                    {
                                        EditorGUILayout.HelpBox("The provided cusRequireLogic does not have the IRequireLogic interface implemented", MessageType.Warning);
                                    }

                                    EditorGUILayout.PropertyField(cusRequireLogicProp);
                                    EditorGUI.indentLevel--;
                                }

                                EditorGUILayout.EndFadeGroup();

                                EditorGUI.indentLevel--;
                            }

                            EditorGUILayout.EndFadeGroup();

                            EditorGUI.indentLevel--;
                        }

                        EditorGUILayout.EndFadeGroup();
                    }

                    EditorGUILayout.EndFadeGroup();

                    EditorGUI.indentLevel--;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("colliderGenMode"));

                    EditorGUILayout.Space();
                    if (Application.isPlaying) GUI.enabled = true;
                    EditorGUILayout.PropertyField(canInheritChildTransProp);
                    canInheritChildTransAnim.target = canInheritChildTransProp.boolValue;
                    if (EditorGUILayout.BeginFadeGroup(canInheritChildTransAnim.faded))
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("inheritChildDataList"));
                        EditorGUI.indentLevel--;
                    }

                    if (Application.isPlaying) GUI.enabled = false;

                    EditorGUILayout.EndFadeGroup();
                }
            );

            DrawSeparator();

            DrawFoldoutSection(
                "FRAG SETTINGS",
                showFragSettings,
                Color.Lerp(StartColor, EndColor, 0.5f),
                () =>
                {
                    EditorGUILayout.Slider(
                        serializedObject.FindProperty("massFac"),
                        0.01f,
                        10f
                    );
                    EditorGUILayout.PropertyField(callerStrengthMulProp, new GUIContent("Caller Strength Multiplier"));
                    EditorGUILayout.PropertyField(canAdvancedFragProp);

                    canFragAnim.target = canAdvancedFragProp.boolValue;
                    if (EditorGUILayout.BeginFadeGroup(canFragAnim.faded))
                    {
                        EditorGUI.indentLevel++;
                        var fragSettingsProp = serializedObject.FindProperty("fragSettings");
                        FragSettings fragSettings = fragSettingsProp.objectReferenceValue as FragSettings;
                        EditorGUILayout.PropertyField(fragSettingsProp, true);
                        if (fragSettings != null && fragSettings.fragmentType == FragmentType.Radial)
                            EditorGUILayout.PropertyField(serializedObject.FindProperty("planeBasedOnCaller"), true);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndFadeGroup();
                }
            );

            DrawSeparator();

            DrawFoldoutSection(
                "MAGICA SETTINGS",
                showMagicaVars,
                Color.Lerp(StartColor, EndColor, 0.75f),
                () =>
                {
                    if (Application.isPlaying) GUI.enabled = false;

                    // Check and automatically fix mesh readability if MagicaVoxel data is present
                    if (magicaDataProp.objectReferenceValue != null)
                    {
                        var bcObj = (BoxObj)target;
                        var meshFilter = bcObj.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            if (!meshFilter.sharedMesh.isReadable)
                            {
                                string assetPath = AssetDatabase.GetAssetPath(meshFilter.sharedMesh);
                                if (!string.IsNullOrEmpty(assetPath))
                                {
                                    ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                                    if (importer != null)
                                    {
                                        importer.isReadable = true;
                                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                                        EditorGUILayout.HelpBox(
                                            "Mesh was automatically set to readable so when the game is built MeshColliders can be generated.",
                                            MessageType.Info
                                        );
                                    }
                                    else
                                    {
                                        EditorGUILayout.HelpBox(
                                            "The mesh on this object is not readable and could not be automatically fixed.\n" +
                                            "Manually enable 'Read/Write' in the mesh import settings.",
                                            MessageType.Warning
                                        );
                                    }
                                }
                                else
                                {
                                    EditorGUILayout.HelpBox(
                                        "The mesh on this object is not readable (procedural mesh).\n" +
                                        "This may mean MeshColliders will not be generated in builds.",
                                        MessageType.Warning
                                    );
                                }
                            }
                        }
                    }

                    EditorGUILayout.PropertyField(magicaDataProp);

                    magicaVoxelDataAnim.target = magicaDataProp.objectReferenceValue != null;
                    if (EditorGUILayout.BeginFadeGroup(magicaVoxelDataAnim.faded))
                    {
                        EditorGUI.indentLevel++;

                        hasModelsAnim.target = hasModelsProp.boolValue;

                        if (EditorGUILayout.BeginFadeGroup(hasModelsAnim.faded))
                        {
                            var maxModelLengthProp = serializedObject.FindProperty("maxModelsLength");

                            showModelIndexHelpAnim.target = modelIndexProp.intValue == -1;
                            if (EditorGUILayout.BeginFadeGroup(showModelIndexHelpAnim.faded))
                            {
                                EditorGUILayout.HelpBox("If there are multiple models, use the slider to select the model to use. It is also recommended to turn on CanShowGizmos to verify the model index is correct.", MessageType.Info);
                            }

                            EditorGUILayout.EndFadeGroup();
                            EditorGUILayout.IntSlider(modelIndexProp, -1, maxModelLengthProp.intValue - 1);
                        }

                        EditorGUILayout.EndFadeGroup();

                        GUI.enabled = false;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("totalColors"));
                        GUI.enabled = true;

                        var colliderGenModeProp = serializedObject.FindProperty("colliderGenMode");
                        bool isMeshCollider = colliderGenModeProp.enumValueIndex != (int)BoxCutterManager.ColliderGenModeEnum.Perfect;
                        if (isMeshCollider) GUI.enabled = false;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("forceIgnoreColorCollider"));

                        EditorGUI.indentLevel--;
                        if (isMeshCollider) GUI.enabled = true;
                        EditorGUILayout.PropertyField(autoRefreshMagicaDataProp);

                        // Show rebuild button and change detection info
                        DrawMagicaVoxelRebuildSection();
                    }

                    EditorGUILayout.EndFadeGroup();

                    if (Application.isPlaying) GUI.enabled = true;
                }
            );

            DrawSeparator();
            DrawFoldoutSection(
                "ADVANCED SETTINGS",
                showAdvancedSettings,
                EndColor,
                () =>
                {
                    EditorGUILayout.PropertyField(canOverrideDiagInIslandProp);
                    canOverrideDiagInIslandAnim.target = canOverrideDiagInIslandProp.boolValue;

                    if (EditorGUILayout.BeginFadeGroup(canOverrideDiagInIslandAnim.faded))
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(diagInIslandToProp);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndFadeGroup();

                    EditorGUILayout.PropertyField(canOverrideKdCellTightnessProp);
                    canOverrideValidCellFillRatioAnim.target = canOverrideKdCellTightnessProp.boolValue;

                    if (EditorGUILayout.BeginFadeGroup(canOverrideValidCellFillRatioAnim.faded))
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.Slider(kdCellTightnessToProp, 0, 1);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndFadeGroup();
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(canCustomDataProp);
                    canCustomDataAnim.target = canCustomDataProp.boolValue;
                    if (EditorGUILayout.BeginFadeGroup(canCustomDataAnim.faded))
                    {
                        EditorGUI.indentLevel++;
                        DrawCustomDataList();
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndFadeGroup();
                }
            );

            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDynamicParentFields()
        {
            if (parentDynamicProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Parent has isDynamic set to true",
                    MessageType.Info
                );
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(dynamicProp, new GUIContent("Is Dynamic"));
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUILayout.PropertyField(dynamicProp, new GUIContent("Is Dynamic"));
            }
        }

        private void DrawReadOnlyFields()
        {
            GUI.enabled = false;
            EditorGUILayout.PropertyField(readyProp);
            EditorGUILayout.PropertyField(pooledProp);
            EditorGUILayout.PropertyField(hasReturnDelayProp);
            EditorGUILayout.PropertyField(returnDelayProp);
            EditorGUILayout.PropertyField(voxelSizeProp);
            EditorGUILayout.PropertyField(voxelSize3DProp);
            EditorGUILayout.PropertyField(gameObjProp);
            EditorGUILayout.PropertyField(objProp);
            EditorGUILayout.PropertyField(meshRendProp);
            EditorGUILayout.PropertyField(matsProp);
            EditorGUILayout.PropertyField(meshFilterProp);
            EditorGUILayout.PropertyField(existingCollidersProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(fallUpdateAlreadyProp);
            EditorGUILayout.PropertyField(fallVisitedProp);
            EditorGUILayout.PropertyField(startPosXProp);
            EditorGUILayout.PropertyField(startPosYProp);
            EditorGUILayout.PropertyField(startPosZProp);
            EditorGUILayout.PropertyField(posProp);
            EditorGUILayout.PropertyField(centerPosProp);
            EditorGUILayout.PropertyField(sizeXProp, new GUIContent("SizeX"));
            EditorGUILayout.PropertyField(sizeYProp, new GUIContent("SizeY"));
            EditorGUILayout.PropertyField(sizeZProp, new GUIContent("SizeZ"));
            EditorGUILayout.PropertyField(qxProp);
            EditorGUILayout.PropertyField(qyProp);
            EditorGUILayout.PropertyField(qzProp);
            EditorGUILayout.PropertyField(qwProp);
            EditorGUILayout.PropertyField(iqxProp);
            EditorGUILayout.PropertyField(iqyProp);
            EditorGUILayout.PropertyField(iqzProp);
            EditorGUILayout.PropertyField(iqwProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(magicaLocalPosOffsetXProp);
            EditorGUILayout.PropertyField(magicaLocalPosOffsetYProp);
            EditorGUILayout.PropertyField(magicaLocalPosOffsetZProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(lastLossyScaleProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(rightXProp);
            EditorGUILayout.PropertyField(rightYProp);
            EditorGUILayout.PropertyField(rightZProp);
            EditorGUILayout.PropertyField(upXProp);
            EditorGUILayout.PropertyField(upYProp);
            EditorGUILayout.PropertyField(upZProp);
            EditorGUILayout.PropertyField(fwdXProp);
            EditorGUILayout.PropertyField(fwdYProp);
            EditorGUILayout.PropertyField(fwdZProp);
            EditorGUILayout.PropertyField(vRightXProp);
            EditorGUILayout.PropertyField(vRightYProp);
            EditorGUILayout.PropertyField(vRightZProp);
            EditorGUILayout.PropertyField(vUpXProp);
            EditorGUILayout.PropertyField(vUpYProp);
            EditorGUILayout.PropertyField(vUpZProp);
            EditorGUILayout.PropertyField(vFwdXProp);
            EditorGUILayout.PropertyField(vFwdYProp);
            EditorGUILayout.PropertyField(vFwdZProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(uniqueIdProp);
            EditorGUILayout.PropertyField(inheritableIdProp);
            EditorGUILayout.PropertyField(cusGroupIdProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(fellInTurnProp, true);
            EditorGUILayout.PropertyField(parentHolderProp);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(minXOffsetProp, true);
            EditorGUILayout.PropertyField(minYOffsetProp, true);
            EditorGUILayout.PropertyField(minZOffsetProp, true);
            EditorGUILayout.PropertyField(allSubListProp, true);
            EditorGUILayout.PropertyField(quadRectsProp);
            EditorGUILayout.PropertyField(boxColliderHolderListProp, true);
            EditorGUILayout.PropertyField(cellDataListProp, true);
            EditorGUILayout.PropertyField(groupProp, true);
            EditorGUILayout.Space();
            //EditorGUILayout.PropertyField(destroyingProp);
            EditorGUILayout.PropertyField(queuedCallersProp);
            /*EditorGUILayout.PropertyField(pendingBoxesProp);
            EditorGUILayout.PropertyField(pendingDebrisBoxesProp);
            EditorGUILayout.PropertyField(pendingDebrisOnesProp);
            EditorGUILayout.PropertyField(createdFromCallerDataProp);*/
            //EditorGUILayout.PropertyField(pendingIslandsProp);
            GUI.enabled = true;
        }

        private void DrawMagicaVoxelRebuildSection()
        {
            if (magicaDataProp.objectReferenceValue == null || Application.isPlaying)
                return;

            // Get all selected BoxObj instances
            var allBoxObjs = targets.Cast<BoxObj>().ToList();
            var boxObjsWithMagicaData = allBoxObjs.Where(box => box.magicaVoxelData != null).ToList();

            if (boxObjsWithMagicaData.Count == 0) return;

            // Check for changes across all selected objects using the new BoxObj method
            bool anyHasChanges = false;
            List<string> changedReasons = new List<string>();

            string sizeChangeReason = "Custom voxel size";
            string scaleChangeReason = "Transform scale";
            string customChangeReason = "Custom voxel size toggle";
            string voxChangeReason = "MagicaVoxel data file";
            string modelChangeReason = "Model index";
            string resolutionChangeReason = "Voxel resolution";

            foreach (var boxObj in boxObjsWithMagicaData)
            {
                // Use the new change detection method from BoxObj
                int hasChanges = boxObj.CheckForMagicaDataChanges(); // Force check for UI accuracy

                if (hasChanges > 0)
                {
                    anyHasChanges = true;

                    // Determine specific reasons for changes (for detailed UI feedback)
                    bool sizeChanged = boxObj.lastCusVoxelSize != boxObj.cusVoxelSize;
                    bool scaleChanged = boxObj.lastLossyScale != boxObj.transform.lossyScale;
                    bool customChanged = boxObj.lastCanCusVoxelSize != boxObj.canCusVoxelSize;
                    bool voxChanged = boxObj.lastMagicaVoxelData != boxObj.magicaVoxelData;
                    bool modelChanged = boxObj.lastModelIndex != boxObj.modelIndex;
                    bool resolutionChanged = boxObj.lastVoxelResolution != boxObj.voxelResolution;

                    if (sizeChanged && !changedReasons.Contains(sizeChangeReason)) changedReasons.Add(sizeChangeReason);
                    if (scaleChanged && !changedReasons.Contains(scaleChangeReason)) changedReasons.Add(scaleChangeReason);
                    if (customChanged && !changedReasons.Contains(customChangeReason)) changedReasons.Add(customChangeReason);
                    if (voxChanged && !changedReasons.Contains(voxChangeReason)) changedReasons.Add(voxChangeReason);
                    if (modelChanged && !changedReasons.Contains(modelChangeReason)) changedReasons.Add(modelChangeReason);
                    if (resolutionChanged && !changedReasons.Contains(resolutionChangeReason)) changedReasons.Add(resolutionChangeReason);
                }
            }

            showRebuildHelpAnim.target = anyHasChanges;
            showRebuildButtonAnim.target = anyHasChanges;

            if (EditorGUILayout.BeginFadeGroup(showRebuildHelpAnim.faded))
            {
                EditorGUILayout.Space();

                // Show what has changed
                string changeInfo = boxObjsWithMagicaData.Count > 1
                    ? $"MagicaVoxel data needs rebuilding for {boxObjsWithMagicaData.Count} selected objects due to changes in:\n"
                    : "MagicaVoxel data needs rebuilding due to changes in:\n";

                foreach (var reason in changedReasons)
                {
                    changeInfo += $"• {reason}\n";
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.HelpBox(changeInfo.TrimEnd('\n'), MessageType.Warning);
                EditorGUI.indentLevel++;
                EditorGUI.indentLevel++;
            }

            EditorGUILayout.EndFadeGroup();

            if (EditorGUILayout.BeginFadeGroup(showRebuildButtonAnim.faded))
            {
                EditorGUI.indentLevel--;
                EditorGUI.indentLevel--;

                string buttonText = boxObjsWithMagicaData.Count > 1
                    ? $"REBUILD MAGICAVOXEL DATA ({boxObjsWithMagicaData.Count} OBJECTS)"
                    : "REBUILD MAGICAVOXEL DATA";

                if (DrawGradientButton(buttonText))
                {
                    EditorUtility.DisplayProgressBar("Boxcutter: Reloading Voxels", "Rebuilding MagicaVoxel data...", 0.0f);

                    try
                    {
                        for (int i = 0; i < boxObjsWithMagicaData.Count; i++)
                        {
                            var boxObj = boxObjsWithMagicaData[i];
                            float progress = (float)i / boxObjsWithMagicaData.Count;

                            EditorUtility.DisplayProgressBar(
                                "Boxcutter: Reloading Voxels",
                                $"Rebuilding MagicaVoxel data for {boxObj.name}... ({i + 1}/{boxObjsWithMagicaData.Count})",
                                progress
                            );

                            // Use the same method as BoxCutterCaller
                            if (changedReasons.Contains(resolutionChangeReason) && changedReasons.Count == 1)
                            {
                                MagicaBuilder.UpdateResolution(boxObj);
                            }
                            else
                            {
                                MagicaBuilder.LoadVoxFile(boxObj);
                            }

                            EditorUtility.SetDirty(boxObj);
                        }

                        // Force a repaint to update the UI immediately
                        Repaint();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Boxcutter] Failed to rebuild MagicaVoxel data: {e.Message}");
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }
                }

                EditorGUI.indentLevel++;
                EditorGUI.indentLevel++;
            }
            
            EditorGUILayout.EndFadeGroup();
        }

        private void DrawCustomDataList()
        {
            int listSize = customDataListProp.arraySize;

            int indentSpace = 30;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indentSpace / 2.0f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            indentSpace--;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(-indentSpace);
            EditorGUILayout.LabelField($"Custom Data Entries ({listSize})", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // Check for duplicate variable names
            var variableNames = new Dictionary<string, int>();
            var duplicateNames = new HashSet<string>();

            for (int i = 0; i < listSize; i++)
            {
                var entry = customDataListProp.GetArrayElementAtIndex(i);
                var varNameProp = entry.FindPropertyRelative("varName");
                string varName = varNameProp.stringValue;

                if (variableNames.ContainsKey(varName))
                {
                    duplicateNames.Add(varName);
                }
                else
                {
                    variableNames[varName] = i;
                }
            }

            // Display warning if duplicates found
            if (duplicateNames.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(-indentSpace);
                string duplicateList = string.Join("\n", duplicateNames.Select(name =>
                    $"• {(string.IsNullOrEmpty(name) ? "[Empty]" : name)}"));
                EditorGUILayout.HelpBox(
                    $"Duplicate variable names detected:\n{duplicateList}\n\nEach variable name must be unique.",
                    MessageType.Warning
                );
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(5);
            }

            for (int i = 0; i < listSize; i++)
            {
                var entry = customDataListProp.GetArrayElementAtIndex(i);
                var varNameProp = entry.FindPropertyRelative("varName");
                var varTypeProp = entry.FindPropertyRelative("varType");

                // Check if this entry has a duplicate name
                bool isDuplicate = duplicateNames.Contains(varNameProp.stringValue);

                // Begin the original Vertical group (The Box)
                // Highlight duplicates with a different background color
                if (isDuplicate)
                {
                    var previousColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.5f, 0.5f); // Light red
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = previousColor;
                }
                else
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(-indentSpace);
                EditorGUILayout.PropertyField(varNameProp, new GUIContent("Variable Name"));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(-indentSpace);
                EditorGUILayout.PropertyField(varTypeProp, new GUIContent("Type"));
                EditorGUILayout.EndHorizontal();

                int typeIndex = varTypeProp.enumValueIndex;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(-indentSpace);
                switch (typeIndex)
                {
                    case 0: // Int
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("intValue"), new GUIContent("Value"));
                        break;
                    case 1: // Float
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("floatValue"), new GUIContent("Value"));
                        break;
                    case 2: // String
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("stringValue"), new GUIContent("Value"));
                        break;
                    case 3: // Bool
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("boolValue"), new GUIContent("Value"));
                        break;
                    case 4: // Vector3
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("vector3Value"), new GUIContent("Value"));
                        break;
                    case 5: // Color
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("colorValue"), new GUIContent("Value"));
                        break;
                    case 6: // GameObject
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("gameObjectValue"), new GUIContent("Value"));
                        break;
                    case 7: // Object
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("objectValue"), new GUIContent("Value"));
                        break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                
                if (GUILayout.Button("Delete"))
                {
                    customDataListProp.DeleteArrayElementAtIndex(i);
                    // Ensure we break out of layout groups correctly if we delete
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.EndVertical(); // End the Box
                
                EditorGUILayout.Space(5);
            }

            if (GUILayout.Button("+ Add New Entry"))
            {
                customDataListProp.InsertArrayElementAtIndex(listSize);
                var newEntry = customDataListProp.GetArrayElementAtIndex(listSize);
                newEntry.FindPropertyRelative("varName").stringValue = "NewVariable";
                newEntry.FindPropertyRelative("varType").enumValueIndex = 0;
                newEntry.FindPropertyRelative("intValue").intValue = 0;
                newEntry.FindPropertyRelative("floatValue").floatValue = 0f;
                newEntry.FindPropertyRelative("stringValue").stringValue = "";
                newEntry.FindPropertyRelative("boolValue").boolValue = false;
                newEntry.FindPropertyRelative("vector3Value").vector3Value = Vector3.zero;
                newEntry.FindPropertyRelative("colorValue").colorValue = Color.white;
                newEntry.FindPropertyRelative("gameObjectValue").objectReferenceValue = null;
                newEntry.FindPropertyRelative("objectValue").objectReferenceValue = null;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }
    }
}