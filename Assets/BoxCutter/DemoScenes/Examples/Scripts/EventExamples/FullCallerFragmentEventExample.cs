using BoxCutter;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;


namespace BoxCutter
{
    /// <summary>
    /// Test script showcasing all aspects of the unified fragment event system.
    /// Demonstrates real-time processing, batch tracking, fragment analysis, and OneDebris handling.
    /// </summary>
    public class FullCallerFragmentEventExample : MonoBehaviour
    {
        [Header("Event Tracking")] public bool logDetailedInfo = true;
        public bool showFragmentStats = true;
        public bool colorCodeFragments = true;

        [Header("Visual Effects")] public Color fragmentColor = Color.blue;
        public Color oneDebrisColor = Color.yellow;

        private BoxCutterCaller caller;

        // Comprehensive tracking variables
        private int totalBatches = 0;
        private int totalFragments = 0;
        private int totalOneDebris = 0;
        private int totalBoxObjFragments = 0;

        // Advanced analytics
        private Dictionary<BoxObj, List<BoxObj>> sourceToFragmentsMap = new Dictionary<BoxObj, List<BoxObj>>();
        private List<BoxCutterOneDebris> allOneDebrisObjects = new List<BoxCutterOneDebris>();
        private List<float> batchProgressHistory = new List<float>();
        private float destructionStartTime;
        private bool destructionInProgress = false;

        void Start()
        {
            caller = GetComponent<BoxCutterCaller>();

            // Subscribe to all relevant events
            caller.OnFragmentBatchCreated += HandleFragmentBatch;
            caller.OnAllFragmentsComplete += HandleAllFragmentsComplete;
            caller.OnShootStart += HandleDestructionStart;
            caller.OnDestructionComplete += HandleDestructionEnd;
        }

        private void HandleDestructionStart(BoxCutterCaller.CallerData callerData)
        {
            // Reset tracking for new destruction event
            ResetTracking();
            destructionStartTime = Time.time;
            destructionInProgress = true;

            Debug.Log($"<color=orange>DESTRUCTION STARTED</color> at position {callerData.pos}");
            Debug.Log($"   Mode: {(callerData.boxMode ? "Box" : "Sphere")}, " +
                      $"Force: {callerData.force}, CanSpawnDebris: {callerData.canSpawnDebris}");
        }

        private void HandleFragmentBatch(BoxCutterCaller.FragmentBatch batch)
        {
            totalBatches++;

            totalFragments += batch.Fragments.Length;
            totalOneDebris += batch.OneDebris.Length;
            batchProgressHistory.Add(batch.CreationProgress);

            // Count total BoxObj fragments created
            int batchBoxObjCount = 0;
            foreach (var fragmentData in batch.Fragments)
            {
                batchBoxObjCount += fragmentData.CreatedBoxCutters.Count;
            }

            totalBoxObjFragments += batchBoxObjCount;

            // Log batch information
            Debug.Log($"<color=cyan>BATCH #{totalBatches}</color>");
            Debug.Log($"   Progress: <b>{batch.CreationProgress:P1}</b> | Complete: <b>{batch.IsComplete}</b>");
            Debug.Log($"   Fragments: <b>{batch.Fragments.Length}</b> | BoxObjs: <b>{batchBoxObjCount}</b> | OneDebris: <b>{batch.OneDebris.Length}</b>");

            if (logDetailedInfo)
            {
                LogDetailedBatchInfo(batch);
            }

            // Process fragments for visual effects and tracking
            ProcessBatchFragments(batch);

            if (showFragmentStats)
            {
                ShowFragmentStatistics();
            }
        }

        private void LogDetailedBatchInfo(BoxCutterCaller.FragmentBatch batch)
        {
            // Detailed fragment analysis
            foreach (var fragmentData in batch.Fragments)
            {
                var sourceBox = fragmentData.SourceBox;
                var createdBoxes = fragmentData.CreatedBoxCutters;

                Debug.Log($"         Source: <b>{sourceBox.name}</b> → Created: <b>{createdBoxes.Count}</b> fragments");

                // Track source to fragments mapping
                if (!sourceToFragmentsMap.ContainsKey(sourceBox))
                    sourceToFragmentsMap[sourceBox] = new List<BoxObj>();
                sourceToFragmentsMap[sourceBox].AddRange(createdBoxes);

                // Log individual fragment details
                for (int i = 0; i < createdBoxes.Count && i < 3; i++) // Limit to first 3 for readability
                {
                    var fragment = createdBoxes[i];
                    Debug.Log($"         └─ Fragment[{i}]: {fragment.name} | Size: ({fragment.sizeX:F2}, {fragment.sizeY:F2}, {fragment.sizeZ:F2}) | Connection: {fragment.connectionState}");
                }

                if (createdBoxes.Count > 3)
                {
                    Debug.Log($"         └─ ... and {createdBoxes.Count - 3} more fragments");
                }
            }

            // OneDebris analysis
            if (batch.OneDebris.Length > 0)
            {
                Debug.Log($"         OneDebris Objects: <b>{batch.OneDebris.Length}</b>");
                allOneDebrisObjects.AddRange(batch.OneDebris);

                foreach (var oneDebris in batch.OneDebris.Take(5)) // Show first 5
                {
                    var scale = oneDebris.obj.localScale;
                    Debug.Log($"         └─ OneDebris: {oneDebris.name} | Scale: ({scale.x:F2}, {scale.y:F2}, {scale.z:F2})");
                }

                if (batch.OneDebris.Length > 5)
                {
                    Debug.Log($"         └─ ... and {batch.OneDebris.Length - 5} more OneDebris objects");
                }
            }
        }

        private void ProcessBatchFragments(BoxCutterCaller.FragmentBatch batch)
        {
            if (!colorCodeFragments) return;

            // Color code BoxObj fragments

            foreach (var fragmentData in batch.Fragments)
            {
                foreach (var fragment in fragmentData.CreatedBoxCutters)
                {
                    // Apply color to fragment materials
                    foreach (var material in fragment.mats)
                    {
                        if (material != null)
                            material.SetColor("_BaseColor", fragmentColor);
                    }
                }
            }

            // Color code OneDebris objects
            foreach (var oneDebris in batch.OneDebris)
            {
                if (oneDebris.meshRend != null)
                {
                    foreach (var material in oneDebris.meshRend.materials)
                    {
                        if (material != null)
                            material.SetColor("_BaseColor", oneDebrisColor);
                    }
                }
            }
        }

        private void ShowFragmentStatistics()
        {
            Debug.Log($"   <color=green>CURRENT STATS</color>");
            Debug.Log($"   Batches: {totalBatches}");
            Debug.Log($"   Fragments: BoxObjs={totalBoxObjFragments}, OneDebris={totalOneDebris}, Total={totalBoxObjFragments + totalOneDebris}");
            Debug.Log($"   Sources: {sourceToFragmentsMap.Keys.Count} original objects fragmented");
            Debug.Log($"   Destruction in Progress: {destructionInProgress}");

            if (batchProgressHistory.Count > 0)
            {
                float avgProgress = batchProgressHistory.Average();
                Debug.Log($"   Progress: Current={batchProgressHistory.Last():P1}, Average={avgProgress:P1}");
            }
        }

        private void HandleAllFragmentsComplete(BoxCutterCaller.FragmentBatch finalBatch)
        {
            float destructionDuration = Time.time - destructionStartTime;
            destructionInProgress = false;

            Debug.Log($"   <color=lime>DESTRUCTION COMPLETE!</color> Duration: <b>{destructionDuration:F2}s</b>");
            Debug.Log($"───────────────────────────────────────────────────────────────");

            // Final comprehensive statistics
            Debug.Log($"   <color=lime>FINAL STATISTICS</color>");
            Debug.Log($"      Total Batches: <b>{totalBatches}</b>");
            Debug.Log($"      Total BoxObj Fragments: <b>{totalBoxObjFragments}</b>");
            Debug.Log($"      Total OneDebris Objects: <b>{totalOneDebris}</b>");
            Debug.Log($"      Source Objects Fragmented: <b>{sourceToFragmentsMap.Keys.Count}</b>");
            Debug.Log($"      Average Fragments per Second: <b>{(totalBoxObjFragments + totalOneDebris) / destructionDuration:F1}</b>");

            // Source breakdown
            Debug.Log($"   <color=lime>FRAGMENTATION BREAKDOWN</color>");
            foreach (var kvp in sourceToFragmentsMap)
            {
                var sourceBox = kvp.Key;
                var fragments = kvp.Value;
                Debug.Log($"   {sourceBox.name} → {fragments.Count} BoxObj fragments");
            }

            // Final batch info
            Debug.Log($"   <color=lime>FINAL BATCH CONTENTS</color>");
            Debug.Log($"   All Fragments: <b>{finalBatch.Fragments.Length}</b>");
            Debug.Log($"   All OneDebris: <b>{finalBatch.OneDebris.Length}</b>");
            Debug.Log($"   Progress: <b>{finalBatch.CreationProgress:P0}</b>");
            Debug.Log($"   Complete: <b>{finalBatch.IsComplete}</b>");

            Debug.Log($"───────────────────────────────────────────────────────────────");
        }

        private void HandleDestructionEnd(BoxCutterCaller.CallerData callerData)
        {
            Debug.Log($"🔚 <color=orange>DESTRUCTION PIPELINE FINISHED</color>");
        }

        private void ResetTracking()
        {
            totalBatches = 0;
            totalFragments = 0;
            totalOneDebris = 0;
            totalBoxObjFragments = 0;

            sourceToFragmentsMap.Clear();
            allOneDebrisObjects.Clear();
            batchProgressHistory.Clear();
        }

        void OnDestroy()
        {
            caller.OnFragmentBatchCreated -= HandleFragmentBatch;
            caller.OnAllFragmentsComplete -= HandleAllFragmentsComplete;
            caller.OnShootStart -= HandleDestructionStart;
            caller.OnDestructionComplete -= HandleDestructionEnd;
        }
    }
}