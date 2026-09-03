using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Eresoth
{
    /// <summary>运行时图元与材质工具：全部用内置几何体拼模型，零美术资源。</summary>
    public static class Gfx
    {
        static readonly Dictionary<Color, Material> cache = new();

        public static Material Mat(Color c)
        {
            if (cache.TryGetValue(c, out var m) && m != null) return m;
            // 兼容内置管线与 URP
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            var shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            var mat = new Material(shader);
            if (urp) mat.SetColor("_BaseColor", c); else mat.color = c;
            cache[c] = mat;
            return mat;
        }

        /// <summary>创建装饰用几何体（无碰撞体）。parent 为 null 时 pos 为世界坐标。</summary>
        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            if (parent != null) { go.transform.SetParent(parent, false); go.transform.localPosition = pos; }
            else go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(c);
            return go;
        }
    }
}
