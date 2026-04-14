using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace BoxCutter
{
    public class BoxCutterBuildingExample : MonoBehaviour
    {
        public BoxObj prefab;
        private BoxObj newBox;

        public void Attach()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Please run in play mode.");
                return;
            }
            
            // Creates new a Boxcutter Object
            CreateNewBoxCutterObj();
            // Run whatever you want here in this case it is building/attaching
            AttachCore();
        }

        private void CreateNewBoxCutterObj()
        {
            Transform obj = transform;

            if (prefab != null)
            {
                newBox = Instantiate(prefab);
            }
            // Fetches a BoxCutter Object from the pool
            else
            {
                newBox = BoxCutterManager.BoxCutterManagerInstance.GetBoxObj();
                newBox.canCusVoxelSize = true;
                newBox.cusVoxelSize = BoxCutterManager.BoxCutterManagerInstance.defSetVoxelSize;
                newBox.meshFilter.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                newBox.transform.localScale = obj.localScale;
            }

            newBox.transform.position = obj.position;
            newBox.transform.rotation = obj.rotation;
        }

        private void EnableNewBox()
        {
            newBox.gameObject.SetActive(true);
            newBox.InitAll(true);
        }

        private void AttachCore()
        {
            // Cache position and half-extents for OverlapBox.
            Transform obj = transform;
            Vector3 pos = obj.position;
            Vector3 halfExtents = obj.localScale * 0.5f + new Vector3(0.01f, 0.01f, 0.01f);
            Collider[] colliders = Physics.OverlapBox(pos, halfExtents);
            int colLength = colliders.Length;

            // Collect unique BoxcutterOBJ components.
            BoxObj hitBox = null;
            bool hasUnAnchored = true;

            for (int i = 0, len = colLength; i < len; i++)
            {
                Collider col = colliders[i];
                BoxCutterCaller.IsBoxObj(col.transform, out hitBox);
            }

            // In this example, if there are no hits then it will create a new group for itself
            if (hitBox == null)
            {
                newBox.CreateParentHolder();
                // If ran before CreateParentHolder then the collider will not be parented under the new BoxObj as isDynamic has not been set to true by CreateParentHolder yet. isDynamic controls if the collider is parented under the BoxObj or not. It is not parented by default for performance.
                EnableNewBox();
            }
            else
            {
                if (hitBox.connectionState != BoxCutterManager.ConnectionStateEnum.Disconnected)
                {
                    hasUnAnchored = false;
                }
            }
            // If there is an un-anchored object detected then it will try either attaching to it
            if (hitBox != null && hasUnAnchored)
            {
                // Before enabling the new box so the mesh collider has convex set to true
                newBox.connectionState = BoxCutterManager.ConnectionStateEnum.Disconnected;
                EnableNewBox();
                // Attach to first hit. In a real-world scenario, you would have to you should already have a list of BoxObjs you would want to attach this object to.
                BoxCutterParent parentHolder = hitBox.parentHolder;
                newBox.fellInTurn = hitBox.fellInTurn;
                newBox.obj.parent = parentHolder.transform;

                int childCount = parentHolder.childrenBoxes.Length;
                Array.Resize(ref parentHolder.childrenBoxes, childCount + 1);
                parentHolder.childrenBoxes[childCount] = newBox;

                newBox.parentHolder = parentHolder;
                // Set the parent holder
                BoxCutterWorldPhysics.ParentHolder(newBox, true);
            }
            // If it hits an anchored object then it will become connected to it
            else
            {
                EnableNewBox();
                newBox.connectionState = BoxCutterManager.ConnectionStateEnum.Connected;
            }
        }

        private void OnDrawGizmos()
        {
            if (prefab != null) return;
            BoxCutterGizmosUtil.DrawBox(BoxCutterGlobalVars.boxCutterPrimaryColor, transform.position, transform.rotation, transform.localScale);
        }
    }
}