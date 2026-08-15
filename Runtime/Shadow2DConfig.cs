using UnityEngine;

namespace SleepyHeadStudios
{
    /// <summary>
    /// Project-wide defaults for Shadow2D components. The package ships a default
    /// config in its Resources folder; to customize, create your own via
    /// Assets > Create > SleepyHead Studios > Shadow2D Config and place it in any
    /// Resources folder as "Shadow2DConfig" - it takes precedence over the packaged one.
    /// </summary>
    [CreateAssetMenu(fileName = "Shadow2DConfig", menuName = "SleepyHead Studios/Shadow2D Config", order = 1)]
    public class Shadow2DConfig : ScriptableObject
    {
        private const string UserConfigPath = "Shadow2DConfig";
        private const string PackagedConfigPath = "Shadow2DConfigDefault";

        [Header("Default Settings")]
        [Tooltip("Enable Y-sorting by default (recommended for 2D games with Y-position sorting)")]
        public bool useYSortingByDefault = true;

        [Header("Shadow Transform")]
        [Tooltip("Default local position offset for shadow")]
        public Vector3 defaultPosition = new Vector3(0f, 0f, 0.01f);

        [Tooltip("Default rotation for shadow (Z-axis)")]
        public float defaultRotationZ = 12.5f;

        [Tooltip("Default scale for shadow")]
        public Vector3 defaultScale = new Vector3(1f, 0.9f, 1f);

        [Header("Shadow Appearance")]
        [Tooltip("Default shadow color and transparency")]
        public Color defaultShadowColor = new Color(0f, 0f, 0f, 0.5f);

        [Header("Materials")]
        [Tooltip("Material applied to generated shadow renderers. Leave empty to fall back to the packaged shadow material.")]
        public Material shadowSpriteMaterial;

        [Tooltip("Material applied to casters that still use Sprites/Default when a shadow is created. Leave empty to fall back to the packaged caster material.")]
        public Material casterMaterial;

        [Tooltip("Replace the caster's material with the caster material above when a shadow is created. Off by default: the caster material is a built-in render pipeline shader, and turning this on in a URP project replaces Sprite-Lit-Default and silently drops the caster out of 2D lighting. Only enable on built-in RP. Materials that aren't Sprites/Default are never touched either way.")]
        public bool replaceCasterMaterial = false;

        private static Shadow2DConfig instance;
        private static Shadow2DConfig packaged;

        /// <summary>
        /// The active config: a user "Shadow2DConfig" in Resources if one exists,
        /// otherwise the packaged default, otherwise built-in values.
        /// </summary>
        public static Shadow2DConfig GetOrCreateDefault()
        {
            if (instance == null)
            {
                instance = Resources.Load<Shadow2DConfig>(UserConfigPath);

                if (instance == null)
                    instance = GetPackagedDefault();

                if (instance == null)
                {
                    // Package Resources folder missing or stripped - fall back to code defaults.
                    instance = CreateInstance<Shadow2DConfig>();
                    Debug.LogWarning("No Shadow2DConfig found in any Resources folder. Using built-in defaults. " +
                        "Create one via: Assets > Create > SleepyHead Studios > Shadow2D Config");
                }
            }

            return instance;
        }

        private static Shadow2DConfig GetPackagedDefault()
        {
            if (packaged == null)
                packaged = Resources.Load<Shadow2DConfig>(PackagedConfigPath);
            return packaged;
        }

        /// <summary>Shadow material to use, falling back to the packaged default's.</summary>
        public Material ResolveShadowMaterial()
        {
            if (shadowSpriteMaterial != null)
                return shadowSpriteMaterial;

            Shadow2DConfig fallback = GetPackagedDefault();
            return fallback != null ? fallback.shadowSpriteMaterial : null;
        }

        /// <summary>Caster material to use, falling back to the packaged default's.</summary>
        public Material ResolveCasterMaterial()
        {
            if (casterMaterial != null)
                return casterMaterial;

            Shadow2DConfig fallback = GetPackagedDefault();
            return fallback != null ? fallback.casterMaterial : null;
        }
    }
}
