using UnityEngine;
using UnityEngine.UI;

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

    /// <summary>
    /// Preset hotspot action #3 — world-space artwork info card.
    ///
    /// Bind Show() to GalleryHotspot.onActivate and Hide() to GalleryHotspot.onDeactivate.
    /// The card is built at runtime (UGUI world-space canvas), so no prefab or
    /// pre-placed scene object is required. Content comes from the GalleryHotspot on
    /// the same object (title/artist/year/description), which GalleryArtworkCatalog
    /// fills from artworks.json.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GalleryHotspotInfoCard : MonoBehaviour
    {
        private const float PixelsPerMeter = 1000f;

        [Header("Content source")]
        [Tooltip("留空则自动取同物体上的 GalleryHotspot")]
        public GalleryHotspot hotspot;

        [Header("Layout")]
        [Tooltip("留空则自动取 Camera.main")]
        public Camera targetCamera;
        [Tooltip("卡片浮在视线前方多远（米）")]
        public float distance = 2.0f;
        [Tooltip("卡片高度（米）")]
        public float cardHeight = 0.62f;
        [Tooltip("宽 : 高 比例")]
        public Vector2 cardAspect = new Vector2(16f, 10f);
        [Tooltip("相对视线中心下移（米）")]
        public float verticalOffset = -0.08f;

        [Header("Text")]
        [Tooltip("留空则自动解析系统中文字体；打包到 Android/Pico 建议指定带中文字形的 Font 资源")]
        public Font font;
        [Range(0.05f, 0.2f)] public float titleSizeRatio = 0.105f;
        [Range(0.03f, 0.12f)] public float subtitleSizeRatio = 0.058f;
        [Range(0.03f, 0.12f)] public float bodySizeRatio = 0.055f;

        [Header("Style")]
        public Color panelColor = new Color(0.05f, 0.06f, 0.08f, 0.90f);
        public Color accentColor = new Color(0.25f, 1f, 0.65f, 0.95f);
        public Color titleColor = Color.white;
        public Color subtitleColor = new Color(0.70f, 0.78f, 0.86f, 1f);
        public Color bodyColor = new Color(0.86f, 0.88f, 0.92f, 1f);
        public float fadeSeconds = 0.15f;

        [Header("Behaviour")]
        [Tooltip(">0：自动关闭秒数；0：一直显示到再次按下「使用」")]
        public float autoHideSeconds = 0f;

        private GameObject _root;
        private CanvasGroup _group;
        private RectTransform _rect;
        private Text _title;
        private Text _subtitle;
        private Text _body;
        private float _showTime;
        private bool _visible;

        public bool IsVisible => _visible;

        private void Awake()
        {
            if (hotspot == null)
                hotspot = GetComponent<GalleryHotspot>();
        }

        public void Show()
        {
            if (hotspot == null)
                hotspot = GetComponent<GalleryHotspot>();

            Build();
            RefreshContent();

            _showTime = Time.unscaledTime;
            _visible = true;
            _root.SetActive(true);
            PlaceInFrontOfViewer();
        }

        public void Hide()
        {
            _visible = false;
            if (_root != null)
                _root.SetActive(false);
        }

        private void Update()
        {
            if (!_visible || _root == null)
                return;

            if (fadeSeconds > 0f && _group != null)
                _group.alpha = Mathf.Clamp01((Time.unscaledTime - _showTime) / fadeSeconds);

            FaceViewer();

            if (autoHideSeconds > 0f && Time.unscaledTime - _showTime >= autoHideSeconds)
            {
                // 走热点自己的关闭路径，保持「激活状态」与卡片可见性一致
                if (hotspot != null && hotspot.IsActive)
                    hotspot.Deactivate();
                else
                    Hide();
            }
        }

        private void Build()
        {
            if (_root != null)
                return;

            font = GalleryUiFont.Resolve(font);

            float h = Mathf.Max(0.1f, cardHeight) * PixelsPerMeter;
            float w = h * (cardAspect.x / Mathf.Max(0.01f, cardAspect.y));

            _root = new GameObject("HotspotInfoCard");
            _root.transform.SetParent(transform, false);
            _root.transform.localScale = Vector3.one / PixelsPerMeter;

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;

            _group = _root.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _group.alpha = 1f;

            _rect = _root.GetComponent<RectTransform>();
            _rect.sizeDelta = new Vector2(w, h);

            // 顶部锚定（y=1）时：offsetMax.y 为负=上边距，offsetMin.y 更负=下边缘
            float pad = w * 0.05f;

            CreateImage("Background", _rect, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, panelColor);

            CreateImage("Accent", _rect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -h * 0.034f), Vector2.zero, accentColor);

            _title = CreateText("Title", _rect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(pad, -h * 0.245f), new Vector2(-pad, -h * 0.045f),
                h * titleSizeRatio, titleColor, FontStyle.Bold, TextAnchor.UpperLeft);

            _subtitle = CreateText("Subtitle", _rect, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(pad, -h * 0.355f), new Vector2(-pad, -h * 0.245f),
                h * subtitleSizeRatio, subtitleColor, FontStyle.Normal, TextAnchor.UpperLeft);

            _body = CreateText("Body", _rect, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(pad, h * 0.05f), new Vector2(-pad, -h * 0.40f),
                h * bodySizeRatio, bodyColor, FontStyle.Normal, TextAnchor.UpperLeft);

            _root.SetActive(false);
        }

        private Image CreateImage(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, float fontSize, Color color, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize));
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.1f;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        private void RefreshContent()
        {
            if (_title == null)
                return;

            string title = hotspot != null ? hotspot.DisplayTitle : name;
            _title.text = title;

            string subtitle = JoinSubtitle(hotspot != null ? hotspot.artist : null, hotspot != null ? hotspot.year : null);
            _subtitle.text = subtitle;
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));

            string body = hotspot != null ? hotspot.description : null;
            _body.text = body;
            _body.gameObject.SetActive(!string.IsNullOrEmpty(body));
        }

        private static string JoinSubtitle(string artist, string year)
        {
            bool hasArtist = !string.IsNullOrWhiteSpace(artist);
            bool hasYear = !string.IsNullOrWhiteSpace(year);
            if (hasArtist && hasYear) return $"{artist}  ·  {year}";
            if (hasArtist) return artist;
            if (hasYear) return year;
            return string.Empty;
        }

        private Camera ResolveCamera()
        {
            if (targetCamera != null)
                return targetCamera;
            targetCamera = Camera.main;
            return targetCamera;
        }

        private void PlaceInFrontOfViewer()
        {
            var camera = ResolveCamera();
            if (camera == null)
                return;

            var t = camera.transform;
            _root.transform.position = t.position + t.forward * distance + Vector3.up * verticalOffset;
            FaceViewer();
        }

        private void FaceViewer()
        {
            var camera = ResolveCamera();
            if (camera == null)
                return;

            Vector3 to = _root.transform.position - camera.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-6f)
                return;

            _root.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        private void OnDestroy()
        {
            if (_root != null)
                Destroy(_root);
        }
    }
}
