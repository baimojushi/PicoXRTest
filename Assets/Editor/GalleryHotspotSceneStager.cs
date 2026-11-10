namespace GalleryEditor
{
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using GalleryRuntime;
    using UnityEditor;
    using UnityEditor.Events;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.Events;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// Stages the auto-detected artwork zones from zones.json as hotspot boxes in
    /// GalleryScene and installs the runtime interaction rig (reticle + content
    /// catalog).
    ///
    /// A box is only an interaction trigger: the artwork Gaussians live in the
    /// pre-sliced PLY files that SplatZoneManager streams by distance.
    ///
    /// Run from the menu (Tools/Gallery 一键落地热点方体) or headless:
    ///   Unity.exe -batchmode -nographics -quit -projectPath <proj>
    ///            -executeMethod GalleryEditor.GalleryHotspotSceneStager.StageFromCommandLine
    /// </summary>
    public static class GalleryHotspotSceneStager
    {
        public const string ScenePath = "Assets/Scenes/GalleryScene.unity";
        public const string AutoRootName = "Hotspots_Auto";
        public const string InteractionRootName = "GalleryInteraction";
        public const string ZonesRelativePath = "Gallery/Config/zones.json";

        [MenuItem("Tools/Gallery 一键落地热点方体")]
        public static void StageFromMenu()
        {
            try
            {
                int total = Stage();
                EditorUtility.DisplayDialog(
                    "热点方体已落地",
                    $"已处理 {total} 个 zone（新建 + 更新），场景已保存：\n{ScenePath}",
                    "好");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GalleryHotspotSceneStager] 失败: {e}");
                EditorUtility.DisplayDialog("落地失败", e.Message, "好");
            }
        }

        /// <summary>Headless entry point (-executeMethod). Unity quits afterwards thanks to -quit.</summary>
        public static void StageFromCommandLine()
        {
            int code = 0;
            try
            {
                int total = Stage();
                Debug.Log($"[GalleryHotspotSceneStager] StageFromCommandLine finished, zones={total}");
            }
            catch (System.Exception e)
            {
                code = 1;
                Debug.LogError($"[GalleryHotspotSceneStager] 失败: {e}");
            }

            // An exception escaping -executeMethod leaves batch mode hanging forever
            EditorApplication.Exit(code);
        }

        /// <summary>
        /// Installs only the interaction rig (reticle + content catalog) and saves the
        /// scene. Separated from Stage() so it can be run on its own.
        /// </summary>
        public static bool SetupRigOnly()
        {
            var scene = ResolveScene();
            SetupInteractionRig(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[GalleryHotspotSceneStager] 交互装置已就绪，场景已保存 {ScenePath}");
            return true;
        }

        /// <summary>
        /// Uses the already open GalleryScene when possible, so interactive use never
        /// discards unsaved edits; otherwise opens it from disk.
        /// </summary>
        private static Scene ResolveScene()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.isLoaded && active.path == ScenePath)
                return active;

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// Creates (or refreshes) one hotspot box per zone and saves the scene.
        /// Returns the number of zones processed.
        /// </summary>
        public static int Stage()
        {
            string zonesPath = Path.Combine(
                Application.streamingAssetsPath,
                ZonesRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(zonesPath))
            {
                Debug.LogError($"[GalleryHotspotSceneStager] 找不到 zones.json: {zonesPath}");
                return 0;
            }

            SplatZoneManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<SplatZoneManifest>(File.ReadAllText(zonesPath, Encoding.UTF8));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GalleryHotspotSceneStager] zones.json 解析失败: {e.Message}");
                return 0;
            }

            if (manifest?.zones == null || manifest.zones.Length == 0)
            {
                Debug.LogError("[GalleryHotspotSceneStager] zones.json 里没有 zone");
                return 0;
            }

            var scene = ResolveScene();

            var root = FindRoot(scene, AutoRootName);
            if (root == null)
            {
                var go = new GameObject(AutoRootName);
                SceneManager.MoveGameObjectToScene(go, scene);
                root = go;
            }

            var existing = new Dictionary<string, GalleryHotspot>();
            foreach (var spot in FindHotspots(scene))
            {
                if (!string.IsNullOrEmpty(spot.hotspotId) && !existing.ContainsKey(spot.hotspotId))
                    existing[spot.hotspotId] = spot;
            }

            int created = 0;
            int updated = 0;

            foreach (var zone in manifest.zones)
            {
                if (zone == null || string.IsNullOrWhiteSpace(zone.id))
                    continue;

                // JsonVec3 is a struct: a missing field keeps the all-zero default
                var size = zone.boxSize.ToVector3();
                if (size == Vector3.zero)
                    size = new Vector3(0.6f, 0.6f, 0.4f);
                size = new Vector3(
                    Mathf.Max(0.12f, size.x), Mathf.Max(0.12f, size.y), Mathf.Max(0.12f, size.z));

                if (!existing.TryGetValue(zone.id, out var spot))
                {
                    var go = new GameObject($"Hotspot_{zone.id}");
                    go.transform.SetParent(root.transform, true);
                    spot = go.AddComponent<GalleryHotspot>();
                    spot.hotspotId = zone.id;
                    created++;
                }
                else
                {
                    updated++;
                }

                // zones.json 中的中心坐标来自 3DGS 点云坐标系，其 Z 轴与 Unity 世界坐标系方向相反；
                // 落地前需做 Z 轴镜像翻转才能与高斯点云对齐。boxSize 为尺寸，不受影响。
                var center = zone.center.ToVector3();
                center = new Vector3(center.x, center.y, -center.z);
                spot.transform.SetPositionAndRotation(center, Quaternion.identity);

                var collider = spot.GetComponent<BoxCollider>();
                if (collider == null)
                    collider = spot.gameObject.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = size;

                var card = spot.GetComponent<GalleryHotspotInfoCard>();
                if (card == null)
                    card = spot.gameObject.AddComponent<GalleryHotspotInfoCard>();
                card.hotspot = spot;

                if (spot.onActivate.GetPersistentEventCount() == 0)
                    UnityEventTools.AddPersistentListener(spot.onActivate, new UnityAction(card.Show));
                if (spot.onDeactivate.GetPersistentEventCount() == 0)
                    UnityEventTools.AddPersistentListener(spot.onDeactivate, new UnityAction(card.Hide));

                EditorUtility.SetDirty(spot);
                EditorUtility.SetDirty(card);
            }

            // Auto hotspots absent from zones.json are stale leftovers from older runs
            // (wrong Sim(3) coordinates) - drop them so the scene matches the manifest.
            var validIds = new HashSet<string>();
            foreach (var zone in manifest.zones)
            {
                if (zone != null && !string.IsNullOrWhiteSpace(zone.id))
                    validIds.Add(zone.id);
            }

            int removed = 0;
            foreach (var autoSpot in root.GetComponentsInChildren<GalleryHotspot>(true))
            {
                if (autoSpot == null)
                    continue;
                if (string.IsNullOrEmpty(autoSpot.hotspotId) || !validIds.Contains(autoSpot.hotspotId))
                {
                    UnityEngine.Object.DestroyImmediate(autoSpot.gameObject);
                    removed++;
                }
            }

            SetupInteractionRig(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[GalleryHotspotSceneStager] 落地完成：新建 {created}，更新 {updated}，移除孤儿 {removed}，场景已保存 {ScenePath}");
            return created + updated;
        }

        private static void SetupInteractionRig(Scene scene)
        {
            var rig = FindRoot(scene, InteractionRootName);
            if (rig == null)
            {
                var go = new GameObject(InteractionRootName);
                SceneManager.MoveGameObjectToScene(go, scene);
                rig = go;
            }

            var reticle = rig.GetComponent<GalleryReticle>();
            if (reticle == null)
                reticle = rig.AddComponent<GalleryReticle>();

            var catalog = rig.GetComponent<GalleryArtworkCatalog>();
            if (catalog == null)
                catalog = rig.AddComponent<GalleryArtworkCatalog>();

            var interactor = UnityEngine.Object.FindFirstObjectByType<GalleryInteractor>();
            if (interactor != null)
            {
                reticle.interactor = interactor;
                EditorUtility.SetDirty(reticle);
            }
            else
            {
                Debug.LogWarning("[GalleryHotspotSceneStager] 场景里没找到 GalleryInteractor，准星将自动查找");
            }

            EditorUtility.SetDirty(catalog);
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            var roots = new List<GameObject>();
            scene.GetRootGameObjects(roots);
            foreach (var go in roots)
            {
                if (go.name == name)
                    return go;
            }

            return null;
        }

        private static List<GalleryHotspot> FindHotspots(Scene scene)
        {
            var list = new List<GalleryHotspot>();
            var roots = new List<GameObject>();
            scene.GetRootGameObjects(roots);

            foreach (var root in roots)
            {
                list.AddRange(root.GetComponentsInChildren<GalleryHotspot>(true));
            }

            return list;
        }
    }
}
