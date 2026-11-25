// Unity on a local PC -> Railway over HTTPS; no reverse connection or cloud->localhost required.
// The 21 GalleryHotspot components are discovery-based: no per-hotspot Inspector wiring.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace GalleryRuntime
{
    [Serializable]
    public class JourneyLocalConfig
    {
        public string schemaVersion;
        public string deliveryMode;
        public string exhibitionId;
        public string apiBaseUrl;
        public bool allowInsecureLocalhost;
        public int refreshSeconds = 60;
        public string sceneToken;
    }

    [Serializable]
    public class JourneyArtwork
    {
        public string hotspot_id;
        public string artwork_id;
        public string exhibition_id;
        public string title;
        public string artist;
        public string description;
        public string size_text;
        public string display_price;
        public string image_url;
    }

    [Serializable]
    public class JourneyManifest
    {
        public string schema_version;
        public string exhibition_id;
        public int revision;
        public JourneyArtwork[] artworks;
    }

    /// <summary>
    /// Local build client of the public Railway Journey manifest. The manifest is
    /// published only after Admin verifies every registered hotspot against a
    /// distinct, published artwork with an active SKU.
    /// Selecting a hotspot opens the Railway journey page in the desktop browser.
    /// In PicoXRTest, a connected WebRTC bridge may forward the selection to the
    /// remote browser instead. No admin token is ever present in Unity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GalleryJourneyBridge : MonoBehaviour
    {
        public string configRelativePath = "Gallery/Config/journey.json";
        public bool openBrowserOnSelect = true;
        public bool verboseLogging = true;

        // Optional local WebRTC sender. Returns true only when a remote receiver is connected.
        public static Func<string, string, string, bool> RemoteSelectionHandler;

        private JourneyLocalConfig config;
        private readonly Dictionary<string, JourneyArtwork> byHotspot = new Dictionary<string, JourneyArtwork>(StringComparer.Ordinal);
        private readonly Dictionary<GalleryHotspot, UnityAction> listeners = new Dictionary<GalleryHotspot, UnityAction>();
        private string origin;
        private int revision;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<GalleryJourneyBridge>() != null) return;
            var root = new GameObject("GalleryJourneyRailwayBridge");
            root.AddComponent<GalleryJourneyBridge>();
            DontDestroyOnLoad(root);
        }

        private IEnumerator Start()
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, configRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            string fileUri = path.Contains("://") ? path : new Uri(path).AbsoluteUri;
            using (var file = UnityWebRequest.Get(fileUri))
            {
                file.timeout = 10;
                yield return file.SendWebRequest();
                if (file.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[GalleryJourney] Missing journey.json: " + file.error);
                    yield break;
                }
                try { config = JsonUtility.FromJson<JourneyLocalConfig>(file.downloadHandler.text); }
                catch (Exception e) { Debug.LogError("[GalleryJourney] Configuration JSON: " + e.Message); yield break; }
            }
            if (config == null || string.IsNullOrWhiteSpace(config.exhibitionId) ||
                !TryValidateOrigin(config.apiBaseUrl, config.allowInsecureLocalhost, out origin))
            {
                Debug.LogWarning("[GalleryJourney] Configure exhibitionId and public Railway HTTPS apiBaseUrl first; bridge disabled.");
                yield break;
            }
            int seconds = Mathf.Clamp(config.refreshSeconds <= 0 ? 60 : config.refreshSeconds, 15, 3600);
            while (enabled)
            {
                BindAllHotspots();
                yield return FetchPublishedManifest();
                ApplyPublishedTitles();
                yield return new WaitForSecondsRealtime(seconds);
            }
        }

        private IEnumerator FetchPublishedManifest()
        {
            string url = origin + "/api/journey/exhibitions/" + Uri.EscapeDataString(config.exhibitionId) + "/manifest";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 20;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[GalleryJourney] Manifest unavailable (not published or network error): " + request.responseCode + " " + request.error);
                    yield break;
                }
                JourneyManifest manifest;
                try { manifest = JsonUtility.FromJson<JourneyManifest>(request.downloadHandler.text); }
                catch (Exception e) { Debug.LogWarning("[GalleryJourney] Invalid manifest: " + e.Message); yield break; }
                if (manifest == null || manifest.exhibition_id != config.exhibitionId || manifest.artworks == null || manifest.artworks.Length == 0)
                {
                    Debug.LogWarning("[GalleryJourney] Manifest failed exhibition contract; old mapping retained.");
                    yield break;
                }
                var next = new Dictionary<string, JourneyArtwork>(StringComparer.Ordinal);
                bool valid = true;
                foreach (var item in manifest.artworks)
                {
                    if (item == null || string.IsNullOrEmpty(item.artwork_id) || !IsValidHotspotId(item.hotspot_id) || next.ContainsKey(item.hotspot_id)) { valid = false; break; }
                    next.Add(item.hotspot_id, item);
                }
                if (!valid || next.Count != manifest.artworks.Length)
                {
                    Debug.LogWarning("[GalleryJourney] Invalid or duplicate hotspot IDs in manifest.");
                    yield break;
                }
                byHotspot.Clear();
                foreach (var pair in next) byHotspot.Add(pair.Key, pair.Value);
                revision = manifest.revision;
                if (verboseLogging) Debug.Log("[GalleryJourney] Railway manifest v" + revision + ": " + byHotspot.Count + " artworks ready");
            }
        }

        // Hotspots are registered per exhibition on the business side; the local scene only
        // needs a stable, non-empty identifier that fits the admin identifier rules.
        private static bool IsValidHotspotId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) return false;
            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        private static bool TryValidateOrigin(string raw, bool localhostAllowed, out string baseUrl)
        {
            baseUrl = null;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed) || !string.IsNullOrEmpty(parsed.UserInfo)) return false;
            if (parsed.AbsolutePath != "/" || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment)) return false;
            bool local = parsed.Host == "localhost" || parsed.Host == "127.0.0.1";
            if (parsed.Scheme != Uri.UriSchemeHttps && !(localhostAllowed && local && parsed.Scheme == Uri.UriSchemeHttp)) return false;
            baseUrl = parsed.GetLeftPart(UriPartial.Authority);
            return true;
        }

        private void BindAllHotspots()
        {
            // Dynamic scene zones may add/remove hotspot GameObjects after startup.
            foreach (var spot in FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (spot == null || listeners.ContainsKey(spot)) continue;
                var selected = spot;
                UnityAction action = () => OnHotspotSelected(selected);
                if (spot.onActivate == null) spot.onActivate = new UnityEvent();
                spot.onActivate.AddListener(action);
                listeners.Add(spot, action);
            }
        }

        private void ApplyPublishedTitles()
        {
            foreach (var pair in listeners)
            {
                var spot = pair.Key;
                if (spot == null) continue;
                if (!byHotspot.TryGetValue(spot.hotspotId, out var item)) continue;
#if MOLINK_GALLERY_NO_CONTENT
                // The simpler UnitySplatsGallery GalleryHotspot only has a hotspotId.
#else
                spot.title = item.title;
                spot.artist = item.artist;
                spot.description = item.description;
#endif
            }
        }

        private void OnHotspotSelected(GalleryHotspot spot)
        {
            if (spot == null || config == null || !byHotspot.TryGetValue(spot.hotspotId, out var selected))
            {
                if (verboseLogging) Debug.LogWarning("[GalleryJourney] Selected hotspot has no published mapping: " + (spot == null ? "null" : spot.hotspotId));
                return;
            }
            if (RemoteSelectionHandler != null && RemoteSelectionHandler(config.exhibitionId, spot.hotspotId, selected.artwork_id)) return;
            if (openBrowserOnSelect)
            {
                string target = origin + "/journey/?exhibition_id=" + Uri.EscapeDataString(config.exhibitionId) +
                                "&hotspot_id=" + Uri.EscapeDataString(spot.hotspotId);
                Application.OpenURL(target);
            }
        }

        private void OnDestroy()
        {
            foreach (var pair in listeners) if (pair.Key != null) pair.Key.onActivate.RemoveListener(pair.Value);
            listeners.Clear();
        }
    }
}
