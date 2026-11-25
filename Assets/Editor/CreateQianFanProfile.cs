// 一键生成前滩源 ExhibitionProfile 资产并挂到场景组件
// 菜单：Tools/前滩源 自动生成展览 profile
using UnityEditor;
using UnityEngine;
using GalleryRuntime;

namespace GalleryEditor
{
    public static class CreateQianFanProfile
    {
        private const string AssetPath = "Assets/Exhibitions/QianFan/QianFanProfile.asset";
        private const string Folder = "Assets/Exhibitions/QianFan";

        [MenuItem("Tools/前滩源 自动生成展览 profile", false, 110)]
        public static void Execute()
        {
            // 确保目录存在
            if (!System.IO.Directory.Exists(Folder))
            {
                string parent = "Assets/Exhibitions";
                if (!System.IO.Directory.Exists(parent))
                    AssetDatabase.CreateFolder("Assets", "Exhibitions");
                AssetDatabase.CreateFolder(parent, "QianFan");
            }

            // 若已存在则直接加载复用
            ExhibitionProfile profile;
            if (System.IO.File.Exists(AssetPath))
            {
                profile = AssetDatabase.LoadAssetAtPath<ExhibitionProfile>(AssetPath);
                if (profile != null)
                {
                    Debug.Log("[Profile] 已存在，直接复用: " + AssetPath);
                    WireSceneComponents(profile);
                    AssetDatabase.SaveAssets();
                    return;
                }
            }

            profile = ScriptableObject.CreateInstance<ExhibitionProfile>();
            profile.name = "QianFanProfile";

            // 业务端（Railway）—— 与 StreamingAssets/Gallery/Config/journey.json 同步
            profile.exhibitionId = ""; // 待运营建展后填
            profile.apiBaseUrl = "https://www.molink.art";
            profile.sceneToken = "D1AUZMLH8DMXCEPASk58ioECOwYaTfmYoFDKkhjY7YY";
            profile.refreshSeconds = 60;
            profile.deliveryMode = "local-browser";

            // 点云 LOD（与 QianFanLodViewer 默认值一致）
            profile.lodLevels = new System.Collections.Generic.List<ExhibitionProfile.LodLevel>
            {
                new ExhibitionProfile.LodLevel { relativePath = "QianFan/lods/point_cloud_3.ply", maxDistance = 12f },
                new ExhibitionProfile.LodLevel { relativePath = "QianFan/lods/point_cloud_4.ply", maxDistance = 30f },
                new ExhibitionProfile.LodLevel { relativePath = "QianFan/lods/point_cloud_5.ply", maxDistance = 9999f },
            };

            profile.collisionPath = "QianFan/collision.ply";
            profile.hotspotPrefix = "artwork_";
            profile.localHotspotsJsonPath = "Gallery/Config/journey_hotspots.json";
            profile.localZonesJsonPath = "Gallery/Config/zones.json";

            AssetDatabase.CreateAsset(profile, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Profile] 已生成: " + AssetPath);

            WireSceneComponents(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static void WireSceneComponents(ExhibitionProfile profile)
        {
            // 把 profile 挂到场景内对应的组件，保证后续运行走 profile 路径而非脚本硬编码
            var lodViewers = UnityEngine.Object.FindObjectsByType<QianFanLodViewer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var v in lodViewers)
            {
                if (v.profile == null)
                {
                    v.profile = profile;
                    EditorUtility.SetDirty(v);
                    Debug.Log($"[Profile] {v.name} (QianFanLodViewer) -> profile");
                }
            }

            var colliders = UnityEngine.Object.FindObjectsByType<QianFanCollisionLoader>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in colliders)
            {
                if (c.profile == null)
                {
                    c.profile = profile;
                    EditorUtility.SetDirty(c);
                    Debug.Log($"[Profile] {c.name} (QianFanCollisionLoader) -> profile");
                }
            }

            var hotspotHosts = UnityEngine.Object.FindObjectsByType<GalleryJourneyHotspots>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var h in hotspotHosts)
            {
                if (h.profile == null)
                {
                    h.profile = profile;
                    EditorUtility.SetDirty(h);
                    Debug.Log($"[Profile] {h.name} (GalleryJourneyHotspots) -> profile");
                }
            }
        }
    }
}
