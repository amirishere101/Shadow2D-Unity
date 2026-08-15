using System.IO;
using UnityEditor;
using UnityEngine;

namespace SleepyHeadStudios.Editor
{
    /// <summary>
    /// In-editor brush for painting and erasing custom shadow silhouettes directly
    /// in the Scene view. Opened from the Shadow2DStatic / Shadow2DDynamic inspector.
    ///
    /// The source sprite is copied through a temporary RenderTexture rather than
    /// GetPixels, so the source texture never has to be marked readable. The result
    /// is written to Assets/ShadowEdits as a white silhouette that keeps the original
    /// alpha - only alpha matters, since the shadow tint supplies the colour.
    /// </summary>
    public class ShadowBrushTool : EditorWindow
    {
        public enum BrushMode
        {
            Paint,
            Erase
        }

        private const string ShadowEditFolder = "Assets/ShadowEdits";

        private SpriteRenderer targetShadowRenderer;
        private Shadow2DBase targetComponent;
        private Texture2D editableTexture;
        private string texturePath;

        private BrushMode brushMode = BrushMode.Paint;
        private int brushSize = 5;
        private float brushOpacity = 1f;

        private bool isActive;
        private bool isDirty;

        /// <summary>Open the brush on a shadow component, creating its edit texture if needed.</summary>
        public static void Open(Shadow2DBase shadow)
        {
            if (shadow == null)
                return;

            GameObject shadowObject = shadow.GetShadowObject();
            if (shadowObject == null)
            {
                EditorUtility.DisplayDialog("Shadow Brush",
                    "Create the shadow first - there's nothing to paint on yet.", "OK");
                return;
            }

            SpriteRenderer source = shadow.GetSourceRenderer();
            Sprite sourceSprite = shadow.OverrideSprite != null
                ? shadow.OverrideSprite
                : (source != null ? source.sprite : null);

            var window = GetWindow<ShadowBrushTool>("Shadow Brush");
            window.StartEditing(shadowObject.GetComponent<SpriteRenderer>(), shadow, sourceSprite);
            window.Show();
        }

        /// <summary>
        /// Begin editing a shadow. Reuses an existing _ShadowEdit texture when the
        /// shadow already points at one, so reopening carries on from where you stopped.
        /// </summary>
        public void StartEditing(SpriteRenderer shadowRenderer, Shadow2DBase shadowComponent, Sprite sourceSprite)
        {
            if (shadowRenderer == null || sourceSprite == null)
            {
                Debug.LogError("ShadowBrushTool: missing shadow renderer or source sprite.");
                return;
            }

            StopEditingInternal(save: true);

            targetShadowRenderer = shadowRenderer;
            targetComponent = shadowComponent;

            string existingPath = AssetDatabase.GetAssetPath(shadowRenderer.sprite);
            if (!string.IsNullOrEmpty(existingPath) && existingPath.Contains("_ShadowEdit"))
            {
                texturePath = existingPath;
                EnsureTextureReadable(texturePath);
                editableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (editableTexture != null)
                {
                    ActivateSceneGui();
                    return;
                }
            }

            CreateEditableTexture(sourceSprite);
            ActivateSceneGui();
        }

        // ─────────────────────────── Texture creation ───────────────────────────

        private void CreateEditableTexture(Sprite sourceSprite)
        {
            Texture2D sourceTex = sourceSprite.texture;
            Rect srcRect = sourceSprite.textureRect;
            int w = Mathf.Max(1, Mathf.RoundToInt(srcRect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(srcRect.height));

            var renderTexture = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                RenderTexture.active = renderTexture;
                GL.Clear(true, true, Color.clear);

                var sourceRectNormalized = new Rect(
                    srcRect.x / sourceTex.width,
                    srcRect.y / sourceTex.height,
                    srcRect.width / sourceTex.width,
                    srcRect.height / sourceTex.height);

                Graphics.DrawTexture(new Rect(0f, 0f, w, h), sourceTex, sourceRectNormalized,
                    0, 0, 0, 0, Color.white, null);

                var newTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                newTex.filterMode = sourceTex.filterMode;
                newTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                newTex.Apply();

                // White silhouette, original alpha. The shadow tint supplies the colour.
                Color[] pixels = newTex.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color(1f, 1f, 1f, pixels[i].a);
                newTex.SetPixels(pixels);
                newTex.Apply();

                texturePath = BuildShadowTexturePath(sourceSprite);
                File.WriteAllBytes(texturePath, newTex.EncodeToPNG());
                DestroyImmediate(newTex);
                AssetDatabase.Refresh();

                ConfigureImporter(sourceSprite, texturePath, sourceTex.filterMode);

                editableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                Sprite newSprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);

                targetShadowRenderer.sprite = newSprite;
                SetOverrideSprite(newSprite);
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static void ConfigureImporter(Sprite sourceSprite, string path, FilterMode filterMode)
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
            importer.spritePixelsPerUnit = sourceSprite.pixelsPerUnit;

            var normalizedPivot = new Vector2(
                sourceSprite.pivot.x / sourceSprite.rect.width,
                sourceSprite.pivot.y / sourceSprite.rect.height);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            importer.SetTextureSettings(settings);
            importer.spritePivot = normalizedPivot;

            importer.SaveAndReimport();
        }

        private static string BuildShadowTexturePath(Sprite sourceSprite)
        {
            EnsureShadowEditFolderExists();
            string path = Path.Combine(ShadowEditFolder, sourceSprite.name + "_ShadowEdit.png").Replace("\\", "/");
            return AssetDatabase.GenerateUniqueAssetPath(path);
        }

        private static void EnsureShadowEditFolderExists()
        {
            if (AssetDatabase.IsValidFolder(ShadowEditFolder))
                return;

            AssetDatabase.CreateFolder("Assets", "ShadowEdits");
        }

        // ─────────────────────────── Scene GUI painting ─────────────────────────

        private void ActivateSceneGui()
        {
            isActive = true;
            isDirty = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            SceneView.RepaintAll();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isActive || editableTexture == null || targetShadowRenderer == null)
                return;

            Event e = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Vector2 worldPos = ray.origin;

            float ppu = targetShadowRenderer.sprite != null ? targetShadowRenderer.sprite.pixelsPerUnit : 16f;
            float worldRadius = brushSize / ppu;

            // Shift temporarily inverts the mode.
            BrushMode activeMode = e.shift
                ? (brushMode == BrushMode.Paint ? BrushMode.Erase : BrushMode.Paint)
                : brushMode;

            Handles.color = activeMode == BrushMode.Paint
                ? new Color(1f, 1f, 1f, 0.5f)
                : new Color(1f, 0f, 0f, 0.5f);
            Handles.DrawWireDisc(worldPos, Vector3.forward, worldRadius);

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
            {
                PaintAtWorld(worldPos, activeMode);
                e.Use();
                isDirty = true;
            }

            // Swallow clicks so painting doesn't reselect objects.
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            sceneView.Repaint();
            Repaint();
        }

        private void PaintAtWorld(Vector2 worldPos, BrushMode mode)
        {
            Sprite sprite = targetShadowRenderer.sprite;
            if (sprite == null)
                return;

            Vector2 localPos = targetShadowRenderer.transform.InverseTransformPoint(worldPos);
            Vector2 boundsMin = sprite.bounds.min;
            Vector2 boundsSize = sprite.bounds.size;

            float normX = (localPos.x - boundsMin.x) / boundsSize.x;
            float normY = (localPos.y - boundsMin.y) / boundsSize.y;

            if (targetShadowRenderer.flipX) normX = 1f - normX;
            if (targetShadowRenderer.flipY) normY = 1f - normY;

            int cx = Mathf.RoundToInt(normX * (editableTexture.width - 1));
            int cy = Mathf.RoundToInt(normY * (editableTexture.height - 1));

            int r = brushSize;
            int rSq = r * r;

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (dx * dx + dy * dy > rSq)
                        continue;

                    int px = cx + dx;
                    int py = cy + dy;
                    if (px < 0 || px >= editableTexture.width || py < 0 || py >= editableTexture.height)
                        continue;

                    Color c = editableTexture.GetPixel(px, py);
                    if (mode == BrushMode.Paint)
                        c = new Color(1f, 1f, 1f, Mathf.Min(1f, c.a + brushOpacity));
                    else
                        c.a = Mathf.Max(0f, c.a - brushOpacity);

                    editableTexture.SetPixel(px, py, c);
                }
            }

            editableTexture.Apply();
        }

        // ─────────────────────────── Window GUI ─────────────────────────────────

        private void OnGUI()
        {
            if (!isActive || targetShadowRenderer == null)
            {
                EditorGUILayout.HelpBox(
                    "No shadow selected for editing.\n" +
                    "Use the 'Edit Shadow Shape' button on a Shadow2DStatic or Shadow2DDynamic component.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Shadow Brush", EditorStyles.boldLabel);
            GUILayout.Space(4);

            brushMode = (BrushMode)EditorGUILayout.EnumPopup("Mode", brushMode);
            brushSize = EditorGUILayout.IntSlider("Brush Size (px)", brushSize, 1, 50);
            brushOpacity = EditorGUILayout.Slider("Opacity", brushOpacity, 0.05f, 1f);

            GUILayout.Space(8);

            if (isDirty)
                EditorGUILayout.HelpBox("Unsaved changes.", MessageType.Warning);

            if (GUILayout.Button("Save"))
                SaveTexture();

            if (GUILayout.Button("Stop Editing"))
                StopEditingInternal(save: true);

            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Left-click and drag in the Scene view to paint or erase.\n" +
                "Hold Shift to temporarily switch mode.",
                MessageType.Info);
        }

        // ─────────────────────────── Save / cleanup ─────────────────────────────

        private void SaveTexture()
        {
            if (editableTexture == null || string.IsNullOrEmpty(texturePath))
                return;

            File.WriteAllBytes(texturePath, editableTexture.EncodeToPNG());
            AssetDatabase.Refresh();
            editableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            isDirty = false;

            if (targetComponent != null)
                targetComponent.UpdateShadow();
        }

        private void StopEditingInternal(bool save)
        {
            if (!isActive)
                return;

            if (save && isDirty &&
                EditorUtility.DisplayDialog("Unsaved Changes",
                    "Save shadow brush changes before stopping?", "Save", "Discard"))
            {
                SaveTexture();
            }

            isActive = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.RepaintAll();
        }

        private void OnDestroy()
        {
            if (!isActive)
                return;

            if (isDirty)
                SaveTexture();
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        // ─────────────────────────── Helpers ────────────────────────────────────

        private void SetOverrideSprite(Sprite sprite)
        {
            if (targetComponent == null)
                return;

            var so = new SerializedObject(targetComponent);
            SerializedProperty prop = so.FindProperty("overrideSprite");
            if (prop != null)
            {
                prop.objectReferenceValue = sprite;
                so.ApplyModifiedProperties();
            }
        }

        private static void EnsureTextureReadable(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }
    }
}
