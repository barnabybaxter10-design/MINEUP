using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using static BoxCutter.BoxCutterGlobalVars;

namespace BoxCutter
{
    [ExecuteInEditMode]
    [DefaultExecutionOrder(1)]
    public class BoxCutterGroup : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────────────
        //                              Inspector Fields
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Enables automatic setup of BoxObj children during editor updates.
        /// When true, automatically populates boxList with child BoxObj components.
        /// </summary>
        [Tooltip("Enables automatic setup of BoxObj children during editor updates. When true, automatically populates boxList with child BoxObj components.")]
        public bool autoSetUp = true;

        /// <summary>
        /// Unique identifier for this BoxCutter group instance.
        /// Used to coordinate behavior across multiple BoxObj instances.
        /// </summary>
        [Tooltip("Unique identifier for this BoxCutter group instance. Used to coordinate behavior across multiple BoxObj instances.")]
        [ReadOnly] public int boxGroupIdTo;

        /// <summary>
        /// Fall turn identifier for physics simulation timing.
        /// Tracks when disconnected fragments began falling during destruction events.
        /// </summary>
        [Tooltip("Fall turn identifier for physics simulation timing. Tracks when disconnected fragments began falling during destruction events.")]
        [ReadOnly] public int fallIdTo = -1;

        /// <summary>
        /// Collection of all BoxObj instances managed by this group.
        /// Automatically populated when autoSetUp is enabled in editor mode.
        /// </summary>
        [Tooltip("Collection of all BoxObj instances managed by this group. Automatically populated when autoSetUp is enabled in editor mode.")]
        public List<BoxObj> boxList = new List<BoxObj>();

        // ─────────────────────────────────────────────────────────────────────────────
        //                              Runtime Init
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Initializes the group at runtime, setting up physics for disconnected fragments.
        /// Assigns unique group ID and creates shared Rigidbody parents for physics simulation.
        /// </summary>
        private void Start()
        {
            if (!IsPlaying) return; // Skip initialization in editor mode
            StartCoroutine(DelayInit());
        }

        private IEnumerator DelayInit()
        {
            yield return null;
            Init();
        }

        private void Init()
        {
            // Assign unique group identifier from global manager
            boxGroupIdTo = ++BoxCutterManager.BoxCutterManagerInstance.boxGroupId;

            // Clean up any destroyed or missing BoxObj references
            RemoveNullBoxes();

            bool hasDisconnected = false;
            int boxCount = boxList.Count;

            List<int> ogId = new List<int>(boxCount);
            for (int i = 0; i < boxCount; i++)
                ogId.Add(boxList[i].inheritableId);

            // Check if any box is disconnected
            for (int i = 0; i < boxCount; i++)
            {
                if (boxList[i].connectionState == BoxCutterManager.ConnectionStateEnum.Disconnected)
                {
                    hasDisconnected = true;
                    break;
                }
            }

            for (int i = 0; i < boxCount; i++)
            {
                BoxObj box = boxList[i];
                box.cusGroupId = boxGroupIdTo;
                box.group = this;
            }
            
            // If nothing is disconnected, we're done
            if (!hasDisconnected)
            {
                return;
            }

            // Collect disconnected boxes and compute center
            var disconnectedBoxList = new List<BoxObj>(8);
            Vector3 centerAcc = Vector3.zero;

            for (int i = 0; i < boxCount; i++)
            {
                var boxObj = boxList[i];
                if (boxObj == null) continue;

                // Assign group membership
                boxObj.isDynamic = true;
                boxObj.cusGroupId = boxGroupIdTo;

                boxObj.canWlAttach = true;
                boxObj.wlAttachedBoxInheritId = new List<int>(ogId);

                // Identify disconnected fragments that need physics simulation
                boxObj.connectionState = BoxCutterManager.ConnectionStateEnum.Disconnected;
                centerAcc += boxObj.transform.position;
                disconnectedBoxList.Add(boxObj);
            }

            if (disconnectedBoxList.Count == 0) return;

            // Center-of-mass position for physics parent
            Vector3 groupCenter = centerAcc / disconnectedBoxList.Count;

            // Create physics parent for disconnected fragments
            CreateParentRb(groupCenter, disconnectedBoxList);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //                              Editor Mode Updates
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Automatically updates the BoxObj list during editor mode when autoSetUp is enabled.
        /// Scans child objects to maintain current list of managed BoxObj components.
        /// </summary>
        private void Update()
        {
            if (!autoSetUp || IsPlaying) return; // Only run in editor mode with auto-setup enabled

            // Refresh BoxObj list from child components
            boxList = GetComponentsInChildren<BoxObj>(true).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //                              Physics Parent Creation
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a shared Rigidbody parent for disconnected fragments after one frame delay.
        /// Calculates total mass from volume and enables realistic physics simulation.
        /// The delay ensures all destruction processing is complete before physics activation.
        /// </summary>
        /// <param name="groupObjPosTo">Center-of-mass position for the physics parent</param>
        /// <param name="disconnectedBoxList">List of disconnected BoxObj fragments to parent</param>
        private void CreateParentRb(Vector3 groupObjPosTo, List<BoxObj> disconnectedBoxList)
        {
            // Create physics parent GameObject with BoxCutterParent component
            GameObject parentHolder = new GameObject("Rb Parent");
            BoxCutterParent boxParent = parentHolder.AddComponent<BoxCutterParent>();

            // Assign fall turn identifier for physics timing coordination
            fallIdTo = ++BoxCutterManager.BoxCutterManagerInstance.boxCutterCurrentFallTurn;

            // Configure parent transform and physics components
            Transform islandParentObj = boxParent.transform;
            islandParentObj.position = groupObjPosTo;
            Rigidbody debrisRb = boxParent.gameObject.AddComponent<Rigidbody>();
            debrisRb.isKinematic = true; // Start kinematic to prevent physics until setup is complete

            // Initialize BoxCutterParent component references
            boxParent.obj = islandParentObj;
            boxParent.rb = debrisRb;
            boxParent.childrenBoxes = disconnectedBoxList.ToArray();

            debrisRb.MovePosition(groupObjPosTo); // Ensure accurate initial position
            float totalVolume = 0f; // Accumulator for mass calculation

            // Process each disconnected fragment
            for (int i = 0; i < disconnectedBoxList.Count; i++)
            {
                BoxObj boxObj = disconnectedBoxList[i];
                if (boxObj == null) continue;

                boxObj.parentHolder = boxParent;         // Link fragment to physics parent
                boxObj.transform.parent = islandParentObj; // Establish transform hierarchy
                boxObj.fellInTurn = fallIdTo;            // Track fall timing for coordination

                // Accumulate volume for realistic mass calculation
                totalVolume += BoxCutterWorldPhysics.CalcVolume(boxObj);
            }

            // Configure Rigidbody for realistic physics simulation
            debrisRb.mass = Mathf.Max(0.0001f, totalVolume); // Avoid zero mass
            debrisRb.interpolation = RigidbodyInterpolation.Interpolate; // Smooth movement
            debrisRb.isKinematic = false; // Enable physics simulation
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //                                Auto Wl Set Up
        // ─────────────────────────────────────────────────────────────────────────────
        
        #if UNITY_EDITOR
        public void SetUpWl()
        {
            //─────────────────────────────────────────────────────────────────────────────────────
            UnityEditor.EditorUtility.DisplayProgressBar("Auto WL", "Preparing...", 0f);
            try
            {
                //─────────────────────────────────────────────────────────────────────────────────────
                // Init Data
                //─────────────────────────────────────────────────────────────────────────────────────
                int boxCount = boxList.Count;
                
                for (int i = 0; i < boxCount; i++)
                {
                    if ((i & 7) == 0)
                    {
                        float p = (float)(i + 1) / boxCount * 0.15f;
                        if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Auto WL",
                                $"Init {i + 1}/{boxCount}", p))
                        {
                            CancelAutoWl();
                            return;
                        }
                    }

                    BoxObj box = boxList[i];
                    Extend.RefreshBoxObjInEditor(box);
                    box.BuildSpatialData(false);

                    box.canWlAttach = true;
                    box.wlAttachedBox.Clear();
                }
                //─────────────────────────────────────────────────────────────────────────────────────
                // Run Auto Wl
                //─────────────────────────────────────────────────────────────────────────────────────
                for (int i = 0; i < boxCount; i++)
                {
                    BoxObj aBox = boxList[i];

                    SubBox[] aAllSubArr = new SubBox[aBox.allSubList.Count];
                    aBox.allSubList.CopyTo(aAllSubArr);
                    using NativeArray<SubBox> aAllSubs = new NativeArray<SubBox>(aAllSubArr, Allocator.TempJob);

                    int startIndex = i + 1;

                    if (UnityEditor.EditorUtility.DisplayCancelableProgressBar(
                            "Auto WL",
                            $"Checking {i + 1}/{boxCount}",
                            (boxCount == 0) ? 0f : (float)(i + 1) / boxCount))
                    {
                        CancelAutoWl();
                        return;
                    }
                    
                    for (int v = startIndex; v < boxCount; v++)
                    {
                        BoxObj bBox = boxList[v];
                        bool connection = BoxCutterCollisionUtil.BoxIntersectWithBox(ref aBox, ref bBox, aAllSubs);

                        if (connection)
                        {
                            aBox.wlAttachedBox.Add(bBox);
                            bBox.wlAttachedBox.Add(aBox);
                        }
                    }
                }

                //─────────────────────────────────────────────────────────────────────────────────────
                // Restore
                //─────────────────────────────────────────────────────────────────────────────────────
                UnityEditor.EditorUtility.DisplayProgressBar("Restoring temp variables", "Running...", 0f);
                RestoreAutoWl();
                //─────────────────────────────────────────────────────────────────────────────────────
            }
            finally
            {
                UnityEditor.EditorUtility.ClearProgressBar();
            }
        }

        private void CancelAutoWl()
        {
            int boxCount = boxList.Count;
            for (int i = 0; i < boxCount; i++)
            {
                BoxObj box = boxList[i];
                box.wlAttachedBox.Clear();
            }

            RestoreAutoWl();
        }

        private void RestoreAutoWl()
        {
            int boxCount = boxList.Count;
            
            for (int i = 0; i < boxCount; i++)
            {
                BoxObj box = boxList[i];
                
                if (box.magicaVoxelData == null)
                {
                    box.sizeX = 0;
                    box.sizeY = 0;
                    box.sizeZ = 0;
                    
                    box.allSubList.Clear();
                }

                box.gridOriginalIndicesArr = new int[] { };
                box.gridCellDataArr = new BoxObj.CellData[] { };
                box.gridCellLength = 0;
                box.gridLeafLength = 0;
            }
        }
        
        #endif
        // ─────────────────────────────────────────────────────────────────────────────
        //                              Utility Methods
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Removes null or destroyed BoxObj references from the managed list.
        /// </summary>
        public void RemoveNullBoxes()
        {
            int boxCount = boxList.Count;
            if (boxCount <= 0) return;

            /*for (int i = boxCount - 1; i >= 0; i--)
            {
                if (boxList[i] == null)
                {
                    boxList.RemoveAt(i); // Remove destroyed or missing references
                }
            }*/
        }

        /// <summary>
        /// Checks if all the Boxes in the group are fully disconnected or not
        /// </summary>
        /// <returns></returns>
        public bool IsActive()
        {
            RemoveNullBoxes();
            
            int boxCount = boxList.Count;
            if (boxCount == 0) return false;

            bool atLeastOneConnected = false;
            for (int i = 0; i < boxCount; i++)
            {
                BoxObj box = boxList[i];

                if (box.connectionState != BoxCutterManager.ConnectionStateEnum.Disconnected)
                {
                    atLeastOneConnected = true;
                    break;
                }
            }

            return atLeastOneConnected;
        }
    }
}