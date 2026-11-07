using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GalleryRuntime
{
    [Serializable]
    public class GalleryArtworkInfo
    {
        public string id;
        public string title;
        public string artist;
        public string year;
        public string description;
    }

    [Serializable]
    public class GalleryArtworkManifest
    {
        public GalleryArtworkInfo[] artworks;
    }

    /// <summary>
    /// Loads Gallery/Config/artworks.json (id -> title/artist/year/description) and
    /// fills matching GalleryHotspot content by hotspotId at startup, so the
    /// auto-detected zones get real titles without hand-editing every hotspot.
    ///
    /// Missing file is not an error: hotspots simply keep their own content and
    /// DisplayTitle falls back to the hotspotId.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GalleryArtworkCatalog : MonoBehaviour
    {
        [Tooltip("相对于 StreamingAssets 的清单路径")]
        public string manifestRelativePath = "Gallery/Config/artworks.json";

        [Tooltip("启动时把清单内容填入场景中的热点")]
        public bool applyOnStart = true;

        [Tooltip("勾选则用清单覆盖热点上已有内容；默认只填空缺字段")]
        public bool overwriteExisting;

        public bool verboseLogging = true;

        private readonly Dictionary<string, GalleryArtworkInfo> _byId =
            new Dictionary<string, GalleryArtworkInfo>(StringComparer.Ordinal);

        public int Count => _byId.Count;

        public bool TryGet(string id, out GalleryArtworkInfo info)
        {
            info = null;
            return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out info);
        }

        private void Start()
        {
            Load();
            if (applyOnStart)
                ApplyToHotspots();
        }

        /// <summary>读取清单（可重复调用以热更新）。返回成功读入的条目数。</summary>
        public int Load()
        {
            _byId.Clear();

            string path = ResolvePath(manifestRelativePath);
            if (!File.Exists(path))
            {
                Log($"清单不存在（跳过）：{path}");
                return 0;
            }

            GalleryArtworkManifest manifest = null;
            try
            {
                manifest = JsonUtility.FromJson<GalleryArtworkManifest>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[GalleryArtworkCatalog] 解析失败 {path}: {e.Message}");
                return 0;
            }

            if (manifest?.artworks == null)
                return 0;

            foreach (var info in manifest.artworks)
            {
                if (info == null || string.IsNullOrEmpty(info.id))
                    continue;
                _byId[info.id] = info;
            }

            Log($"载入 {_byId.Count} 条作品内容：{path}");
            return _byId.Count;
        }

        /// <summary>把清单内容填入场景中 hotspotId 匹配的热点。返回命中数量。</summary>
        public int ApplyToHotspots()
        {
            var spots = FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int applied = 0;

            foreach (var spot in spots)
            {
                if (spot == null || !TryGet(spot.hotspotId, out var info))
                    continue;

                if (overwriteExisting || string.IsNullOrWhiteSpace(spot.title))
                    spot.title = info.title;
                if (overwriteExisting || string.IsNullOrWhiteSpace(spot.artist))
                    spot.artist = info.artist;
                if (overwriteExisting || string.IsNullOrWhiteSpace(spot.year))
                    spot.year = info.year;
                if (overwriteExisting || string.IsNullOrWhiteSpace(spot.description))
                    spot.description = info.description;

                applied++;
            }

            if (verboseLogging)
                Debug.Log($"[GalleryArtworkCatalog] 内容应用到 {applied} 个热点（场景热点总数 {spots.Length}）");

            return applied;
        }

        private static string ResolvePath(string relativePath)
        {
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Application.streamingAssetsPath, normalized);
        }

        private void Log(string message)
        {
            if (verboseLogging)
                Debug.Log($"[GalleryArtworkCatalog] {message}");
        }
    }
}
