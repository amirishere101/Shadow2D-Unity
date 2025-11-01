using UnityEngine;
using UnityEditor;
using SleepyHeadStudios;

namespace SleepyHeadStudios.Editor
{
    [CustomEditor(typeof(Shadow2DDynamic))]
    public class Shadow2DDynamicEditor : UnityEditor.Editor
    {
        private Shadow2DDynamic shadow;

        private void OnEnable()
        {
            shadow = (Shadow2DDynamic)target;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Shadow Controls", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Dynamic Shadow - Updates every frame\n\n" +
                "✓ Use for: Animated characters, moving objects\n" +
                "✗ Don't use for: Static grass, decorations (use Shadow2DStatic)\n\n" +
                "Default settings:\n" +
                "• Rotation: 12.5°\n" +
                "• Scale: (1, 0.9)\n" +
                "• Position: (0, 0)",
                MessageType.Info
            );

            SerializedProperty useYSortingProp = serializedObject.FindProperty("useYSorting");
            if (useYSortingProp != null && useYSortingProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "✓ Y-Sorting Mode: Shadow uses same sorting layer and order as parent.\n" +
                    "Position the shadow BEHIND parent in world space (lower Y or Z).",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Standard Mode: Shadow renders 1 sorting order behind parent.",
                    MessageType.None
                );
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Create Shadow", GUILayout.Height(30)))
            {
                shadow.CreateShadow();
                EditorUtility.SetDirty(shadow);
            }

            if (GUILayout.Button("Delete Shadow", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("Delete Shadow",
                    "Are you sure you want to delete the shadow?", "Yes", "No"))
                {
                    shadow.DeleteShadow();
                    EditorUtility.SetDirty(shadow);
                }
            }

            EditorGUILayout.EndHorizontal();

            if (shadow.GetShadowObject() != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("✓ Shadow created! Select it in Hierarchy to move/rotate/scale.", MessageType.None);

                if (GUILayout.Button("Select Shadow in Hierarchy"))
                {
                    Selection.activeGameObject = shadow.GetShadowObject();
                    EditorGUIUtility.PingObject(shadow.GetShadowObject());
                }
            }
        }
    }
}
