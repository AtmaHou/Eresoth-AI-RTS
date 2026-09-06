using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eresoth
{
    /// <summary>运行时建模与材质工具：纯代码生成低多边形风格化模型，零美术资源。
    /// 升级版：支持金属度/光滑度/自发光材质，内置常用网格生成器（屋顶/棱柱/锥台/平板），
    /// 并附带建模助手（批量上色、随机抖动、软阴影参数）。</summary>
    public static class Gfx
    {
        static readonly Dictionary<long, Material> cache = new();
        static readonly Dictionary<string, Mesh> meshCache = new();

        // ---------------------------------------------------------------
        //  材质
        // ---------------------------------------------------------------

        public static Material Mat(Color c) => Mat(c, 0f, 0.35f, false);

        /// <summary>带 PBR 参数的材质。metallic 0..1，smooth 0..1，emissive 让颜色自发光，emisMul 控制发光强度。</summary>
        public static Material Mat(Color c, float metallic, float smooth, bool emissive = false, float emisMul = 1.5f)
        {
            long key = Hash(c, metallic, smooth, emissive, emisMul);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            var shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            var mat = new Material(shader);
            if (urp)
            {
                mat.SetColor("_BaseColor", c);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", smooth);
                if (emissive)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", c * emisMul);
                }
            }
            else
            {
                mat.color = c;
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Glossiness", smooth);
                if (emissive)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", c * emisMul);
                }
            }
            cache[key] = mat;
            return mat;
        }

        static long Hash(Color c, float m, float s, bool e, float em)
        {
            int r = Mathf.RoundToInt(c.r * 255), g = Mathf.RoundToInt(c.g * 255),
                b = Mathf.RoundToInt(c.b * 255), a = Mathf.RoundToInt(c.a * 255);
            long h = ((long)(uint)r << 40) | ((long)(uint)g << 32) | ((long)(uint)b << 24) | ((long)(uint)a << 16)
                   | ((long)(uint)Mathf.RoundToInt(m * 100) << 8) | (uint)Mathf.RoundToInt(s * 100);
            h ^= ((long)Mathf.RoundToInt(em * 20)) << 1;
            return e ? h | 1 : h & ~1L;
        }

        // ---------------------------------------------------------------
        //  图元创建
        // ---------------------------------------------------------------

        /// <summary>创建装饰用几何体（无碰撞体）。parent 为 null 时 pos 为世界坐标。</summary>
        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c)
        {
            return Prim(type, parent, pos, scale, c, 0f, 0.35f, false);
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c,
                                      float metallic, float smooth, bool emissive = false, float emisMul = 1.5f)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            if (parent != null) { go.transform.SetParent(parent, false); go.transform.localPosition = pos; }
            else go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(c, metallic, smooth, emissive, emisMul);
            return go;
        }

        /// <summary>创建自定义网格物体。</summary>
        public static GameObject MeshGo(Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Color c,
                                        float metallic = 0f, float smooth = 0.35f, bool emissive = false,
                                        float emisMul = 1.5f)
        {
            var go = new GameObject("mesh");
            if (parent != null) { go.transform.SetParent(parent, false); go.transform.localPosition = pos; }
            else go.transform.position = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat(c, metallic, smooth, emissive, emisMul);
            return go;
        }

        // ---------------------------------------------------------------
        //  常用网格生成器（全部扁平着色风格，配合低多边形美术方向）
        // ---------------------------------------------------------------

        /// <summary>四棱锥屋顶（底面长宽 1，高 1，可再缩放）。ridge=true 时屋脊沿 X 方向拉成长条形。</summary>
        public static Mesh Roof(bool ridge = false)
        {
            string key = ridge ? "roof_ridge" : "roof_pyramid";
            if (meshCache.TryGetValue(key, out var m) && m != null) return m;
            // 底面四角
            var v = new List<Vector3>
            {
                new(-.5f, 0, -.5f), new(.5f, 0, -.5f), new(.5f, 0, .5f), new(-.5f, 0, .5f)
            };
            var tri = new List<int>();
            if (!ridge)
            {
                v.Add(new Vector3(0, 1, 0));                       // 顶点 index=4
                tri.AddRange(new[] { 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0 });
            }
            else
            {
                v.Add(new Vector3(-.5f, 1, 0));                    // 屋脊端点 A index=4
                v.Add(new Vector3(.5f, 1, 0));                     // 屋脊端点 B index=5
                // 前后两个三角坡
                tri.AddRange(new[] { 0, 4, 5, 0, 5, 1 });          // 后坡 (-Z)
                tri.AddRange(new[] { 2, 5, 4, 2, 4, 3 });          // 前坡 (+Z)
                tri.AddRange(new[] { 1, 5, 2 });                   // 右山墙
                tri.AddRange(new[] { 3, 4, 0 });                   // 左山墙
            }
            return CacheMesh(key, v.ToArray(), tri.ToArray());
        }

        /// <summary>三棱柱（长 1 高 1 宽 1，屋脊沿 X），常用于尖顶小屋/帐篷。</summary>
        public static Mesh Prism()
        {
            const string key = "prism";
            if (meshCache.TryGetValue(key, out var m) && m != null) return m;
            var v = new Vector3[]
            {
                new(-.5f, 0, -.5f), new(.5f, 0, -.5f), new(.5f, 0, .5f), new(-.5f, 0, .5f), // 底
                new(-.5f, 1, 0),    new(.5f, 1, 0)                                            // 脊 4,5
            };
            var tri = new[]
            {
                0, 4, 5, 0, 5, 1,   // 后坡
                2, 5, 4, 2, 4, 3,   // 前坡
                1, 5, 2,            // 右山墙
                3, 4, 0             // 左山墙
            };
            return CacheMesh(key, v, tri);
        }

        /// <summary>锥台（下半径 .5，上半径 r，高 1，n 边形）。</summary>
        public static Mesh Frustum(float topR = 0.35f, int n = 6)
        {
            string key = $"frustum_{topR}_{n}";
            if (meshCache.TryGetValue(key, out var m) && m != null) return m;
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                v.Add(new Vector3(Mathf.Cos(a) * .5f, 0, Mathf.Sin(a) * .5f));       // 底圈 0..n-1
                v.Add(new Vector3(Mathf.Cos(a) * topR, 1, Mathf.Sin(a) * topR));     // 顶圈 n..2n-1
            }
            for (int i = 0; i < n; i++)
            {
                int i2 = (i + 1) % n;
                int b0 = i * 2, t0 = i * 2 + 1, b1 = i2 * 2, t1 = i2 * 2 + 1;
                tri.AddRange(new[] { b0, t0, t1, b0, t1, b1 });
            }
            // 顶盖扇
            int cIdx = v.Count;
            v.Add(new Vector3(0, 1, 0));
            for (int i = 0; i < n; i++)
            {
                int i2 = (i + 1) % n;
                tri.AddRange(new[] { cIdx, i * 2 + 1, i2 * 2 + 1 });
            }
            return CacheMesh(key, v.ToArray(), tri.ToArray());
        }

        /// <summary>薄平板（1×1，位于 XZ 平面，法线朝上），用于地面贴片、地毯、路面。</summary>
        public static Mesh Quad()
        {
            const string key = "quad";
            if (meshCache.TryGetValue(key, out var m) && m != null) return m;
            var v = new[]
            {
                new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f),
                new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f)
            };
            var tri = new[] { 0, 2, 1, 0, 3, 2 };
            return CacheMesh(key, v, tri);
        }

        /// <summary>XZ 平面环形网格（内外半径，n 段），用于选中环/符文光环/法阵。</summary>
        public static Mesh RingMesh(float inner = .42f, float outer = .5f, int n = 24)
        {
            string key = $"ring_{inner}_{outer}_{n}";
            if (meshCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                v.Add(new Vector3(Mathf.Cos(a) * outer, 0, Mathf.Sin(a) * outer));
                v.Add(new Vector3(Mathf.Cos(a) * inner, 0, Mathf.Sin(a) * inner));
                if (i < n)
                {
                    int b = i * 2;
                    tri.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            return CacheMesh(key, v.ToArray(), tri.ToArray());
        }

        /// <summary>菱形晶体（双锥，底半径 .5，高 1）。</summary>
        public static Mesh Crystal()
        {
            const string key = "crystal";
            if (meshCache.TryGetValue(key, out var m) && m != null) return m;
            const int n = 5;
            var v = new List<Vector3> { new(0, -.4f, 0) };            // 下尖 0
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                v.Add(new Vector3(Mathf.Cos(a) * .5f, 0, Mathf.Sin(a) * .5f)); // 腰 1..n
            }
            v.Add(new Vector3(0, .6f, 0));                              // 上尖 n+1
            var tri = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n;
                tri.AddRange(new[] { 0, b, a });            // 下锥
                tri.AddRange(new[] { n + 1, a, b });        // 上锥
            }
            return CacheMesh(key, v.ToArray(), tri.ToArray());
        }

        static Mesh CacheMesh(string key, Vector3[] verts, int[] tris)
        {
            var mesh = new Mesh { name = key, vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshCache[key] = mesh;
            return mesh;
        }

        // ---------------------------------------------------------------
        //  建模助手
        // ---------------------------------------------------------------

        /// <summary>颜色变体：亮度在 [minMul,maxMul] 之间抖动，用于同材质群体去重复感。</summary>
        public static Color Vary(Color c, System.Random rnd, float minMul = 0.85f, float maxMul = 1.1f)
        {
            float k = Mathf.Lerp(minMul, maxMul, (float)rnd.NextDouble());
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
        }

        public static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

        /// <summary>加噪声的三维随机偏移。</summary>
        public static Vector3 Jitter(System.Random rnd, float radius)
        {
            var c = new Vector2((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1));
            return new Vector3(c.x, 0, c.y) * radius;
        }
    }
}
