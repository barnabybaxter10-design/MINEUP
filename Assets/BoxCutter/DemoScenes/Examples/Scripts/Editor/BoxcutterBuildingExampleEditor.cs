#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BoxCutter
{
    [CustomEditor(typeof(BoxCutterBuildingExample)), CanEditMultipleObjects]
    public class BoxcutterBuildingExampleEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var mono = (BoxCutterBuildingExample)target;

            if (GUILayout.Button("Attach"))
            {
                Undo.RecordObject(mono, "Attach BoxCutter");
                mono.Attach();

                EditorUtility.SetDirty(mono);
            }
        }
    }
}
#endif