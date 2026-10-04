using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// Scene-view helper for marking a future artwork zone.
    /// This is only authoring metadata; it does not modify splats at runtime.
    /// </summary>
    public sealed class SplatZoneAuthoring : MonoBehaviour
    {
        public string zoneId = "artwork_001";
        public Vector3 cropSize = new Vector3(2f, 2f, 0.5f);
        public float activateDistance = 4f;
        public float prefetchDistance = 6f;
        public float unloadDistance = 8f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, cropSize);

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireSphere(transform.position, activateDistance);
            Gizmos.DrawWireSphere(transform.position, prefetchDistance);
            Gizmos.DrawWireSphere(transform.position, unloadDistance);
        }
    }
}
