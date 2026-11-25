// 一键注册场景热点到业务端（Railway）
// 1) 读取当前场景所有 GalleryHotspot 的 hotspotId
// 2) 用 PUT /api/journey/scenes/<exhibitionId>/hotspots 上报（需 x-scene-token）
// 3) 成功后业务端 journey_bindings 的 expected_hotspots 即更新
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using GalleryRuntime;

namespace GalleryEditor
{
    [Serializable]
    public class SceneHotspotEntry { public string hotspot_id; public string label; public int sort_order; }
    [Serializable]
    public class SceneHotspotList { public SceneHotspotEntry[] hotspots; }

    public static class QianFanAutoRegisterHotspots
    {
        // 从 journey.json 读取 exhibitionId，避免硬编码
        private static (string exhibitionId, string apiBaseUrl, string sceneToken) ReadConfig()
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath,
                "Gallery/Config/journey.json".Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(path)) return ("", "", "");
            try
            {
                var config = JsonUtility.FromJson<GalleryRuntime.JourneyLocalConfig>(System.IO.File.ReadAllText(path));
                return (config?.exhibitionId ?? "", config?.apiBaseUrl ?? "", config?.sceneToken ?? "");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QianFan] 解析 journey.json 失败: " + e.Message);
                return ("", "", "");
            }
        }

        private static List<string> CollectHotspotIds()
        {
            var spots = UnityEngine.Object.FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var ids = new List<string>();
            foreach (var s in spots)
            {
                if (!string.IsNullOrEmpty(s.hotspotId)) ids.Add(s.hotspotId);
            }
            return ids.Distinct().OrderBy(id => id).ToList();
        }

        [MenuItem("Tools/前滩源 自动注册热点到业务端", false, 100)]
        public static void Execute()
        {
            var ids = CollectHotspotIds();
            if (ids.Count == 0)
            {
                EditorUtility.DisplayDialog("没有热点", "场景中没有找到任何 GalleryHotspot。请先用筛查工具放置热点。", "好");
                return;
            }

            var (exhibitionId, apiBaseUrl, sceneToken) = ReadConfig();
            if (string.IsNullOrEmpty(exhibitionId))
            {
                EditorUtility.DisplayDialog("未配置", "StreamingAssets/Gallery/Config/journey.json 的 exhibitionId 为空。\n" +
                    "请先填入展览 ID（与业务端展览资料页一致）。", "好");
                return;
            }
            if (string.IsNullOrEmpty(apiBaseUrl) || apiBaseUrl.Contains("REPLACE"))
            {
                EditorUtility.DisplayDialog("未配置", "apiBaseUrl 未填入实际域名。", "好");
                return;
            }
            if (string.IsNullOrEmpty(sceneToken))
            {
                EditorUtility.DisplayDialog("缺少令牌", "journey.json 中 sceneToken 为空。\n" +
                    "请找业务端管理员获取 JOURNEY_SCENE_TOKEN（Railway 环境变量）填入。", "好");
                return;
            }

            // 构造请求体：{"hotspots":[{"hotspot_id":"artwork_qf_001","label":"","sort_order":0},...]}
            var list = new SceneHotspotList
            {
                hotspots = ids.Select((id, i) => new SceneHotspotEntry { hotspot_id = id, label = "", sort_order = i }).ToArray()
            };
            string body = JsonUtility.ToJson(list);

            PutHotspotsAsync(apiBaseUrl, exhibitionId, sceneToken, body, ids.Count);
        }

        [MenuItem("Tools/前滩源 自动注册热点到业务端", true)]
        public static bool Validate()
        {
            return CollectHotspotIds().Count > 0;
        }

        private static void PutHotspotsAsync(string apiBaseUrl, string exhibitionId, string token, string body, int count)
        {
            EditorUtility.DisplayDialog("正在同步", $"将把 {count} 个热点上报到业务端...\n{apiBaseUrl}\n展览: {exhibitionId}", "开始");

            var client = new HttpClient();
            var request = new HttpRequestMessage(System.Net.HttpMethod.Put,
                $"{apiBaseUrl.TrimEnd('/')}/api/journey/scenes/{Uri.EscapeDataString(exhibitionId)}/hotspots")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-scene-token", token);

            Task.Run(async () =>
            {
                try
                {
                    var response = await client.SendAsync(request);
                    var text = await response.Content.ReadAsStringAsync();
                    EditorApplication.delayCall += () =>
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            Debug.Log($"[QianFan] 热点注册成功: {text}");
                            EditorUtility.DisplayDialog("注册成功", $"{count} 个热点已同步到业务端。\n{exhibitionId}\n\n现在可以打开 /admin/journey 绑定作品并发布清单了。", "好");
                        }
                        else
                        {
                            Debug.LogWarning($"[QianFan] 注册失败 ({(int)response.StatusCode}): {text}");
                            EditorUtility.DisplayDialog("注册失败", $"HTTP {(int)response.StatusCode}\n{text}", "好");
                        }
                    };
                }
                catch (System.Exception e)
                {
                    EditorApplication.delayCall += () =>
                    {
                        Debug.LogWarning($"[QianFan] 网络错误: {e.Message}");
                        EditorUtility.DisplayDialog("网络错误", e.Message, "好");
                    };
                }
            });
        }
    }
}
