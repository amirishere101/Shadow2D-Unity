using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DryFlyStudio.Editor
{
    /// <summary>
    /// In-editor brush for adding to and erasing from a shadow's silhouette, painted
    /// directly in the Scene view. Opened automatically when a shadow is created, or from
    /// the component inspector.
    ///
    /// The silhouette starts as an exact copy of the shadow you already have, on a canvas
    /// padded out beyond the original sprite so you can paint outside its edges. The copy
    /// goes through a RenderTexture blit rather than GetPixels, so the source texture
    /// never has to be marked readable, and it is only written to disk once you actually
    /// paint - opening the brush on a shadow you end up not editing leaves no assets behind.
    /// </summary>
    public class ShadowBrushTool : EditorWindow
    {
        public enum BrushMode
        {
            Paint,
            Erase
        }

        /// <summary>Padding added on every side, as a fraction of the sprite's longest edge.</summary>
        private const float PaddingFraction = 0.5f;
        private const int MinPadding = 16;
        private const int MaxPadding = 128;
        private const int MaxUndoSteps = 20;

        private const string FollowSelectionKey = "DryFlyStudio.Shadow2D.FollowSelection";

        [SerializeField] private Shadow2DBase targetComponent;
        [SerializeField] private int targetIndex;
        [SerializeField] private SpriteRenderer targetShadowRenderer;
        [SerializeField] private BrushMode brushMode = BrushMode.Paint;
        [SerializeField] private int brushSize = 5;
        [SerializeField] private bool isPainting;
        [SerializeField] private string texturePath;

        private Texture2D editableTexture;
        private bool isDirty;

        // Strokes mutate this buffer and upload once, rather than calling SetPixel per
        // pixel - a 200px brush covers ~125k pixels, which is unusable one call at a time.
        private Color32[] pixels;

        private readonly List<Color32[]> undoStack = new List<Color32[]>();

        private static Texture2D blankCursor;

        // ─────────────────────────── Opening ────────────────────────────────────

        /// <summary>
        /// Whether a shadow's shape can be hand-edited at all.
        ///
        /// Dynamic shadows re-copy the caster's sprite every LateUpdate, which is the
        /// whole point of them. Painting a silhouette sets an override, and an override
        /// wins over that copy - so a painted dynamic shadow is frozen on whichever
        /// animation frame happened to be showing when you started. Refusing up front is
        /// kinder than shipping a character whose shadow stopped walking.
        /// </summary>
        public static bool CanEdit(Shadow2DBase shadow)
        {
            return shadow != null && !(shadow is Shadow2DDynamic);
        }

        /// <summary>
        /// Point the brush at one of a caster's shadows, opening the window if it isn't
        /// already up and retargeting it if it is. Does not create any asset - that waits
        /// for the first stroke - and does not start painting, so the Scene view stays usable.
        /// </summary>
        public static void Open(Shadow2DBase shadow, int index = 0)
        {
            if (shadow == null)
                return;

            if (!CanEdit(shadow))
            {
                EditorUtility.DisplayDialog("Shadow Brush",
                    "Dynamic shadows can't be shape-edited.\n\n" +
                    "A dynamic shadow copies the caster's sprite every frame, so a painted " +
                    "silhouette would override that and freeze the shadow on one animation " +
                    "frame.\n\n" +
                    "If this object doesn't actually animate, convert it to Shadow2DStatic " +
                    "and the brush will work.", "OK");
                return;
            }

            GameObject shadowObject = shadow.GetShadowObject(index);
            if (shadowObject == null)
            {
                EditorUtility.DisplayDialog("Shadow Brush",
                    "That shadow doesn't exist to paint on.", "OK");
                return;
            }

            var window = GetWindow<ShadowBrushTool>("Shadow Brush");
            window.SetTarget(shadow, index, shadowObject.GetComponent<SpriteRenderer>());
            window.Show();
        }

        /// <summary>
        /// Whether selecting a shadow retargets an open brush window at it. Only affects
        /// a window that is already open - selection never opens one.
        /// </summary>
        public static bool FollowSelection
        {
            get => EditorPrefs.GetBool(FollowSelectionKey, true);
            set => EditorPrefs.SetBool(FollowSelectionKey, value);
        }

        /// <summary>
        /// Point an open brush window at a shadow because the selection changed. Does
        /// nothing when the window is closed: opening a window off the back of a click is
        /// intrusive, so the brush is only ever opened deliberately.
        /// </summary>
        internal static void FollowSelectionTo(Shadow2DBase shadow, int index)
        {
            if (shadow == null)
                return;

            GameObject shadowObject = shadow.GetShadowObject(index);
            if (shadowObject == null)
                return;

            // Deliberately not GetWindow: that would create the window when none exists,
            // and pull focus onto it every time you click something in the Hierarchy.
            ShadowBrushTool[] open = Resources.FindObjectsOfTypeAll<ShadowBrushTool>();
            if (open.Length == 0)
                return;

            open[0].SetTarget(shadow, index, shadowObject.GetComponent<SpriteRenderer>());
        }

        private void SetTarget(Shadow2DBase shadow, int index, SpriteRenderer shadowRenderer)
        {
            // Selecting the caster and selecting its shadow child both resolve here, and
            // re-entering on the same target would wipe the undo history for no reason.
            if (targetComponent == shadow && targetIndex == index && targetShadowRenderer == shadowRenderer)
                return;

            // Whatever was being edited is being switched away from - commit it rather
            // than silently dropping the strokes. The painting session itself carries
            // over, so switching shadows mid-edit just keeps going on the new one.
            if (isDirty)
                SaveTexture();

            targetComponent = shadow;
            targetIndex = index;
            targetShadowRenderer = shadowRenderer;
            undoStack.Clear();
            isDirty = false;

            // Adopt an existing silhouette if this shadow already has one; otherwise leave
            // the texture null and clone lazily on the first stroke.
            Shadow2DInstance instance = shadow.GetShadow(index);
            string existing = instance?.overrideSprite != null
                ? AssetDatabase.GetAssetPath(instance.overrideSprite)
                : null;

            if (ShadowEditAssets.IsEditTexturePath(existing))
            {
                texturePath = existing;
                EnsureTextureReadable(texturePath);
                editableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                LoadPixels();
            }
            else
            {
                texturePath = null;
                editableTexture = null;
                pixels = null;
            }

            Repaint();

            // Adopting an existing silhouette can force a reimport to make it readable,
            // which replaces the texture object out from under the derived sprite.
            ResyncTarget();
        }

        private void OnEnable()
        {
            // Survive a domain reload: the serialized fields come back but the texture
            // handle, pixel buffer and scene-view subscription do not.
            if (!string.IsNullOrEmpty(texturePath))
            {
                editableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                LoadPixels();
            }

            if (isPainting)
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                SceneView.duringSceneGui += OnSceneGUI;
                Tools.hidden = true;
            }
        }

        private void LoadPixels()
        {
            pixels = editableTexture != null ? editableTexture.GetPixels32() : null;
        }

        // ─────────────────────────── Painting session ───────────────────────────

        private void StartPainting()
        {
            if (targetShadowRenderer == null)
                return;

            isPainting = true;
            Cursor.SetCursor(BlankCursor, Vector2.zero, CursorMode.Auto);

            // Hide the transform gizmo. Its handles sit right on top of the shadow and
            // claim the mouse before the Scene view's default control ever sees it.
            Tools.hidden = true;

            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            SceneView.RepaintAll();
        }

        private void StopPainting(bool save)
        {
            if (isPainting && save && isDirty)
                SaveTexture();

            isPainting = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Tools.hidden = false;
            SceneView.RepaintAll();
        }

        private void OnDisable()
        {
            // Covers both closing the window and a domain reload.
            if (isPainting && isDirty)
                SaveTexture();

            SceneView.duringSceneGui -= OnSceneGUI;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Tools.hidden = false;
        }

        // ─────────────────────────── Scene view ─────────────────────────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isPainting || targetShadowRenderer == null)
                return;

            Event e = Event.current;
            Transform shadowTransform = targetShadowRenderer.transform;

            // Claim the Scene view before anything else looks at the event. Registering
            // this late meant the selection picker and the transform gizmo got first
            // refusal on every click, so strokes only landed in whatever gaps their
            // handles left.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            // The OS cursor would sit on top of the brush outline and fight it for
            // attention. The blank cursor is installed once when painting starts; this
            // rect is what scopes it to the Scene view, so the rest of the editor keeps
            // its normal cursor.
            EditorGUIUtility.AddCursorRect(
                new Rect(0f, 0f, sceneView.position.width, sceneView.position.height),
                MouseCursor.CustomCursor);

            if (HandleShortcuts(e))
                return;

            // Intersect the mouse ray with the shadow's own plane. Using ray.origin here
            // only worked in an orthographic Scene view - under a perspective camera it is
            // the camera position, so every stroke landed somewhere unrelated.
            var shadowPlane = new Plane(shadowTransform.forward, shadowTransform.position);
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (shadowPlane.Raycast(ray, out float distance))
            {
                Vector3 worldPos = ray.GetPoint(distance);
                DrawCanvasOutline(shadowTransform);
                DrawBrushCursor(shadowTransform, worldPos, e);
                HandleStroke(e, controlId, worldPos);
            }

            sceneView.Repaint();
            Repaint();
        }

        private void HandleStroke(Event e, int controlId, Vector3 worldPos)
        {
            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (e.button != 0)
                        break;
                    if (!EnsureEditableTexture())
                        break;

                    // Hold the control for the whole drag, so no other handle can take
                    // over partway through a stroke.
                    GUIUtility.hotControl = controlId;
                    PushUndo();
                    PaintAtWorld(worldPos, ResolveMode(e));
                    isDirty = true;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId)
                        break;
                    PaintAtWorld(worldPos, ResolveMode(e));
                    isDirty = true;
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId)
                        break;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        /// <summary>
        /// Outline the full paintable canvas, padding included, so the area you can paint
        /// in is visible rather than guessed at.
        /// </summary>
        private void DrawCanvasOutline(Transform shadowTransform)
        {
            Sprite sprite = targetShadowRenderer.sprite;
            if (sprite == null)
                return;

            float ppu = sprite.pixelsPerUnit;
            Rect rect = sprite.rect;
            var size = new Vector3(rect.width / ppu, rect.height / ppu, 0f);
            var min = new Vector3(-sprite.pivot.x / ppu, -sprite.pivot.y / ppu, 0f);

            using (new Handles.DrawingScope(new Color(1f, 1f, 1f, 0.25f), shadowTransform.localToWorldMatrix))
            {
                Handles.DrawWireCube(min + size * 0.5f, size);
            }
        }

        private BrushMode ResolveMode(Event e)
        {
            // Shift temporarily inverts the mode.
            if (!e.shift)
                return brushMode;
            return brushMode == BrushMode.Paint ? BrushMode.Erase : BrushMode.Paint;
        }

        private void DrawBrushCursor(Transform shadowTransform, Vector3 worldPos, Event e)
        {
            float ppu = targetShadowRenderer.sprite != null ? targetShadowRenderer.sprite.pixelsPerUnit : 16f;
            float localRadius = brushSize / ppu;

            Color color = ResolveMode(e) == BrushMode.Paint
                ? new Color(1f, 1f, 1f, 0.8f)
                : new Color(1f, 0.3f, 0.3f, 0.8f);

            // Drawn in the shadow's local space so the outline squashes and rotates with
            // it and matches the pixels the stroke will actually touch.
            using (new Handles.DrawingScope(color, shadowTransform.localToWorldMatrix))
            {
                Vector3 local = shadowTransform.InverseTransformPoint(worldPos);
                Handles.DrawWireDisc(local, Vector3.forward, localRadius);
                Handles.DrawWireDisc(local, Vector3.forward, localRadius * 0.04f);
            }
        }

        private bool HandleShortcuts(Event e)
        {
            return HandleBrushSizeScroll(e) || HandleSaveShortcut(e) || HandleUndoShortcut(e);
        }

        private bool HandleSaveShortcut(Event e)
        {
            if (e.type != EventType.KeyDown || e.keyCode != KeyCode.S || !(e.control || e.command))
                return false;

            // Nothing painted yet, so there is no silhouette to save - let it through and
            // save the scene the way it normally would.
            if (editableTexture == null || string.IsNullOrEmpty(texturePath))
                return false;

            // Otherwise consume it: while the brush owns the Scene view, Ctrl+S means
            // "save the silhouette".
            e.Use();
            SaveTexture();
            return true;
        }

        private bool HandleBrushSizeScroll(Event e)
        {
            if (e.type != EventType.ScrollWheel || !(e.control || e.command))
                return false;

            brushSize = Mathf.Clamp(brushSize - (int)Mathf.Sign(e.delta.y), 1, 200);
            e.Use();
            SceneView.RepaintAll();
            Repaint();
            return true;
        }

        private bool HandleUndoShortcut(Event e)
        {
            if (e.type != EventType.KeyDown || e.keyCode != KeyCode.Z || !(e.control || e.command))
                return false;

            // Consume it so Unity's global undo doesn't also fire and roll back something
            // unrelated while the brush is active.
            e.Use();
            PopUndo();
            return true;
        }

        // ─────────────────────────── Texture creation ───────────────────────────

        /// <summary>
        /// Make sure a writable silhouette exists, cloning the shadow's current sprite on
        /// first use. Returns false if there's nothing to clone.
        /// </summary>
        private bool EnsureEditableTexture()
        {
            if (editableTexture != null)
                return true;

            // The renderer draws a pivot-corrected copy whose pivot has been nudged to the
            // sort point. Cloning that would bake the nudge into the silhouette, and the
            // next sync would nudge it again.
            Sprite sprite = GetBaseSprite();
            if (sprite == null)
            {
                Debug.LogWarning("Shadow2D: the shadow has no sprite to trace, so there's nothing to paint on.", targetComponent);
                return false;
            }

            CreateEditableTexture(sprite);
            return editableTexture != null;
        }

        /// <param name="forcedPath">
        /// When set, overwrite this exact asset instead of allocating a new unique path,
        /// so Reset From Source doesn't orphan the previous texture.
        /// </param>
        private void CreateEditableTexture(Sprite sprite, string forcedPath = null)
        {
            Texture2D sourceTex = sprite.texture;
            Rect srcRect = sprite.textureRect;
            int w = Mathf.Max(1, Mathf.RoundToInt(srcRect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(srcRect.height));

            // Room to paint beyond the original silhouette. Without it the canvas is
            // exactly the sprite's rect and strokes stop dead at its edge.
            int pad = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(w, h) * PaddingFraction), MinPadding, MaxPadding);
            int paddedWidth = w + pad * 2;
            int paddedHeight = h + pad * 2;

            var renderTexture = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previousActive = RenderTexture.active;
            Texture2D cropped = null;
            Texture2D padded = null;

            try
            {
                // Blit rather than Graphics.DrawTexture: DrawTexture only renders during a
                // Repaint event, and this runs from a mouse event, so it silently produced
                // a fully transparent texture - which is why starting to edit used to make
                // the whole shadow disappear.
                var scale = new Vector2(srcRect.width / sourceTex.width, srcRect.height / sourceTex.height);
                var offset = new Vector2(srcRect.x / sourceTex.width, srcRect.y / sourceTex.height);
                Graphics.Blit(sourceTex, renderTexture, scale, offset);

                RenderTexture.active = renderTexture;
                cropped = new Texture2D(w, h, TextureFormat.RGBA32, false);
                cropped.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                cropped.Apply();

                // White silhouette on a transparent margin, original alpha preserved. Only
                // alpha matters - the shadow tint supplies the colour.
                Color32[] source = cropped.GetPixels32();
                var result = new Color32[paddedWidth * paddedHeight];
                for (int y = 0; y < h; y++)
                {
                    int sourceRow = y * w;
                    int targetRow = (y + pad) * paddedWidth + pad;
                    for (int x = 0; x < w; x++)
                        result[targetRow + x] = new Color32(255, 255, 255, source[sourceRow + x].a);
                }

                padded = new Texture2D(paddedWidth, paddedHeight, TextureFormat.RGBA32, false);
                padded.filterMode = sourceTex.filterMode;
                padded.SetPixels32(result);
                padded.Apply();

                texturePath = forcedPath ?? BuildShadowTexturePath(sprite);
                File.WriteAllBytes(texturePath, padded.EncodeToPNG());
                AssetDatabase.Refresh();

                // Shift the pivot by the padding so the silhouette lands exactly where the
                // original sprite did.
                var pivot = new Vector2(
                    (sprite.pivot.x + pad) / paddedWidth,
                    (sprite.pivot.y + pad) / paddedHeight);
                ConfigureImporter(texturePath, sprite.pixelsPerUnit, pivot, sourceTex.filterMode);

                editableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                LoadPixels();
                Sprite newSprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);

                targetShadowRenderer.sprite = newSprite;
                SetOverrideSprite(newSprite);
                targetComponent?.GetShadow(targetIndex)?.ReleaseDerivedSprites();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (cropped != null) DestroyImmediate(cropped);
                if (padded != null) DestroyImmediate(padded);
            }
        }

        private static void ConfigureImporter(string path, float pixelsPerUnit, Vector2 normalizedPivot, FilterMode filterMode)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.filterMode = filterMode;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = pixelsPerUnit;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            // FullRect is not optional here. The default, Tight, trims the sprite mesh to
            // the opaque pixels - which throws away the transparent margin this tool exists
            // to give you. sprite.bounds then collapses to the original silhouette, so the
            // paintable area shrinks to a fraction of the canvas and anything painted into
            // the margin only appears after a reimport regenerates the mesh.
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = normalizedPivot;
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
        }

        private static string BuildShadowTexturePath(Sprite sprite)
        {
            string folder = ShadowEditAssets.EnsureFolder();
            string path = Path.Combine(folder, sprite.name + ShadowEditAssets.EditSuffix + ".png").Replace("\\", "/");
            return AssetDatabase.GenerateUniqueAssetPath(path);
        }

        // ─────────────────────────── Strokes ────────────────────────────────────

        private void PaintAtWorld(Vector2 worldPos, BrushMode mode)
        {
            Sprite sprite = targetShadowRenderer.sprite;
            if (sprite == null || editableTexture == null)
                return;

            if (pixels == null)
                LoadPixels();
            if (pixels == null)
                return;

            int width = editableTexture.width;
            int height = editableTexture.height;

            // Defensive: a stale buffer from a differently-sized texture would scatter
            // strokes across wrong rows rather than fail loudly.
            if (pixels.Length != width * height)
            {
                LoadPixels();
                if (pixels == null || pixels.Length != width * height)
                    return;
            }

            // Map through the sprite's own rect and pivot rather than sprite.bounds.
            // Bounds depend on the mesh type - a Tight mesh reports the opaque region, not
            // the full canvas - and this needs to be exact regardless of how it imported.
            Vector2 localPos = targetShadowRenderer.transform.InverseTransformPoint(worldPos);
            float ppu = sprite.pixelsPerUnit;
            Rect rect = sprite.rect;

            float spriteX = localPos.x * ppu + sprite.pivot.x;
            float spriteY = localPos.y * ppu + sprite.pivot.y;

            if (targetShadowRenderer.flipX) spriteX = rect.width - spriteX;
            if (targetShadowRenderer.flipY) spriteY = rect.height - spriteY;

            int cx = Mathf.RoundToInt(rect.x + spriteX);
            int cy = Mathf.RoundToInt(rect.y + spriteY);

            int r = brushSize;
            int rSq = r * r;
            var value = new Color32(255, 255, 255, mode == BrushMode.Paint ? (byte)255 : (byte)0);

            for (int dy = -r; dy <= r; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= height)
                    continue;

                int row = py * width;
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > rSq)
                        continue;

                    int px = cx + dx;
                    if (px < 0 || px >= width)
                        continue;

                    pixels[row + px] = value;
                }
            }

            editableTexture.SetPixels32(pixels);
            editableTexture.Apply();
        }

        private void PushUndo()
        {
            if (pixels == null)
                LoadPixels();
            if (pixels == null)
                return;

            undoStack.Add((Color32[])pixels.Clone());
            if (undoStack.Count > MaxUndoSteps)
                undoStack.RemoveAt(0);
        }

        private void PopUndo()
        {
            if (editableTexture == null || undoStack.Count == 0)
                return;

            int last = undoStack.Count - 1;
            pixels = undoStack[last];
            undoStack.RemoveAt(last);

            editableTexture.SetPixels32(pixels);
            editableTexture.Apply();

            isDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        // ─────────────────────────── Window GUI ─────────────────────────────────

        private void OnGUI()
        {
            // A target can stop being editable underneath us - converting the component to
            // Dynamic while the brush is open on it.
            if (targetComponent != null && !CanEdit(targetComponent))
            {
                StopPainting(save: true);
                targetComponent = null;
                targetShadowRenderer = null;
            }

            if (targetComponent == null || targetShadowRenderer == null)
            {
                if (isPainting)
                    StopPainting(save: false);

                EditorGUILayout.HelpBox(
                    "No shadow selected for editing.\n" +
                    "Use 'Edit Shape (Brush)' on a Shadow2DStatic or Shadow2DDynamic component.",
                    MessageType.Info);
                return;
            }

            Shadow2DInstance current = targetComponent.GetShadow(targetIndex);
            EditorGUILayout.LabelField(
                current != null ? $"{targetComponent.gameObject.name} - {current.name}" : targetComponent.gameObject.name,
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(texturePath) ? "No edits yet" : Path.GetFileName(texturePath),
                EditorStyles.miniLabel);

            GUILayout.Space(6);

            GUI.backgroundColor = isPainting ? new Color(1f, 0.5f, 0.5f) : Color.white;
            if (GUILayout.Button(isPainting ? "Stop Painting" : "Start Painting", GUILayout.Height(32)))
            {
                if (isPainting) StopPainting(save: true);
                else StartPainting();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);

            using (new EditorGUI.DisabledScope(!isPainting))
            {
                brushMode = (BrushMode)EditorGUILayout.EnumPopup("Mode", brushMode);
                brushSize = EditorGUILayout.IntSlider("Brush Size (px)", brushSize, 1, 200);
            }

            EditorGUI.BeginChangeCheck();
            bool follow = EditorGUILayout.Toggle(
                new GUIContent("Follow Selection",
                    "Retarget the brush at whichever shadow you select, saving the current one first."),
                FollowSelection);
            if (EditorGUI.EndChangeCheck())
                FollowSelection = follow;

            GUILayout.Space(8);

            if (isDirty)
                EditorGUILayout.HelpBox("Unsaved changes.", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!isDirty))
            {
                if (GUILayout.Button("Save"))
                    SaveTexture();
            }
            using (new EditorGUI.DisabledScope(undoStack.Count == 0))
            {
                if (GUILayout.Button($"Undo ({undoStack.Count})"))
                    PopUndo();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(texturePath) || !isDirty))
            {
                if (GUILayout.Button(new GUIContent("Revert",
                        "Drop unsaved strokes and reload the last saved version.")))
                {
                    RevertTexture();
                }
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(texturePath)))
            {
                if (GUILayout.Button(new GUIContent("Reset From Source",
                        "Throw away all edits and re-clone the caster's sprite.")))
                {
                    ResetFromSource();
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Left-click and drag in the Scene view to paint or erase.\n" +
                "Shift - temporarily switch mode\n" +
                "Ctrl/Cmd + scroll - brush size\n" +
                "Ctrl/Cmd + Z - undo the last stroke\n" +
                "Ctrl/Cmd + S - save the silhouette",
                MessageType.Info);

            // Keep the shortcuts working while the brush window itself has focus.
            HandleShortcuts(Event.current);
        }

        // ─────────────────────────── Save / revert ──────────────────────────────

        private void SaveTexture()
        {
            if (editableTexture == null || string.IsNullOrEmpty(texturePath))
                return;

            File.WriteAllBytes(texturePath, editableTexture.EncodeToPNG());
            AssetDatabase.Refresh();
            editableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            LoadPixels();
            isDirty = false;

            ResyncTarget();
        }

        /// <summary>
        /// Push the silhouette back onto the shadow after a reimport. The derived
        /// pivot-corrected sprite is built from the texture object, and reimporting
        /// replaces that object - so it has to be thrown away or the shadow draws a sprite
        /// whose texture no longer exists.
        /// </summary>
        private void ResyncTarget()
        {
            if (targetComponent == null)
                return;

            targetComponent.GetShadow(targetIndex)?.ReleaseDerivedSprites();
            targetComponent.UpdateShadow();
            SceneView.RepaintAll();
        }

        /// <summary>Drop unsaved strokes by forcing the texture to reimport from the PNG on disk.</summary>
        private void RevertTexture()
        {
            if (string.IsNullOrEmpty(texturePath))
                return;

            // The in-memory Texture2D is the imported asset, so painting already changed
            // it. Only a forced reimport restores what is actually on disk.
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
            editableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            LoadPixels();
            undoStack.Clear();
            isDirty = false;

            ResyncTarget();
        }

        /// <summary>Throw away every edit and re-clone the caster's sprite into the same asset.</summary>
        private void ResetFromSource()
        {
            if (targetComponent == null || targetShadowRenderer == null)
                return;

            SpriteRenderer source = targetComponent.GetSourceRenderer();
            Sprite sprite = source != null ? source.sprite : null;
            if (sprite == null)
            {
                EditorUtility.DisplayDialog("Reset From Source",
                    "The caster has no sprite to re-clone from.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Reset From Source",
                    "Discard all edits to this silhouette and re-clone the caster's sprite?",
                    "Reset", "Cancel"))
            {
                return;
            }

            CreateEditableTexture(sprite, texturePath);
            undoStack.Clear();
            isDirty = false;

            ResyncTarget();
        }

        // ─────────────────────────── Helpers ────────────────────────────────────

        /// <summary>
        /// The sprite this shadow is derived from - its own silhouette if it has one,
        /// otherwise the caster's - rather than the pivot-corrected copy on the renderer.
        /// </summary>
        private Sprite GetBaseSprite()
        {
            if (targetComponent == null)
                return null;

            Sprite silhouette = targetComponent.GetShadow(targetIndex)?.overrideSprite;
            if (silhouette != null)
                return silhouette;

            SpriteRenderer source = targetComponent.GetSourceRenderer();
            return source != null ? source.sprite : null;
        }

        private static Texture2D BlankCursor
        {
            get
            {
                if (blankCursor == null)
                {
                    blankCursor = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    blankCursor.SetPixels32(new Color32[4]); // all zero = fully transparent
                    blankCursor.Apply();
                }
                return blankCursor;
            }
        }

        private void SetOverrideSprite(Sprite sprite)
        {
            if (targetComponent == null)
                return;

            var so = new SerializedObject(targetComponent);
            SerializedProperty prop = so.FindProperty($"shadows.Array.data[{targetIndex}].overrideSprite");
            if (prop != null)
            {
                prop.objectReferenceValue = sprite;
                so.ApplyModifiedProperties();
            }
        }

        /// <summary>
        /// Bring an existing silhouette up to the settings the brush needs. Also repairs
        /// textures written before 1.3.1, which imported with the default Tight mesh and
        /// so reported a paintable area collapsed to their opaque pixels.
        /// </summary>
        private static void EnsureTextureReadable(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            bool needsReimport = false;

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                needsReimport = true;
            }

            if (settings.spriteMeshType != SpriteMeshType.FullRect)
            {
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteExtrude = 0;
                importer.SetTextureSettings(settings);
                needsReimport = true;
            }

            if (needsReimport)
                importer.SaveAndReimport();
        }
    }
}
