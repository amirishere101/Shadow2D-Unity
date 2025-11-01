using UnityEngine;

namespace SleepyHeadStudios
{
    /// <summary>
    /// Marker component that identifies a GameObject as a shadow sprite.
    /// Other systems (like TargetHighlighter) can check for this component and skip color modifications.
    /// ZERO runtime overhead - just a marker!
    /// </summary>
    [DisallowMultipleComponent]
    public class ShadowColorEnforcer : MonoBehaviour
    {
        // This component is just a marker - no runtime logic needed!
        // Other scripts can check: if (spriteRenderer.GetComponent<ShadowColorEnforcer>() == null)
    }
}
