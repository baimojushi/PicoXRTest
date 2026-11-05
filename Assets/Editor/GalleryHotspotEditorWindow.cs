namespace GalleryEditor
{
    using System.Collections.Generic;
    using GalleryRuntime;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Gallery hotspot zone editor. A box acts as the hotspot trigger collider:
    /// it does NOT contain splat data — Gaussians live in pre-sliced PLY files.
    /// The player "USE" ray (GalleryInteractor, 3 m) hitting the box calls
    /// GalleryHotspot.Activate(), which fires the onActivate event bound in the Inspector.
    /// </summary>
    public class GalleryHotspotEditorWindow : EditorWindow
    {
        [MenuItem("Tools/Gallery 热点编辑器")]
        public static void Open()
        {
            var window = GetWindow<GalleryHotspotEditorWindow>("Gallery 热点");
            window.minSize = new Vector2(380f, 440f);
        }

        private Vector2 scrollPos;

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "方体 = 热点碰撞体（BoxCollider IsTrigger），不包含高斯数据——高斯在预切片的 PLY 文件里。\n" +
                "玩家按「使用」时从视角射出 3 米射线，打中方体即触发 onActivate 事件（在 Inspector 绑定）。\n" +
                "放置技巧：Scene 视图对着挂画看一眼 → 点「新建：放在视线落点」，方体贴着表面生成，再微调大小。\n" +
                "注意：方体不要包住玩家站位，否则射线从内部打不中自身碰撞体。", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("新建：放在视线落点")) CreateAtSceneViewHit();
                if (GUILayout.Button("新建：放在场景原点")) CreateHotspot(Vector3.zero, Quaternion.identity);
            }

            GUILayout.Space(8);
            EditorGUILayout.LabelField("场景中的热点", EditorStyles.boldLabel);

            var spots = FindObjectsByType<GalleryHotspot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (spots.Length == 0)
            {
                EditorGUILayout.HelpBox("场景中还没有热点，点上面的「新建」开始。", MessageType.None);
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
                        SceneView.lastSceneView?.FrameSelected();
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
            }
        }

        private void CreateAtSceneViewHit()
        {
            var point = Vector3.zero;
            var rotation = Quaternion.identity;

            var view = SceneView.lastSceneView;
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
            SceneView.lastSceneView?.FrameSelected();
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

        private void DrawIssueReport(GalleryHotspot[] spots)
        {
            var issues = new List<string>();
            var idCounts = new Dictionary<string, int>();
            var player = FindFirstObjectByType<GalleryPlayerController>(FindObjectsInactive.Include);

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
