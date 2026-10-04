using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

/// <summary>
/// 详细分析碰撞网格结构：边界、法线、薄壳检测、体积封闭性
/// </summary>
public class AnalyzeCollisionMesh
{
    [MenuItem("Tools/Gallery/Analyze Collision Mesh")]
    public static void Analyze()
    {
        Debug.Log("=== ANALYZE COLLISION MESH ===");

        var hcw = GameObject.FindObjectOfType<GalleryRuntime.HiddenCollisionWorld>();
        if (hcw == null)
        {
            Debug.LogError("HiddenCollisionWorld not found!");
            return;
        }
        var root = hcw.transform.root;

        // 确保碰撞体已构建
        hcw.buildOnAwake = false;
        hcw.hideRenderers = false;
        hcw.BuildHiddenColliders();

        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        var colliders = root.GetComponentsInChildren<MeshCollider>(true);

        Debug.Log($"MeshFilters: {filters.Length}, MeshColliders: {colliders.Length}");

        long totalVerts = 0;
        long totalTris = 0;
        Bounds totalBounds = new Bounds();
        bool firstBounds = true;

        // 统计所有子 mesh
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            
            totalVerts += mf.sharedMesh.vertexCount;
            totalTris += mf.sharedMesh.triangles.Length / 3;
            
            var bounds = mf.sharedMesh.bounds;
            if (firstBounds) { totalBounds = bounds; firstBounds = false; }
            else totalBounds.Encapsulate(bounds);

            // 分析 mesh 结构
            AnalyzeMeshStructure(mf.name, mf.sharedMesh);
        }

        Debug.Log($"=== TOTAL ===");
        Debug.Log($"Total vertices: {totalVerts}, triangles: {totalTris}");
        Debug.Log($"Total bounds: center={totalBounds.center}, size={totalBounds.size}");

        // 检查 MeshCollider
        foreach (var mc in colliders)
        {
            Debug.Log($"MeshCollider: {mc.gameObject.name}, enabled={mc.enabled}, convex={mc.convex}, bounds={mc.bounds}");
        }

        // 3D 网格采样测试内部/外部
        TestVolumeEnclosure(totalBounds, root);

        // 法线分布统计
        AnalyzeNormals(filters);

        // 薄壳检测
        DetectThinShells(filters);
    }

    static void AnalyzeMeshStructure(string name, Mesh mesh)
    {
        Debug.Log($"--- Mesh: {name} ---");
        Debug.Log($"  Vertices: {mesh.vertexCount}, Triangles: {mesh.triangles.Length / 3}");
        Debug.Log($"  Bounds: center={mesh.bounds.center}, size={mesh.bounds.size}");
        
        // 检查是否有法线
        if (mesh.normals != null && mesh.normals.Length > 0)
            Debug.Log($"  Has normals: {mesh.normals.Length}");
        else
            Debug.LogWarning($"  NO NORMALS!");

        // 检查 UV
        if (mesh.uv != null && mesh.uv.Length > 0)
            Debug.Log($"  Has UV: {mesh.uv.Length}");
        
        // 简单的封闭性检查：边界边统计
        var edges = new Dictionary<EdgeKey, int>();
        var tris = mesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
        {
            AddEdge(edges, tris[i], tris[i+1]);
            AddEdge(edges, tris[i+1], tris[i+2]);
            AddEdge(edges, tris[i+2], tris[i]);
        }
        
        int boundaryEdges = 0;
        foreach (var kv in edges)
        {
            if (kv.Value == 1) boundaryEdges++;
        }
        
        Debug.Log($"  Edges: total={edges.Count}, boundary={boundaryEdges}, interior={edges.Count - boundaryEdges}");
        if (boundaryEdges > 0)
            Debug.LogWarning($"  Mesh has {boundaryEdges} boundary edges (not watertight)");
    }

    struct EdgeKey : IEquatable<EdgeKey>
    {
        public int a, b;
        public EdgeKey(int v1, int v2) { a = Math.Min(v1, v2); b = Math.Max(v1, v2); }
        public bool Equals(EdgeKey other) => a == other.a && b == other.b;
        public override int GetHashCode() => a * 1000000 + b;
        public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
    }

    static void AddEdge(Dictionary<EdgeKey, int> edges, int v1, int v2)
    {
        var key = new EdgeKey(v1, v2);
        if (edges.ContainsKey(key)) edges[key]++;
        else edges[key] = 1;
    }

    static void TestVolumeEnclosure(Bounds bounds, Transform root)
    {
        Debug.Log("=== VOLUME ENCLOSURE TEST ===");
        
        // 在 bounds 内部采样点，测试是否在碰撞体内部
        int samples = 10;
        int insideCount = 0;
        
        for (int ix = 0; ix < samples; ix++)
        {
            float x = bounds.min.x + (bounds.size.x / samples) * (ix + 0.5f);
            for (int iy = 0; iy < samples; iy++)
            {
                float y = bounds.min.y + (bounds.size.y / samples) * (iy + 0.5f);
                for (int iz = 0; iz < samples; iz++)
                {
                    float z = bounds.min.z + (bounds.size.z / samples) * (iz + 0.5f);
                    Vector3 p = new Vector3(x, y, z);
                    
                    // 向 6 个方向射线，奇数次击中 = 内部
                    int hits = 0;
                    Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
                    foreach (var dir in dirs)
                    {
                        var ray = new Ray(p, dir);
                        if (Physics.Raycast(ray, bounds.size.magnitude, ~0, QueryTriggerInteraction.Ignore))
                            hits++;
                    }
                    
                    if (hits >= 5) // 几乎所有方向都击中 = 可能在实体内部
                        insideCount++;
                }
            }
        }
        
        Debug.Log($"Volume test: {insideCount}/{samples*samples*samples} points inside solid");
        if (insideCount == 0)
            Debug.LogWarning("NO interior points found - mesh may be hollow/thin-shell only");
    }

    static void AnalyzeNormals(MeshFilter[] filters)
    {
        Debug.Log("=== NORMAL DISTRIBUTION ===");
        
        int upCount = 0, downCount = 0, sideCount = 0;
        int totalTris = 0;
        
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null || mf.sharedMesh.normals == null) continue;
            
            var normals = mf.sharedMesh.normals;
            var tris = mf.sharedMesh.triangles;
            
            for (int i = 0; i < tris.Length; i += 3)
            {
                // 三角形面法线（三个顶点法线平均）
                Vector3 n = (normals[tris[i]] + normals[tris[i+1]] + normals[tris[i+2]]).normalized;
                
                if (n.y > 0.7f) upCount++;
                else if (n.y < -0.7f) downCount++;
                else sideCount++;
                
                totalTris++;
            }
        }
        
        Debug.Log($"Total triangles analyzed: {totalTris}");
        Debug.Log($"  Up (floor/ceiling): {upCount} ({100f*upCount/totalTris:F1}%)");
        Debug.Log($"  Down: {downCount} ({100f*downCount/totalTris:F1}%)");
        Debug.Log($"  Side (walls): {sideCount} ({100f*sideCount/totalTris:F1}%)");
    }

    static void DetectThinShells(MeshFilter[] filters)
    {
        Debug.Log("=== THIN SHELL DETECTION ===");
        
        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            
            var mesh = mf.sharedMesh;
            var bounds = mesh.bounds;
            
            // 计算最薄维度
            float minDim = Mathf.Min(bounds.size.x, bounds.size.y, bounds.size.z);
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float ratio = minDim / maxDim;
            
            Debug.Log($"Mesh {mf.name}: bounds={bounds.size}, min/max ratio={ratio:F3}");
            
            if (ratio < 0.05f)
                Debug.LogWarning($"  -> THIN SHELL detected (ratio < 0.05)");
            
            // 检查法线方向一致性（同一平面的三角形法线应该相近）
            if (mesh.normals != null && mesh.normals.Length > 0 && mesh.triangles.Length > 0)
            {
                var tris = mesh.triangles;
                var normals = mesh.normals;
                
                // 采样检查法线一致性
                int consistent = 0;
                int checkedTris = Mathf.Min(100, tris.Length / 3);
                
                for (int i = 0; i < checkedTris * 3; i += 3)
                {
                    Vector3 n1 = (mesh.normals[tris[i]] + mesh.normals[tris[i+1]] + mesh.normals[tris[i+2]]).normalized;
                    
                    // 与相邻三角形比较
                    bool foundSimilar = false;
                    for (int j = 0; j < Mathf.Min(10, tris.Length / 3) * 3; j += 3)
                    {
                        if (j == i) continue;
                        Vector3 n2 = (mesh.normals[tris[j]] + mesh.normals[tris[j+1]] + mesh.normals[tris[j+2]]).normalized;
                        if (Vector3.Dot(n1, n2) > 0.95f) { foundSimilar = true; break; }
                    }
                    if (foundSimilar) consistent++;
                }
                
                Debug.Log($"  Normal consistency: {consistent}/{checkedTris} triangles have similar neighbors");
            }
        }
    }
}