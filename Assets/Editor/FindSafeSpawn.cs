using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 自动寻找安全出生点：从碰撞体 bounds 采样 XZ，向下射线找地板，验证胶囊体不重叠、有头部空间、四向通行空间
/// 找到后自动更新 Player.position 和 ResetPose.position
/// </summary>
public class FindSafeSpawn
{
    const float CHARACTER_RADIUS = 0.3f;
    const float CHARACTER_HEIGHT = 1.7f;
    const float CHARACTER_CENTER_Y = 0.85f;
    const float STEP_OFFSET = 0.3f;
    const float SLOPE_LIMIT = 45f;
    const float MIN_FLOOR_DIST = 0.05f; // 胶囊底部到地板的最小距离
    const float HEAD_CLEARANCE = 0.5f;  // 头顶最小净空
    const float WALL_CLEARANCE = 1.0f;  // 前后左右最小通行距离
    const int GRID_SAMPLES = 30;        // X/Z 各采样点数

    [MenuItem("Tools/Gallery/Find Safe Spawn")]
    public static void Find()
    {
        Debug.Log("=== FIND SAFE SPAWN ===");

        // 0. 确保场景已加载
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (!scene.isLoaded || scene.name != "GalleryScene")
        {
            Debug.Log("Loading GalleryScene...");
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/GalleryScene.unity");
            scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        }

        // 1. 获取碰撞体根节点
        var hcwList = GameObject.FindObjectsByType<GalleryRuntime.HiddenCollisionWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (hcwList == null || hcwList.Length == 0)
        {
            Debug.LogError("HiddenCollisionWorld not found!");
            return;
        }
        var hcw = hcwList[0];
        var root = hcw.transform.root;

        // 2. 显式调用 BuildHiddenColliders() 生成 MeshCollider
        hcw.buildOnAwake = false; // 防止重复
        hcw.hideRenderers = false;
        hcw.disableShadows = true;
        hcw.BuildHiddenColliders();
        Debug.Log($"Built hidden colliders: {root.GetComponentsInChildren<MeshCollider>(true).Length} colliders");

        // 3. 合并所有 MeshCollider 的 bounds
        var colliders = root.GetComponentsInChildren<MeshCollider>(true);
        if (colliders.Length == 0)
        {
            Debug.LogError("No MeshColliders found!");
            return;
        }

        Bounds totalBounds = new Bounds();
        bool first = true;
        foreach (var mc in colliders)
        {
            if (!mc.enabled) continue;
            if (first) { totalBounds = mc.bounds; first = false; }
            else totalBounds.Encapsulate(mc.bounds);
        }
        Debug.Log($"Total collision bounds: center={totalBounds.center}, size={totalBounds.size}, min={totalBounds.min}, max={totalBounds.max}");

        // 4. 找 Player 和 ResetPose
        var player = GameObject.Find("Player");
        var resetPose = GameObject.Find("ResetPose");
        if (player == null) { Debug.LogError("Player not found!"); return; }
        var cc = player.GetComponent<CharacterController>();
        if (cc == null) { Debug.LogError("CharacterController not found!"); return; }

        // 5. 采样 XZ 网格寻找候选位置
        var candidates = new List<SpawnCandidate>();

        float stepX = totalBounds.size.x / GRID_SAMPLES;
        float stepZ = totalBounds.size.z / GRID_SAMPLES;
        float startX = totalBounds.min.x + stepX * 0.5f;
        float startZ = totalBounds.min.z + stepZ * 0.5f;

        int checkedCount = 0;
        for (int ix = 0; ix < GRID_SAMPLES; ix++)
        {
            float x = startX + ix * stepX;
            for (int iz = 0; iz < GRID_SAMPLES; iz++)
            {
                float z = startZ + iz * stepZ;
                checkedCount++;

                // 批处理模式下关闭 debug 以避免超时
                bool debug = true; // Enable debug for first few positions
                var candidate = TestCandidatePosition(x, z, totalBounds, cc, debug);
                if (candidate != null)
                    candidates.Add(candidate);
            }
        }

        Debug.Log($"Checked {checkedCount} positions, found {candidates.Count} valid candidates");

        if (candidates.Count == 0)
        {
            Debug.LogError("NO SAFE SPAWN FOUND! Collision mesh may be completely invalid.");
            // 降级：扩大采样范围或降低要求
            Debug.Log("Trying fallback with relaxed requirements...");
            candidates = FindWithRelaxedRequirements(totalBounds, cc);
            if (candidates.Count == 0)
            {
                Debug.LogError("Fallback also failed. Need manual intervention or new collision mesh.");
                return;
            }
        }

        // 6. 选最佳候选：优先靠近 bounds 中心，其次四向空间最大
        candidates.Sort((a, b) => {
            float distA = Vector2.Distance(new Vector2(a.position.x, a.position.z), new Vector2(totalBounds.center.x, totalBounds.center.z));
            float distB = Vector2.Distance(new Vector2(b.position.x, b.position.z), new Vector2(totalBounds.center.x, totalBounds.center.z));
            int cmp = distA.CompareTo(distB);
            if (cmp != 0) return cmp;
            // 次要：四向空间总和
            float clearanceA = a.forwardClear + a.backClear + a.leftClear + a.rightClear;
            float clearanceB = b.forwardClear + b.backClear + b.leftClear + b.rightClear;
            return clearanceB.CompareTo(clearanceA); // 更大优先
        });

        var best = candidates[0];
        Debug.Log($"=== SAFE SPAWN FOUND ===");
        Debug.Log($"Position: ({best.position.x:F2}, {best.position.y:F2}, {best.position.z:F2})");
        Debug.Log($"Floor Y: {best.floorY:F2}");
        Debug.Log($"Floor normal: ({best.floorNormal.x:F3}, {best.floorNormal.y:F3}, {best.floorNormal.z:F3})");
        Debug.Log($"Capsule overlap: {(best.capsuleOverlap ? "FAIL" : "PASS")}");
        Debug.Log($"Head clearance: {(best.headClear ? "PASS" : "FAIL")} ({best.headSpace:F2}m)");
        Debug.Log($"Forward clearance: {best.forwardClear:F2}m");
        Debug.Log($"Backward clearance: {best.backClear:F2}m");
        Debug.Log($"Left clearance: {best.leftClear:F2}m");
        Debug.Log($"Right clearance: {best.rightClear:F2}m");

        // 7. 应用到 Player 和 ResetPose
        ApplySpawn(best, player, resetPose, cc);
    }

    static SpawnCandidate TestCandidatePosition(float x, float z, Bounds totalBounds, CharacterController cc, bool debug = false)
    {
        // 从高处向下射线
        float rayStartY = totalBounds.max.y + 10f;
        var ray = new Ray(new Vector3(x, rayStartY, z), Vector3.down);
        var hits = Physics.RaycastAll(ray, rayStartY - totalBounds.min.y + 5f);

        if (debug && hits.Length > 0)
        {
            Debug.Log($"TestCandidatePosition({x:F2}, {z:F2}): {hits.Length} hits");
            foreach (var hit in hits)
            {
                Debug.Log($"  Hit: {hit.collider.name} at {hit.point}, normal={hit.normal}, dist={hit.distance:F2}");
            }
        }
        
        if (hits.Length == 0) return null;

        // 找最近的、法线朝上的撞击点（忽略 Player 自己的碰撞体）
        RaycastHit? bestHit = null;
        foreach (var hit in hits)
        {
            if (hit.collider.name == "Player" || hit.collider.GetComponent<CharacterController>() != null)
                continue; // 忽略玩家自己的碰撞体
                
            if (hit.distance < MIN_FLOOR_DIST) continue; // 太近
            if (hit.normal.y < 0.7f) continue; // 不够水平
            if (bestHit == null || hit.distance < bestHit.Value.distance)
                bestHit = hit;
        }
        if (bestHit == null) return null;

        float floorY = bestHit.Value.point.y;
        Vector3 floorNormal = bestHit.Value.normal;

        // 胶囊体位置：胶囊底部 = floorY + MIN_FLOOR_DIST
        // Player Transform Y = 胶囊底部 + center.y = floorY + MIN_FLOOR_DIST + center.y
        float playerY = floorY + MIN_FLOOR_DIST + CHARACTER_CENTER_Y;
        Vector3 playerPos = new Vector3(x, playerY, z);

        // 2. 胶囊体重叠检查
        Vector3 capsuleBottom = new Vector3(x, floorY + MIN_FLOOR_DIST, z);
        Vector3 capsuleTop = capsuleBottom + Vector3.up * (CHARACTER_HEIGHT - CHARACTER_RADIUS * 2);
        bool capsuleOverlap = Physics.CheckCapsule(capsuleBottom, capsuleTop, CHARACTER_RADIUS, ~0, QueryTriggerInteraction.Ignore);
        if (debug)
            Debug.Log($"  Capsule check: bottom={capsuleBottom}, top={capsuleTop}, overlap={capsuleOverlap}");
        if (capsuleOverlap)
        {
            // Find what's overlapping
            var overlaps = Physics.OverlapCapsule(capsuleBottom, capsuleTop, CHARACTER_RADIUS, ~0, QueryTriggerInteraction.Ignore);
            if (debug && overlaps.Length > 0)
            {
                Debug.Log($"  Overlap colliders ({overlaps.Length}):");
                foreach (var col in overlaps)
                {
                    Debug.Log($"    name='{col.name}' type={col.GetType().FullName} pos={col.transform.position} bounds={col.bounds}");
                }
            }
            if (debug) Debug.Log($"  -> FAIL: capsule overlap");
            return null; // 与碰撞体重叠
        }

        // 3. 头部净空检查
        var headRay = new Ray(playerPos + Vector3.up * (CHARACTER_HEIGHT * 0.5f), Vector3.up);
        bool headHit = Physics.Raycast(headRay, HEAD_CLEARANCE, ~0, QueryTriggerInteraction.Ignore);
        if (debug)
            Debug.Log($"  Head check: pos={playerPos + Vector3.up * (CHARACTER_HEIGHT * 0.5f)}, hit={headHit}");
        if (headHit)
        {
            if (debug) Debug.Log($"  -> FAIL: head hit");
            return null; // 头顶有遮挡
        }

        // 4. 四向胶囊投射
        float forwardClear = CapsuleCastDistance(playerPos, Vector3.forward, cc);
        float backClear = CapsuleCastDistance(playerPos, Vector3.back, cc);
        float leftClear = CapsuleCastDistance(playerPos, Vector3.left, cc);
        float rightClear = CapsuleCastDistance(playerPos, Vector3.right, cc);
        if (debug)
            Debug.Log($"  Wall clearance: F={forwardClear:F2} B={backClear:F2} L={leftClear:F2} R={rightClear:F2}");

        if (forwardClear < WALL_CLEARANCE || backClear < WALL_CLEARANCE ||
            leftClear < WALL_CLEARANCE || rightClear < WALL_CLEARANCE)
        {
            if (debug) Debug.Log($"  -> FAIL: wall clearance too small");
            return null; // 空间太窄
        }

        return new SpawnCandidate
        {
            position = playerPos,
            floorY = floorY,
            floorNormal = floorNormal,
            capsuleOverlap = false,
            headClear = true,
            headSpace = HEAD_CLEARANCE,
            forwardClear = forwardClear,
            backClear = backClear,
            leftClear = leftClear,
            rightClear = rightClear
        };
    }

    static float CapsuleCastDistance(Vector3 playerPos, Vector3 direction, CharacterController cc)
    {
        Vector3 bottom = playerPos + Vector3.down * CHARACTER_CENTER_Y;
        Vector3 top = playerPos + Vector3.up * (CHARACTER_HEIGHT - CHARACTER_CENTER_Y);

        var hit = new RaycastHit();
        // 从胶囊中心稍微偏移开始，避免自碰撞
        Vector3 start = playerPos + direction * CHARACTER_RADIUS * 0.5f;
        if (Physics.CapsuleCast(bottom, top, CHARACTER_RADIUS, direction, out hit, 50f, ~0, QueryTriggerInteraction.Ignore))
        {
            // Ignore self-collision
            if (hit.collider.name == "Player" || hit.collider.GetComponent<CharacterController>() != null)
                return 50f;
            return hit.distance - CHARACTER_RADIUS * 0.5f;
        }
        return 50f; // 无阻挡
    }

    static List<SpawnCandidate> FindWithRelaxedRequirements(Bounds totalBounds, CharacterController cc)
    {
        // 放宽要求：减小 WALL_CLEARANCE，允许稍微倾斜的地板
        const float RELAXED_WALL_CLEARANCE = 0.5f;
        const float RELAXED_HEAD_CLEARANCE = 0.3f;
        const float RELAXED_NORMAL_Y = 0.5f;
        var candidates = new List<SpawnCandidate>();

        int samples = GRID_SAMPLES * 2; // 加密采样
        float stepX = totalBounds.size.x / samples;
        float stepZ = totalBounds.size.z / samples;
        float startX = totalBounds.min.x + stepX * 0.5f;
        float startZ = totalBounds.min.z + stepZ * 0.5f;

        for (int ix = 0; ix < samples; ix++)
        {
            float x = startX + ix * stepX;
            for (int iz = 0; iz < samples; iz++)
            {
                float z = startZ + iz * stepZ;

                float rayStartY = totalBounds.max.y + 10f;
                var ray = new Ray(new Vector3(x, rayStartY, z), Vector3.down);
                var hits = Physics.RaycastAll(ray, rayStartY - totalBounds.min.y + 5f);

                foreach (var hit in hits)
                {
                    if (hit.distance < MIN_FLOOR_DIST) continue;
                    if (hit.normal.y < RELAXED_NORMAL_Y) continue;

                    float floorY = hit.point.y;
                    float playerY = floorY + MIN_FLOOR_DIST + CHARACTER_CENTER_Y;
                    Vector3 playerPos = new Vector3(x, playerY, z);

                    Vector3 capsuleBottom = new Vector3(x, floorY + MIN_FLOOR_DIST, z);
                    Vector3 capsuleTop = capsuleBottom + Vector3.up * (CHARACTER_HEIGHT - CHARACTER_RADIUS * 2);
                    if (Physics.CheckCapsule(capsuleBottom, capsuleTop, CHARACTER_RADIUS, ~0, QueryTriggerInteraction.Ignore))
                        continue;

                    var headRay = new Ray(playerPos + Vector3.up * (CHARACTER_HEIGHT * 0.5f), Vector3.up);
                    if (Physics.Raycast(headRay, RELAXED_HEAD_CLEARANCE, ~0, QueryTriggerInteraction.Ignore))
                        continue;

                    float forwardClear = CapsuleCastDistance(playerPos, Vector3.forward, cc);
                    float backClear = CapsuleCastDistance(playerPos, Vector3.back, cc);
                    float leftClear = CapsuleCastDistance(playerPos, Vector3.left, cc);
                    float rightClear = CapsuleCastDistance(playerPos, Vector3.right, cc);

                    if (forwardClear < RELAXED_WALL_CLEARANCE || backClear < RELAXED_WALL_CLEARANCE ||
                        leftClear < RELAXED_WALL_CLEARANCE || rightClear < RELAXED_WALL_CLEARANCE)
                        continue;

                    candidates.Add(new SpawnCandidate
                    {
                        position = playerPos,
                        floorY = floorY,
                        floorNormal = hit.normal,
                        capsuleOverlap = false,
                        headClear = true,
                        headSpace = RELAXED_HEAD_CLEARANCE,
                        forwardClear = forwardClear,
                        backClear = backClear,
                        leftClear = leftClear,
                        rightClear = rightClear
                    });
                }
            }
        }
        return candidates;
    }

    static void ApplySpawn(SpawnCandidate best, GameObject player, GameObject resetPose, CharacterController cc)
    {
        // 禁用 CharacterController 避免瞬移触发碰撞
        bool ccEnabled = cc.enabled;
        cc.enabled = false;

        player.transform.position = best.position;
        if (resetPose != null)
            resetPose.transform.position = best.position;

        cc.enabled = ccEnabled;

        Debug.Log($"Applied safe spawn to Player and ResetPose: {best.position}");

        // 场景标记脏
        EditorUtility.SetDirty(player);
        if (resetPose != null) EditorUtility.SetDirty(resetPose);
    }

    class SpawnCandidate
    {
        public Vector3 position;
        public float floorY;
        public Vector3 floorNormal;
        public bool capsuleOverlap;
        public bool headClear;
        public float headSpace;
        public float forwardClear;
        public float backClear;
        public float leftClear;
        public float rightClear;
    }
}