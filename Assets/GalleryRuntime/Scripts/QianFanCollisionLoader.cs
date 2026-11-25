// 前滩源碰撞网格加载器：运行时读取 StreamingAssets/QianFan/collision.ply，
// 构建 Mesh + MeshCollider（隐藏渲染），供人工筛查时 Physics.Raycast 贴面放置热点。
//
// collision.ply 是 binary_little_endian，element vertex (x,y,z) + element face (list uchar int vertex_indices)。
// 运行时构建一个 MeshCollider 用于射线命中；[ExecuteAlways] 让编辑器 Scene 视图也能命中。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GalleryRuntime
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class QianFanCollisionLoader : MonoBehaviour
    {
        [Tooltip("相对 StreamingAssets 的 collision.ply 路径")]
        public string collisionRelativePath = "QianFan/collision.ply";

        [Tooltip("展览 profile；设置后本组件的 collisionRelativePath 会被 profile.collisionPath 覆盖（便于多场展览共用脚本）")]
        public ExhibitionProfile profile;

        [Tooltip("构建后隐藏渲染（只留碰撞）")]
        public bool hideRenderers = true;

        [Tooltip("碰撞体所在层（建议单独一层，避免挡住交互射线之外的东西）")]
        public string collisionLayerName = "Default";

        private MeshCollider _collider;
        private Mesh _mesh;
        private bool _built;

        void OnEnable()
        {
            if (!_built) Build();
        }

        void OnDisable()
        {
            if (_collider != null) _collider.enabled = false;
        }

        private void ApplyProfile()
        {
            if (profile != null && !string.IsNullOrEmpty(profile.collisionPath))
                collisionRelativePath = profile.collisionPath;
        }

        [ContextMenu("Rebuild Collision")]
        public void Build()
        {
            ApplyProfile();
            var path = Path.Combine(Application.streamingAssetsPath, collisionRelativePath);
            if (!File.Exists(path))
            {
                Debug.LogError($"[QianFanCollision] 找不到 collision.ply: {path}");
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (verts, tris) = ReadPly(path);
            sw.Stop();
            Debug.Log($"[QianFanCollision] 解析 collision.ply: {verts.Length} 顶点, {tris.Length / 3} 三角面, {sw.ElapsedMilliseconds}ms");

            if (_mesh != null) DestroyImmediate(_mesh);
            _mesh = new Mesh { name = "QianFanCollisionMesh" };
            _mesh.indexFormat = verts.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();
            // 法线用于后续吸附/可视化（不做光照，可省略，但保留便于调试）
            _mesh.RecalculateNormals();

            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = _mesh;

            var mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
            if (hideRenderers) mr.enabled = false;

            _collider = GetComponent<MeshCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            // 106 万面的碰撞网格关闭 Fast Midphase，避免射线偶尔漏检（Unity 会为此报警告）
            _collider.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning
                                     | MeshColliderCookingOptions.WeldColocatedVertices;
            _collider.sharedMesh = null;          // 先清空，避免旧网格缓存
            _collider.sharedMesh = _mesh;
            _collider.convex = false;             // 非凸静态网格，射线命中精确
            _collider.enabled = true;

            int layer = LayerMask.NameToLayer(collisionLayerName);
            if (layer >= 0) gameObject.layer = layer;

            _built = true;
            Debug.Log($"[QianFanCollision] 碰撞体就绪，bounds={_mesh.bounds.size}");
        }

        // ---------- PLY 解析（binary_little_endian，vertex xyz + face list uchar int） ----------
        private static (Vector3[] verts, int[] tris) ReadPly(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // 读头部（ASCII，直到 end_header\n）
            var headerLines = new List<string>();
            string line;
            while (!string.IsNullOrEmpty(line = ReadAsciiLine(br)))
            {
                headerLines.Add(line);
                if (line.StartsWith("end_header")) break;
            }

            int vertexCount = 0, faceCount = 0;
            bool ascii = false;
            var vertProps = new List<string>();
            bool inVertex = false, inFace = false;
            var facePropTypes = new List<string>();

            foreach (var h in headerLines)
            {
                if (h.StartsWith("format ascii")) ascii = true;
                else if (h.StartsWith("format binary_little_endian")) ascii = false;
                else if (h.StartsWith("element vertex")) { vertexCount = int.Parse(h.Split(' ')[2]); inVertex = true; inFace = false; }
                else if (h.StartsWith("element face")) { faceCount = int.Parse(h.Split(' ')[2]); inFace = true; inVertex = false; }
                else if (h.StartsWith("property") && inVertex)
                {
                    // property float x / property float nx ...
                    var parts = h.Split(' ');
                    vertProps.Add(parts[^1]);
                }
                else if (h.StartsWith("property list") && inFace)
                {
                    // property list uchar int vertex_indices
                    facePropTypes.Add(h);
                }
            }

            if (ascii) throw new NotSupportedException("仅支持 binary_little_endian PLY");

            int stride = vertProps.Count * 4; // 全 float
            int xIdx = vertProps.IndexOf("x"), yIdx = vertProps.IndexOf("y"), zIdx = vertProps.IndexOf("z");
            if (xIdx < 0 || yIdx < 0 || zIdx < 0) throw new InvalidDataException("vertex 缺少 x/y/z");

            var verts = new Vector3[vertexCount];
            var buf = br.ReadBytes(vertexCount * stride);
            for (int i = 0; i < vertexCount; i++)
            {
                int o = i * stride;
                float x = BitConverter.ToSingle(buf, o + xIdx * 4);
                float y = BitConverter.ToSingle(buf, o + yIdx * 4);
                float z = BitConverter.ToSingle(buf, o + zIdx * 4);
                // collision.ply 是 LCC2 的 Z-up 坐标系，与 point_cloud.ply 同源。
                // Z-up → Y-up 的轴交换由所在容器的 -90° X 旋转承担（见 FixQianFanOrientation），
                // 这里保持原始坐标，不再做额外的 z 翻转。
                verts[i] = new Vector3(x, y, z);
            }

            // face: 每面 "uchar count" + count 个 int32 索引（构造上为 3）
            var tris = new List<int>(faceCount * 3);
            for (int i = 0; i < faceCount; i++)
            {
                byte n = br.ReadByte();
                if (n == 3)
                {
                    int a = br.ReadInt32(), b = br.ReadInt32(), c = br.ReadInt32();
                    // 顶点坐标保持原样（无 z 翻转），绕序也保持原样
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
                else
                {
                    for (int k = 0; k < n; k++) br.ReadInt32();
                }
            }

            return (verts, tris.ToArray());
        }

        private static string ReadAsciiLine(BinaryReader br)
        {
            var bytes = new List<byte>(64);
            while (true)
            {
                int b = br.BaseStream.ReadByte();
                if (b < 0) break;
                if (b == '\n') break;
                if (b != '\r') bytes.Add((byte)b);
            }
            return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
        }
    }
}
