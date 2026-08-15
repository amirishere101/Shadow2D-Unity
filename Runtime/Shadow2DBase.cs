using System.Collections.Generic;
using UnityEngine;

namespace DryFlyStudio
{
    /// <summary>
    /// Shared behaviour for <see cref="Shadow2DStatic"/> and <see cref="Shadow2DDynamic"/>:
    /// owns one or more shadow GameObjects, copies sprite state from the caster, and keeps
    /// sorting in sync. Not attachable directly - use one of the two subclasses.
    /// </summary>
    public abstract class Shadow2DBase : MonoBehaviour
    {
        /// <summary>
        /// Reused by every instance so resolving the source renderer never allocates.
        /// Safe as a static because resolution runs to completion inside one call and
        /// Unity components only ever resolve on the main thread.
        /// </summary>
        private static readonly List<SpriteRenderer> RendererBuffer = new List<SpriteRenderer>(8);

        private static readonly int CasterTexId = Shader.PropertyToID("_CasterTex");
        private static readonly int CasterStId = Shader.PropertyToID("_CasterST");
        private static readonly int CasterMatrixId = Shader.PropertyToID("_CasterMatrix");
        private static readonly int SelfMaskId = Shader.PropertyToID("_SelfMask");

        /// <summary>Reused for every shadow so pushing the mask never allocates.</summary>
        private static MaterialPropertyBlock propertyBlock;

        /// <summary>
        /// How far up the sort axis a shadow sits relative to its caster, purely to lose
        /// ties. Small enough to be meaningless against real scene spacing, large enough
        /// to survive float precision at ordinary world coordinates.
        /// </summary>
        private const float SortTieBias = 0.001f;

        [Tooltip("Hide shadows whenever the caster's SpriteRenderer is disabled or its GameObject is inactive, so hiding a sprite doesn't leave its shadows behind.")]
        [SerializeField] private bool followCasterVisibility = true;

        [Tooltip("Multiply each shadow's alpha by the caster's, so fading a sprite out fades its shadows with it.")]
        [SerializeField] private bool followCasterAlpha = true;

        [Tooltip("Enable if using Y-position sorting - shadows keep the caster's sorting layer and order, and their own Y position settles the rest, so they draw over the objects behind them and are hidden by the ones in front. Disable to render one order behind the caster instead, for projects that don't sort by Y.")]
        [SerializeField] private bool useYSorting = true;

        [SerializeField] private List<Shadow2DInstance> shadows = new List<Shadow2DInstance>();

        // Distinguishes "component was just added, make a shadow" from "the user deleted
        // the shadows on purpose, leave them alone and say so".
        [HideInInspector][SerializeField] private bool shadowEverCreated;

        // ── Pre-1.7 single-shadow fields, kept only so existing scenes migrate ──────
        [HideInInspector][SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
        [HideInInspector][SerializeField] private Sprite overrideSprite;
        [HideInInspector][SerializeField] private Material shadowMaterial;
        [HideInInspector][SerializeField] private bool overrideTransform;
        [HideInInspector][SerializeField] private Vector3 shadowOffset = new Vector3(0f, 0f, 0.01f);
        [HideInInspector][SerializeField] private float shadowRotationZ = 12.5f;
        [HideInInspector][SerializeField] private Vector3 shadowScale = new Vector3(1f, 0.9f, 1f);
        [HideInInspector][SerializeField] private GameObject shadowObject;

        private SpriteRenderer sourceRenderer;

        /// <summary>Every shadow on this caster, in inspector order.</summary>
        public IReadOnlyList<Shadow2DInstance> Shadows
        {
            get { MigrateLegacyShadow(); return shadows; }
        }

        /// <summary>How many shadows this caster currently has.</summary>
        public int ShadowCount
        {
            get { MigrateLegacyShadow(); return shadows.Count; }
        }

        /// <summary>
        /// True once this component has created a shadow at least once. A component with
        /// no shadows and this still false has just been added and should get one; with it
        /// true, they were deleted deliberately and are not silently recreated.
        /// </summary>
        public bool HasEverCreatedShadow => shadowEverCreated;

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: raised when a Shadow2D component is destroyed outside play mode,
        /// so the editor assembly can tear down the shadow objects and any silhouette
        /// textures that belonged to them. Arguments are (owner, shadowObjects, silhouettes).
        ///
        /// The owner is passed because the handler has to tell "component removed from a
        /// living GameObject" (delete the shadows) from "the whole GameObject is going
        /// away" (Unity already handles it) - and that is only knowable a frame later.
        /// </summary>
        public static event System.Action<GameObject, GameObject[], Sprite[]> EditorComponentDestroyed;
#endif

        /// <summary>The shadow at <paramref name="index"/>, or null if out of range.</summary>
        public Shadow2DInstance GetShadow(int index)
        {
            MigrateLegacyShadow();
            return index >= 0 && index < shadows.Count ? shadows[index] : null;
        }

        /// <summary>Index of the shadow owning <paramref name="candidate"/>, or -1.</summary>
        public int IndexOfShadowObject(GameObject candidate)
        {
            MigrateLegacyShadow();
            if (candidate == null)
                return -1;

            for (int i = 0; i < shadows.Count; i++)
            {
                if (shadows[i] != null && shadows[i].shadowObject == candidate)
                    return i;
            }
            return -1;
        }

        // ─────────────────────────── Migration ──────────────────────────────────

        /// <summary>
        /// Fold a pre-1.7 single shadow into the list. Self-clearing: the legacy reference
        /// is nulled once moved, so this can be called from anywhere without a version flag
        /// (a flag would deserialize to its initializer on old data and read as migrated).
        ///
        /// Run the pre-1.7 migration and report whether anything moved, so the editor can
        /// mark the object dirty and make it stick. Without that the migration re-runs
        /// from the same stale serialized data on every domain reload.
        /// </summary>
        public bool MigrateLegacyData() => MigrateLegacyShadow();

        private bool MigrateLegacyShadow()
        {
            if (shadows == null)
                shadows = new List<Shadow2DInstance>();

            if (shadowObject == null)
                return false;

            shadows.Insert(0, new Shadow2DInstance
            {
                name = "Shadow",
                color = shadowColor,
                overrideSprite = overrideSprite,
                material = shadowMaterial,
                offset = shadowOffset,
                rotationZ = shadowRotationZ,
                scale = shadowScale,
                shadowObject = shadowObject,
            });

            shadowObject = null;
            overrideSprite = null;
            shadowMaterial = null;
            shadowEverCreated = true;
            return true;
        }

        /// <summary>
        /// Make sure every shadow in the list is one of <em>this</em> object's children,
        /// re-pointing at our own copies where it isn't.
        ///
        /// Duplicating or copy-pasting a caster copies its shadow children too, but the
        /// duplicate's list can come back still referencing the original's shadow. Two
        /// components then drive one shadow object, each computing its transform, sprite
        /// and self-cast mask against a different caster - which looks like the original's
        /// shadow breaking, since it is the one being fought over.
        /// </summary>
        /// <returns>True if anything was re-pointed.</returns>
        public bool RepairShadowOwnership()
        {
            MigrateLegacyShadow();

            var owned = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (child.GetComponent<Shadow2DMarker>() != null)
                    owned.Add(child.gameObject);
            }

            // A child already spoken for - by us or by another Shadow2D component on this
            // same object - can't be handed out again.
            var claimed = new HashSet<GameObject>();
            Shadow2DBase[] siblings = GetComponents<Shadow2DBase>();
            foreach (Shadow2DBase sibling in siblings)
            {
                for (int i = 0; i < sibling.shadows.Count; i++)
                {
                    GameObject obj = sibling.shadows[i]?.shadowObject;
                    if (obj != null && obj.transform.parent == transform)
                        claimed.Add(obj);
                }
            }

            bool changed = false;
            int next = 0;

            for (int i = 0; i < shadows.Count; i++)
            {
                Shadow2DInstance instance = shadows[i];
                if (instance == null)
                    continue;

                bool ours = instance.shadowObject != null &&
                            instance.shadowObject.transform.parent == transform;
                if (ours)
                    continue;

                while (next < owned.Count && claimed.Contains(owned[next]))
                    next++;

                if (next < owned.Count)
                {
                    instance.shadowObject = owned[next];
                    claimed.Add(owned[next]);
                    next++;
                }
                else
                {
                    // Referenced a stranger and we have no copy of our own to adopt.
                    instance.shadowObject = null;
                }

                // Both the renderer and the pivot-corrected sprites belonged to the
                // other object's shadow.
                instance.Renderer = null;
                instance.ReleaseDerivedSprites();
                changed = true;
            }

            return changed;
        }

        // ─────────────────────────── Source renderer ────────────────────────────

        /// <summary>
        /// The renderer the shadows copy from. The root is checked first; if its
        /// renderer is missing or has no sprite, children are searched. That fallback
        /// exists for setups that keep the visible sprite on a child (e.g. a
        /// "Visual" child) while this component sits on the root.
        /// </summary>
        public SpriteRenderer GetSourceRenderer()
        {
            // Re-resolve when the cached renderer has gone away, been disabled, or
            // lost its sprite. Objects that swap their visual between children
            // (crops moving through growth stages) rely on this.
            if (sourceRenderer == null || !sourceRenderer.enabled || sourceRenderer.sprite == null)
                sourceRenderer = ResolveSourceRenderer();
            return sourceRenderer;
        }

        private SpriteRenderer ResolveSourceRenderer()
        {
            SpriteRenderer own = GetComponent<SpriteRenderer>();
            if (own != null && own.enabled && own.sprite != null)
                return own;

            // Non-allocating overload: a dynamic shadow whose caster is legitimately
            // spriteless for a frame would otherwise allocate an array every LateUpdate.
            GetComponentsInChildren(true, RendererBuffer);

            for (int i = 0; i < RendererBuffer.Count; i++)
            {
                SpriteRenderer candidate = RendererBuffer[i];
                if (candidate == own || !candidate.enabled || candidate.sprite == null)
                    continue;
                if (candidate.GetComponent<Shadow2DMarker>() != null)
                    continue; // never treat a shadow (ours or a child's) as the source

                RendererBuffer.Clear();
                return candidate;
            }

            RendererBuffer.Clear();

            // Last resort: keep the root renderer even if disabled or spriteless.
            return own;
        }

        // ─────────────────────────── Unity messages ─────────────────────────────

        private void Reset()
        {
            Shadow2DConfig config = Shadow2DConfig.GetOrCreateDefault();
            if (config != null)
                useYSorting = config.useYSortingByDefault;
        }

        private void Awake()
        {
            MigrateLegacyShadow();
            RepairShadowOwnership();

            // Repair shadows created before the marker component existed.
            for (int i = 0; i < shadows.Count; i++)
            {
                GameObject obj = shadows[i]?.shadowObject;
                if (obj != null && obj.GetComponent<Shadow2DMarker>() == null)
                    obj.AddComponent<Shadow2DMarker>();
            }
        }

        private void Start()
        {
            UpdateShadow();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && ShadowCount > 0)
                UpdateShadow();
        }

        private void OnDestroy()
        {
            // Derived pivot sprites are created at runtime and owned by nobody else.
            for (int i = 0; i < shadows.Count; i++)
                shadows[i]?.ReleaseDerivedSprites();

            if (Application.isPlaying)
            {
                for (int i = 0; i < shadows.Count; i++)
                {
                    if (shadows[i]?.shadowObject != null)
                        Destroy(shadows[i].shadowObject);
                }
                return;
            }

#if UNITY_EDITOR
            // Removing the component in the editor should take its shadows with it. The
            // editor assembly owns that, because deciding whether the silhouette textures
            // can also go needs the AssetDatabase.
            if (shadows.Count == 0)
                return;

            var objects = new GameObject[shadows.Count];
            var silhouettes = new Sprite[shadows.Count];
            for (int i = 0; i < shadows.Count; i++)
            {
                objects[i] = shadows[i]?.shadowObject;
                silhouettes[i] = shadows[i]?.overrideSprite;
            }

            EditorComponentDestroyed?.Invoke(gameObject, objects, silhouettes);
#endif
        }

        // ─────────────────────────── Syncing ────────────────────────────────────

        /// <summary>
        /// Sync every shadow with the caster: sprite (or override), flips, colour,
        /// visibility, material override, sorting, self-cast mask, and - where enabled -
        /// the shadow's local transform.
        /// Call after changing the caster's sprite yourself on a static shadow.
        /// </summary>
        public void UpdateShadow()
        {
            MigrateLegacyShadow();

            if (shadows.Count == 0)
                return;

            // Resolving the source can walk the whole child hierarchy, so it happens once
            // for the whole list rather than once per shadow.
            SpriteRenderer source = GetSourceRenderer();
            if (source == null)
                return;

            bool casterVisible = source.enabled && source.gameObject.activeInHierarchy;

            // With Y-sorting the shadow shares the caster's order and lets its own Y
            // position settle the rest, which is what makes it behave like a thing lying
            // on the ground: drawn over the props behind it, hidden by the props in front.
            //
            // It briefly rendered at order + 1 to guarantee it landed on other objects.
            // That was the wrong lever - shadows were being forced behind everything by
            // the Transparent-1 render queue, not by their sorting order - and +1 put them
            // on top of the whole layer, including objects standing in front of the caster.
            //
            // Without Y-sorting the shadow sits one order behind, which is what a project
            // sorting purely by order expects.
            int order = useYSorting ? source.sortingOrder : source.sortingOrder - 1;

            for (int i = 0; i < shadows.Count; i++)
                SyncShadow(shadows[i], source, casterVisible, order);
        }

        private void SyncShadow(Shadow2DInstance instance, SpriteRenderer source, bool casterVisible, int order)
        {
            if (instance == null)
                return;

            SpriteRenderer shadow = instance.Renderer;
            if (shadow == null)
                return;

            Sprite baseSprite = instance.overrideSprite != null ? instance.overrideSprite : source.sprite;
            if (shadow.flipX != source.flipX) shadow.flipX = source.flipX;
            if (shadow.flipY != source.flipY) shadow.flipY = source.flipY;

            // A shadow whose caster is hidden used to keep drawing, leaving a silhouette
            // on the ground with nothing above it.
            if (followCasterVisibility && shadow.enabled != casterVisible)
                shadow.enabled = casterVisible;

            Color tint = instance.color;
            if (followCasterAlpha) tint.a *= source.color.a;
            if (shadow.color != tint) shadow.color = tint;

            // Only enforce an explicit per-shadow override. The config-resolved material is
            // applied at creation and left alone afterwards, so hand-assigning a material on
            // the shadow child still sticks.
            if (instance.material != null && shadow.sharedMaterial != instance.material)
                shadow.sharedMaterial = instance.material;

            instance.ApplyTransform();
            ApplySortPoint(instance, source, shadow, baseSprite);

            if (shadow.sortingLayerID != source.sortingLayerID)
                shadow.sortingLayerID = source.sortingLayerID;
            if (shadow.sortingOrder != order)
                shadow.sortingOrder = order;

            // Y-sorting compares sort points, and Unity's default is the bounds centre.
            // For a shadow that is half a sprite tall, the centre sits well above the
            // ground, so it sorts as though it were standing upright and slides behind
            // props it should be lying in front of. Pivot puts the sort point at the
            // shadow's own origin - for the bottom-pivot art a Y-sorted project uses,
            // that is as close to its lowest point as Unity's API allows.
            if (shadow.spriteSortPoint != SpriteSortPoint.Pivot)
                shadow.spriteSortPoint = SpriteSortPoint.Pivot;

            ApplySelfMask(source, shadow);
        }

        /// <summary>
        /// Make the shadow sort from exactly the same point as its caster.
        ///
        /// This is what keeps a shadow off the objects standing in front of the thing
        /// casting it. Sharing the caster's sort point means the shadow sorts wherever the
        /// caster sorts - so it is drawn over everything the caster is drawn over, and
        /// behind everything the caster is behind, with no third answer possible. Any sort
        /// point below the caster reintroduces the bug: lower means drawn later under
        /// Y-sorting, so the shadow starts winning against props between it and the camera.
        ///
        /// Getting it there is the fiddly part. Y-sorting compares sort points, and
        /// SpriteRenderer offers only the bounds centre or the pivot - neither of which can
        /// be aimed somewhere else. So the shadow draws its own copy of the sprite with the
        /// pivot moved onto the caster's sort point, and the object is nudged by exactly
        /// the opposite amount so the sprite still lands where it was. The nudge is
        /// recorded in <see cref="Shadow2DInstance.sortShift"/> so the next sync subtracts
        /// it instead of compounding it.
        /// </summary>
        private static void ApplySortPoint(Shadow2DInstance instance, SpriteRenderer source,
            SpriteRenderer shadow, Sprite baseSprite)
        {
            Transform t = shadow.transform;

            if (baseSprite == null)
            {
                if (shadow.sprite != null) shadow.sprite = null;
                return;
            }

            // Where the sprite has to end up. Read straight from the field, which is the
            // single source of truth - deriving it from the live transform instead meant
            // anything that wrote localPosition without updating sortShift (a Scene view
            // drag) left the two disagreeing, and the shadow flipped between positions
            // depending on which path synced last.
            Vector3 basePosition = instance.offset;

            // Whatever Unity will use to sort the caster is what the shadow has to match.
            Vector3 casterSortPoint = source.spriteSortPoint == SpriteSortPoint.Pivot
                ? source.transform.position
                : source.bounds.center;

            Transform parent = t.parent;
            Vector3 target = parent != null
                ? parent.InverseTransformPoint(casterSortPoint)
                : casterSortPoint;

            // Only X and Y take part in sorting; leave the author's Z alone.
            target.z = basePosition.z;

            // Sit a hair behind the caster on the sort axis. Exactly matching it leaves
            // every shadow tied with every caster at that Y, and Unity breaks ties by
            // internal index - so with two props at identical Y (duplicates pasted and
            // dragged sideways, which is most of them) one prop's shadow arbitrarily wins
            // against the other prop's body.
            //
            // Higher Y sorts farther, so this loses every tie to a caster while changing
            // nothing else: a prop genuinely behind the shadow is still further up the
            // axis, and one in front is still lower. The offset is far below any spacing
            // a scene would use, and the pivot correction below cancels it visually.
            target.y += SortTieBias;

            Vector3 shift = target - basePosition;

            Matrix4x4 rotationScale = Matrix4x4.TRS(Vector3.zero, t.localRotation, t.localScale);

            // The same displacement expressed in the sprite's own unrotated space, which
            // is where a pivot lives.
            Vector3 pivotDelta = rotationScale.inverse.MultiplyPoint3x4(shift);
            float ppu = baseSprite.pixelsPerUnit;
            Rect textureRect = baseSprite.textureRect;

            Vector2 pivotPixels = baseSprite.pivot - baseSprite.textureRectOffset
                                  + new Vector2(pivotDelta.x, pivotDelta.y) * ppu;
            var normalizedPivot = new Vector2(
                pivotPixels.x / textureRect.width,
                pivotPixels.y / textureRect.height);

            Sprite corrected = instance.GetPivotCorrected(
                baseSprite, normalizedPivot, t.localRotation, t.localScale, shift);

            if (corrected != null && shadow.sprite != corrected)
                shadow.sprite = corrected;

            if (t.localPosition != target)
                t.localPosition = target;

            instance.sortShift = shift;
        }

        /// <summary>
        /// Tell the shadow shader where its caster is, so it can discard the pixels the
        /// caster covers.
        ///
        /// This is a mask rather than a draw-order trick because draw order can't express
        /// it: for a shadow to fall across a prop standing in front of its caster, it has
        /// to be drawn after that prop, and therefore after the caster too. Ordering can
        /// put a shadow behind its caster or in front of the scene, never both.
        ///
        /// Not optional. Shadows render one sorting order in front of their caster, so they
        /// cover it every single time - there is no configuration where switching this off
        /// produces something you would want.
        /// </summary>
        private static void ApplySelfMask(SpriteRenderer source, SpriteRenderer shadow)
        {
            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            shadow.GetPropertyBlock(propertyBlock);

            Sprite casterSprite = source.sprite;
            if (casterSprite == null || casterSprite.texture == null)
            {
                propertyBlock.SetFloat(SelfMaskId, 0f);
                shadow.SetPropertyBlock(propertyBlock);
                return;
            }

            Texture casterTexture = casterSprite.texture;
            Rect textureRect = casterSprite.textureRect;
            float texWidth = casterTexture.width;
            float texHeight = casterTexture.height;
            float ppu = casterSprite.pixelsPerUnit;

            // Where the caster's pixels actually sit on their texture page, in UV. The
            // shader uses this to ignore samples that fall outside the sprite.
            propertyBlock.SetTexture(CasterTexId, casterTexture);
            propertyBlock.SetVector(CasterStId, new Vector4(
                textureRect.width / texWidth,
                textureRect.height / texHeight,
                textureRect.x / texWidth,
                textureRect.y / texHeight));

            // Caster local units -> pixels from the pivot -> pixels in the sprite's
            // untrimmed rect -> pixels in the trimmed textureRect -> pixels on the page
            // -> UV.
            //
            // textureRectOffset is the part that matters and the part that was missing:
            // the importer trims transparent borders, so textureRect is smaller than rect
            // and sits inside it. Normalising by rect while sampling through textureRect
            // stretched the mask over the full rect, cutting a hole larger than the
            // sprite - visible as a gap between a character and its shadow, worst furthest
            // from the pivot.
            Vector2 trimOffset = casterSprite.textureRectOffset;
            var toPageUv = new Vector3(
                textureRect.x - trimOffset.x + casterSprite.pivot.x,
                textureRect.y - trimOffset.y + casterSprite.pivot.y,
                0f);

            Matrix4x4 toSpriteSpace =
                Matrix4x4.Scale(new Vector3(1f / texWidth, 1f / texHeight, 1f)) *
                Matrix4x4.Translate(toPageUv) *
                Matrix4x4.Scale(new Vector3(ppu, ppu, 1f));

            // flipX/flipY mirror the rendered sprite about the caster's own origin.
            Matrix4x4 flip = Matrix4x4.Scale(new Vector3(
                source.flipX ? -1f : 1f,
                source.flipY ? -1f : 1f,
                1f));

            propertyBlock.SetMatrix(CasterMatrixId,
                toSpriteSpace * flip * source.transform.worldToLocalMatrix * shadow.transform.localToWorldMatrix);
            propertyBlock.SetFloat(SelfMaskId, 1f);

            shadow.SetPropertyBlock(propertyBlock);
        }

        /// <summary>
        /// Recompute only the self-cast masks. They depend on where each shadow sits
        /// relative to its caster, so they go stale whenever either is moved - but unlike
        /// <see cref="UpdateShadow"/> this touches no serialized state, so the editor can
        /// call it freely without dirtying the scene.
        /// </summary>
        public void RefreshSelfMask()
        {
            MigrateLegacyShadow();

            if (shadows.Count == 0)
                return;

            SpriteRenderer source = GetSourceRenderer();
            if (source == null)
                return;

            for (int i = 0; i < shadows.Count; i++)
            {
                SpriteRenderer shadow = shadows[i]?.Renderer;
                if (shadow != null)
                    ApplySelfMask(source, shadow);
            }
        }

        // ─────────────────────────── Creation / removal ─────────────────────────

        /// <summary>
        /// Add a shadow, seeded from Shadow2DConfig, and create its GameObject.
        /// Returns the new instance, or null if no source renderer was found.
        /// </summary>
        public Shadow2DInstance AddShadow()
        {
            MigrateLegacyShadow();

            SpriteRenderer source = GetSourceRenderer();
            if (source == null)
            {
                Debug.LogError("No SpriteRenderer found on this GameObject or its children. Shadow2D needs a sprite to copy.", this);
                return null;
            }

            Shadow2DConfig config = Shadow2DConfig.GetOrCreateDefault();

            var instance = new Shadow2DInstance();
            instance.ApplyConfigDefaults(config);
            instance.name = shadows.Count == 0 ? "Shadow" : $"Shadow {shadows.Count + 1}";

            var created = new GameObject($"{gameObject.name}_{instance.name}");
            created.transform.SetParent(transform, false);
            created.transform.localPosition = instance.offset;
            created.transform.localRotation = Quaternion.Euler(0f, 0f, instance.rotationZ);
            created.transform.localScale = instance.scale;

            instance.Renderer = created.AddComponent<SpriteRenderer>();
            created.AddComponent<Shadow2DMarker>();
            instance.shadowObject = created;

            Material material = instance.material != null ? instance.material : config.ResolveShadowMaterial();
            if (material != null)
                instance.Renderer.sharedMaterial = material;

            shadows.Add(instance);
            shadowEverCreated = true;

            ApplyCasterMaterial(source, config);
            UpdateShadow();
            return instance;
        }

        private static void ApplyCasterMaterial(SpriteRenderer source, Shadow2DConfig config)
        {
            if (!config.replaceCasterMaterial)
                return;

            // Only replace stock unlit sprite materials - never stomp a custom one, and
            // never a lit one, which would drop the caster out of URP's 2D lighting.
            if (!Shadow2DConfig.IsReplaceableCasterMaterial(source.sharedMaterial))
                return;

            Material caster = config.ResolveCasterMaterial();
            if (caster != null)
                source.sharedMaterial = caster;
        }

        /// <summary>
        /// Remove a shadow and destroy its GameObject. The silhouette asset is left alone;
        /// deciding whether it can go needs the AssetDatabase and lives in the editor.
        /// </summary>
        public void RemoveShadow(int index)
        {
            MigrateLegacyShadow();

            Shadow2DInstance instance = GetShadow(index);
            if (instance == null)
                return;

            instance.ReleaseDerivedSprites();

            if (instance.shadowObject != null)
            {
                if (Application.isPlaying)
                    Destroy(instance.shadowObject);
                else
                    DestroyImmediate(instance.shadowObject);
            }

            shadows.RemoveAt(index);
        }

        /// <summary>Remove every shadow and destroy their GameObjects.</summary>
        public void RemoveAllShadows()
        {
            for (int i = ShadowCount - 1; i >= 0; i--)
                RemoveShadow(i);
        }

        /// <summary>Enable or disable a shadow without destroying it. Pass -1 for all of them.</summary>
        public void SetShadowActive(bool active, int index = -1)
        {
            MigrateLegacyShadow();

            if (index >= 0)
            {
                Shadow2DInstance one = GetShadow(index);
                if (one?.shadowObject != null)
                    one.shadowObject.SetActive(active);
                return;
            }

            for (int i = 0; i < shadows.Count; i++)
            {
                if (shadows[i]?.shadowObject != null)
                    shadows[i].shadowObject.SetActive(active);
            }
        }

        /// <summary>A shadow's GameObject, for manual positioning. Defaults to the first.</summary>
        public GameObject GetShadowObject(int index = 0) => GetShadow(index)?.shadowObject;

        /// <summary>A shadow's SpriteRenderer, or null if it hasn't been created. Defaults to the first.</summary>
        public SpriteRenderer GetShadowRenderer(int index = 0) => GetShadow(index)?.Renderer;
    }
}
