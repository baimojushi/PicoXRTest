// 前滩源多级 LOD 高斯可视化（自建，替代 SDK 里被 #if false 禁用的 GsplatLodStreamer）。
//
// 从 StreamingAssets/QianFan/lods/ 读 point_cloud[_N].ply 多个层级，按相机到本对象距离
// 切换 active 的 GsplatRenderer。**懒加载**：只在某层首次成为 active 时才解码该 PLY，
// 避免编辑器每次重载都解析上百 MB。
// [ExecuteAlways] 让编辑器 Scene 视图也能看到（GsplatRenderer 本身带 [ExecuteAlways]）。
using System.Collections.Generic;
using System.IO;
using Gsplat;
using UnityEngine;

namespace GalleryRuntime
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class QianFanLodViewer : MonoBehaviour
    {
        [System.Serializable]
        public class LodLevel
        {
            [Tooltip("相对 StreamingAssets 的 ply 路径")]
            public string relativePath = "QianFan/lods/point_cloud_5.ply";
            [Tooltip("相机距离 ≤ 该值(米)时使用本层")]
            public float maxDistance = 9999f;
        }

        [Tooltip("LOD 层级，按距离从近到远排列（近→精细，远→粗糙）。建议最后一级 maxDistance 设很大做兜底。")]
        public List<LodLevel> levels = new List<LodLevel>
        {
            new LodLevel { relativePath = "QianFan/lods/point_cloud_3.ply", maxDistance = 12f },
            new LodLevel { relativePath = "QianFan/lods/point_cloud_4.ply", maxDistance = 30f },
            new LodLevel { relativePath = "QianFan/lods/point_cloud_5.ply", maxDistance = 9999f },
        };

        [Tooltip("展览 profile；设置后本组件的 levels 会被 profile.lodLevels 覆盖（便于多场展览共用脚本）")]
        public ExhibitionProfile profile;

        [Tooltip("判定距离用的参考相机；留空则用 Camera.main / SceneView 相机")]
        public Camera referenceCamera;

        private readonly List<GsplatRenderer> _renderers = new List<GsplatRenderer>();
        private readonly List<GsplatAsset> _assets = new List<GsplatAsset>();
        private readonly List<bool> _loaded = new List<bool>();
        private int _activeIndex = -1;
        private bool _built;

        void OnEnable() { if (!_built) Build(); }

        private void ApplyProfile()
        {
            if (profile == null) return;
            if (profile.lodLevels == null || profile.lodLevels.Count == 0) return;
            levels = new List<LodLevel>(profile.lodLevels.Count);
            foreach (var p in profile.lodLevels)
                levels.Add(new LodLevel { relativePath = p.relativePath, maxDistance = p.maxDistance });
        }

        [ContextMenu("Rebuild LOD")]
        public void Build()
        {
            ApplyProfile();
            ClearChildren();
            _renderers.Clear();
            _assets.Clear();
            _loaded.Clear();

            for (int i = 0; i < levels.Count; i++)
            {
                var go = new GameObject($"LOD_{i}_{Path.GetFileNameWithoutExtension(levels[i].relativePath)}");
                go.transform.SetParent(transform, false);
                var gr = go.AddComponent<GsplatRenderer>();
                gr.SHDegree = 0;   // 本数据集 SH=0（DC 颜色）
                go.SetActive(false);
                _renderers.Add(gr);
                _assets.Add(null);
                _loaded.Add(false);
            }
            _built = true;
            _activeIndex = -1;
        }

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            for (int i = 0; i < _assets.Count; i++)
            {
                var a = _assets[i];
                if (a == null) continue;
                if (Application.isPlaying) Destroy(a); else DestroyImmediate(a);
            }
        }

        private void Update()
        {
            if (!_built || _renderers.Count == 0) return;
            var cam = ResolveCamera();
            if (cam == null) return;

            float dist = Vector3.Distance(cam.transform.position, transform.position);
            int target = PickLevel(dist);
            if (target != _activeIndex) SwitchTo(target);
        }

        private int PickLevel(float dist)
        {
            for (int i = 0; i < levels.Count; i++)
                if (dist <= levels[i].maxDistance) return i;
            return levels.Count - 1;
        }

        private void SwitchTo(int index)
        {
            if (index < 0 || index >= _renderers.Count) return;

            // 懒加载目标层
            if (!_loaded[index])
            {
                var path = Path.Combine(Application.streamingAssetsPath, levels[index].relativePath);
                if (!File.Exists(path))
                {
                    Debug.LogError($"[QianFanLod] 找不到 {levels[index].relativePath}");
                }
                else
                {
                    try
                    {
                        var asset = GsplatRuntimeLoader.LoadFile(path, CompressionMode.Uncompressed);
                        _assets[index] = asset;
                        _renderers[index].GsplatAsset = asset;
                        _loaded[index] = true;
                        Debug.Log($"[QianFanLod] 懒加载 LOD{index} {levels[index].relativePath} = {asset.SplatCount} splats");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[QianFanLod] 加载 LOD{index} 失败: {e.Message}");
                    }
                }
            }

            for (int i = 0; i < _renderers.Count; i++)
            {
                var go = _renderers[i].gameObject;
                bool on = i == index;
                if (go.activeSelf != on) go.SetActive(on);
            }
            _activeIndex = index;
        }

        private Camera ResolveCamera()
        {
            if (referenceCamera != null) return referenceCamera;
            if (Camera.main != null) return Camera.main;
#if UNITY_EDITOR
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null) return sv.camera;
#endif
            return null;
        }
    }
}
