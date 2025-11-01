using UnityEngine;

namespace SleepyHeadStudios
{
    /// <summary>
    /// Dynamic shadow component for animated objects.
    /// Updates every frame to follow sprite animations.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Shadow2DDynamic : MonoBehaviour
    {
        [Header("Shadow Settings")]
        [Tooltip("Shadow color and transparency")]
        [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.5f);

        [Header("Sorting Settings")]
        [Tooltip("Enable if using YPositionSorting - shadow will match parent's sorting layer and order")]
        [SerializeField] private bool useYSorting = true;

        [HideInInspector][SerializeField] private GameObject shadowObject;
        [HideInInspector][SerializeField] private Material shadowMaterial;

        private SpriteRenderer parentSpriteRenderer;
        private SpriteRenderer shadowSpriteRenderer;

        private void Awake()
        {
            parentSpriteRenderer = GetComponent<SpriteRenderer>();

            if (shadowObject != null)
            {
                shadowSpriteRenderer = shadowObject.GetComponent<SpriteRenderer>();

                // Add marker component to identify this as a shadow
                if (shadowObject.GetComponent<ShadowColorEnforcer>() == null)
                {
                    shadowObject.AddComponent<ShadowColorEnforcer>();
                }
            }
        }

        private void Start()
        {
            UpdateShadow();
            UpdateSortingOrder();
        }

        private void LateUpdate()
        {
            if (shadowSpriteRenderer != null && parentSpriteRenderer != null)
            {
                UpdateShadow();

                // Only update sorting if NOT using built in Unity Y-sorting
                if (!useYSorting)
                {
                    UpdateSortingOrder();
                }
            }
        }

        private void OnValidate()
        {
            // Update sorting in editor when useYSorting changes
            if (shadowObject != null && Application.isPlaying)
            {
                UpdateSortingOrder();
            }
        }

        /// <summary>
        /// Update sorting order based on parent sprite and Y-sorting settings
        /// </summary>
        private void UpdateSortingOrder()
        {
            if (shadowSpriteRenderer == null || parentSpriteRenderer == null)
                return;

            shadowSpriteRenderer.sortingLayerID = parentSpriteRenderer.sortingLayerID;

            if (useYSorting)
            {
                // Same sorting order when using Y-sorting (position determines depth)
                shadowSpriteRenderer.sortingOrder = parentSpriteRenderer.sortingOrder;
            }
            else
            {
                // Shadow is 1 sorting order behind when not using Y-sorting
                shadowSpriteRenderer.sortingOrder = parentSpriteRenderer.sortingOrder - 1;
            }
        }

        /// <summary>
        /// Update shadow sprite to match parent (works with animations)
        /// </summary>
        private void UpdateShadow()
        {
            if (shadowSpriteRenderer == null || parentSpriteRenderer == null)
                return;

            // Copy sprite from parent (this makes it work with animations)
            shadowSpriteRenderer.sprite = parentSpriteRenderer.sprite;
            shadowSpriteRenderer.flipX = parentSpriteRenderer.flipX;
            shadowSpriteRenderer.flipY = parentSpriteRenderer.flipY;

            // Set shadow color
            shadowSpriteRenderer.color = shadowColor;
        }

        /// <summary>
        /// Create the shadow GameObject with default settings
        /// </summary>
        public void CreateShadow()
        {
            if (shadowObject != null)
            {
                Debug.LogWarning("Shadow already exists! Delete the existing shadow first.");
                return;
            }

            if (parentSpriteRenderer == null)
            {
                parentSpriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (parentSpriteRenderer == null)
            {
                Debug.LogError("No SpriteRenderer found on parent GameObject! Shadow2DDynamic requires a SpriteRenderer component.");
                return;
            }

            // Get config for default values
            Shadow2DConfig config = Shadow2DConfig.GetOrCreateDefault();

            // Create shadow GameObject
            shadowObject = new GameObject($"{gameObject.name}_Shadow");
            shadowObject.transform.SetParent(transform);

            // Set default transform values from config
            shadowObject.transform.localPosition = config.defaultPosition;
            shadowObject.transform.localRotation = Quaternion.Euler(0, 0, config.defaultRotationZ);
            shadowObject.transform.localScale = config.defaultScale;

            // Add and setup SpriteRenderer
            shadowSpriteRenderer = shadowObject.AddComponent<SpriteRenderer>();
            shadowSpriteRenderer.sprite = parentSpriteRenderer.sprite;
            shadowSpriteRenderer.flipX = parentSpriteRenderer.flipX;
            shadowSpriteRenderer.flipY = parentSpriteRenderer.flipY;
            shadowSpriteRenderer.color = shadowColor;

            // Add marker component
            shadowObject.AddComponent<ShadowColorEnforcer>();

            // Set sorting
            UpdateSortingOrder();

            // Apply shadow shader for stencil masking
            Shader shadowShader = Shader.Find("SleepyHeadStudios/ShadowSprite");
            if (shadowShader != null)
            {
                Material shadowMat = new Material(shadowShader);
                shadowMat.name = "Auto_ShadowMaterial";
                shadowSpriteRenderer.sharedMaterial = shadowMat;
            }

            // Apply parent shader for stencil writing (only if not already custom)
            string currentShader = parentSpriteRenderer.sharedMaterial != null ?
                parentSpriteRenderer.sharedMaterial.shader.name : "Sprites/Default";

            if (currentShader == "Sprites/Default")
            {
                Shader parentShader = Shader.Find("SleepyHeadStudios/SpriteWithShadowBlock");
                if (parentShader != null)
                {
                    Material parentMat = new Material(parentShader);
                    parentMat.name = "Auto_ParentMaterial";
                    parentSpriteRenderer.sharedMaterial = parentMat;
                }
            }

            // Apply custom material if provided (overrides auto shader)
            if (shadowMaterial != null)
            {
                shadowSpriteRenderer.sharedMaterial = shadowMaterial;
            }
        }

        /// <summary>
        /// Delete the shadow GameObject
        /// </summary>
        public void DeleteShadow()
        {
            if (shadowObject != null)
            {
                if (Application.isPlaying)
                    Destroy(shadowObject);
                else
                    DestroyImmediate(shadowObject);

                shadowObject = null;
                shadowSpriteRenderer = null;
            }
        }

        /// <summary>
        /// Enable or disable the shadow
        /// </summary>
        public void SetShadowActive(bool active)
        {
            if (shadowObject != null)
                shadowObject.SetActive(active);
        }

        /// <summary>
        /// Get the shadow GameObject for manual positioning
        /// </summary>
        public GameObject GetShadowObject() => shadowObject;

        private void OnDestroy()
        {
            if (shadowObject != null && Application.isPlaying)
            {
                Destroy(shadowObject);
            }
        }
    }
}
