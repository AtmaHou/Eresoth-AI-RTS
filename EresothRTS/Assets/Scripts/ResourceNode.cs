using UnityEngine;

namespace Eresoth
{
    /// <summary>资源点：树木(木头) / 魔法水晶(魔法矿)，可枯竭。</summary>
    public class ResourceNode : MonoBehaviour
    {
        public string kind;   // "wood" | "mana"
        public int amount = 1500;

        static readonly System.Random visualRnd = new(20240607);

        public static ResourceNode Spawn(string kind, Vector3 pos, int amount = 1500)
        {
            bool wood = kind == "wood";
            var root = new GameObject(wood ? "树木" : "魔法水晶");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0, (float)visualRnd.NextDouble() * 360f, 0);

            if (wood)
            {
                Models.Tree(root.transform, visualRnd);
                var col = root.AddComponent<SphereCollider>();
                col.center = new Vector3(0, 2f, 0); col.radius = 1.4f;
            }
            else
            {
                Models.ManaCrystal(root.transform, visualRnd);
                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 1, 0); col.size = new Vector3(1.8f, 2, 1.8f);
            }

            var n = root.AddComponent<ResourceNode>();
            n.kind = kind;
            n.amount = amount;
            Game.I.nodes.Add(n);
            return n;
        }

        void OnDestroy() { if (Game.I != null) Game.I.nodes.Remove(this); }
    }
}
