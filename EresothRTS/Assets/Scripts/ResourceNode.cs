using UnityEngine;

namespace Eresoth
{
    /// <summary>资源点：树木(木头) / 魔法水晶(魔法矿)，可枯竭。</summary>
    public class ResourceNode : MonoBehaviour
    {
        public string kind;   // "wood" | "mana"
        public int amount = 1500;

        public static ResourceNode Spawn(string kind, Vector3 pos)
        {
            bool wood = kind == "wood";
            var root = new GameObject(wood ? "树木" : "魔法水晶");
            root.transform.position = pos;

            if (wood)
            {
                Gfx.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 1.2f, 0),
                         new Vector3(.5f, 1.2f, .5f), new Color(.40f, .28f, .15f));
                Gfx.Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 3f, 0),
                         Vector3.one * 2.4f, new Color(.15f, .45f, .20f));
                var col = root.AddComponent<SphereCollider>();
                col.center = new Vector3(0, 2f, 0); col.radius = 1.4f;
            }
            else
            {
                Color c = new Color(.30f, .85f, 1f);
                Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 1f, 0),
                         new Vector3(.8f, 2f, .8f), c).transform.rotation = Quaternion.Euler(0, 30, 10);
                Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(.7f, .6f, .3f),
                         new Vector3(.5f, 1.2f, .5f), c).transform.rotation = Quaternion.Euler(15, 10, -15);
                Gfx.Prim(PrimitiveType.Cube, root.transform, new Vector3(-.6f, .5f, -.4f),
                         new Vector3(.4f, 1f, .4f), c).transform.rotation = Quaternion.Euler(-10, 50, 20);
                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 1, 0); col.size = new Vector3(1.6f, 2, 1.6f);
            }

            var n = root.AddComponent<ResourceNode>();
            n.kind = kind;
            Game.I.nodes.Add(n);
            return n;
        }

        void OnDestroy() { if (Game.I != null) Game.I.nodes.Remove(this); }
    }
}
