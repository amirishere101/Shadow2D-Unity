using UnityEngine;

namespace SleepyHeadStudios
{
    /// <summary>
    /// Shared behaviour for <see cref="Shadow2DStatic"/> and <see cref="Shadow2DDynamic"/>:
    /// owns the shadow GameObject, copies sprite state from the caster, and keeps
    /// sorting in sync. Not attachable directly - use one of the two subclasses.
    /// </summary>
    public abstract class Shadow2DBase : MonoBehaviour
    {
        [Header("Shadow Settings")]
        [Tooltip("Shadow color and transparency")]
        [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.5f);

        [Tooltip("Optional custom silhouette. When set, the shadow draws this sprite instead of copying the caster's. Use for objects with holes, very tall objects, or art with baked-in shading.")]
        [SerializeField] private Sprite overrideSprite;

        [Tooltip("Optional material override for the shadow renderer. Leave empty to use the material from Shadow2DConfig.")]
        [SerializeField] private Material shadowMaterial;

        [Header("Sorting Settings")]
        [Tooltip("Enable if using Y-position sorting - the shadow keeps the caster's sorting layer and order, and its Y position settles depth. Disable to render one sorting order behind the caster instead.")]
        [SerializeField] private bool useYSorting = true;

        [HideInInspector][SerializeField] private GameObject shadowObject;

        private SpriteRenderer sourceRenderer;
        private SpriteRenderer shadowRenderer;

        /// <summary>Shadow tint. Setting it refreshes the shadow immediately.</summary>
        public Color ShadowColor
        {
            get => shadowColor;
            set { shadowColor = value; UpdateShadow(); }
        }

        /// <summary>Custom silhouette sprite. Setting it refreshes the shadow immediately; set null to go back to copying the caster.</summary>
        public Sprite OverrideSprite
        {
            get => overrideSprite;
            set { overrideSprite = value; UpdateShadow(); }
        }

        /// <summary>
        /// The renderer the shadow copies from. The root is checked first; if its
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

            foreach (SpriteRenderer candidate in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (candidate == own || !candidate.enabled || candidate.sprite == null)
                    continue;
                if (shadowObject != null && candidate.gameObject == shadowObject)
                    continue;
                if (candidate.GetComponent<ShadowColorEnforcer>() != null)
                    continue; // never treat a shadow (ours or a child's) as the source

                return candidate;
            }

            // Last resort: keep the root renderer even if disabled or spriteless.
            return own;
        }

        private SpriteRenderer ShadowRenderer
        {
            get
            {
                if (shadowRenderer == null && shadowObject != null)
                    shadowRenderer = shadowObject.GetComponent<SpriteRenderer>();
                return shadowRenderer;
            }
        }

        private void Reset()
        {
            // Pull authoring defaults from the config when the component is first added.
            Shadow2DConfig config = Shadow2DConfig.GetOrCreateDefault();
            if (config != null)
            {
                shadowColor = config.defaultShadowColor;
                useYSorting = config.useYSortingByDefault;
            }
        }

        private void Awake()
        {
            // Repair shadows created before the marker component existed.
            if (shadowObject != null && shadowObject.GetComponent<ShadowColorEnforcer>() == null)
                shadowObject.AddComponent<ShadowColorEnforcer>();
        }

        private void Start()
        {
            UpdateShadow();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && shadowObject != null)
                UpdateShadow();
        }

        /// <summary>
        /// Sync the shadow with the caster: sprite (or override), flips, color, sorting.
        /// Call after changing the caster's sprite yourself on a static shadow.
        /// </summary>
        public void UpdateShadow()
        {
            SpriteRenderer source = GetSourceRenderer();
            SpriteRenderer shadow = ShadowRenderer;
            if (source == null || shadow == null)
                return;

            Sprite sprite = overrideSprite != null ? overrideSprite : source.sprite;
            if (shadow.sprite != sprite) shadow.sprite = sprite;
            if (shadow.flipX != source.flipX) shadow.flipX = source.flipX;
            if (shadow.flipY != source.flipY) shadow.flipY = source.flipY;
            if (shadow.color != shadowColor) shadow.color = shadowColor;

            UpdateSortingOrder(source, shadow);
        }

        private void UpdateSortingOrder(SpriteRenderer source, SpriteRenderer shadow)
        {
            if (shadow.sortingLayerID != source.sortingLayerID)
                shadow.sortingLayerID = source.sortingLayerID;

            // With Y-sorting the shadow shares the caster's order and its Y position
            // settles depth; without it, it sits one order behind.
            int order = useYSorting ? source.sortingOrder : source.sortingOrder - 1;
            if (shadow.sortingOrder != order)
                shadow.sortingOrder = order;
        }

        /// <summary>
        /// Create the shadow GameObject using defaults from Shadow2DConfig.
        /// Returns the created object, or null if one already exists or no source renderer was found.
        /// </summary>
        public GameObject CreateShadow()
        {
            if (shadowObject != null)
            {
                Debug.LogWarning("Shadow already exists! Delete the existing shadow first.", this);
                return null;
            }

            SpriteRenderer source = GetSourceRenderer();
            if (source == null)
            {
                Debug.LogError("No SpriteRenderer found on this GameObject or its children. Shadow2D needs a sprite to copy.", this);
                return null;
            }

            Shadow2DConfig config = Shadow2DConfig.GetOrCreateDefault();

            shadowObject = new GameObject($"{gameObject.name}_Shadow");
            shadowObject.transform.SetParent(transform, false);
            shadowObject.transform.localPosition = config.defaultPosition;
            shadowObject.transform.localRotation = Quaternion.Euler(0f, 0f, config.defaultRotationZ);
            shadowObject.transform.localScale = config.defaultScale;

            shadowRenderer = shadowObject.AddComponent<SpriteRenderer>();
            shadowObject.AddComponent<ShadowColorEnforcer>();

            Material material = shadowMaterial != null ? shadowMaterial : config.ResolveShadowMaterial();
            if (material != null)
                shadowRenderer.sharedMaterial = material;

            ApplyCasterMaterial(source, config);
            UpdateShadow();
            return shadowObject;
        }

        private static void ApplyCasterMaterial(SpriteRenderer source, Shadow2DConfig config)
        {
            if (!config.replaceCasterMaterial)
                return;

            // Only replace the stock sprite material - never stomp a custom one.
            Material current = source.sharedMaterial;
            if (current != null && current.shader != null && current.shader.name != "Sprites/Default")
                return;

            Material caster = config.ResolveCasterMaterial();
            if (caster != null)
                source.sharedMaterial = caster;
        }

        /// <summary>Destroy the shadow GameObject.</summary>
        public void DeleteShadow()
        {
            if (shadowObject == null)
                return;

            if (Application.isPlaying)
                Destroy(shadowObject);
            else
                DestroyImmediate(shadowObject);

            shadowObject = null;
            shadowRenderer = null;
        }

        /// <summary>Enable or disable the shadow without destroying it.</summary>
        public void SetShadowActive(bool active)
        {
            if (shadowObject != null)
                shadowObject.SetActive(active);
        }

        /// <summary>The shadow GameObject, for manual positioning. Null until created.</summary>
        public GameObject GetShadowObject() => shadowObject;

        private void OnDestroy()
        {
            if (shadowObject != null && Application.isPlaying)
                Destroy(shadowObject);
        }
    }
}
