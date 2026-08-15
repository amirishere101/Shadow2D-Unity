using UnityEngine;

namespace DryFlyStudio
{
    /// <summary>
    /// Shadow for objects that never animate (props, fences, rocks, decorations).
    /// The shadow is written once in Start and never touched again - no Update,
    /// no LateUpdate, no per-frame cost beyond the extra SpriteRenderer.
    /// If you change the caster's sprite yourself, call <see cref="Shadow2DBase.UpdateShadow"/>.
    /// </summary>
    [AddComponentMenu("DryFly Studio/Shadow 2D (Static)")]
    public class Shadow2DStatic : Shadow2DBase
    {
    }
}
