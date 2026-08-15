using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DryFlyStudio
{
    /// <summary>
    /// Project-wide defaults for Shadow2D components. The package ships a default
    /// config in its Resources folder; to customize, create your own via
    /// Assets > Create > DryFly Studio > Shadow2D Config and place it in any
    /// Resources folder as "Shadow2DConfig" - it takes precedence over the packaged one.
    /// </summary>
    [CreateAssetMenu(fileName = "Shadow2DConfig", menuName = "DryFly Studio/Shadow2D Config", order = 1)]
    public class Shadow2DConfig : ScriptableObject
    {
        private const string UserConfigPath = "Shadow2DConfig";
        private const string PackagedConfigPath = "Shadow2DConfigDefault";

        /// <summary>
        /// Caster materials the package is willing to replace. Anything else - including
        /// URP's Sprite-Lit-Default - is left alone, because swapping a lit material for an
        /// unlit one silently drops the caster out of 2D lighting.
        /// </summary>
        private static readonly string[] ReplaceableCasterShaders =
        {
            "Sprites/Default",
            "Universal Render Pipeline/2D/Sprite-Unlit-Default",
        };

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

        [Header("Materials (Built-in Render Pipeline)")]
        [Tooltip("Material applied to generated shadow renderers on the built-in render pipeline. Leave empty to fall back to the packaged shadow material.")]
        public Material shadowSpriteMaterial;

        [Tooltip("Material applied to casters that still use Sprites/Default when a shadow is created. Leave empty to fall back to the packaged caster material.")]
        public Material casterMaterial;

        [Header("Materials (Universal Render Pipeline)")]
        [Tooltip("Shadow material used when a Universal Render Pipeline asset is active. Leave empty to fall back to the packaged URP shadow material, which keeps stencil overlap merging working under URP.")]
        public Material urpShadowSpriteMaterial;

        [Tooltip("Caster material used when a Universal Render Pipeline asset is active. Leave empty to fall back to the packaged URP caster material.")]
        public Material urpCasterMaterial;

        [Header("Caster Material Replacement")]
        [Tooltip("Legacy. Replace the caster's material with the caster material above when a shadow is created. This existed to make casters write depth so shadows couldn't cross them; shadows now sort like ordinary sprites and mask out their own caster in the shader, so it is no longer needed and is off by default. Only stock unlit sprite materials are ever replaced (Sprites/Default and URP's Sprite-Unlit-Default); custom materials and URP's Sprite-Lit-Default are always left alone.")]
        public bool replaceCasterMaterial = false;

        private static Shadow2DConfig instance;
        private static Shadow2DConfig packaged;

        /// <summary>
        /// True when a Universal Render Pipeline asset is driving rendering, so the
        /// URP material variants should be preferred. Reads the quality-level override
        /// first, since that is what actually renders the frame.
        /// </summary>
        public static bool IsUniversalPipelineActive
        {
            get
            {
                RenderPipelineAsset pipeline = QualitySettings.renderPipeline != null
                    ? QualitySettings.renderPipeline
                    : GraphicsSettings.defaultRenderPipeline;

                return pipeline != null &&
                       pipeline.GetType().FullName.IndexOf("Universal", StringComparison.Ordinal) >= 0;
            }
        }

        /// <summary>
        /// Static caches survive a play session when Domain Reload is disabled, which
        /// would otherwise leave a destroyed ScriptableObject reference behind and
        /// resolve every material to null on the second play. Clear them per run.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            instance = null;
            packaged = null;
        }

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

                    // Not an asset and nobody owns it; without this it leaks on every
                    // domain reload and shows up as a stray object in the profiler.
                    instance.hideFlags = HideFlags.HideAndDontSave;

                    Debug.LogWarning("No Shadow2DConfig found in any Resources folder. Using built-in defaults. " +
                        "Create one via: Assets > Create > DryFly Studio > Shadow2D Config");
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

        /// <summary>
        /// Shadow material for the active render pipeline: this config's slot first,
        /// then the packaged default's, then the other pipeline's slot as a last resort
        /// so a half-filled config still draws something.
        /// </summary>
        public Material ResolveShadowMaterial()
        {
            bool urp = IsUniversalPipelineActive;
            Shadow2DConfig fallback = GetPackagedDefault();

            return Pick(
                urp ? urpShadowSpriteMaterial : shadowSpriteMaterial,
                fallback != null ? (urp ? fallback.urpShadowSpriteMaterial : fallback.shadowSpriteMaterial) : null,
                urp ? shadowSpriteMaterial : urpShadowSpriteMaterial,
                fallback != null ? (urp ? fallback.shadowSpriteMaterial : fallback.urpShadowSpriteMaterial) : null);
        }

        /// <summary>Caster material for the active render pipeline, resolved like <see cref="ResolveShadowMaterial"/>.</summary>
        public Material ResolveCasterMaterial()
        {
            bool urp = IsUniversalPipelineActive;
            Shadow2DConfig fallback = GetPackagedDefault();

            return Pick(
                urp ? urpCasterMaterial : casterMaterial,
                fallback != null ? (urp ? fallback.urpCasterMaterial : fallback.casterMaterial) : null,
                urp ? casterMaterial : urpCasterMaterial,
                fallback != null ? (urp ? fallback.casterMaterial : fallback.urpCasterMaterial) : null);
        }

        private static Material Pick(params Material[] candidates)
        {
            foreach (Material candidate in candidates)
            {
                if (candidate != null)
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// Whether a caster's current material is one the package is allowed to replace.
        /// A null or shaderless material counts as stock.
        /// </summary>
        public static bool IsReplaceableCasterMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return true;

            foreach (string shaderName in ReplaceableCasterShaders)
            {
                if (material.shader.name == shaderName)
                    return true;
            }
            return false;
        }
    }
}
