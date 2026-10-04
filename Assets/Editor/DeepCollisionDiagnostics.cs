using UnityEngine;
using UnityEditor;

/// <summary>
/// 深度诊断碰撞问题：检查 MeshCollider 有效性、位置、缩放、层
/// </summary>
public class DeepCollisionDiagnostics
{
    [MenuItem("Tools/Gallery/Deep Collision Diagnostics")]
    public static void Diagnose()
    {
        Debug.Log("=== DEEP COLLISION DIAGNOSTICS ===");

        // 查找包含 HiddenCollisionWorld 的对象
        var hcw = GameObject.FindObjectOfType<GalleryRuntime.HiddenCollisionWorld>();
        if (hcw == null)
        {
            Debug.LogError("HiddenCollisionWorld not found in scene!");
            return;
        }
        var root = hcw.gameObject;

        // 1. 检查根对象变换
        Debug.Log($"Root Transform: pos={root.transform.position}, rot={root.transform.rotation.eulerAngles}, scale={root.transform.lossyScale}");

        // 2. 检查每个子 MeshFilter
        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        Debug.Log($"Total MeshFilters: {filters.Length}");

        int validColliders = 0;
        int invalidMeshes = 0;
        
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null)
            {
                Debug.LogWarning($"  {mf.name}: NO MESH!");
                continue;
            }

            var mc = mf.GetComponent<MeshCollider>();
            if (mc == null)
            {
                Debug.LogWarning($"  {mf.name}: NO MESHCOLLIDER - adding one");
                mc = mf.gameObject.AddComponent<MeshCollider>();
            }

            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            mc.enabled = true;

            // 检查 mesh 是否有效
            bool meshValid = true;
            if (mf.sharedMesh.vertexCount < 3)
            {
                Debug.LogError($"  {mf.name}: Mesh too small ({mf.sharedMesh.vertexCount} verts)");
                meshValid = false;
            }
            if (mf.sharedMesh.triangles.Length < 3)
            {
                Debug.LogError($"  {mf.name}: No triangles!");
                meshValid = false;
            }
            if (mf.sharedMesh.bounds.size == Vector3.zero)
            {
                Debug.LogError($"  {mf.name}: Zero bounds!");
                meshValid = false;
            }

            // 检查 MeshCollider 是否匹配
            if (mc.sharedMesh != mf.sharedMesh)
            {
                Debug.LogError($"  {mf.name}: MeshCollider.sharedMesh mismatch!");
                meshValid = false;
            }
            if (mc.convex)
            {
                Debug.LogError($"  {mf.name}: MeshCollider is CONVEX - should be false!");
                meshValid = false;
            }

            // 检查层
            if (mf.gameObject.layer != LayerMask.NameToLayer("Default"))
            {
                Debug.LogWarning($"  {mf.name}: Layer is {LayerMask.LayerToName(mf.gameObject.layer)} - setting to Default");
                mf.gameObject.layer = LayerMask.NameToLayer("Default");
            }

            // 世界空间 bounds
            var worldBounds = mc.bounds;
            Debug.Log($"  {mf.name}: verts={mf.sharedMesh.vertexCount}, tris={mf.sharedMesh.triangles.Length/3}, "
                + $"bounds=center{worldBounds.center} size{worldBounds.size}, "
                + $"collider.enabled={mc.enabled}, convex={mc.convex}, layer={LayerMask.LayerToName(mf.gameObject.layer)}, "
                + $"valid={meshValid}");

            if (meshValid) validColliders++;
            else invalidMeshes++;
        }

        // 3. 检查 Player
        var player = GameObject.Find("Player");
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null)
            {
                Debug.Log($"Player: pos={player.transform.position}, cc.enabled={cc.enabled}");
                Debug.Log($"  cc.radius={cc.radius}, height={cc.height}, center={cc.center}");
                Debug.Log($"  skinWidth={cc.skinWidth}, minMoveDistance={cc.minMoveDistance}");
                Debug.Log($"  stepOffset={cc.stepOffset}, slopeLimit={cc.slopeLimit}");
                Debug.Log($"  collisionFlags={cc.collisionFlags}");
                
                // 检查 Player 层
                Debug.Log($"  Player layer: {LayerMask.LayerToName(player.layer)}");
            }
        }

        // 4. 测试物理射线
        TestPhysicsRaycasts(root);

        // 5. 检查 Physics 设置
        Debug.Log($"Physics settings:");
        Debug.Log($"  defaultSolverIterations={Physics.defaultSolverIterations}");
        Debug.Log($"  defaultSolverVelocityIterations={Physics.defaultSolverVelocityIterations}");
        Debug.Log($"  queriesHitTriggers={Physics.queriesHitTriggers}");
        Debug.Log($"  queriesHitBackfaces={Physics.queriesHitBackfaces}");
        Debug.Log($"  reuseCollisionCallbacks={Physics.reuseCollisionCallbacks}");

        // 6. 应用调试材质确保可见
        var shader = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.color = new Color(1f, 0f, 1f, 0.35f);
        mat.SetFloat("_Mode", 3);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;

        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        foreach (var r in renderers)
        {
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.sharedMaterial = mat;
        }

        Debug.Log($"=== SUMMARY: {validColliders} valid colliders, {invalidMeshes} invalid ===");
        Debug.Log("=== Enter Play mode and test movement ===");
        SceneView.RepaintAll();
    }

    static void TestPhysicsRaycasts(GameObject root)
    {
        Debug.Log("=== Physics Raycast Tests ===");
        
        // 从上方向下射线
        var ray = new Ray(new Vector3(0, 10, 0), Vector3.down);
        var hits = Physics.RaycastAll(ray, 20f);
        Debug.Log($"Downward raycast from (0,10,0): {hits.Length} hits");
        foreach (var hit in hits)
        {
            Debug.Log($"  Hit: {hit.collider.name} at {hit.point}, normal={hit.normal}, dist={hit.distance}");
        }

        // 从玩家位置向四周
        var player = GameObject.Find("Player");
        if (player != null)
        {
            Vector3 playerPos = player.transform.position;
            Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            
            foreach (var dir in directions)
            {
                var r = new Ray(playerPos + Vector3.up * 0.5f, dir);
                var h = Physics.RaycastAll(r, 2f);
                Debug.Log($"  Raycast {dir} from {playerPos}: {h.Length} hits");
                foreach (var hit in h)
                {
                    Debug.Log($"    Hit: {hit.collider.name} at {hit.point}, dist={hit.distance}");
                }
            }
        }
    }
}