using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GalleryRuntime
{
    [Serializable] public sealed class JourneyConfig
    {
        public bool enabled = true;
        public string apiBaseUrl;
        public string exhibitionId;
        public string deliveryMode = "browser";
        public bool allowInsecureLocalhost;
        public int requestTimeoutSeconds = 15;
    }
    [Serializable] public sealed class JourneyArtwork
    {
        public string exhibition_id;
        public string hotspot_id;
        public string artwork_id;
        public string title;
        public string artist;
        public string description;
        public string size_text;
        public string image_url;
    }
    [Serializable] public sealed class JourneyManifest { public JourneyArtwork[] artworks; }
    [Serializable] public sealed class JourneySelection
    {
        public string type = "molink.artwork.selected";
        public string schema_version = "molink.gallery-event/1.0";
        public string exhibition_id;
        public string hotspot_id;
        public string artwork_id;
        public string title;
        public string detail_url;
    }

    /// <summary>
    /// Public-catalog-only bridge. Platform, GPU and payment credentials must
    /// never be stored in StreamingAssets or sent through the data channel.
    /// One Unity player process corresponds to one remotely controlled session.
    /// </summary>
    public sealed class GalleryJourneyBridge : MonoBehaviour
    {
        public static event Func<string, bool> RemoteSelection;
        private static GalleryJourneyBridge instance;
        private JourneyConfig config;
        private readonly Dictionary<string, JourneyArtwork> byHotspot = new Dictionary<string, JourneyArtwork>(StringComparer.Ordinal);
        private string pendingHotspot;
        private bool ready;
        private float nextBindingRefresh;
        private float lastSelectionAt = -10f;
        public bool IsReady => ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; RemoteSelection = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            var host = new GameObject("MoLink_JourneyBridge");
            instance = host.AddComponent<GalleryJourneyBridge>();
            DontDestroyOnLoad(host);
        }

        private IEnumerator Start()
        {
            string configPath = Path.Combine(Application.streamingAssetsPath, "Gallery/Config/journey.json").Replace('\\', '/');
            if (!configPath.Contains("://")) configPath = new Uri(configPath).AbsoluteUri;
            using (var request = UnityWebRequest.Get(configPath))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { Debug.LogWarning("[Journey] Configuration could not be loaded"); yield break; }
                try { config = JsonUtility.FromJson<JourneyConfig>(request.downloadHandler.text); }
                catch (Exception) { Debug.LogWarning("[Journey] Invalid configuration"); yield break; }
            }
            if (config == null || !config.enabled) yield break;
            if (string.IsNullOrWhiteSpace(config.exhibitionId) || !IsSafeBaseUrl(config.apiBaseUrl, config.allowInsecureLocalhost))
            { Debug.LogWarning("[Journey] Configure a public HTTPS business URL and exhibitionId in journey.json"); yield break; }
            if (config.deliveryMode != "browser" && config.deliveryMode != "remote")
            { Debug.LogError("[Journey] deliveryMode must be browser or remote"); yield break; }
            while (enabled)
            {
                yield return LoadCatalog();
                yield return new WaitForSecondsRealtime(120f);
            }
        }

        public static bool IsSafeBaseUrl(string value, bool allowLoopback)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/") return false;
            return uri.Scheme == "https" || (allowLoopback && uri.Scheme == "http" && uri.IsLoopback);
        }

        public static string DetailUrl(string baseUrl, string exhibitionId, string hotspotId, string artworkId)
        {
            return baseUrl.TrimEnd('/') + "/journey/?exhibition_id=" + Uri.EscapeDataString(exhibitionId) +
                "&hotspot_id=" + Uri.EscapeDataString(hotspotId) + "&artwork_id=" + Uri.EscapeDataString(artworkId);
        }

        private IEnumerator LoadCatalog()
        {
            string url = config.apiBaseUrl.TrimEnd('/') + "/api/journey/catalog?exhibition_id=" + Uri.EscapeDataString(config.exhibitionId);
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = Mathf.Clamp(config.requestTimeoutSeconds, 3, 30);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    ready = false;
                    Debug.LogWarning("[Journey] Catalog unavailable; selection is disabled until refresh");
                    yield break;
                }
                JourneyManifest manifest;
                try { manifest = JsonUtility.FromJson<JourneyManifest>(request.downloadHandler.text); }
                catch (Exception) { ready = false; Debug.LogWarning("[Journey] Invalid catalog response"); yield break; }
                byHotspot.Clear();
                if (manifest?.artworks != null)
                    foreach (var artwork in manifest.artworks)
                    {
                        if (artwork == null || artwork.exhibition_id != config.exhibitionId ||
                            string.IsNullOrWhiteSpace(artwork.hotspot_id) || string.IsNullOrWhiteSpace(artwork.artwork_id)) continue;
                        if (byHotspot.ContainsKey(artwork.hotspot_id))
                        { ready = false; byHotspot.Clear(); Debug.LogError("[Journey] Duplicate hotspot mapping; catalog rejected"); yield break; }
                        byHotspot.Add(artwork.hotspot_id, artwork);
                    }
                ready = true;
                BindSceneHotspots();
                if (!string.IsNullOrEmpty(pendingHotspot)) { string pending = pendingHotspot; pendingHotspot = null; Select(pending); }
            }
        }

        private void Update()
        {
            if (!ready || Time.unscaledTime < nextBindingRefresh) return;
            nextBindingRefresh = Time.unscaledTime + 2f;
            BindSceneHotspots();
        }

        private void BindSceneHotspots()
        {
            foreach (var spot in FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!byHotspot.TryGetValue(spot.hotspotId, out var artwork)) continue;
                spot.exhibitionId = artwork.exhibition_id;
                spot.artworkId = artwork.artwork_id;
                spot.title = artwork.title;
                spot.artist = artwork.artist;
                spot.description = artwork.description;
            }
        }

        public static void NotifyActivated(GalleryHotspot hotspot)
        {
            if (hotspot == null) return;
            if (instance == null) Bootstrap();
            instance.Select(hotspot.hotspotId);
        }

        private void Select(string hotspotId)
        {
            if (config != null && !config.enabled) return;
            if (!ready) { pendingHotspot = hotspotId; return; }
            if (Time.unscaledTime - lastSelectionAt < 0.35f) return;
            if (!byHotspot.TryGetValue(hotspotId, out var artwork))
            { Debug.LogWarning("[Journey] No business artwork is bound to hotspot " + hotspotId); return; }
            lastSelectionAt = Time.unscaledTime;
            var message = new JourneySelection { exhibition_id = artwork.exhibition_id, hotspot_id = hotspotId,
                artwork_id = artwork.artwork_id, title = artwork.title,
                detail_url = DetailUrl(config.apiBaseUrl, artwork.exhibition_id, hotspotId, artwork.artwork_id) };
            string serialized = JsonUtility.ToJson(message);
            bool delivered = false;
            if (RemoteSelection != null)
                foreach (Func<string, bool> sink in RemoteSelection.GetInvocationList())
                    try { delivered |= sink(serialized); }
                    catch (Exception) { Debug.LogWarning("[Journey] Remote selection delivery failed"); }
            if (config.deliveryMode == "remote")
            {
                if (!delivered) Debug.LogWarning("[Journey] No connected browser received the selection");
                return; // Never open a browser on the streaming server.
            }
            if (!delivered) Application.OpenURL(message.detail_url);
        }

        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
