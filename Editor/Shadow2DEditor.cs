using System;
using UnityEditor;
using UnityEngine;

namespace DryFlyStudio.Editor
{
    /// <summary>
    /// Shared inspector for both shadow components: grouped into collapsible sections, and
    /// surfaces the setup mistakes that are otherwise only visible as "the shadow looks
    /// wrong" at runtime.
    ///
    /// A caster can carry several shadows, each with its own colour, silhouette and
    /// transform, so the bulk of this is a list. Settings that can only have one answer
    /// per object - which renderer to copy, sorting mode, following the caster - stay
    /// above it.
    /// </summary>
    public abstract class Shadow2DEditorBase : UnityEditor.Editor
    {
        private const string FoldoutPrefix = "DryFlyStudio.Shadow2D.Foldout.";

        protected abstract string HelpText { get; }

        /// <summary>The component type this editor's target should be converted to, or null if conversion doesn't apply.</summary>
        protected abstract Type ConvertTargetType { get; }

        /// <summary>Label for the conversion button, e.g. "Convert to Dynamic".</summary>
        protected abstract string ConvertLabel { get; }

        /// <summary>True when this component type shouldn't be used on an animated object.</summary>
        protected abstract bool WarnWhenAnimated { get; }

        private SerializedProperty shadowsProp;
        private SerializedProperty followCasterVisibilityProp;
        private SerializedProperty followCasterAlphaProp;
        private SerializedProperty useYSortingProp;

        private bool autoCreateAttempted;

        protected virtual void OnEnable()
        {
            MigrateTargets();
            CacheProperties();
        }

        /// <summary>
        /// Fold any pre-1.7 single shadow into the list and persist it. The migration is
        /// self-clearing but lives in memory until something dirties the object, so
        /// without this it would re-run from the same stale data every domain reload.
        /// </summary>
        private void MigrateTargets()
        {
            if (Application.isPlaying)
                return;

            foreach (UnityEngine.Object t in targets)
            {
                var component = t as Shadow2DBase;
                if (component == null)
                    continue;

                // Ownership first: a pasted duplicate can reference the original's
                // shadow, and everything downstream would be computed against the
                // wrong pair.
                bool repaired = component.RepairShadowOwnership();

                if (component.MigrateLegacyData() || repaired)
                {
                    EditorUtility.SetDirty(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }
        }

        private void CacheProperties()
        {
            shadowsProp = serializedObject.FindProperty("shadows");
            followCasterVisibilityProp = serializedObject.FindProperty("followCasterVisibility");
            followCasterAlphaProp = serializedObject.FindProperty("followCasterAlpha");
            useYSortingProp = serializedObject.FindProperty("useYSorting");
        }

        public override void OnInspectorGUI()
        {
            // Belt and braces: a private OnEnable on an abstract editor base isn't
            // reliably dispatched, and a null property here throws on every repaint.
            if (shadowsProp == null)
                CacheProperties();

            // Adding the component is the "create" gesture. This is the first GUI pass
            // after that, and OnGUI is a safe place to spawn GameObjects.
            if (!autoCreateAttempted)
            {
                autoCreateAttempted = true;
                if (TryAutoCreateShadows())
                {
                    GUIUtility.ExitGUI();
                    return;
                }
            }

            serializedObject.Update();

            DrawStatus();
            DrawDiagnostics();

            EditorGUI.BeginChangeCheck();
            DrawCasterSection();
            bool changed = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();
            if (changed)
                RefreshShadows();

            DrawShadowList();
            DrawActions();
        }

        // ─────────────────────────── Lifecycle ──────────────────────────────────

        /// <summary>
        /// Give any selected component that has never had a shadow its first one. A
        /// component with no shadows but <see cref="Shadow2DBase.HasEverCreatedShadow"/>
        /// set was deliberately emptied by the user and is left alone.
        /// </summary>
        private bool TryAutoCreateShadows()
        {
            if (Application.isPlaying)
                return false;

            int created = 0;
            Shadow2DBase last = null;

            foreach (UnityEngine.Object t in targets)
            {
                var shadow = (Shadow2DBase)t;
                if (shadow.ShadowCount > 0 || shadow.HasEverCreatedShadow)
                    continue;
                if (shadow.GetSourceRenderer() == null)
                    continue; // no sprite anywhere; the inspector error explains why

                if (AddShadowTo(shadow) != null)
                {
                    created++;
                    last = shadow;
                }
            }

            if (created == 0)
                return false;

            // Selection changes are not safe mid-OnInspectorGUI; a frame later they are.
            if (created == 1 && last != null)
            {
                Shadow2DBase focus = last;
                EditorApplication.delayCall += () => FocusShadow(focus, focus.ShadowCount - 1);
            }

            return true;
        }

        private static Shadow2DInstance AddShadowTo(Shadow2DBase shadow)
        {
            SpriteRenderer source = shadow.GetSourceRenderer();
            Undo.RecordObject(shadow, "Add Shadow");
            if (source != null)
                Undo.RecordObject(source, "Add Shadow"); // its material may be replaced

            Shadow2DInstance created = shadow.AddShadow();
            if (created == null)
                return null;

            if (created.shadowObject != null)
                Undo.RegisterCreatedObjectUndo(created.shadowObject, "Add Shadow");

            PrefabUtility.RecordPrefabInstancePropertyModifications(shadow);
            EditorUtility.SetDirty(shadow);
            return created;
        }

        /// <summary>
        /// Drop the user in front of a shadow: selected, framed, Rect tool active for
        /// nudging it, brush window pointed at it.
        /// </summary>
        private static void FocusShadow(Shadow2DBase shadow, int index)
        {
            if (shadow == null)
                return;

            GameObject shadowObject = shadow.GetShadowObject(index);
            if (shadowObject == null)
                return;

            Tools.current = Tool.Rect;
            Selection.activeGameObject = shadowObject;

            // Not for dynamic shadows: there is no shape to paint, so popping the brush
            // open would only be something to close.
            if (ShadowBrushTool.CanEdit(shadow))
                ShadowBrushTool.Open(shadow, index);

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                // Frame the renderer's bounds directly - FrameSelected can lag a frame
                // behind a selection set in the same call.
                var renderer = shadowObject.GetComponent<Renderer>();
                if (renderer != null)
                    view.Frame(renderer.bounds, false);
                else
                    view.FrameSelected();
            }
        }

        // ─────────────────────────── Status & diagnostics ───────────────────────

        private void DrawStatus()
        {
            bool anyEmptied = false;
            foreach (UnityEngine.Object t in targets)
            {
                var shadow = (Shadow2DBase)t;
                if (shadow.ShadowCount == 0 && shadow.HasEverCreatedShadow)
                {
                    anyEmptied = true;
                    break;
                }
            }

            if (anyEmptied)
            {
                EditorGUILayout.HelpBox(
                    "Every shadow on this object has been deleted. Recreating one starts over from " +
                    "the caster's sprite and discards any silhouette painted for it.",
                    MessageType.Warning);

                if (!Application.isPlaying && GUILayout.Button("Recreate Shadow", GUILayout.Height(26)))
                {
                    RecreateShadows();
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.Space(4);
            }

            EditorGUILayout.HelpBox(HelpText, MessageType.None);
        }

        private void RecreateShadows()
        {
            Shadow2DBase last = null;
            int created = 0;

            foreach (UnityEngine.Object t in targets)
            {
                var shadow = (Shadow2DBase)t;
                if (shadow.ShadowCount > 0)
                    continue;

                if (AddShadowTo(shadow) != null)
                {
                    created++;
                    last = shadow;
                }
            }

            if (created == 1 && last != null)
            {
                Shadow2DBase focus = last;
                EditorApplication.delayCall += () => FocusShadow(focus, focus.ShadowCount - 1);
            }
        }

        private void DrawDiagnostics()
        {
            bool missingRenderer = false;
            bool animatedMismatch = false;
            bool casterMaterialSkipped = false;
            bool frozenDynamic = false;

            foreach (UnityEngine.Object t in targets)
            {
                var shadow = (Shadow2DBase)t;

                SpriteRenderer source = shadow.GetSourceRenderer();
                if (source == null)
                    missingRenderer = true;

                if (WarnWhenAnimated && IsAnimated(shadow))
                    animatedMismatch = true;

                if (!ShadowBrushTool.CanEdit(shadow))
                {
                    for (int i = 0; i < shadow.ShadowCount; i++)
                    {
                        if (shadow.GetShadow(i)?.overrideSprite != null)
                            frozenDynamic = true;
                    }
                }

                if (source != null &&
                    Shadow2DConfig.GetOrCreateDefault().replaceCasterMaterial &&
                    !Shadow2DConfig.IsReplaceableCasterMaterial(source.sharedMaterial))
                {
                    casterMaterialSkipped = true;
                }
            }

            if (missingRenderer)
            {
                EditorGUILayout.HelpBox(
                    "No SpriteRenderer found on this GameObject or any of its children. " +
                    "Shadow2D copies a sprite, so it needs one somewhere in the hierarchy.",
                    MessageType.Error);
            }

            if (animatedMismatch)
            {
                EditorGUILayout.HelpBox(
                    "This object has an Animator, but a static shadow is written once in Start and never again. " +
                    "The shadow will hold the first animation frame while the sprite above it keeps moving.",
                    MessageType.Warning);

                if (ConvertTargetType != null && !Application.isPlaying &&
                    GUILayout.Button(ConvertLabel + " (recommended)"))
                {
                    ConvertSelection();
                    GUIUtility.ExitGUI();
                }
            }

            if (frozenDynamic)
            {
                EditorGUILayout.HelpBox(
                    "A shadow here has an Override Sprite, so it draws that fixed shape instead of " +
                    "following the caster's animation frames. That's a reasonable choice for a simple blob " +
                    "shadow under a character - but clear the field if you expected it to animate.",
                    MessageType.Warning);
            }

            if (casterMaterialSkipped)
            {
                EditorGUILayout.HelpBox(
                    "Caster material replacement is on in Shadow2DConfig, but this caster uses a custom or lit " +
                    "material, so it is left untouched. That is deliberate - swapping it would drop the object " +
                    "out of 2D lighting.",
                    MessageType.None);
            }
        }

        private static bool IsAnimated(Shadow2DBase shadow)
        {
            return shadow.GetComponentInChildren<Animator>(true) != null ||
                   shadow.GetComponentInChildren<Animation>(true) != null;
        }

        // ─────────────────────────── Caster-wide settings ───────────────────────

        private void DrawCasterSection()
        {
            if (!Section("Caster", true))
                return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(followCasterAlphaProp, new GUIContent("Follow Caster Alpha"));
            EditorGUILayout.PropertyField(followCasterVisibilityProp, new GUIContent("Follow Caster Visibility"));
            EditorGUILayout.PropertyField(useYSortingProp, new GUIContent("Use Y Sorting"));

            if (!useYSortingProp.hasMultipleDifferentValues)
            {
                EditorGUILayout.LabelField(" ", useYSortingProp.boolValue
                    ? "Shadows keep the caster's layer and render one order in front, landing on everything else on that layer."
                    : "Shadows render one sorting order behind the caster.", EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ─────────────────────────── The shadow list ────────────────────────────

        private void DrawShadowList()
        {
            EditorGUILayout.Space(2);

            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox(
                    "Shadows are edited one object at a time. The settings above apply to the whole selection.",
                    MessageType.Info);
                return;
            }

            var component = (Shadow2DBase)target;

            EditorGUILayout.LabelField($"Shadows ({shadowsProp.arraySize})", EditorStyles.boldLabel);

            for (int i = 0; i < shadowsProp.arraySize; i++)
            {
                if (DrawShadowEntry(component, i))
                {
                    // The list changed underneath the loop; bail and let the next repaint
                    // draw the new state.
                    GUIUtility.ExitGUI();
                    return;
                }
            }

            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(new GUIContent("Add Shadow",
                        "Add another shadow to this object, with its own colour, shape and position."),
                        GUILayout.Height(26)))
                {
                    AddShadowTo(component);
                    serializedObject.Update();
                    EditorApplication.delayCall += () => FocusShadow(component, component.ShadowCount - 1);
                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>Draw one shadow. Returns true when the list was structurally changed.</summary>
        private bool DrawShadowEntry(Shadow2DBase component, int index)
        {
            SerializedProperty entry = shadowsProp.GetArrayElementAtIndex(index);
            SerializedProperty nameProp = entry.FindPropertyRelative("name");

            string key = $"{FoldoutPrefix}Entry{index}";
            bool open = EditorPrefs.GetBool(key, index == 0);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            bool nextOpen = EditorGUILayout.Foldout(open, nameProp.stringValue, true, EditorStyles.foldoutHeader);
            if (nextOpen != open)
                EditorPrefs.SetBool(key, nextOpen);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(new GUIContent("×", "Delete this shadow"), GUILayout.Width(24)))
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    RemoveShadow(component, index);
                    return true;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (nextOpen)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.PropertyField(nameProp, new GUIContent("Name"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("color"), new GUIContent("Color"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("overrideSprite"), new GUIContent("Override Sprite"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("material"), new GUIContent("Material"));

                DrawEntryTransform(component, entry, index);

                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    RefreshShadows();
                }

                DrawEntryButtons(component, index);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
            return false;
        }

        private void DrawEntryTransform(Shadow2DBase component, SerializedProperty entry, int index)
        {
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("offset"), new GUIContent("Offset"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("rotationZ"), new GUIContent("Rotation Z"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("scale"), new GUIContent("Scale"));

            EditorGUILayout.LabelField(" ",
                "Dragging the shadow in the Scene view writes back here.",
                EditorStyles.miniLabel);

            if (GUILayout.Button(new GUIContent("Reset To Config",
                    "Restore the offset, rotation and scale defaults from Shadow2DConfig.")))
            {
                Undo.RecordObject(component, "Reset Shadow Transform");
                component.GetShadow(index)?.ApplyConfigDefaults(Shadow2DConfig.GetOrCreateDefault());
                EditorUtility.SetDirty(component);
                serializedObject.Update();
                RefreshShadows();
            }
        }

        private void DrawEntryButtons(Shadow2DBase component, int index)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("Select In Hierarchy",
                    "Select this shadow object so you can move it by hand.")))
            {
                GameObject shadowObject = component.GetShadowObject(index);
                Selection.activeGameObject = shadowObject;
                EditorGUIUtility.PingObject(shadowObject);
            }

            // Dynamic shadows redraw from the caster every frame, so there is no stable
            // shape to paint. The button is hidden rather than disabled - a greyed-out
            // control invites you to wonder what would un-grey it.
            if (ShadowBrushTool.CanEdit(component) &&
                GUILayout.Button(new GUIContent("Edit Shape (Brush)",
                    "Paint a custom silhouette for this shadow in the Scene view.")))
            {
                ShadowBrushTool.Open(component, index);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void RemoveShadow(Shadow2DBase component, int index)
        {
            Shadow2DInstance instance = component.GetShadow(index);
            if (instance == null)
                return;

            // Only this shadow is going, so only this shadow is discounted - a sibling on
            // the same caster may well be sharing the silhouette.
            ShadowEditAssets.DeleteSilhouetteIfUnused(instance.overrideSprite, component, index);

            GameObject shadowObject = instance.shadowObject;

            Undo.RecordObject(component, "Delete Shadow");
            var so = new SerializedObject(component);
            so.FindProperty("shadows").DeleteArrayElementAtIndex(index);
            so.ApplyModifiedProperties();

            if (shadowObject != null)
                Undo.DestroyObjectImmediate(shadowObject);

            EditorUtility.SetDirty(component);
            serializedObject.Update();
        }

        // ─────────────────────────── Actions ────────────────────────────────────

        private void DrawActions()
        {
            if (ConvertTargetType == null || Application.isPlaying)
                return;

            EditorGUILayout.Space(6);
            if (GUILayout.Button(new GUIContent(ConvertLabel,
                    "Swap the component type, keeping every setting and every shadow.")))
            {
                ConvertSelection();
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// Re-sync every selected caster after an inspector edit, recording the generated
        /// shadow renderers and transforms so the change is undoable and actually saved.
        /// </summary>
        private void RefreshShadows()
        {
            foreach (UnityEngine.Object t in targets)
            {
                var component = (Shadow2DBase)t;

                for (int i = 0; i < component.ShadowCount; i++)
                {
                    Shadow2DInstance instance = component.GetShadow(i);
                    if (instance?.shadowObject == null)
                        continue;

                    SpriteRenderer renderer = instance.Renderer;
                    if (renderer != null)
                        Undo.RecordObject(renderer, "Update Shadow");

                    // The sort-point nudge writes localPosition on every sync, not just
                    // when the inspector is driving the transform.
                    Undo.RecordObject(instance.shadowObject.transform, "Update Shadow");
                }

                component.UpdateShadow();

                for (int i = 0; i < component.ShadowCount; i++)
                {
                    Shadow2DInstance instance = component.GetShadow(i);
                    if (instance?.shadowObject == null)
                        continue;

                    SpriteRenderer renderer = instance.Renderer;
                    if (renderer != null)
                    {
                        EditorUtility.SetDirty(renderer);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                    EditorUtility.SetDirty(instance.shadowObject.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance.shadowObject.transform);
                }
            }
        }

        /// <summary>
        /// Swap Static for Dynamic (or back) on every selected object, carrying across
        /// every serialized field - including the shadow list, so the existing shadows are
        /// adopted rather than orphaned.
        /// </summary>
        private void ConvertSelection()
        {
            Type destination = ConvertTargetType;
            if (destination == null || Application.isPlaying)
                return;

            Undo.SetCurrentGroupName("Convert Shadow2D Component");
            int group = Undo.GetCurrentGroup();

            foreach (UnityEngine.Object t in targets)
            {
                var from = (Shadow2DBase)t;
                if (from == null || from.GetType() == destination)
                    continue;

                var to = (Shadow2DBase)Undo.AddComponent(from.gameObject, destination);
                CopySerializedFields(from, to);

                // The old component still lists the shadows, and its OnDestroy would ask
                // the lifecycle hook to tear them down. Clear the list first so conversion
                // keeps them instead of deleting them.
                ClearShadowList(from);
                Undo.DestroyObjectImmediate(from);
            }

            Undo.CollapseUndoOperations(group);
        }

        private static void ClearShadowList(Shadow2DBase component)
        {
            var so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty("shadows");
            if (prop != null)
            {
                prop.ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void CopySerializedFields(UnityEngine.Object from, UnityEngine.Object to)
        {
            var source = new SerializedObject(from);
            var destination = new SerializedObject(to);

            // Next(true) then Next(false) walks top-level fields only, and includes the
            // [HideInInspector] bookkeeping that NextVisible would skip.
            SerializedProperty property = source.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = false;
                if (property.propertyPath == "m_Script")
                    continue;
                if (destination.FindProperty(property.propertyPath) != null)
                    destination.CopyFromSerializedProperty(property);
            }

            destination.ApplyModifiedPropertiesWithoutUndo();
        }

        private bool Section(string title, bool defaultOpen)
        {
            string key = FoldoutPrefix + title;
            bool open = EditorPrefs.GetBool(key, defaultOpen);
            bool next = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            if (next != open)
                EditorPrefs.SetBool(key, next);
            return next;
        }
    }

    [CustomEditor(typeof(Shadow2DStatic))]
    [CanEditMultipleObjects]
    public class Shadow2DStaticEditor : Shadow2DEditorBase
    {
        protected override string HelpText =>
            "Static Shadow - written once in Start, no per-frame cost.\n" +
            "Use for: grass, rocks, fences, decorations, static props.\n" +
            "If you change the caster's sprite yourself, call UpdateShadow().";

        protected override Type ConvertTargetType => typeof(Shadow2DDynamic);

        protected override string ConvertLabel => "Convert To Dynamic";

        protected override bool WarnWhenAnimated => true;
    }

    [CustomEditor(typeof(Shadow2DDynamic))]
    [CanEditMultipleObjects]
    public class Shadow2DDynamicEditor : Shadow2DEditorBase
    {
        protected override string HelpText =>
            "Dynamic Shadow - re-syncs with the caster every LateUpdate.\n" +
            "Use for: animated characters, anything whose sprite changes.\n" +
            "For props that never animate, Static is free after Start.\n" +
            "Shape editing is unavailable: silhouettes are rewritten every frame.";

        protected override Type ConvertTargetType => typeof(Shadow2DStatic);

        protected override string ConvertLabel => "Convert To Static";

        protected override bool WarnWhenAnimated => false;
    }
}
