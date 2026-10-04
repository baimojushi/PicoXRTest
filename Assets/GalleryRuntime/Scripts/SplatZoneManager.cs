using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gsplat;
using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// Server-first spatial Gaussian streaming manager.
    ///
    /// Intended asset contract:
    ///   - globalBasePath: low-resolution scene base, always resident.
    ///   - zone.lowPath: optional zone-local LOW asset.
    ///   - zone.highPath: zone-local HIGH asset loaded near the player.
    ///
    /// Production recommendation:
    ///   global base should exclude artwork ROI splats, while zone LOW/HIGH keep
    ///   the exact same scene coordinates. Then switching is a clean LOW<->HIGH
    ///   renderer toggle without long-lived overlap.
    /// </summary>
    public sealed class SplatZoneManager : MonoBehaviour
    {
        [Header("References")]
        public Transform observer;
        public Transform visualRoot;

        [Header("Manifest")]
        public string manifestRelativePath = "Gallery/Config/zones.json";

        [Header("Runtime Budget")]
        [Min(1)] public int maxHighResidentZones = 2;
        [Range(0.05f, 1f)] public float evaluationInterval = 0.15f;
        public bool verboseLogging = true;

        private sealed class LoadedSplat
        {
            public GameObject gameObject;
            public GsplatRenderer renderer;
            public GsplatAsset asset;
        }

        private sealed class ZoneRuntime
        {
            public SplatZoneDefinition def;
            public LoadedSplat low;
            public LoadedSplat high;
            public bool highLoading;
            public bool showingHigh;
            public float distance;
            public float lastUsedTime;
        }

        private LoadedSplat _globalBase;
        private readonly List<ZoneRuntime> _zones = new List<ZoneRuntime>();
        private float _nextEvaluation;

        private IEnumerator Start()
        {
            if (observer == null && Camera.main != null)
                observer = Camera.main.transform;

            if (visualRoot == null)
                visualRoot = transform;

            var manifestPath = ResolveStreamingPath(manifestRelativePath);
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[SplatZoneManager] Manifest not found: {manifestPath}");
                yield break;
            }

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonUtility.FromJson<SplatZoneManifest>(json);
            if (manifest == null)
            {
                Debug.LogError("[SplatZoneManager] Could not parse zone manifest.");
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(manifest.globalBasePath))
            {
                yield return null;
                _globalBase = LoadSplat(manifest.globalBasePath, "GS_Global_Base", true);
            }

            if (manifest.zones != null)
            {
                foreach (var def in manifest.zones)
                {
                    if (def == null || !def.enabled)
                        continue;

                    var zr = new ZoneRuntime { def = def };
                    _zones.Add(zr);

                    if (!string.IsNullOrWhiteSpace(def.lowPath))
                    {
                        yield return null;
                        zr.low = LoadSplat(def.lowPath, $"GS_{def.id}_LOW", true);
                    }
                }
            }

            Log($"Ready. zones={_zones.Count}, maxHighResident={maxHighResidentZones}");
        }

        private void Update()
        {
            if (observer == null || Time.unscaledTime < _nextEvaluation)
                return;

            _nextEvaluation = Time.unscaledTime + evaluationInterval;
            EvaluateZones();
        }

        private void EvaluateZones()
        {
            foreach (var z in _zones)
                z.distance = Vector3.Distance(observer.position, z.def.center.ToVector3());

            var desiredHigh = _zones
                .Where(z => z.distance <= z.def.prefetchDistance)
                .OrderBy(z => z.distance)
                .Take(maxHighResidentZones)
                .ToHashSet();

            foreach (var z in _zones)
            {
                if (desiredHigh.Contains(z) && z.high == null && !z.highLoading &&
                    !string.IsNullOrWhiteSpace(z.def.highPath))
                {
                    StartCoroutine(LoadHighCoroutine(z));
                }

                if (z.high != null)
                {
                    if (!z.showingHigh && z.distance <= z.def.activateDistance)
                        ShowHigh(z);
                    else if (z.showingHigh && z.distance >= z.def.deactivateDistance)
                        ShowLow(z);

                    bool outsideUnload = z.distance >= z.def.unloadDistance;
                    bool overBudgetCandidate = !desiredHigh.Contains(z) && !z.showingHigh;
                    if (outsideUnload || overBudgetCandidate)
                        UnloadHigh(z);
                }
            }

            EnforceHardBudget();
        }

        private IEnumerator LoadHighCoroutine(ZoneRuntime z)
        {
            z.highLoading = true;
            // Let current frame finish before parsing/loading the zone.
            yield return null;

            try
            {
                z.high = LoadSplat(z.def.highPath, $"GS_{z.def.id}_HIGH", false);
                z.lastUsedTime = Time.unscaledTime;
                Log($"Prefetched HIGH {z.def.id}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SplatZoneManager] Failed loading {z.def.id}: {e}");
            }
            finally
            {
                z.highLoading = false;
            }
        }

        private LoadedSplat LoadSplat(string relativePath, string objectName, bool initiallyVisible)
        {
            string absolutePath = ResolveStreamingPath(relativePath);
            Log($"LoadSplat: relative={relativePath}, absolute={absolutePath}, exists={File.Exists(absolutePath)}");
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException($"Splat asset not found: {absolutePath}");

            try
            {
                // UnitySplats runtime objects must be created/assigned on Unity's main thread.
                GsplatAsset asset = GsplatRuntimeLoader.LoadFile(
                    absolutePath,
                    Gsplat.CompressionMode.Spark);
                Log($"LoadSplat: LoadFile succeeded, asset={asset?.name ?? "null"}");

                var go = new GameObject(objectName);
                go.transform.SetParent(visualRoot, false);

                // Important for alignment: create the renderer manually with identity transform.
                // Do not depend on the editor drag/drop helper's convenience rotation.
                var renderer = go.AddComponent<GsplatRenderer>();
                renderer.enabled = initiallyVisible;
                renderer.GsplatAsset = asset;
                Log($"LoadSplat: Created {objectName} with renderer.enabled={renderer.enabled}, asset={asset?.name ?? "null"}");

                return new LoadedSplat
                {
                    gameObject = go,
                    renderer = renderer,
                    asset = asset
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"[SplatZoneManager] LoadSplat failed for {relativePath}: {e}");
                throw;
            }
        }

        private void ShowHigh(ZoneRuntime z)
        {
            if (z.high == null)
                return;

            if (z.low != null)
                z.low.renderer.enabled = false;

            z.high.renderer.enabled = true;
            z.showingHigh = true;
            z.lastUsedTime = Time.unscaledTime;
            Log($"HIGH ON {z.def.id} d={z.distance:F2}m");
        }

        private void ShowLow(ZoneRuntime z)
        {
            if (z.high != null)
                z.high.renderer.enabled = false;

            if (z.low != null)
                z.low.renderer.enabled = true;

            z.showingHigh = false;
            Log($"LOW ON {z.def.id} d={z.distance:F2}m");
        }

        private void EnforceHardBudget()
        {
            var loaded = _zones.Where(z => z.high != null).ToList();
            if (loaded.Count <= maxHighResidentZones)
                return;

            foreach (var z in loaded
                         .OrderBy(z => z.showingHigh ? 1 : 0)
                         .ThenByDescending(z => z.distance)
                         .ThenBy(z => z.lastUsedTime)
                         .ToList())
            {
                if (loaded.Count <= maxHighResidentZones)
                    break;

                if (z.showingHigh)
                    continue;

                UnloadHigh(z);
                loaded.Remove(z);
            }
        }

        private void UnloadHigh(ZoneRuntime z)
        {
            if (z.high == null)
                return;

            if (z.showingHigh)
                ShowLow(z);

            z.high.renderer.GsplatAsset = null;
            Destroy(z.high.gameObject);
            Destroy(z.high.asset);
            z.high = null;
            z.showingHigh = false;
            Log($"HIGH UNLOAD {z.def.id}");
        }

        private string ResolveStreamingPath(string relativePath)
        {
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Application.streamingAssetsPath, normalized);
        }

        private void Log(string msg)
        {
            if (verboseLogging)
                Debug.Log($"[SplatZoneManager] {msg}");
        }

        private void OnDestroy()
        {
            foreach (var z in _zones)
            {
                if (z.high != null) UnloadLoaded(z.high);
                if (z.low != null) UnloadLoaded(z.low);
            }

            if (_globalBase != null)
                UnloadLoaded(_globalBase);
        }

        private static void UnloadLoaded(LoadedSplat s)
        {
            if (s == null) return;
            if (s.renderer != null) s.renderer.GsplatAsset = null;
            if (s.gameObject != null) Destroy(s.gameObject);
            if (s.asset != null) Destroy(s.asset);
        }
    }
}
