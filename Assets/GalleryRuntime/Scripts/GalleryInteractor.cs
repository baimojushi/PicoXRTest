using UnityEngine;

namespace GalleryRuntime
{
    public sealed class GalleryInteractor : MonoBehaviour
    {
        public RemoteControlUdpReceiver remoteInput;
        public Camera playerCamera;
        public float maxDistance = 3f;
        public LayerMask hotspotMask = ~0;

        private void Update()
        {
            if (remoteInput == null || !remoteInput.InteractPressedThisFrame || playerCamera == null)
                return;

            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (Physics.Raycast(ray, out var hit, maxDistance, hotspotMask, QueryTriggerInteraction.Collide))
            {
                var hotspot = hit.collider.GetComponentInParent<GalleryHotspot>();
                if (hotspot != null)
                    hotspot.Activate();
            }
        }
    }
}
