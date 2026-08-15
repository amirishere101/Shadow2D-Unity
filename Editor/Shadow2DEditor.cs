using UnityEditor;
using UnityEngine;

namespace SleepyHeadStudios.Editor
{
    /// <summary>
    /// Shared inspector for both shadow components: multi-selection aware,
    /// creation/deletion register with Undo, and the scene is only dirtied
    /// when something actually changed.
    /// </summary>
    public abstract class Shadow2DEditorBase : UnityEditor.Editor
    {
        protected abstract string HelpText { get; }

        public override void OnInspectorGUI()
        {
            bool changed = DrawDefaultInspector();
            if (changed)
                RefreshShadows();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Shadow Controls", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(HelpText, MessageType.Info);

            SerializedProperty useYSortingProp = serializedObject.FindProperty("useYSorting");
            if (useYSortingProp != null && !useYSortingProp.hasMultipleDifferentValues)
            {
                if (useYSortingProp.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "Y-Sorting Mode: the shadow keeps the caster's sorting layer and order; its Y position settles depth.",
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Standard Mode: the shadow renders one sorting order behind the caster.",
                        MessageType.None);
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Create Shadow", GUILayout.Height(30)))
                CreateShadows();

            if (GUILayout.Button("Delete Shadow", GUILayout.Height(30)))
                DeleteShadows();

            EditorGUILayout.EndHorizontal();

            if (targets.Length == 1)
                DrawSingleTargetControls((Shadow2DBase)target);
        }

        private void DrawSingleTargetControls(Shadow2DBase shadow)
        {
            if (shadow.GetShadowObject() == null)
                return;

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("Shadow created. Select it in the Hierarchy to move/rotate/scale it.", MessageType.None);

            if (GUILayout.Button("Select Shadow in Hierarchy"))
            {
                Selection.activeGameObject = shadow.GetShadowObject();
                EditorGUIUtility.PingObject(shadow.GetShadowObject());
            }

            if (GUILayout.Button("Edit Shadow Shape (Brush)"))
                ShadowBrushTool.Open(shadow);
        }

        private void RefreshShadows()
        {
            foreach (Object t in targets)
            {
                var shadow = (Shadow2DBase)t;
                if (shadow.GetShadowObject() != null)
                    shadow.UpdateShadow();
            }
        }

        private void CreateShadows()
        {
            foreach (Object t in targets)
            {
                var shadow = (Shadow2DBase)t;
                if (shadow.GetShadowObject() != null)
                    continue;

                SpriteRenderer source = shadow.GetSourceRenderer();
                Undo.RecordObject(shadow, "Create Shadow");
                if (source != null)
                    Undo.RecordObject(source, "Create Shadow"); // its material may be replaced

                GameObject created = shadow.CreateShadow();
                if (created != null)
                {
                    Undo.RegisterCreatedObjectUndo(created, "Create Shadow");
                    PrefabUtility.RecordPrefabInstancePropertyModifications(shadow);
                    EditorUtility.SetDirty(shadow);
                }
            }
        }

        private void DeleteShadows()
        {
            bool anyShadow = false;
            foreach (Object t in targets)
            {
                if (((Shadow2DBase)t).GetShadowObject() != null)
                {
                    anyShadow = true;
                    break;
                }
            }

            if (!anyShadow)
                return;

            string message = targets.Length > 1
                ? "Delete the shadows of all selected objects?"
                : "Are you sure you want to delete the shadow?";
            if (!EditorUtility.DisplayDialog("Delete Shadow", message, "Delete", "Cancel"))
                return;

            foreach (Object t in targets)
            {
                var shadow = (Shadow2DBase)t;
                GameObject obj = shadow.GetShadowObject();
                if (obj == null)
                    continue;

                if (Application.isPlaying)
                {
                    shadow.DeleteShadow();
                    continue;
                }

                var so = new SerializedObject(shadow);
                so.FindProperty("shadowObject").objectReferenceValue = null;
                so.ApplyModifiedProperties();
                Undo.DestroyObjectImmediate(obj);
            }
        }
    }

    [CustomEditor(typeof(Shadow2DStatic))]
    [CanEditMultipleObjects]
    public class Shadow2DStaticEditor : Shadow2DEditorBase
    {
        protected override string HelpText =>
            "Static Shadow - written once in Start, no per-frame cost.\n\n" +
            "Use for: grass, rocks, fences, decorations, static props.\n" +
            "Don't use for: animated sprites (use Shadow2DDynamic).\n\n" +
            "Transform defaults come from the Shadow2DConfig asset.";
    }

    [CustomEditor(typeof(Shadow2DDynamic))]
    [CanEditMultipleObjects]
    public class Shadow2DDynamicEditor : Shadow2DEditorBase
    {
        protected override string HelpText =>
            "Dynamic Shadow - re-syncs with the caster every LateUpdate.\n\n" +
            "Use for: animated characters, anything whose sprite changes.\n" +
            "Don't use for: static props (use Shadow2DStatic - it's free after Start).\n\n" +
            "Transform defaults come from the Shadow2DConfig asset.";
    }
}
