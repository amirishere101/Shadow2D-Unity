using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DryFlyStudio.Editor
{
    /// <summary>
    /// Shared editor-side asset plumbing for silhouette textures: where they live, and
    /// when one is safe to delete.
    /// </summary>
    public static class ShadowEditAssets
    {
        public const string EditSuffix = "_ShadowEdit";

        private const string FallbackFolder = "Assets/Shadow2D/ShadowEdits";
        private const string FolderName = "ShadowEdits";

        private static string cachedFolder;

        /// <summary>
        /// Where silhouette PNGs are written: a "ShadowEdits" folder inside the package
        /// when the package lives under Assets, otherwise Assets/Shadow2D/ShadowEdits.
        ///
        /// The fallback matters because a package installed from a git URL lands in
        /// Packages/, which is immutable - writing a texture into it fails outright.
        /// </summary>
        public static string Folder
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedFolder))
                    return cachedFolder;

                cachedFolder = FallbackFolder;

                string[] guids = AssetDatabase.FindAssets("Shadow2DBase t:MonoScript");
                foreach (string guid in guids)
                {
                    string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!scriptPath.EndsWith("/Shadow2DBase.cs", System.StringComparison.Ordinal))
                        continue;

                    // <root>/Runtime/Shadow2DBase.cs -> <root>
                    string root = Path.GetDirectoryName(Path.GetDirectoryName(scriptPath));
                    if (string.IsNullOrEmpty(root))
                        continue;

                    root = root.Replace("\\", "/");
                    if (root.StartsWith("Assets/", System.StringComparison.Ordinal) || root == "Assets")
                        cachedFolder = root + "/" + FolderName;

                    break;
                }

                return cachedFolder;
            }
        }

        /// <summary>Create the silhouette folder if it doesn't exist, and return it.</summary>
        public static string EnsureFolder()
        {
            string folder = Folder;
            if (AssetDatabase.IsValidFolder(folder))
                return folder;

            string parent = Path.GetDirectoryName(folder).Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(parent))
                Directory.CreateDirectory(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            return folder;
        }

        /// <summary>True when the path is one of our generated silhouettes, and not a user's own sprite.</summary>
        public static bool IsEditTexturePath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith(Folder + "/", System.StringComparison.Ordinal) &&
                   path.Contains(EditSuffix);
        }

        /// <summary>
        /// Delete the silhouette behind <paramref name="sprite"/>, but only if it is one
        /// of ours and no other shadow still points at it.
        /// </summary>
        /// <param name="excludingComponent">The component being torn down or reset.</param>
        /// <param name="excludingIndex">
        /// Which of that component's shadows to discount, or -1 for all of them. An object
        /// can carry several shadows sharing one silhouette, so excluding the whole
        /// component when only one shadow is going would delete a file still in use.
        /// </param>
        /// <returns>True if the asset was deleted.</returns>
        public static bool DeleteSilhouetteIfUnused(Sprite sprite, Shadow2DBase excludingComponent, int excludingIndex)
        {
            if (sprite == null)
                return false;

            string path = AssetDatabase.GetAssetPath(sprite);
            if (!IsEditTexturePath(path))
                return false;

            if (IsSilhouetteUsedElsewhere(path, excludingComponent, excludingIndex))
            {
                Debug.Log($"Shadow2D: kept {Path.GetFileName(path)} - another shadow is still using that shape.");
                return false;
            }

            return AssetDatabase.DeleteAsset(path);
        }

        /// <summary>
        /// Whether any shadow other than the excluded one uses the silhouette at
        /// <paramref name="path"/>.
        ///
        /// Loaded scenes are checked directly. Prefabs need a dependency scan, which is
        /// the slow part - it is cancellable, and cancelling counts as "in use", because
        /// keeping an orphaned 4KB PNG is a much cheaper mistake than deleting a shape
        /// that forty prefabs were sharing.
        /// </summary>
        public static bool IsSilhouetteUsedElsewhere(string path, Shadow2DBase excludingComponent, int excludingIndex)
        {
            foreach (Shadow2DBase other in FindAllInLoadedScenes())
            {
                if (other == null)
                    continue;

                for (int i = 0; i < other.ShadowCount; i++)
                {
                    if (other == excludingComponent && (excludingIndex < 0 || excludingIndex == i))
                        continue;

                    Sprite silhouette = other.GetShadow(i)?.overrideSprite;
                    if (silhouette != null && AssetDatabase.GetAssetPath(silhouette) == path)
                        return true;
                }
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            try
            {
                for (int i = 0; i < prefabGuids.Length; i++)
                {
                    if (i % 32 == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Shadow2D",
                            "Checking whether any prefab still uses this shadow shape...",
                            i / (float)prefabGuids.Length))
                    {
                        return true;
                    }

                    string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    foreach (string dependency in AssetDatabase.GetDependencies(prefabPath, false))
                    {
                        if (dependency == path)
                            return true;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return false;
        }

        /// <summary>Every Shadow2D component across the loaded scenes, inactive ones included.</summary>
        internal static IEnumerable<Shadow2DBase> FindAllInLoadedScenes()
        {
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType<Shadow2DBase>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<Shadow2DBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<Shadow2DBase>(true);
#endif
        }
    }

    /// <summary>
    /// Ties shadow objects' lifetime to their component: removing Shadow2DStatic or
    /// Shadow2DDynamic in the editor takes every shadow it owned - and their silhouettes,
    /// if nothing else wants them - with it. Also keeps the self-cast masks current, since
    /// those live in property blocks that aren't serialized.
    /// </summary>
    [InitializeOnLoad]
    internal static class Shadow2DLifecycleHooks
    {
        static Shadow2DLifecycleHooks()
        {
            Shadow2DBase.EditorComponentDestroyed -= OnComponentDestroyed;
            Shadow2DBase.EditorComponentDestroyed += OnComponentDestroyed;

            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;

            // The self-cast mask lives in a MaterialPropertyBlock, which isn't serialized,
            // so it has to be rebuilt whenever the editor reloads or a scene opens -
            // otherwise shadows sit on top of their own casters until something happens to
            // resync them.
            EditorApplication.delayCall += RefreshAllSelfMasks;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnSceneOpened;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;

            // And it goes stale as soon as a shadow or its caster is dragged. Only the
            // selection is refreshed, which is the only thing that can be being dragged.
            EditorApplication.update -= RefreshSelectionSelfMasks;
            EditorApplication.update += RefreshSelectionSelfMasks;
        }

        private static void OnComponentDestroyed(GameObject owner, GameObject[] shadowObjects, Sprite[] silhouettes)
        {
            if (shadowObjects == null || shadowObjects.Length == 0)
                return;

            // OnDestroy can't tell "component removed" from "whole GameObject destroyed",
            // and both look identical until the frame ends. A frame later the difference
            // is obvious: if the owner survived, only the component went away.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                if (owner == null)
                    return; // the whole object went away; Unity already took the children with it

                // Several shadows on one caster can share a silhouette, so only attempt
                // each distinct asset once.
                var handled = new HashSet<Sprite>();

                for (int i = 0; i < shadowObjects.Length; i++)
                {
                    Sprite silhouette = silhouettes != null && i < silhouettes.Length ? silhouettes[i] : null;
                    if (silhouette != null && handled.Add(silhouette))
                    {
                        // The component is gone, so it can't be excluded by reference - but
                        // it also can no longer be counted as a user, which is what matters.
                        ShadowEditAssets.DeleteSilhouetteIfUnused(silhouette, null, -1);
                    }

                    if (shadowObjects[i] != null)
                        Undo.DestroyObjectImmediate(shadowObjects[i]);
                }
            };
        }

        /// <summary>
        /// Keep an open brush pointed at whatever shadow is selected, saving the one it
        /// was on first. Selecting anything that isn't a shadow leaves the current target
        /// alone rather than blanking the window, and a closed brush stays closed.
        /// </summary>
        private static void OnSelectionChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            // Drag tracking is per-selection; a new selection starts from a fresh
            // baseline rather than reading the switch itself as an edit.
            trackedTransforms.Clear();
            awaitingResync.Clear();

            if (!ShadowBrushTool.FollowSelection)
                return;

            if (TryResolveShadow(Selection.activeGameObject, out Shadow2DBase shadow, out int index))
                ShadowBrushTool.FollowSelectionTo(shadow, index);
        }

        /// <summary>
        /// Map a selected GameObject to the shadow it belongs to, whether the user clicked
        /// the caster or one of the generated shadow children. Returns false when there is
        /// no editable shadow to point at.
        /// </summary>
        private static bool TryResolveShadow(GameObject selected, out Shadow2DBase shadow, out int index)
        {
            shadow = null;
            index = -1;

            if (selected == null)
                return false;

            if (selected.GetComponent<Shadow2DMarker>() != null)
            {
                // A caster can carry several Shadow2D components and several shadows each,
                // so find the one that actually owns this object rather than the first
                // component up the chain.
                foreach (Shadow2DBase candidate in selected.GetComponentsInParent<Shadow2DBase>(true))
                {
                    int found = candidate.IndexOfShadowObject(selected);
                    if (found < 0)
                        continue;

                    shadow = candidate;
                    index = found;
                    return ShadowBrushTool.CanEdit(candidate);
                }
                return false;
            }

            foreach (Shadow2DBase candidate in selected.GetComponents<Shadow2DBase>())
            {
                if (candidate.ShadowCount == 0)
                    continue;

                shadow = candidate;
                index = 0;
                return ShadowBrushTool.CanEdit(candidate);
            }

            return false;
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene,
            UnityEditor.SceneManagement.OpenSceneMode mode)
        {
            RefreshAllSelfMasks();
        }

        private static void RefreshAllSelfMasks()
        {
            foreach (Shadow2DBase shadow in ShadowEditAssets.FindAllInLoadedScenes())
            {
                if (shadow == null)
                    continue;

                if (shadow.RepairShadowOwnership())
                {
                    EditorUtility.SetDirty(shadow);
                    shadow.UpdateShadow();
                }

                shadow.RefreshSelfMask();
            }
        }

        private struct TransformSnapshot
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;

            public static TransformSnapshot Of(Transform t) => new TransformSnapshot
            {
                position = t.localPosition,
                rotation = t.localRotation,
                scale = t.localScale,
            };

            public bool Differs(Transform t) =>
                position != t.localPosition || rotation != t.localRotation || scale != t.localScale;
        }

        // Keyed on the Transform itself rather than an instance id: GetInstanceID is a
        // hard error in Unity 6 and its replacement doesn't exist on the versions this
        // package still supports. Cleared whenever the selection changes, so it stays
        // bounded by what is currently selected.
        private static readonly Dictionary<Transform, TransformSnapshot> trackedTransforms =
            new Dictionary<Transform, TransformSnapshot>();

        private static readonly HashSet<Shadow2DBase> awaitingResync = new HashSet<Shadow2DBase>();
        private static readonly List<Shadow2DBase> selectedOwners = new List<Shadow2DBase>();

        /// <summary>
        /// Watch the selected casters and shadows, refreshing masks continuously and
        /// running a full resync once a drag finishes.
        ///
        /// Moving or resizing a shadow by hand changes the position its sprite has to keep,
        /// which changes the pivot correction that puts its sort point on the caster - so
        /// it can't be left until something else happens to sync it. Resyncing mid-drag
        /// would fight the handles, so it waits for the drag to end: the transform stopped
        /// changing and nothing holds the GUI's hot control.
        /// </summary>
        private static void RefreshSelectionSelfMasks()
        {
            if (Application.isPlaying)
                return;

            CollectSelectedOwners();

            bool changedThisFrame = false;

            foreach (Shadow2DBase owner in selectedOwners)
            {
                if (owner == null)
                    continue;

                owner.RefreshSelfMask();

                if (NoteTransformChange(owner.transform))
                {
                    awaitingResync.Add(owner);
                    changedThisFrame = true;
                }

                for (int i = 0; i < owner.ShadowCount; i++)
                {
                    GameObject shadowObject = owner.GetShadowObject(i);
                    if (shadowObject == null)
                        continue;

                    if (NoteTransformChange(shadowObject.transform))
                    {
                        awaitingResync.Add(owner);
                        changedThisFrame = true;
                    }
                }
            }

            // Still moving, or a handle is still held - not finished yet.
            if (changedThisFrame || GUIUtility.hotControl != 0 || awaitingResync.Count == 0)
                return;

            foreach (Shadow2DBase owner in awaitingResync)
            {
                if (owner == null)
                    continue;

                ResyncAfterEdit(owner);

                // The resync moves the shadow objects itself, so re-baseline rather than
                // reading that back as another edit next frame.
                NoteTransformChange(owner.transform);
                for (int i = 0; i < owner.ShadowCount; i++)
                {
                    GameObject shadowObject = owner.GetShadowObject(i);
                    if (shadowObject != null)
                        NoteTransformChange(shadowObject.transform);
                }
            }

            awaitingResync.Clear();
        }

        private static void ResyncAfterEdit(Shadow2DBase owner)
        {
            Undo.RecordObject(owner, "Adjust Shadow");

            for (int i = 0; i < owner.ShadowCount; i++)
            {
                Shadow2DInstance instance = owner.GetShadow(i);
                if (instance?.shadowObject == null)
                    continue;

                Undo.RecordObject(instance.shadowObject.transform, "Adjust Shadow");
                if (instance.Renderer != null)
                    Undo.RecordObject(instance.Renderer, "Adjust Shadow");
            }

            // Read the drag back into the fields first. They are the single source of
            // truth for where a shadow sits, so a drag that isn't adopted would be undone
            // by the very next sync.
            for (int i = 0; i < owner.ShadowCount; i++)
                owner.GetShadow(i)?.AdoptFromTransform();

            owner.UpdateShadow();

            EditorUtility.SetDirty(owner);
            for (int i = 0; i < owner.ShadowCount; i++)
            {
                Shadow2DInstance instance = owner.GetShadow(i);
                if (instance?.shadowObject == null)
                    continue;

                EditorUtility.SetDirty(instance.shadowObject.transform);
                if (instance.Renderer != null)
                    EditorUtility.SetDirty(instance.Renderer);
            }
        }

        /// <summary>Record a transform's state, returning true if it moved since last seen.</summary>
        private static bool NoteTransformChange(Transform t)
        {
            if (trackedTransforms.TryGetValue(t, out TransformSnapshot previous) && !previous.Differs(t))
                return false;

            bool known = trackedTransforms.ContainsKey(t);
            trackedTransforms[t] = TransformSnapshot.Of(t);

            // First sighting is a baseline, not an edit.
            return known;
        }

        private static void CollectSelectedOwners()
        {
            selectedOwners.Clear();

            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                GameObject go = selected[i];
                if (go == null)
                    continue;

                // Selecting a shadow child is as common as selecting the caster, and
                // dragging either invalidates the mask.
                if (go.GetComponent<Shadow2DMarker>() != null)
                {
                    foreach (Shadow2DBase owner in go.GetComponentsInParent<Shadow2DBase>(true))
                    {
                        if (!selectedOwners.Contains(owner))
                            selectedOwners.Add(owner);
                    }
                    continue;
                }

                foreach (Shadow2DBase owner in go.GetComponents<Shadow2DBase>())
                {
                    if (!selectedOwners.Contains(owner))
                        selectedOwners.Add(owner);
                }
            }
        }
    }
}
