using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace GalleryRuntime
{
    [Serializable] public sealed class JourneyHotspotDefinition
    {
        public string hotspot_id;
        public Vector3 center_ruf;
        public Vector3 size;
    }
    [Serializable] public sealed class JourneyHotspotManifest { public JourneyHotspotDefinition[] hotspots; }

    /// <summary>Materializes missing interaction boxes from the shared RUF manifest.
    /// Existing scene-authored hotspots take precedence and are never moved.</summary>
    public sealed class GalleryJourneyHotspots : MonoBehaviour
    {
        [Tooltip("展览 profile；设置后本组件的 localHotspotsJsonPath 被 profile.localHotspotsJsonPath 覆盖")]
        public ExhibitionProfile profile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<GalleryJourneyHotspots>() != null) return;
            var host = new GameObject("MoLink_JourneyHotspots");
            host.AddComponent<GalleryJourneyHotspots>();
            DontDestroyOnLoad(host);
        }
        private void Awake() { SceneManager.sceneLoaded += OnSceneLoaded; }
        private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StopAllCoroutines();
            foreach (Transform child in transform) Destroy(child.gameObject);
            StartCoroutine(Start());
        }
        private IEnumerator Start()
        {
            yield return null; // Allow scene-authored hotspot/catalog initialization first.
            string rel = "Gallery/Config/journey_hotspots.json";
            if (profile != null && !string.IsNullOrEmpty(profile.localHotspotsJsonPath))
                rel = profile.localHotspotsJsonPath;
            string file = Path.Combine(Application.streamingAssetsPath, rel).Replace('\\', '/');
            if (!file.Contains("://")) file = new Uri(file).AbsoluteUri;
            using (var request = UnityWebRequest.Get(file))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) yield break;
                JourneyHotspotManifest manifest;
                try { manifest = JsonUtility.FromJson<JourneyHotspotManifest>(request.downloadHandler.text); }
                catch (Exception) { Debug.LogWarning("[Journey] Invalid hotspot manifest"); yield break; }
                var existing = new HashSet<string>(StringComparer.Ordinal);
                foreach (var spot in FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None)) existing.Add(spot.hotspotId);
                if (manifest?.hotspots == null) yield break;
                foreach (var item in manifest.hotspots)
                {
                    if (item == null || string.IsNullOrEmpty(item.hotspot_id) || !existing.Add(item.hotspot_id)) continue;
                    var host = new GameObject("Journey_" + item.hotspot_id);
                    host.transform.SetParent(transform, false);
                    host.transform.position = item.center_ruf;
                    var collider = host.AddComponent<BoxCollider>();
                    collider.isTrigger = true;
                    collider.size = new Vector3(Mathf.Max(0.12f, item.size.x), Mathf.Max(0.12f, item.size.y), Mathf.Max(0.12f, item.size.z));
                    host.AddComponent<GalleryHotspot>().hotspotId = item.hotspot_id;
                }
            }
        }
    }
}
