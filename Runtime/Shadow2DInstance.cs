using System;
using System.Collections.Generic;
using UnityEngine;

namespace DryFlyStudio
{
    /// <summary>
    /// One shadow belonging to a Shadow2D component. An object can carry several - two
    /// light directions, or a soft pool under a prop plus a hard silhouette beside it -
    /// and each one owns its own colour, silhouette, material and transform.
    ///
    /// Settings that describe the *caster* rather than an individual shadow (which
    /// renderer to copy, whether to follow the caster's visibility, sorting mode) stay on
    /// the component, because they can only have one answer per object.
    /// </summary>
    [Serializable]
    public class Shadow2DInstance
    {
        [Tooltip("Label for this shadow in the inspector, and the name given to its GameObject.")]
        public string name = "Shadow";

        [Tooltip("Shadow color and transparency")]
        public Color color = new Color(0f, 0f, 0f, 0.5f);

        [Tooltip("Optional custom silhouette. When set, this shadow draws the sprite instead of copying the caster's.")]
        public Sprite overrideSprite;

        [Tooltip("Optional material override for this shadow's renderer. Leave empty to use the material from Shadow2DConfig.")]
        public Material material;

        [Tooltip("Where this shadow's sprite sits, relative to the caster. Always authoritative: dragging the shadow in the Scene view writes back here, and every sync draws it from this.")]
        public Vector3 offset = new Vector3(0f, 0f, 0.01f);

        [Tooltip("Local Z rotation in degrees. This is the angle the light is arriving from.")]
        [Range(-180f, 180f)]
        public float rotationZ = 12.5f;

        [Tooltip("Local scale. Squashing Y is what makes it read as lying on the ground.")]
        public Vector3 scale = new Vector3(1f, 0.9f, 1f);

        [HideInInspector] public GameObject shadowObject;

        /// <summary>
        /// How far the shadow object has been nudged down so its pivot lands on the sort
        /// point. Serialized because the nudge is baked into the transform, and without a
        /// record of it the base position can't be recovered after a reload - the shadow
        /// would sink a little further every time.
        /// </summary>
        [HideInInspector] public Vector3 sortShift;

        [NonSerialized] private SpriteRenderer cachedRenderer;

        // Pivot-corrected sprites, one per source sprite. A dynamic shadow cycles through
        // its animation frames, so this settles at the frame count rather than growing.
        [NonSerialized] private Dictionary<Sprite, Sprite> pivotCache;
        [NonSerialized] private Quaternion cacheRotation;
        [NonSerialized] private Vector3 cacheScale;
        [NonSerialized] private Vector3 cacheShift;
        [NonSerialized] private bool cacheValid;

        /// <summary>
        /// A copy of <paramref name="source"/> whose pivot sits at this shadow's sort
        /// point, creating and caching it if needed. Returns null when there is nothing to
        /// derive from.
        /// </summary>
        internal Sprite GetPivotCorrected(Sprite source, Vector2 normalizedPivot,
            Quaternion rotation, Vector3 scale, Vector3 shift)
        {
            if (source == null || source.texture == null)
                return null;

            // The corrected pivot is a function of the shadow's rotation, squash and the
            // distance it has to reach back to the caster's sort point. A change to any of
            // them invalidates every derived sprite.
            if (!cacheValid || pivotCache == null || cacheRotation != rotation ||
                cacheScale != scale || (cacheShift - shift).sqrMagnitude > 1e-10f)
            {
                ReleaseDerivedSprites();
                pivotCache = new Dictionary<Sprite, Sprite>();
                cacheRotation = rotation;
                cacheScale = scale;
                cacheShift = shift;
                cacheValid = true;
            }

            if (pivotCache.TryGetValue(source, out Sprite derived))
            {
                // The derived sprite captures the source's texture, and a reimport - saving
                // a painted silhouette, or changing import settings - replaces that texture
                // object while leaving the source Sprite reference intact. A cache keyed on
                // the sprite alone would keep handing back a sprite pointing at a destroyed
                // texture, which draws nothing at all.
                if (derived != null && derived.texture == source.texture)
                    return derived;

                DestroySprite(derived);
                pivotCache.Remove(source);
            }

            derived = Sprite.Create(source.texture, source.textureRect, normalizedPivot,
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            derived.name = source.name + "_ShadowPivot";
            derived.hideFlags = HideFlags.HideAndDontSave;

            pivotCache[source] = derived;
            return derived;
        }

        /// <summary>
        /// Destroy the derived sprites. They are created at runtime and owned by nobody
        /// else, so without this they accumulate for the lifetime of the editor.
        /// </summary>
        public void ReleaseDerivedSprites()
        {
            if (pivotCache == null)
                return;

            foreach (Sprite derived in pivotCache.Values)
                DestroySprite(derived);

            pivotCache.Clear();
            pivotCache = null;
            cacheValid = false;
        }

        private static void DestroySprite(Sprite sprite)
        {
            if (sprite == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(sprite);
            else
                UnityEngine.Object.DestroyImmediate(sprite);
        }

        /// <summary>This shadow's SpriteRenderer, or null if it hasn't been created.</summary>
        public SpriteRenderer Renderer
        {
            get
            {
                if (cachedRenderer == null && shadowObject != null)
                    cachedRenderer = shadowObject.GetComponent<SpriteRenderer>();
                return cachedRenderer;
            }
            internal set => cachedRenderer = value;
        }

        /// <summary>Seed appearance and transform from the project defaults.</summary>
        public void ApplyConfigDefaults(Shadow2DConfig config)
        {
            if (config == null)
                return;

            color = config.defaultShadowColor;
            offset = config.defaultPosition;
            rotationZ = config.defaultRotationZ;
            scale = config.defaultScale;
        }

        /// <summary>
        /// Adopt whatever the shadow object currently is, after the user has dragged it.
        ///
        /// The transform holds the sort point rather than the sprite's position - the two
        /// are separated by <see cref="sortShift"/> - so the drag has to be read back
        /// through that shift to recover where the sprite actually ended up.
        /// </summary>
        public void AdoptFromTransform()
        {
            if (shadowObject == null)
                return;

            Transform t = shadowObject.transform;
            offset = t.localPosition - sortShift;
            rotationZ = t.localEulerAngles.z;
            scale = t.localScale;
        }

        /// <summary>
        /// Push rotation and scale onto the shadow object. Position is not set here - it
        /// is owned by the sort-point pass, which places the transform on the caster's
        /// sort point and compensates with the pivot.
        /// </summary>
        public void ApplyTransform()
        {
            if (shadowObject == null)
                return;

            Transform t = shadowObject.transform;

            Quaternion rotation = Quaternion.Euler(0f, 0f, rotationZ);
            if (t.localRotation != rotation)
                t.localRotation = rotation;

            if (t.localScale != scale)
                t.localScale = scale;
        }
    }
}
