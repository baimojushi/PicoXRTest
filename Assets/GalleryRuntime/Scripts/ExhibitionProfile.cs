using System.Collections.Generic;
using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// 一场展览的全部可复用配置。每场展览一份 profile 资产；
    /// 新展览 = 复制 profile + 填新字段，代码不动。
    /// 点云/碰撞体/热点清单路径都是相对 StreamingAssets。
    /// </summary>
    [CreateAssetMenu(fileName = "NewExhibitionProfile", menuName = "Gallery/Exhibition Profile", order = 100)]
    [System.Serializable]
    public class ExhibitionProfile : ScriptableObject
    {
        [System.Serializable]
        public class LodLevel
        {
            [Tooltip("相对 StreamingAssets 的 ply 路径")]
            public string relativePath;
            [Tooltip("相机距离 ≤ 该值(米)时使用本层")]
            public float maxDistance = 9999f;
        }

        [Header("业务端（Railway）")]
        [Tooltip("业务端展览 ID（/admin/exhibitions 创建后填入）")]
        public string exhibitionId;
        [Tooltip("业务端 HTTPS 域名，如 https://www.molink.art")]
        public string apiBaseUrl = "https://www.molink.art";
        [Tooltip("JOURNEY_SCENE_TOKEN（服务级，跨展复用）")]
        public string sceneToken;
        [Tooltip("manifest 轮询间隔（秒）")]
        public int refreshSeconds = 60;
        [Tooltip("deliveryMode: local-browser 或 remote")]
        public string deliveryMode = "local-browser";

        [Header("点云")]
        [Tooltip("LOD 层级，按距离从近到远排列")]
        public List<LodLevel> lodLevels = new List<LodLevel>();

        [Header("碰撞体")]
        [Tooltip("相对 StreamingAssets 的 collision.ply 路径")]
        public string collisionPath = "QianFan/collision.ply";

        [Header("热点")]
        [Tooltip("热点 ID 前缀（导出 zones / 命名 GalleryHotspot 用）")]
        public string hotspotPrefix = "artwork_";
        [Tooltip("旧版本地热点清单（journey_hotspots.json），可选")]
        public string localHotspotsJsonPath = "Gallery/Config/journey_hotspots.json";
        [Tooltip("旧版本地 zones.json（SplatZoneManager 用），可选")]
        public string localZonesJsonPath = "Gallery/Config/zones.json";

        [Header("场景对象名（可选，缺省自动找）")]
        [Tooltip("LOD 容器 GameObject 名（如 QianFan_LodViewer）")]
        public string lodContainerName;
        [Tooltip("碰撞体容器 GameObject 名（如 QianFan_Collision）")]
        public string collisionContainerName;
        [Tooltip("热点容器 GameObject 名（如 QianFan_Hotspots）")]
        public string hotspotsContainerName;

        [ContextMenu("打印配置摘要")]
        public void PrintSummary()
        {
            Debug.Log($"[ExhibitionProfile] {name}: exhibitionId={exhibitionId}, apiBaseUrl={apiBaseUrl}, " +
                $"lodLevels={lodLevels.Count}, collision={collisionPath}, hotspotPrefix={hotspotPrefix}");
        }
    }
}
