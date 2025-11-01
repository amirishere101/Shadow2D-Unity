using UnityEngine;

namespace SleepyHeadStudios
{
    /// <summary>
    /// Configuration settings for Shadow2D components.
    /// Create via: Assets > Create > SleepyHead Studios > Shadow2D Config
    /// </summary>
    [CreateAssetMenu(fileName = "Shadow2DConfig", menuName = "SleepyHead Studios/Shadow2D Config", order = 1)]
    public class Shadow2DConfig : ScriptableObject
    {
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

        private static Shadow2DConfig instance;

        /// <summary>
        /// Get the active config, or create a default one if none exists
        /// </summary>
        public static Shadow2DConfig GetOrCreateDefault()
        {
            if (instance == null)
            {
                // Try to find existing config in Resources
                instance = Resources.Load<Shadow2DConfig>("Shadow2DConfig");

                // Create default if not found
                if (instance == null)
                {
                    instance = CreateInstance<Shadow2DConfig>();
                    Debug.LogWarning("No Shadow2DConfig found in Resources folder. Using default settings. " +
                        "Create one via: Assets > Create > SleepyHead Studios > Shadow2D Config");
                }
            }

            return instance;
        }
    }
}
