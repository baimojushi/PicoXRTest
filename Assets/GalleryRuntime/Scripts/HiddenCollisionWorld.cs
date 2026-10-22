using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// Turns an imported collision model into a hidden, static, non-convex
    /// collider world. The mesh remains physically active while all renderers
    /// are disabled.
    /// </summary>
    public sealed class HiddenCollisionWorld : MonoBehaviour
    {
        public bool buildOnAwake = true;
        public bool hideRenderers = true;
        public bool disableShadows = true;

        private void Awake()
        {
            if (buildOnAwake)
                BuildHiddenColliders();
        }

        [ContextMenu("Build Hidden Colliders")]
        public void BuildHiddenColliders()
        {
            var filters = GetComponentsInChildren<MeshFilter>(true);
            int built = 0;

            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null)
                    continue;

                var mc = mf.GetComponent<MeshCollider>();
                if (mc == null)
                    mc = mf.gameObject.AddComponent<MeshCollider>();

                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                mc.enabled = true;
                built++;

                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    if (disableShadows)
                    {
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                    }

                    if (hideRenderers)
                        mr.enabled = false;
                }
            }

            Debug.Log($"[HiddenCollisionWorld] Active MeshColliders: {built}. Renderers hidden={hideRenderers}");
        }

        [ContextMenu("Toggle Collision Renderers")]
        public void ToggleCollisionRenderers()
        {
            foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = !mr.enabled;
        }
    }
}
