using UnityEditor;
using UnityEngine;

namespace BoxCutter
{
    [CustomPropertyDrawer(typeof(BoxCutterObjectPool.BoxCutterOPSetting))]
    public class BoxCutterOPSettingDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line  = EditorGUIUtility.singleLineHeight;
            float space = EditorGUIUtility.standardVerticalSpacing;
            float row   = line + space;

            // Foldout header only
            float h = line;
            if (!property.isExpanded) return h;

            // Space after header
            h += space;

            // Always visible when expanded
            h += row; // pooledOBJCount
            h += row; // obj
            h += row; // boxCutterPrefabType
            h += row; // poolSize
            h += row; // maintainPool

            // Maintenance block (instant show/hide)
            if (property.FindPropertyRelative("maintainPool").boolValue)
            {
                h += row; // refillDuration
                h += row; // objSpawnPerSecond
            }

            // Custom parent toggle
            h += row; // canCustomOBJParent
            if (property.FindPropertyRelative("canCustomOBJParent").boolValue)
            {
                h += row; // objParent
            }

            // Always active
            h += row; // alwaysActive

            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float y      = position.y;
            float line   = EditorGUIUtility.singleLineHeight;
            float space  = EditorGUIUtility.standardVerticalSpacing;
            float width  = position.width;

            // Header / foldout
            var objProp      = property.FindPropertyRelative("obj");
            string headerTxt = objProp.objectReferenceValue != null ? objProp.objectReferenceValue.name : "Pool Setting";
            property.isExpanded = EditorGUI.Foldout(new Rect(position.x, y, width, line), property.isExpanded, headerTxt, true);
            y += line + space;

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;

            // Read-only pooled count
            var countProp = property.FindPropertyRelative("pooledOBJCount");
            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.PropertyField(new Rect(position.x, y, width, line), countProp, new GUIContent("Current Pool Count"));
            EditorGUI.EndDisabledGroup();
            y += line + space;

            // Object reference
            EditorGUI.PropertyField(new Rect(position.x, y, width, line), objProp);
            y += line + space;

            // Prefab type
            EditorGUI.PropertyField(new Rect(position.x, y, width, line), property.FindPropertyRelative("boxCutterPrefabType"));
            y += line + space;

            // Pool size
            EditorGUI.PropertyField(new Rect(position.x, y, width, line), property.FindPropertyRelative("poolSize"));
            y += line + space;

            // Maintain pool
            var maintainProp = property.FindPropertyRelative("maintainPool");
            EditorGUI.PropertyField(new Rect(position.x, y, width, line), maintainProp);
            y += line + space;

            if (maintainProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(new Rect(position.x, y, width, line), property.FindPropertyRelative("refillDuration"));
                y += line + space;

                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.PropertyField(
                    new Rect(position.x, y, width, line),
                    property.FindPropertyRelative("objSpawnPerSecond"),
                    new GUIContent("Objects Per Second")
                );
                EditorGUI.EndDisabledGroup();
                y += line + space;
                EditorGUI.indentLevel--;
            }

            // Custom parent toggle
            var customParentToggle = property.FindPropertyRelative("canCustomOBJParent");
            EditorGUI.PropertyField(new Rect(position.x, y, width, line), customParentToggle);
            y += line + space;

            if (customParentToggle.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUI.PropertyField(new Rect(position.x, y, width, line), property.FindPropertyRelative("objParent"));
                y += line + space;
                EditorGUI.indentLevel--;
            }

            // Always active
            EditorGUI.PropertyField(new Rect(position.x, y, width, line), property.FindPropertyRelative("alwaysActive"));

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }
    }
}
