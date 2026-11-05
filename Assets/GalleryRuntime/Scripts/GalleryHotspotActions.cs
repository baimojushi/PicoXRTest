<arg_value>using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// Preset hotspot action #1 — debug log.
    /// Bind it in GalleryHotspot.onActivate to verify the whole chain:
    /// player "USE" ray → box collider → GalleryHotspot.Activate() → this method.
    /// </summary>
    public sealed class GalleryHotspotLog : MonoBehaviour
    {
        [TextArea] public string message = "热点被触发";

        private int _count;

        public void Show()
        {
            _count++;
            Debug.Log($"[GalleryHotspotLog] {message}（第 {_count} 次，物体：{name}）", this);
        }
    }

    /// <summary>
    /// Preset hotspot action #2 — teleport the player to a staged viewpoint,
    /// e.g. the best standing spot in front of a painting. Stage an empty
    /// GameObject at the viewpoint, aim it at the painting, and drag it into
    /// the `viewpoint` slot, then bind Teleport() in GalleryHotspot.onActivate.
    /// </summary>
    public sealed class GalleryHotspotTeleport : MonoBehaviour
    {
        [Tooltip("最佳观赏位：把空物体摆在挂画正前方，旋转它朝向挂画，拖到这里")]
        public Transform viewpoint;

        [Tooltip("留空则自动查找场景中的 GalleryPlayerController")]
        public GalleryPlayerController player;

        public void Teleport()
        {
            if (viewpoint == null)
            {
                Debug.LogWarning($"[GalleryHotspotTeleport] {name}: viewpoint 未指定", this);
                return;
            }

            var controller = player != null ? player : FindFirstObjectByType<GalleryPlayerController>();
            if (controller == null)
            {
                Debug.LogWarning($"[GalleryHotspotTeleport] {name}: 场景中没有 GalleryPlayerController", this);
                return;
            }

            Vector3 euler = viewpoint.eulerAngles;
            controller.TeleportTo(viewpoint.position, euler.y, NormalizePitch(euler.x));
            Debug.Log($"[GalleryHotspotTeleport] {name}: 已传送到 {viewpoint.name}", this);
        }

        private static float NormalizePitch(float x)
        {
            return x > 180f ? x - 360f : x;
        }
    }
}
