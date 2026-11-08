using UnityEngine;
using UnityEngine.UI;

namespace GalleryRuntime
{
    /// <summary>
    /// Aim feedback for hotspot interaction: a dot in the centre of the view that
    /// changes colour/size when a hotspot is aimed at (focus) or open (active),
    /// plus an optional hint line. Built at runtime with UGUI — no prefab needed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GalleryReticle : MonoBehaviour
    {
        [Tooltip("留空则自动查找场景中的 GalleryInteractor")]
        public GalleryInteractor interactor;

        [Tooltip("留空则自动解析系统中文字体")]
        public Font font;

        [Header("Dot")]
        public Color focusColor = new Color(0.25f, 1f, 0.65f, 0.95f);
        public Color activeColor = new Color(1f, 0.78f, 0.25f, 1f);
        [Range(1f, 4f)] public float focusScale = 1.9f;

        [Header("Hint")]
        public string focusHint = "按「使用」查看作品信息";
        [Range(8, 48)] public int hintFontSize = 20;
        public Color hintColor = new Color(1f, 1f, 1f, 0.9f);

        private Image _dot;
        private Text _hint;
        private Canvas _canvas;
        private bool _logged;
        private bool _wasFocused;

        private void Start()
        {
            Build();
        }

        private void Update()
        {
            if (_dot == null)
                return;

            // 惰性绑定相机：ScreenSpaceCamera 画布必须在相机就绪后指向 worldCamera，
            // 否则画布不渲染；WebRTC 推流源就是 Camera.main，绑它才会进视频。
            if (_canvas != null && _canvas.worldCamera == null)
                _canvas.worldCamera = ResolveCaptureCamera();

            var target = interactor != null ? interactor : (interactor = FindFirstObjectByType<GalleryInteractor>());
            bool active = target != null && target.Active != null;
            bool focused = target != null && target.Focused != null;

            _dot.color = active ? activeColor : focused ? focusColor : new Color(1f, 1f, 1f, 0.75f);
            _dot.rectTransform.localScale = Vector3.one * (focused || active ? focusScale : 1f);

            if (!_logged)
            {
                _logged = true;
                Debug.Log($"[GalleryReticle] canvas={_canvas.renderMode} camera={(_canvas.worldCamera != null ? _canvas.worldCamera.name : "NULL")} " +
                          $"plane={_canvas.planeDistance} dot={_dot.rectTransform.sizeDelta} alpha={_dot.color.a} " +
                          $"dotActive={_dot.gameObject.activeInHierarchy} interactor={(target != null ? target.name : "NULL")}");
            }

            if (focused != _wasFocused)
            {
                _wasFocused = focused;
                Debug.Log($"[GalleryReticle] focus={(focused && target.Focused != null ? target.Focused.hotspotId : "none")}");
            }

            bool showHint = (focused && !active) || active;
            if (_hint != null)
            {
                _hint.gameObject.SetActive(showHint);
                if (focused && !active)
                    _hint.text = focusHint;
                else if (active)
                    _hint.text = "再按「使用」关闭";
            }
        }

        private void Build()
        {
            var canvasGo = new GameObject("ReticleCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            // ScreenSpaceOverlay canvases are composited after every camera has
            // rendered, so they never reach Camera.CaptureStreamTrack - the WebRTC
            // video would not contain them. ScreenSpaceCamera canvases are rendered
            // as part of that camera's pass and therefore show up in the stream.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = ResolveCaptureCamera();
            canvas.sortingOrder = 100;
            ApplyCanvasDistance(canvas);
            _canvas = canvas;

            var dotGo = new GameObject("Dot", typeof(RectTransform));
            var dotRt = (RectTransform)dotGo.transform;
            dotRt.SetParent(canvasGo.transform, false);
            dotRt.anchorMin = new Vector2(0.5f, 0.5f);
            dotRt.anchorMax = new Vector2(0.5f, 0.5f);
            dotRt.pivot = new Vector2(0.5f, 0.5f);
            dotRt.anchoredPosition = Vector2.zero;
            dotRt.sizeDelta = new Vector2(16f, 16f);

            _dot = dotGo.AddComponent<Image>();
            _dot.sprite = CreateDotSprite();
            // 场景序列化里的 dotSize=10 / alpha=0.28 会覆盖代码默认值，
            // 白点在白墙上等于隐形，这里强制覆盖为可见值。
            _dot.color = new Color(1f, 1f, 1f, 0.75f);

            // Gallery walls are white, so a translucent white dot would be invisible.
            // A dark outline keeps the dot readable on any background.
            var outline = dotGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, 2f);
            _dot.raycastTarget = false;

            var hintGo = new GameObject("Hint", typeof(RectTransform));
            var hintRt = (RectTransform)hintGo.transform;
            hintRt.SetParent(canvasGo.transform, false);
            hintRt.anchorMin = new Vector2(0.5f, 0.5f);
            hintRt.anchorMax = new Vector2(0.5f, 0.5f);
            hintRt.pivot = new Vector2(0.5f, 1f);
            hintRt.anchoredPosition = new Vector2(0f, -34f);
            hintRt.sizeDelta = new Vector2(420f, 40f);

            _hint = hintGo.AddComponent<Text>();
            _hint.font = GalleryUiFont.Resolve(font);
            _hint.fontSize = hintFontSize;
            _hint.alignment = TextAnchor.UpperCenter;
            _hint.color = hintColor;
            _hint.raycastTarget = false;
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint.verticalOverflow = VerticalWrapMode.Overflow;
            _hint.gameObject.SetActive(false);
        }

        private static Sprite CreateDotSprite()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private Camera ResolveCaptureCamera()
        {
            if (_captureCamera != null)
                return _captureCamera;

            var camera = Camera.main;
            if (camera != null)
                _captureCamera = camera;
            return _captureCamera;
        }

        private Camera _captureCamera;

        private static void ApplyCanvasDistance(Canvas canvas)
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
                return;

            canvas.planeDistance = 1f;
        }
    }
}
