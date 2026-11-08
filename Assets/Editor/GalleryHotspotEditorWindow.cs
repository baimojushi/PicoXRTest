namespace GalleryEditor
{
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using GalleryRuntime;
    using UnityEditor;
    using UnityEditor.Events;
    using UnityEngine;
    using UnityEngine.Events;

    /// <summary>
    /// Gallery hotspot editor. A box acts as the hotspot trigger collider:
    /// it does NOT contain splat data — Gaussians live in pre-sliced PLY files.
    ///
    /// Interaction chain:
    ///   aim  -> GalleryInteractor focuses the box (GalleryReticle shows feedback)
    ///   USE  -> GalleryHotspot.Activate() (toggle) -> onActivate / onDeactivate events
    ///
    /// The "从 zones.json 批量生成" button turns the artwork-recognition pipeline
    /// output (zones.json: id + center + boxSize) into ready-to-use hotspots whose
    /// onActivate is already wired to GalleryHotspotInfoCard.
    /// </summary>
    public class GalleryHotspotEditorWindow : EditorWindow
    {
        private const string AutoRootName = "Hotspots_Auto";
        private const string InteractionRootName = "GalleryInteraction";
        private const string ZonesRelativePath = "Gallery/Config/zones.json";
        private const string ArtworksRelativePath = "Gallery/Config/artworks.json";

        [MenuItem("Tools/Gallery 热点编辑器")]
        public static void Open()
        {
            var window = GetWindow<GalleryHotspotEditorWindow>("Gallery 热点");
            window.minSize = new Vector2(380f, 520f);
        }

        private Vector2 scrollPos;

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "方体 = 热点碰撞体（BoxCollider IsTrigger），不包含高斯数据——高斯在预切片的 PLY 文件里。\n" +
                "交互：玩家视线瞄准方体聚焦（准星变色）→ 按「使用」激活/再按关闭 → 触发 onActivate / onDeactivate。\n" +
                "放置技巧：Scene 视图对着挂画看一眼 → 点「新建：放在视线落点」，方体贴着表面生成，再微调大小。\n" +
                "注意：方体不要包住玩家站位，否则射线从内部打不中自身碰撞体。", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("新建：放在视线落点")) CreateAtSceneViewHit();
                if (GUILayout.Button("新建：放在场景原点")) CreateHotspot(Vector3.zero, Quaternion.identity);
            }

            GUILayout.Space(10);
            EditorGUILayout.LabelField("自动化 / 内容", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "挂画识别管线输出的 zones.json 已含 26 个 zone 的 id / center / boxSize。\n" +
                "「批量生成」把它们变成可用热点（已自动绑定信息卡动作）；「填充内容」用 artworks.json 补标题/简介。",
                MessageType.None);

            if (GUILayout.Button("从 zones.json 批量生成热点（自动绑定信息卡）")) CreateFromZonesManifest();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("生成/更新 artworks.json 模板")) WriteArtworksTemplate();
                if (GUILayout.Button("从 artworks.json 填充内容")) FillFromArtworks();
            }

            if (GUILayout.Button("在场景中装备准星 + 内容目录")) SetupInteractionRig();

            GUILayout.Space(10);
            EditorGUILayout.LabelField("场景中的热点", EditorStyles.boldLabel);

            var spots = FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (spots.Length == 0)
            {
                EditorGUILayout.HelpBox("场景中还没有热点，点上面的「新建」或「批量生成」开始。", MessageType.None);
            }
            else
            {
                scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
                foreach (var spot in spots) DrawSpotRow(spot);
                EditorGUILayout.EndScrollView();
            }

            GUILayout.Space(8);
            DrawIssueReport(spots);
        }

        private static void DrawSpotRow(GalleryHotspot spot)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(spot.name, EditorStyles.boldLabel);
                    if (GUILayout.Button("定位", GUILayout.Width(44)))
                    {
                        Selection.activeGameObject = spot.gameObject;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }

                    if (GUILayout.Button("删除", GUILayout.Width(44)))
                    {
                        Undo.DestroyObjectImmediate(spot.gameObject);
                        GUIUtility.ExitGUI();
                    }
                }

                var serialized = new SerializedObject(spot);
                var idProp = serialized.FindProperty("hotspotId");
                if (idProp != null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel("热点 ID");
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(idProp, GUIContent.none);
                        if (EditorGUI.EndChangeCheck()) serialized.ApplyModifiedProperties();
                    }
                }

                var collider = spot.GetComponent<BoxCollider>();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("碰撞体");
                    if (collider == null)
                    {
                        EditorGUILayout.LabelField("缺失（射线打不中）", EditorStyles.miniLabel);
                        if (GUILayout.Button("补建", GUILayout.Width(44)))
                        {
                            Undo.AddComponent<BoxCollider>(spot.gameObject).isTrigger = true;
                        }
                    }
                    else if (!collider.isTrigger)
                    {
                        EditorGUILayout.LabelField("非 Trigger（会挡玩家）", EditorStyles.miniLabel);
                        if (GUILayout.Button("修复", GUILayout.Width(44)))
                        {
                            Undo.RecordObject(collider, "Make Hotspot Trigger");
                            collider.isTrigger = true;
                            EditorUtility.SetDirty(collider);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("IsTrigger ✓", EditorStyles.miniLabel);
                    }
                }

                if (collider != null)
                {
                    EditorGUILayout.LabelField(
                        "尺寸/位置",
                        $"{collider.size.ToString("F1")} @ {spot.transform.position.ToString("F1")}",
                        EditorStyles.miniLabel);
                }

                bool hasCard = spot.GetComponent<GalleryHotspotInfoCard>() != null;
                EditorGUILayout.LabelField(
                    $"标题：{(string.IsNullOrWhiteSpace(spot.title) ? "(未录入)" : spot.title)}  信息卡：{(hasCard ? "已绑" : "无")}",
                    EditorStyles.miniLabel);
            }
        }

        private void CreateAtSceneViewHit()
        {
            var point = Vector3.zero;
            var rotation = Quaternion.identity;

            var view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
            {
                var cam = view.camera.transform;
                if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 200f))
                {
                    point = hit.point;
                    // 方体 -Z 面朝玩家（背向墙面法线），贴着表面生成
                    rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);
                }
                else
                {
                    point = cam.position + cam.forward * 3f;
                    rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
                }
            }

            CreateHotspot(point, rotation);
        }

        private void CreateHotspot(Vector3 surfacePoint, Quaternion rotation)
        {
            string id = NextFreeId();
            var go = new GameObject($"Hotspot_{id.Substring("hotspot_".Length)}");
            Undo.RegisterCreatedObjectUndo(go, "Create Gallery Hotspot");

            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(1.5f, 1.5f, 0.4f);

            var spot = go.AddComponent<GalleryHotspot>();
            spot.hotspotId = id;

            // 贴面：沿旋转后的 -Z（朝玩家一侧）外推半个厚度，让方体浮在表面外
            go.transform.SetPositionAndRotation(
                surfacePoint - rotation * new Vector3(0f, 0f, collider.size.z * 0.5f),
                rotation);

            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private static string NextFreeId()
        {
            int max = 0;
            var spots = FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var spot in spots)
            {
                string id = spot.hotspotId ?? string.Empty;
                if (id.StartsWith("hotspot_") && int.TryParse(id.Substring("hotspot_".Length), out int number))
                {
                    max = Mathf.Max(max, number);
                }
            }

            return $"hotspot_{max + 1:000}";
        }

        // ---------- zones.json -> hotspots ----------

        private void CreateFromZonesManifest()
        {
            GalleryHotspotSceneStager.StageFromMenu();
        }

        // ---------- artworks.json ----------

        private void WriteArtworksTemplate()
        {
            string zonesPath = StreamingPath(ZonesRelativePath);
            if (!File.Exists(zonesPath))
            {
                EditorUtility.DisplayDialog("找不到 zones.json", zonesPath, "好");
                return;
            }

            var zones = JsonUtility.FromJson<SplatZoneManifest>(File.ReadAllText(zonesPath, Encoding.UTF8));
            if (zones?.zones == null || zones.zones.Length == 0)
            {
                EditorUtility.DisplayDialog("zones.json 里没有 zone", zonesPath, "好");
                return;
            }

            // 保留已录入的内容，只补齐缺失的 id，避免覆盖用户填写的标题
            var merged = new Dictionary<string, GalleryArtworkInfo>();
            string artworksPath = StreamingPath(ArtworksRelativePath);
            if (File.Exists(artworksPath))
            {
                var existing = JsonUtility.FromJson<GalleryArtworkManifest>(
                    File.ReadAllText(artworksPath, Encoding.UTF8));
                if (existing?.artworks != null)
                {
                    foreach (var a in existing.artworks)
                    {
                        if (a != null && !string.IsNullOrEmpty(a.id)) merged[a.id] = a;
                    }
                }
            }

            var list = new List<GalleryArtworkInfo>();
            int added = 0;
            foreach (var zone in zones.zones)
            {
                if (zone == null || string.IsNullOrWhiteSpace(zone.id)) continue;
                if (merged.TryGetValue(zone.id, out var info))
                {
                    list.Add(info);
                }
                else
                {
                    list.Add(new GalleryArtworkInfo { id = zone.id });
                    added++;
                }
            }

            var manifest = new GalleryArtworkManifest { artworks = list.ToArray() };
            File.WriteAllText(artworksPath, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
            AssetDatabase.Refresh();

            string absolute = Path.GetFullPath(artworksPath);
            Debug.Log($"[Gallery 热点] artworks.json 模板已写入（新增 {added} 条）：{absolute}");
            EditorUtility.DisplayDialog(
                "完成",
                $"已写入 {list.Count} 条（新增 {added} 条，保留已有内容）。\n\n{absolute}\n\n打开它填写 title / artist / year / description 后，回此窗口点「从 artworks.json 填充内容」。",
                "好");
        }

        private void FillFromArtworks()
        {
            string path = StreamingPath(ArtworksRelativePath);
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog("找不到 artworks.json", "先点「生成/更新 artworks.json 模板」。\n" + path, "好");
                return;
            }

            var manifest = JsonUtility.FromJson<GalleryArtworkManifest>(File.ReadAllText(path, Encoding.UTF8));
            var map = new Dictionary<string, GalleryArtworkInfo>();
            if (manifest?.artworks != null)
            {
                foreach (var a in manifest.artworks)
                {
                    if (a != null && !string.IsNullOrEmpty(a.id)) map[a.id] = a;
                }
            }

            int applied = 0;
            foreach (var spot in FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(spot.hotspotId) || !map.TryGetValue(spot.hotspotId, out var info))
                    continue;

                Undo.RecordObject(spot, "Fill Hotspot Content");
                if (string.IsNullOrWhiteSpace(spot.title)) spot.title = info.title;
                if (string.IsNullOrWhiteSpace(spot.artist)) spot.artist = info.artist;
                if (string.IsNullOrWhiteSpace(spot.year)) spot.year = info.year;
                if (string.IsNullOrWhiteSpace(spot.description)) spot.description = info.description;
                EditorUtility.SetDirty(spot);
                applied++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Gallery 热点] 内容填充 {applied} 个热点");
            EditorUtility.DisplayDialog("完成", $"已填充 {applied} 个热点（只补空缺字段）。", "好");
        }

        // ---------- runtime rig ----------

        private void SetupInteractionRig()
        {
            GalleryHotspotSceneStager.SetupRigOnly();
        }

        private static string StreamingPath(string relative)
        {
            var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Application.streamingAssetsPath, normalized);
        }

        private void DrawIssueReport(GalleryHotspot[] spots)
        {
            var issues = new List<string>();
            var idCounts = new Dictionary<string, int>();
            var player = FindFirstObjectByType<GalleryPlayerController>(FindObjectsInactive.Include);
            int missingTitle = 0;

            foreach (var spot in spots)
            {
                if (string.IsNullOrWhiteSpace(spot.hotspotId))
                {
                    issues.Add($"{spot.name}: hotspotId 为空");
                }
                else
                {
                    idCounts.TryGetValue(spot.hotspotId, out int count);
                    idCounts[spot.hotspotId] = count + 1;
                }

                if (string.IsNullOrWhiteSpace(spot.title)) missingTitle++;

                var collider = spot.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    issues.Add($"{spot.name}: 缺 BoxCollider（「使用」射线打不中）");
                }
                else
                {
                    if (!collider.isTrigger) issues.Add($"{spot.name}: BoxCollider 非 Trigger，会挡住玩家走路");
                    if (player != null && collider.bounds.Contains(player.transform.position))
                    {
                        issues.Add($"{spot.name}: 方体包住了玩家站位——射线从内部打不中自身碰撞体，请缩小或外移");
                    }
                }
            }

            foreach (var pair in idCounts)
            {
                if (pair.Value > 1) issues.Add($"hotspotId「{pair.Key}」重复 {pair.Value} 个");
            }

            if (missingTitle > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{missingTitle} 个热点尚未录入标题（激活时会显示 hotspotId）。" +
                    "点「生成/更新 artworks.json 模板」填写后再「从 artworks.json 填充内容」。",
                    MessageType.None);
            }

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("校验通过：无问题。", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("校验提示", EditorStyles.boldLabel);
            foreach (var message in issues)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
        }

        // Scene 视图常显：半透明填充 + 线框 + ID 标签，未选中也能看到
        [DrawGizmo(GizmoType.InSelectionHierarchy | GizmoType.NotInSelectionHierarchy | GizmoType.Pickable)]
        private static void DrawHotspotGizmo(GalleryHotspot spot, GizmoType type)
        {
            var collider = spot.GetComponent<BoxCollider>();
            bool selected = (type & GizmoType.Selected) != 0;

            if (collider == null)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
                Gizmos.DrawWireSphere(spot.transform.position, 0.25f);
                Handles.Label(spot.transform.position + Vector3.up * 0.4f, $"{spot.hotspotId} (缺碰撞体)");
                return;
            }

            var fill = selected ? new Color(0.2f, 1f, 0.6f, 0.30f) : new Color(0.2f, 1f, 0.6f, 0.10f);
            var line = new Color(0.2f, 1f, 0.6f, 0.95f);

            Gizmos.matrix = collider.transform.localToWorldMatrix;
            Gizmos.color = fill;
            Gizmos.DrawCube(collider.center, collider.size);
            Gizmos.color = line;
            Gizmos.DrawWireCube(collider.center, collider.size);
            Gizmos.matrix = Matrix4x4.identity;

            var labelPos = collider.transform.TransformPoint(
                collider.center + new Vector3(0f, collider.size.y * 0.5f, 0f));
            Handles.Label(labelPos, spot.hotspotId, EditorStyles.boldLabel);
        }
    }
}
