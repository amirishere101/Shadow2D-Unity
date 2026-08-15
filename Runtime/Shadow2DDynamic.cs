using UnityEngine;

namespace DryFlyStudio
{
    /// <summary>
    /// Shadow for animated objects. Re-syncs with the caster's SpriteRenderer in
    /// LateUpdate (after the Animator has run), so the silhouette follows the
    /// current animation frame.
    /// </summary>
    [AddComponentMenu("DryFly Studio/Shadow 2D (Dynamic)")]
    public class Shadow2DDynamic : Shadow2DBase
    {
        private void LateUpdate()
        {
            UpdateShadow();
        }
    }
}
