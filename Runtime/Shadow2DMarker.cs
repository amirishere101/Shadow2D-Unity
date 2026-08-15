using UnityEngine;

namespace DryFlyStudio
{
    /// <summary>
    /// Marker component identifying a GameObject as a generated shadow. No fields, no
    /// methods, no runtime behaviour.
    ///
    /// Shadow2D needs it internally: source-renderer resolution walks the caster's
    /// children looking for a sprite to copy, and without a marker it can't tell a
    /// nested shadow apart from a legitimate visual child, so a shadow ends up copying
    /// another shadow.
    ///
    /// It's also the cheapest hook for your own code to skip shadows when it sweeps
    /// renderers - a target highlighter that tints whatever the player is aiming at will
    /// happily tint the shadow too:
    ///
    /// <code>if (renderer.GetComponent&lt;Shadow2DMarker&gt;() != null) continue;</code>
    /// </summary>
    [DisallowMultipleComponent]
    public class Shadow2DMarker : MonoBehaviour
    {
    }
}
