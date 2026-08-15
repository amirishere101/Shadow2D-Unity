using UnityEngine;

namespace SleepyHeadStudios
{
    /// <summary>
    /// Shadow for animated objects. Re-syncs with the caster's SpriteRenderer in
    /// LateUpdate (after the Animator has run), so the silhouette follows the
    /// current animation frame.
    /// </summary>
    [AddComponentMenu("SleepyHead Studios/Shadow 2D (Dynamic)")]
    public class Shadow2DDynamic : Shadow2DBase
    {
        private void LateUpdate()
        {
            UpdateShadow();
        }
    }
}
