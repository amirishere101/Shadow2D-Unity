using UnityEngine;

namespace SleepyHeadStudios
{
    /// <summary>
    /// Marker component identifying a GameObject as a generated shadow.
    /// No fields, no methods, no runtime behaviour. Other systems (target
    /// highlighters, tint effects) check for it before touching a renderer's
    /// color: if (renderer.GetComponent&lt;ShadowColorEnforcer&gt;() != null) skip it.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShadowColorEnforcer : MonoBehaviour
    {
    }
}
