using UnityEngine;
using UnityEngine.Events;

namespace GalleryRuntime
{
    /// <summary>
    /// A gallery interaction hotspot.
    ///
    /// The BoxCollider(IsTrigger) on this object is ONLY an interaction volume — it
    /// carries no splat data. The artwork Gaussians live in the pre-sliced PLY that
    /// SplatZoneManager streams by distance (zones.json -> highPath).
    ///
    /// Interaction model:
    ///   aim  -> SetFocused(true/false)   (short ray from GalleryInteractor)
    ///   USE  -> Activate()               (toggle: press again to Deactivate)
    ///
    /// Bind responses (info card, teleport, log, ...) in the Inspector:
    ///   onFocusEnter / onFocusExit / onActivate / onDeactivate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GalleryHotspot : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("唯一 ID，对应 zones.json 里的 zone id（如 artwork_001）")]
        public string hotspotId = "artwork";

        [Header("Content")]
        [Tooltip("作品标题；留空则显示 hotspotId。可由 GalleryArtworkCatalog 从 artworks.json 自动填充")]
        public string title;
        [Tooltip("作者；可由 artworks.json 自动填充")]
        public string artist;
        public string year;
        [TextArea(2, 8)]
        public string description;

        [Header("Events")]
        public UnityEvent onFocusEnter;
        public UnityEvent onFocusExit;
        public UnityEvent onActivate;
        public UnityEvent onDeactivate;

        /// <summary>当前是否被玩家视线瞄准。</summary>
        public bool IsFocused { get; private set; }

        /// <summary>当前是否已激活（信息卡打开中）。</summary>
        public bool IsActive { get; private set; }

        /// <summary>被激活过的次数（调试/统计用）。</summary>
        public int ActivateCount { get; private set; }

        public string DisplayTitle => string.IsNullOrWhiteSpace(title) ? hotspotId : title;

        public void SetFocused(bool focused)
        {
            if (IsFocused == focused)
                return;

            IsFocused = focused;
            if (focused) onFocusEnter?.Invoke();
            else onFocusExit?.Invoke();
        }

        /// <summary>Press-to-toggle: activating an already active hotspot closes it.</summary>
        public void Activate()
        {
            if (IsActive)
            {
                Deactivate();
                return;
            }

            IsActive = true;
            ActivateCount++;
            Debug.Log($"[GalleryHotspot] Activate {hotspotId}（第 {ActivateCount} 次）", this);
            onActivate?.Invoke();
        }

        public void Deactivate()
        {
            if (!IsActive)
                return;

            IsActive = false;
            Debug.Log($"[GalleryHotspot] Deactivate {hotspotId}", this);
            onDeactivate?.Invoke();
        }

        private void OnDisable()
        {
            // 热点被卸载/禁用时收尾，避免信息卡残留在场景里
            if (IsActive)
                Deactivate();
            SetFocused(false);
        }
    }
}
