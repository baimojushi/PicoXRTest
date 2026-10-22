using UnityEngine;
using UnityEngine.Events;

namespace GalleryRuntime
{
    public sealed class GalleryHotspot : MonoBehaviour
    {
        public string hotspotId = "artwork";
        public UnityEvent onActivate;

        public void Activate()
        {
            Debug.Log($"[GalleryHotspot] Activate {hotspotId}");
            onActivate?.Invoke();
        }
    }
}
