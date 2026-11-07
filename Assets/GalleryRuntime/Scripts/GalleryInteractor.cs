using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// Player-side interaction driver.
    ///
    /// Every frame a short ray is cast from the view camera to decide which hotspot
    /// the player is aiming at (focus, consumed by GalleryReticle); the "USE" edge
    /// then toggles that hotspot. Pressing USE while aiming at nothing closes
    /// whatever hotspot is currently open.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GalleryInteractor : MonoBehaviour
    {
        [Header("References")]
        public RemoteControlUdpReceiver remoteInput;
        public Camera playerCamera;

        [Header("Ray")]
        public float maxDistance = 3f;
        public LayerMask hotspotMask = ~0;
        [Tooltip("命中点离相机过近视为无效，避免打到自己脚下的触发器")]
        public float minHitDistance = 0.10f;
        [Tooltip("每帧更新聚焦状态；关闭则只在按下「使用」时检测一次")]
        public bool continuousFocus = true;

        /// <summary>当前被瞄准的 hotspot（无则 null）。</summary>
        public GalleryHotspot Focused { get; private set; }

        /// <summary>当前处于激活（信息卡打开）状态的 hotspot。</summary>
        public GalleryHotspot Active => _active;

        private GalleryHotspot _active;

        private void Update()
        {
            // 热点可能因对象卸载而静默关闭，及时清掉本地引用
            if (_active != null && !_active.IsActive)
                _active = null;

            bool pressed = remoteInput != null && remoteInput.InteractPressedThisFrame;

            if (playerCamera == null)
            {
                SetFocused(null);
                if (pressed)
                    CloseActive();
                return;
            }

            if (continuousFocus || pressed)
                SetFocused(Probe());

            if (!pressed)
                return;

            if (Focused != null)
            {
                Focused.Activate();
                _active = Focused.IsActive ? Focused : null;
            }
            else
            {
                CloseActive();
            }
        }

        private GalleryHotspot Probe()
        {
            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (!Physics.Raycast(ray, out var hit, maxDistance, hotspotMask, QueryTriggerInteraction.Collide))
                return null;
            if (hit.distance < minHitDistance)
                return null;
            return hit.collider.GetComponentInParent<GalleryHotspot>();
        }

        private void SetFocused(GalleryHotspot next)
        {
            if (Focused == next)
                return;

            if (Focused != null)
                Focused.SetFocused(false);
            Focused = next;
            if (Focused != null)
                Focused.SetFocused(true);
        }

        private void CloseActive()
        {
            if (_active == null)
                return;

            _active.Deactivate();
            _active = null;
        }

        private void OnDisable()
        {
            SetFocused(null);
        }

        private void OnDrawGizmosSelected()
        {
            if (playerCamera == null)
                return;

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawRay(playerCamera.transform.position, playerCamera.transform.forward * maxDistance);
        }
    }
}
