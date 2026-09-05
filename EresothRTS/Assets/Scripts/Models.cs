using UnityEngine;

namespace Eresoth
{
    /// <summary>程序化模型库：用代码拼装低多边形风格化模型，
    /// 让建筑/单位/植被达到"风格化商业产品"的观感而不是几何体堆叠。
    /// 所有方法只负责视觉，不挂逻辑组件、不加碰撞体。</summary>
    public static class Models
    {
        // =================================================================
        //  调色板（两阵营风格化配色，全项目统一）
        // =================================================================

        public static class Pal
        {
            // 人类
            public static readonly Color HWall   = new(0.78f, 0.71f, 0.58f); // 米白木墙
            public static readonly Color HBeam   = new(0.42f, 0.30f, 0.18f); // 深木梁
            public static readonly Color HRoof   = new(0.55f, 0.28f, 0.16f); // 红陶瓦
            public static readonly Color HRoof2  = new(0.62f, 0.34f, 0.20f);
            public static readonly Color HStone  = new(0.62f, 0.61f, 0.57f);
            public static readonly Color HGold   = new(0.95f, 0.78f, 0.30f);
            public static readonly Color HCloth  = new(0.18f, 0.36f, 0.62f); // 蓝旗布
            public static readonly Color HWindow = new(1.00f, 0.85f, 0.45f); // 暖窗灯

            // 不死
            public static readonly Color UWall   = new(0.30f, 0.27f, 0.36f); // 暗紫石墙
            public static readonly Color UBeam   = new(0.20f, 0.16f, 0.24f);
            public static readonly Color URoof   = new(0.36f, 0.18f, 0.45f); // 暗紫瓦
            public static readonly Color URoof2  = new(0.44f, 0.24f, 0.55f);
            public static readonly Color UStone  = new(0.40f, 0.38f, 0.46f);
            public static readonly Color UBone   = new(0.85f, 0.82f, 0.72f); // 骨白
            public static readonly Color UCloth  = new(0.35f, 0.14f, 0.48f);
            public static readonly Color UGlow   = new(0.45f, 1.00f, 0.75f); // 死灵绿光
            public static readonly Color UGlow2  = new(0.75f, 0.45f, 1.00f); // 灵魂紫光

            // 通用
            public static readonly Color Iron    = new(0.55f, 0.56f, 0.60f);
            public static readonly Color IronD   = new(0.33f, 0.34f, 0.38f);
            public static readonly Color Leather = new(0.45f, 0.30f, 0.18f);
            public static readonly Color WoodD   = new(0.38f, 0.26f, 0.14f);
            public static readonly Color WoodL   = new(0.55f, 0.40f, 0.22f);
            public static readonly Color Dirt    = new(0.42f, 0.33f, 0.22f);
            public static readonly Color Stone   = new(0.50f, 0.50f, 0.52f);
        }

        static bool Human(Team t) => t == Team.Player;

        // =================================================================
        //  建筑
        // =================================================================

        /// <summary>建筑总入口：按 kind 分派到具体造型。root 已位于建筑原点（地面）。</summary>
        public static void BuildBuilding(Transform root, Team team, string kind, float s)
        {
            bool h = Human(team);
            switch (kind)
            {
                case "hall":                        Hall(root, h, s); break;
                case "barracks": case "crypt":      Barracks(root, h, s); break;
                case "archery": case "dark_temple": Archery(root, h, s); break;
                case "stable": case "death_stable": Stable(root, h, s); break;
                case "lumber":                      Lumber(root, h, s); break;
                case "house":                       House(root, h, s); break;
                default:                            Tower(root, h, s); break;
            }
        }

        // ---------- 通用小件 ----------

        /// <summary>石质基座：两级台阶，让建筑"落地"而不是浮在地上。</summary>
        static void Plinth(Transform r, Color stone, float w, float d)
        {
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .10f, 0), new Vector3(w + .5f, .20f, d + .5f), stone, 0f, .25f);
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .26f, 0), new Vector3(w + .2f, .12f, d + .2f), stone * 1.07f, 0f, .25f);
        }

        /// <summary>坡屋顶（山墙沿 X 轴），带一层深色屋檐。</summary>
        static void GableRoof(Transform r, Vector3 pos, float w, float h, float d, Color tile, Color tileDark, bool alongX = true)
        {
            var roof = Gfx.MeshGo(Gfx.Roof(true), r, pos, new Vector3(w, h, d), tile, 0f, .3f);
            if (!alongX) roof.transform.localRotation = Quaternion.Euler(0, 90, 0);
            // 屋檐稍宽一圈的暗色薄板，形成层次
            var eave = Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * .02f,
                alongX ? new Vector3(w + .18f, .05f, d + .18f) : new Vector3(d + .18f, .05f, w + .18f),
                tileDark, 0f, .25f);
            eave.transform.localPosition = pos + Vector3.up * .02f;
        }

        /// <summary>尖塔屋顶（四棱锥）。</summary>
        static void Spire(Transform r, Vector3 pos, float w, float h, Color c)
        {
            Gfx.MeshGo(Gfx.Roof(false), r, pos, new Vector3(w, h, w), c, 0f, .32f);
        }

        /// <summary>旗帜：旗杆 + 三角旗面（用斜置薄立方模拟）。</summary>
        static void Flag(Transform r, Vector3 pos, float poleH, Color cloth)
        {
            Gfx.Prim(PrimitiveType.Cylinder, r, pos + Vector3.up * poleH * .5f,
                     new Vector3(.05f, poleH * .5f, .05f), Pal.IronD, .7f, .5f);
            var flag = Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * (poleH - .12f) + Vector3.right * .30f,
                     new Vector3(.55f, .30f, .03f), cloth, 0f, .45f);
            flag.transform.localRotation = Quaternion.Euler(0, 0, -8);
            Gfx.Prim(PrimitiveType.Sphere, r, pos + Vector3.up * (poleH + .04f),
                     Vector3.one * .09f, Pal.HGold, .8f, .7f);
        }

        /// <summary>发光窗：自发光小方块，让建筑"有人住"。</summary>
        static void Windows(Transform r, Color glow, params Vector3[] pos)
        {
            foreach (var p in pos)
                Gfx.Prim(PrimitiveType.Cube, r, p, new Vector3(.22f, .28f, .04f), glow, 0f, .6f, true);
        }

        /// <summary>门：门板 + 门楣。</summary>
        static void Door(Transform r, Vector3 pos, float w, float h, Color wood, Color frame)
        {
            Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * h * .5f, new Vector3(w + .14f, h + .1f, .10f), frame, 0f, .3f);
            Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * h * .5f + Vector3.forward * .03f, new Vector3(w, h, .06f), wood, 0f, .28f);
        }

        /// <summary>烟囱（带回烟帽）。</summary>
        static void Chimney(Transform r, Vector3 pos, Color stone)
        {
            Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * .3f, new Vector3(.30f, .60f, .30f), stone, 0f, .25f);
            Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * .64f, new Vector3(.40f, .10f, .40f), stone * 1.1f, 0f, .25f);
        }

        // ---------- 人族主基地 ----------

        static void Hall(Transform r, bool h, float s)
        {
            if (h) HallHuman(r, s); else HallUndead(r, s);
        }

        static void HallHuman(Transform r, float s)
        {
            float w = s * .95f, d = s * .85f;
            Plinth(r, Pal.HStone, w + .6f, d + .6f);

            // 主楼两层
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .32f + s * .30f, 0), new Vector3(w, s * .60f, d), Pal.HWall, 0f, .3f);
            // 木筋（人族半木结构特色）
            foreach (float x in new[] { -w * .38f, 0, w * .38f })
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(x, .32f + s * .30f, d * .5f + .01f),
                         new Vector3(.10f, s * .58f, .04f), Pal.HBeam, 0f, .25f);
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .32f + s * .52f, d * .5f + .01f),
                     new Vector3(w, .10f, .04f), Pal.HBeam, 0f, .25f);
            // 上层挑檐 + 第二层
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .32f + s * .62f, 0), new Vector3(w * 1.06f, .10f, d * 1.06f), Pal.HBeam, 0f, .25f);
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .32f + s * .82f, 0), new Vector3(w * .78f, s * .30f, d * .78f), Pal.HWall, 0f, .3f);

            // 大屋顶
            GableRoof(r, new Vector3(0, .32f + s * .98f, 0), w * .92f, s * .34f, d * 1.00f, Pal.HRoof, Pal.HRoof2, true);
            Chimney(r, new Vector3(w * .28f, .32f + s * 1.16f, 0), Pal.HStone);

            // 角塔 ×2
            foreach (float side in new[] { -1, 1 })
            {
                var p = new Vector3(side * (w * .5f - .1f), 0, d * .5f - .1f);
                Gfx.Prim(PrimitiveType.Cylinder, r, p + Vector3.up * (s * .55f),
                         new Vector3(.62f, s * .55f, .62f), Pal.HStone, 0f, .28f);
                Spire(r, p + Vector3.up * (s * 1.10f), .85f, s * .30f, Pal.HRoof);
            }
            // 正面高塔（带旗）
            var tp = new Vector3(0, 0, -d * .42f);
            Gfx.Prim(PrimitiveType.Cube, r, tp + Vector3.up * (s * .85f), new Vector3(.70f, s * 1.05f, .70f), Pal.HWall, 0f, .3f);
            Gfx.Prim(PrimitiveType.Cube, r, tp + Vector3.up * (s * 1.40f), new Vector3(.82f, .12f, .82f), Pal.HBeam, 0f, .25f);
            Spire(r, tp + Vector3.up * (s * 1.46f), .78f, s * .36f, Pal.HRoof);
            Flag(r, tp + Vector3.up * (s * 1.82f), .9f, Pal.HCloth);

            Door(r, new Vector3(0, .30f, d * .5f), .9f, 1.4f, Pal.HBeam, Pal.HStone);
            Windows(r, Pal.HWindow,
                new Vector3(-w * .28f, s * .38f, d * .5f + .02f),
                new Vector3(w * .28f, s * .38f, d * .5f + .02f),
                new Vector3(-w * .28f, s * .80f, d * .4f + .02f),
                new Vector3(w * .28f, s * .80f, d * .4f + .02f));

            // 门口石阶 + 两侧木箱/木桶点缀
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .10f, d * .5f + .55f), new Vector3(1.4f, .20f, .9f), Pal.HStone, 0f, .25f);
            Barrel(r, new Vector3(-1.5f, .30f, d * .5f + .6f));
            Crate(r, new Vector3(1.5f, .30f, d * .5f + .55f), .5f);
        }

        // ---------- 不死主基地（通灵塔风格） ----------

        static void HallUndead(Transform r, float s)
        {
            float w = s * .9f;
            Plinth(r, Pal.UStone, w + .6f, w + .6f);

            // 主塔身：八边锥台叠两层
            Gfx.MeshGo(Gfx.Frustum(.42f, 8), r, new Vector3(0, .30f, 0),
                       new Vector3(w, s * .85f, w), Pal.UWall, 0f, .3f);
            Gfx.MeshGo(Gfx.Frustum(.30f, 8), r, new Vector3(0, .30f + s * .85f, 0),
                       new Vector3(w * .8f, s * .55f, w * .8f), Pal.UWall * 1.1f, 0f, .3f);

            // 塔顶尖顶盖（八边锥顶，与塔身同棱数）：之前塔顶露天，悬浮水晶直接插进塔身穿模
            Gfx.MeshGo(Gfx.Frustum(.06f, 8), r, new Vector3(0, .30f + s * 1.40f, 0),
                       new Vector3(w * .9f, s * .22f, w * .9f), Pal.URoof, 0f, .3f);
            Gfx.Prim(PrimitiveType.Cylinder, r, new Vector3(0, .30f + s * 1.42f, 0),
                     new Vector3(w * .97f, .05f, w * .97f), Pal.UBeam, 0f, .25f);

            // 顶部悬浮死灵水晶：置于尖顶盖之上，与塔身分离不再穿模
            var cry = Gfx.MeshGo(Gfx.Crystal(), r, new Vector3(0, .30f + s * 2.25f, 0),
                       new Vector3(.7f, 1.1f, .7f), Pal.UGlow, 0f, .8f, true);
            cry.transform.localRotation = Quaternion.Euler(0, 25, 0);

            // 四根骨刺环绕
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f + .4f;
                var p = new Vector3(Mathf.Cos(a) * w * .72f, 0, Mathf.Sin(a) * w * .72f);
                BoneSpike(r, p, s * .55f, Pal.UBone);
            }
            // 底部幽绿鬼火
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f;
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(Mathf.Cos(a) * w * .6f, .45f, Mathf.Sin(a) * w * .6f),
                         Vector3.one * .18f, Pal.UGlow, 0f, .9f, true);
            }

            Door(r, new Vector3(0, .28f, w * .44f), .8f, 1.2f, Pal.UBeam, Pal.UStone);
            Windows(r, Pal.UGlow,
                new Vector3(-w * .22f, s * .45f, w * .42f),
                new Vector3(w * .22f, s * .45f, w * .42f),
                new Vector3(0, s * 1.05f, w * .34f));
        }

        /// <summary>骨刺：底部粗、顶部尖的锥台，微倾。</summary>
        static void BoneSpike(Transform r, Vector3 pos, float h, Color bone)
        {
            var spike = Gfx.MeshGo(Gfx.Frustum(.12f, 5), r, pos, new Vector3(.45f, h, .45f), bone, 0f, .35f);
            spike.transform.localRotation = Quaternion.Euler(pos.z * 4f, 0, -pos.x * 4f);
        }

        // ---------- 兵营 / 地穴 ----------

        static void Barracks(Transform r, bool h, float s)
        {
            float w = s, d = s * .85f;
            if (h)
            {
                Plinth(r, Pal.HStone, w, d);
                // 单层长屋
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .30f + s * .28f, 0), new Vector3(w, s * .56f, d), Pal.HWall, 0f, .3f);
                GableRoof(r, new Vector3(0, .30f + s * .57f, 0), w * 1.04f, s * .30f, d * 1.02f, Pal.HRoof, Pal.HRoof2, true);
                // 训练场立柱 + 武器架
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(w * .62f, s * .30f, .2f), new Vector3(.12f, s * .6f, .12f), Pal.HBeam, 0f, .25f);
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(w * .62f, s * .30f, -.2f), new Vector3(.12f, s * .6f, .12f), Pal.HBeam, 0f, .25f);
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(w * .62f, s * .55f, 0), new Vector3(.10f, .08f, .7f), Pal.HBeam, 0f, .25f);
                SwordProp(r, new Vector3(w * .66f, s * .35f, .2f), .7f);
                ShieldProp(r, new Vector3(w * .66f, s * .32f, -.2f), .5f, Pal.HCloth);
                Door(r, new Vector3(0, .28f, d * .5f), .8f, 1.2f, Pal.HBeam, Pal.HStone);
                Windows(r, Pal.HWindow, new Vector3(-w * .3f, s * .32f, d * .5f + .02f), new Vector3(w * .3f, s * .32f, d * .5f + .02f));
                Chimney(r, new Vector3(-w * .35f, .30f + s * .78f, 0), Pal.HStone);
            }
            else
            {
                // 地穴：半埋土坡 + 白骨拱门 + 幽绿内光
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(0, 0, 0), new Vector3(w * 1.3f, s * .75f, d * 1.3f), Pal.Dirt, 0f, .2f);
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(0, .1f, .4f), new Vector3(w * 1.0f, s * .6f, d * .9f), Pal.Dirt * 1.15f, 0f, .2f);
                // 洞口（黑色纵深）
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, s * .22f, d * .52f), new Vector3(1.1f, s * .45f, .3f), new Color(.05f, .04f, .08f), 0f, .1f);
                // 白骨拱
                for (int i = 0; i < 5; i++)
                {
                    float t = i / 4f;
                    float a = Mathf.Lerp(210, -30, t) * Mathf.Deg2Rad;
                    var p = new Vector3(Mathf.Cos(a) * .8f, s * .22f + Mathf.Sin(a) * .8f, d * .55f);
                    var rib = Gfx.Prim(PrimitiveType.Cube, r, p, new Vector3(.14f, .5f, .14f), Pal.UBone, 0f, .35f);
                    rib.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Rad2Deg * -a);
                }
                // 洞内绿光 + 鬼火
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(0, s * .2f, d * .45f), Vector3.one * .3f, Pal.UGlow, 0f, .9f, true);
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(.6f, .5f, d * .4f), Vector3.one * .15f, Pal.UGlow, 0f, .9f, true);
                // 两侧骨刺
                BoneSpike(r, new Vector3(-w * .7f, 0, .3f), s * .5f, Pal.UBone);
                BoneSpike(r, new Vector3(w * .7f, 0, .3f), s * .45f, Pal.UBone);
            }
        }

        // ---------- 弓箭场 / 诅咒神殿 ----------

        static void Archery(Transform r, bool h, float s)
        {
            if (h)
            {
                float w = s * .9f;
                Plinth(r, Pal.HStone, w, w);
                // 圆形塔身
                Gfx.Prim(PrimitiveType.Cylinder, r, new Vector3(0, .30f + s * .5f, 0),
                         new Vector3(w * .9f, s * .5f, w * .9f), Pal.HWall, 0f, .3f);
                // 木梁环
                Gfx.Prim(PrimitiveType.Cylinder, r, new Vector3(0, .30f + s * .95f, 0),
                         new Vector3(w * 1.0f, .12f, w * 1.0f), Pal.HBeam, 0f, .25f);
                // 尖顶
                Spire(r, new Vector3(0, .30f + s * 1.02f, 0), w * 1.05f, s * .5f, Pal.HRoof);
                Flag(r, new Vector3(0, .30f + s * 1.55f, 0), .7f, Pal.HCloth);
                // 靶子（箭术主题）
                var target = new Vector3(w * .85f, .3f, w * .3f);
                Gfx.Prim(PrimitiveType.Cube, r, target + Vector3.up * .7f, new Vector3(.1f, 1.4f, .1f), Pal.HBeam, 0f, .25f);
                Gfx.Prim(PrimitiveType.Cylinder, r, target + Vector3.up * 1.3f + Vector3.forward * .06f,
                         new Vector3(.7f, .06f, .7f), Pal.HGold, 0f, .4f).transform.localRotation = Quaternion.Euler(90, 0, 0);
                Gfx.Prim(PrimitiveType.Cylinder, r, target + Vector3.up * 1.3f + Vector3.forward * .10f,
                         new Vector3(.4f, .06f, .4f), new Color(.8f, .2f, .2f), 0f, .4f).transform.localRotation = Quaternion.Euler(90, 0, 0);
                Windows(r, Pal.HWindow, new Vector3(0, s * .55f, w * .46f));
                Door(r, new Vector3(0, .28f, w * .45f), .7f, 1.0f, Pal.HBeam, Pal.HStone);
            }
            else
            {
                // 诅咒神殿：悬浮倒置方尖碑 + 环座
                float w = s * .8f;
                Gfx.Prim(PrimitiveType.Cylinder, r, new Vector3(0, .2f, 0), new Vector3(w * 1.4f, .2f, w * 1.4f), Pal.UStone, 0f, .25f);
                Gfx.Prim(PrimitiveType.Cylinder, r, new Vector3(0, .45f, 0), new Vector3(w * 1.1f, .15f, w * 1.1f), Pal.UStone * 1.1f, 0f, .25f);
                // 方尖碑（下部细长 + 上部晶体）
                Gfx.MeshGo(Gfx.Frustum(.28f, 4), r, new Vector3(0, .5f, 0), new Vector3(w * .8f, s * .9f, w * .8f), Pal.UWall, 0f, .3f);
                var gem = Gfx.MeshGo(Gfx.Crystal(), r, new Vector3(0, .5f + s * 1.15f, 0), new Vector3(.5f, .8f, .5f), Pal.UGlow2, 0f, .85f, true);
                gem.transform.localRotation = Quaternion.Euler(0, 45, 0);
                // 环座上的三根小骨柱
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.PI * 2 / 3;
                    var p = new Vector3(Mathf.Cos(a) * w * .8f, .5f, Mathf.Sin(a) * w * .8f);
                    Gfx.MeshGo(Gfx.Frustum(.1f, 4), r, p, new Vector3(.25f, s * .45f, .25f), Pal.UBone, 0f, .35f);
                    Gfx.Prim(PrimitiveType.Sphere, r, p + Vector3.up * (s * .48f), Vector3.one * .13f, Pal.UGlow2, 0f, .9f, true);
                }
            }
        }

        // ---------- 马厩 / 死亡马厩 ----------

        static void Stable(Transform r, bool h, float s)
        {
            float w = s * 1.35f, d = s * .95f;
            if (h)
            {
                Plinth(r, Pal.HStone, w, d);
                // 开放式棚屋：矮墙 + 高顶
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .28f + s * .22f, 0), new Vector3(w, s * .44f, d), Pal.HBeam, 0f, .28f);
                GableRoof(r, new Vector3(0, .28f + s * .46f, 0), w * 1.04f, s * .30f, d * 1.04f, Pal.HRoof, Pal.HRoof2, true);
                // 前面开间（立柱）
                foreach (float x in new[] { -w * .42f, -w * .14f, w * .14f, w * .42f })
                    Gfx.Prim(PrimitiveType.Cube, r, new Vector3(x, .28f + s * .2f, d * .5f), new Vector3(.12f, s * .4f, .12f), Pal.HBeam, 0f, .25f);
                // 干草堆 + 水槽
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(w * .55f, .35f, .3f), new Vector3(.8f, .7f, .8f), new Color(.85f, .72f, .35f), 0f, .3f);
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(-w * .55f, .35f, .3f), new Vector3(.9f, .4f, .5f), Pal.WoodD, 0f, .25f);
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(-w * .55f, .56f, .3f), new Vector3(.8f, .06f, .4f), new Color(.3f, .5f, .7f), 0f, .7f);
            }
            else
            {
                // 死亡马厩：围栏 + 骨柱 + 魂火
                Plinth(r, Pal.UStone, w, d);
                foreach (float x in new[] { -w * .5f, w * .5f })
                    Gfx.Prim(PrimitiveType.Cube, r, new Vector3(x, .28f + s * .3f, 0), new Vector3(.16f, s * .6f, d), Pal.UBeam, 0f, .25f);
                GableRoof(r, new Vector3(0, .28f + s * .62f, 0), w * 1.04f, s * .26f, d * 1.04f, Pal.URoof, Pal.URoof2, true);
                // 骨栅栏
                for (int i = 0; i < 6; i++)
                {
                    float x = Mathf.Lerp(-w * .5f, w * .5f, i / 5f);
                    Gfx.Prim(PrimitiveType.Cube, r, new Vector3(x, .5f, d * .55f), new Vector3(.08f, .8f, .08f), Pal.UBone, 0f, .3f);
                }
                Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .75f, d * .55f), new Vector3(w, .08f, .08f), Pal.UBone, 0f, .3f);
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(w * .3f, .5f, 0), Vector3.one * .2f, Pal.UGlow2, 0f, .9f, true);
            }
        }

        // ---------- 伐木场 ----------

        static void Lumber(Transform r, bool h, float s)
        {
            float w = s * .95f;
            var wall = h ? Pal.HWall : Pal.UWall;
            var roof = h ? Pal.HRoof : Pal.URoof;
            var roof2 = h ? Pal.HRoof2 : Pal.URoof2;
            Plinth(r, h ? Pal.HStone : Pal.UStone, w, w * .8f);
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .28f + s * .24f, 0), new Vector3(w, s * .48f, w * .8f), wall, 0f, .3f);
            GableRoof(r, new Vector3(0, .28f + s * .5f, 0), w * 1.04f, s * .26f, w * .84f, roof, roof2, true);
            // 堆好的原木堆（三根）
            var logC = Pal.WoodL;
            foreach (var (p, rot) in new[]
            {
                (new Vector3(w * .65f, .32f, .2f), Quaternion.Euler(90, 0, 0)),
                (new Vector3(w * .65f, .32f, -.2f), Quaternion.Euler(90, 0, 0)),
                (new Vector3(w * .65f, .62f, 0), Quaternion.Euler(90, 0, 0)),
            })
            {
                var log = Gfx.Prim(PrimitiveType.Cylinder, r, p, new Vector3(.3f, .8f, .3f), logC, 0f, .3f);
                log.transform.localRotation = rot;
            }
            // 木桩 + 斧头
            Gfx.Prim(PrimitiveType.Cylinder, r, new Vector3(-w * .6f, .4f, .3f), new Vector3(.4f, .4f, .4f), Pal.WoodD, 0f, .25f);
            var axe = Gfx.Prim(PrimitiveType.Cube, r, new Vector3(-w * .6f, .85f, .3f), new Vector3(.05f, .6f, .05f), Pal.HBeam, 0f, .25f);
            axe.transform.localRotation = Quaternion.Euler(0, 0, 30);
            Door(r, new Vector3(0, .26f, w * .4f), .7f, 1.0f, Pal.HBeam, h ? Pal.HStone : Pal.UStone);
        }

        // ---------- 民居（人口 +15，可建多座） ----------

        static void House(Transform r, bool h, float s)
        {
            float w = s * .9f;
            var wall = h ? Pal.HWall : Pal.UWall;
            var roof = h ? Pal.HRoof : Pal.URoof;
            var roof2 = h ? Pal.HRoof2 : Pal.URoof2;
            Plinth(r, h ? Pal.HStone : Pal.UStone, w, w * .8f);
            Gfx.Prim(PrimitiveType.Cube, r, new Vector3(0, .28f + s * .22f, 0), new Vector3(w, s * .44f, w * .8f), wall, 0f, .3f);
            GableRoof(r, new Vector3(0, .28f + s * .46f, 0), w * 1.08f, s * .3f, w * .86f, roof, roof2, true);
            Door(r, new Vector3(0, .26f, w * .4f), .6f, .9f, h ? Pal.HBeam : Pal.UBeam, h ? Pal.HStone : Pal.UStone);
            if (h)
            {
                // 人族小屋：烟囱 + 暖窗 + 小旗
                Chimney(r, new Vector3(w * .28f, .28f + s * .6f, 0), Pal.HStone);
                Windows(r, Pal.HWindow, new Vector3(-w * .28f, .28f + s * .2f, w * .41f));
                Flag(r, new Vector3(-w * .42f, .28f + s * .48f, -w * .32f), .5f, Pal.HCloth);
            }
            else
            {
                // 亡灵小屋：骨窗 + 魂灯火 + 尖刺
                Windows(r, Pal.UGlow, new Vector3(-w * .28f, .28f + s * .2f, w * .41f));
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(w * .3f, .28f + s * .5f, w * .34f), Vector3.one * .13f, Pal.UGlow2, 0f, .9f, true);
                var spike = Gfx.Prim(PrimitiveType.Cube, r, new Vector3(-w * .38f, .28f + s * .55f, -w * .3f), new Vector3(.08f, .5f, .08f), Pal.UBone, 0f, .3f);
                spike.transform.localRotation = Quaternion.Euler(0, 0, 18);
            }
        }

        // ---------- 箭塔 / 亡灵塔 ----------

        static void Tower(Transform r, bool h, float s)
        {
            if (h)
            {
                // 石塔身 + 木顶台
                Gfx.MeshGo(Gfx.Frustum(.38f, 8), r, Vector3.zero, new Vector3(s * .8f, s * 1.15f, s * .8f), Pal.HStone, 0f, .28f);
                // 顶台（外挑）
                Gfx.Prim(PrimitiveType.Cylinder, r, new Vector3(0, s * 1.18f, 0), new Vector3(s * .95f, .16f, s * .95f), Pal.HBeam, 0f, .25f);
                // 垛口
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI * 2 / 6;
                    Gfx.Prim(PrimitiveType.Cube, r, new Vector3(Mathf.Cos(a) * s * .42f, s * 1.3f, Mathf.Sin(a) * s * .42f),
                             new Vector3(.16f, .16f, .16f), Pal.HStone, 0f, .28f);
                }
                Spire(r, new Vector3(0, s * 1.28f, 0), s * .7f, s * .35f, Pal.HRoof);
                Flag(r, new Vector3(0, s * 1.63f, 0), .55f, Pal.HCloth);
                Windows(r, Pal.HWindow, new Vector3(0, s * .6f, s * .36f));
            }
            else
            {
                // 亡灵塔：扭曲骨塔 + 魂火顶
                Gfx.MeshGo(Gfx.Frustum(.30f, 6), r, Vector3.zero, new Vector3(s * .7f, s * 1.1f, s * .7f), Pal.UStone, 0f, .28f);
                var mid = Gfx.MeshGo(Gfx.Frustum(.22f, 6), r, new Vector3(0, s * 1.1f, 0), new Vector3(s * .5f, s * .35f, s * .5f), Pal.UBone, 0f, .3f);
                mid.transform.localRotation = Quaternion.Euler(0, 30, 0);
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(0, s * 1.52f, 0), Vector3.one * .34f, Pal.UGlow, 0f, .9f, true);
                // 骨刺
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.PI * 2 / 3;
                    BoneSpike(r, new Vector3(Mathf.Cos(a) * s * .5f, 0, Mathf.Sin(a) * s * .5f), s * .6f, Pal.UBone);
                }
            }
        }

        // ---------- 建筑道具 ----------

        static void Barrel(Transform r, Vector3 pos)
        {
            Gfx.Prim(PrimitiveType.Cylinder, r, pos + Vector3.up * .3f, new Vector3(.36f, .3f, .36f), Pal.WoodL, 0f, .3f);
            Gfx.Prim(PrimitiveType.Cylinder, r, pos + Vector3.up * .32f, new Vector3(.40f, .08f, .40f), Pal.IronD, .7f, .4f);
            Gfx.Prim(PrimitiveType.Cylinder, r, pos + Vector3.up * .12f, new Vector3(.38f, .06f, .38f), Pal.IronD, .7f, .4f);
        }

        static void Crate(Transform r, Vector3 pos, float s)
        {
            var c = Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * s * .5f, Vector3.one * s, Pal.WoodL, 0f, .3f);
            c.transform.localRotation = Quaternion.Euler(0, 12, 0);
            // 边框
            Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * s * .5f, new Vector3(s * 1.02f, s * .12f, s * 1.02f), Pal.WoodD, 0f, .25f)
               .transform.localRotation = Quaternion.Euler(0, 12, 0);
        }

        static void SwordProp(Transform r, Vector3 pos, float h)
        {
            Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * h * .5f, new Vector3(.07f, h, .02f), Pal.Iron, .85f, .75f);
            Gfx.Prim(PrimitiveType.Cube, r, pos + Vector3.up * (h * .82f), new Vector3(.2f, .04f, .04f), Pal.HGold, .8f, .6f);
        }

        static void ShieldProp(Transform r, Vector3 pos, float s, Color cloth)
        {
            var sh = Gfx.Prim(PrimitiveType.Cylinder, r, pos, new Vector3(s, .05f, s), cloth, .3f, .5f);
            sh.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Gfx.Prim(PrimitiveType.Sphere, r, pos + Vector3.forward * .04f, Vector3.one * s * .3f, Pal.Iron, .8f, .7f);
        }

        // =================================================================
        //  单位
        // =================================================================

        /// <summary>单位总入口。返回可用于动画的身体挂点字典（见 BodyRig）。</summary>
        public static BodyRig BuildUnit(Transform v, Team team, UnitDef def)
        {
            var rig = new BodyRig { root = v };
            bool h = Human(team);
            if (def.worker) BuildWorker(rig, def, h);
            else switch (def.kind)
            {
                case UnitKind.Infantry: BuildInfantry(rig, def, h); break;
                case UnitKind.Ranged:   BuildRanged(rig, def, h); break;
                case UnitKind.Cavalry:  BuildCavalry(rig, def, h); break;
            }
            if (def.hero) AddHeroDecor(rig, def, h);
            return rig;
        }

        /// <summary>人形骨架：腿×2、躯干、头、臂×2（臂挂肩点，便于摆动）。</summary>
        static void HumanoidBase(BodyRig rig, UnitDef def, Color skin, Color cloth, Color armor, bool heavy)
        {
            float s = def.size;
            float legH = s * .75f, torsoH = s * .8f;
            float legW = s * (heavy ? .26f : .22f);

            // 腿（挂点在髋部，脚底向下延伸，便于旋转摆动）
            foreach (float side in new[] { -1, 1 })
            {
                var hip = new GameObject(side < 0 ? "legL" : "legR").transform;
                hip.SetParent(rig.root, false);
                hip.localPosition = new Vector3(side * s * .14f, legH, 0);
                Gfx.Prim(PrimitiveType.Cube, hip, new Vector3(0, -legH * .5f, 0), new Vector3(legW, legH, legW), cloth, 0f, .3f);
                if (side < 0) rig.legL = hip; else rig.legR = hip;
            }
            // 躯干
            var torso = new GameObject("torso").transform;
            torso.SetParent(rig.root, false);
            torso.localPosition = new Vector3(0, legH, 0);
            Gfx.Prim(PrimitiveType.Cube, torso, new Vector3(0, torsoH * .45f, 0),
                     new Vector3(s * (heavy ? .62f : .52f), torsoH, s * (heavy ? .4f : .34f)), cloth, 0f, .3f);
            // 胸甲
            Gfx.Prim(PrimitiveType.Cube, torso, new Vector3(0, torsoH * .55f, s * .05f),
                     new Vector3(s * (heavy ? .66f : .56f), torsoH * .5f, s * .3f), armor, heavy ? .7f : .3f, heavy ? .6f : .35f);
            rig.torso = torso;
            // 头 + 头盔
            var head = new GameObject("head").transform;
            head.SetParent(torso, false);
            head.localPosition = new Vector3(0, torsoH, 0);
            Gfx.Prim(PrimitiveType.Cube, head, new Vector3(0, s * .16f, 0), new Vector3(s * .34f, s * .32f, s * .34f), skin, 0f, .35f);
            rig.head = head;
            // 臂（挂肩点）
            foreach (float side in new[] { -1, 1 })
            {
                var shoulder = new GameObject(side < 0 ? "armL" : "armR").transform;
                shoulder.SetParent(torso, false);
                shoulder.localPosition = new Vector3(side * s * (heavy ? .38f : .32f), torsoH * .8f, 0);
                Gfx.Prim(PrimitiveType.Cube, shoulder, new Vector3(0, -s * .3f, 0),
                         new Vector3(s * .16f, s * .6f, s * .16f), cloth, 0f, .3f);
                if (side < 0) rig.armL = shoulder; else rig.armR = shoulder;
            }
        }

        static void BuildInfantry(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            if (h)
            {
                HumanoidBase(rig, def, new Color(.9f, .72f, .55f), new Color(.25f, .38f, .68f), Pal.Iron, true);
                // 钢盔（带护鼻）
                Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .24f, 0), new Vector3(s * .4f, s * .18f, s * .4f), Pal.Iron, .8f, .7f);
                Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .12f, s * .19f), new Vector3(s * .07f, s * .2f, .04f), Pal.Iron, .8f, .7f);
                // 右手剑
                Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(0, -s * .62f, s * .1f), new Vector3(.04f, .04f, s * .2f), Pal.HBeam, 0f, .3f);
                Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(0, -s * .62f, s * .55f), new Vector3(s * .1f, .03f, s * .85f), Pal.Iron, .85f, .8f);
                Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(0, -s * .62f, s * .14f), new Vector3(s * .28f, .05f, .05f), Pal.HGold, .8f, .6f);
                // 左手盾（鸢形：方块+下尖）
                var shield = Gfx.Prim(PrimitiveType.Cube, rig.armL, new Vector3(-s * .1f, -s * .35f, s * .1f),
                         new Vector3(.05f, s * .75f, s * .55f), new Color(.2f, .34f, .6f), .3f, .5f);
                Gfx.Prim(PrimitiveType.Sphere, rig.armL, new Vector3(-s * .13f, -s * .35f, s * .1f), Vector3.one * s * .2f, Pal.Iron, .8f, .7f);
                shield.transform.localRotation = Quaternion.Euler(0, 0, 4);
            }
            else
            {
                // 骷髅：骨白身、眼窝红光、锈刀
                HumanoidBase(rig, def, Pal.UBone, Pal.UBone * .9f, Pal.UStone, false);
                Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .24f, 0), new Vector3(s * .38f, s * .14f, s * .38f), Pal.UBone, 0f, .4f);
                foreach (float ex in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(ex * s * .09f, s * .17f, s * .16f),
                             new Vector3(s * .08f, s * .08f, .03f), new Color(1f, .25f, .2f), 0f, .8f, true);
                // 肋骨纹路
                for (int i = 0; i < 3; i++)
                    Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(0, s * .5f + i * s * .12f, s * .18f),
                             new Vector3(s * .44f, s * .04f, .03f), Pal.UBone * .8f, 0f, .3f);
                // 弯刀
                Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(0, -s * .62f, s * .1f), new Vector3(.04f, .04f, s * .2f), Pal.WoodD, 0f, .3f);
                var blade = Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(0, -s * .62f, s * .5f), new Vector3(s * .1f, .03f, s * .8f), Pal.IronD, .7f, .5f);
                blade.transform.localRotation = Quaternion.Euler(8, 0, 0);
                // 圆骨盾
                var sh = Gfx.Prim(PrimitiveType.Cylinder, rig.armL, new Vector3(-s * .1f, -s * .35f, s * .1f),
                         new Vector3(s * .55f, .04f, s * .55f), Pal.UBone * .85f, 0f, .35f);
                sh.transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
        }

        static void BuildRanged(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            if (h)
            {
                HumanoidBase(rig, def, new Color(.9f, .72f, .55f), new Color(.3f, .5f, .3f), Pal.Leather, false);
                // 兜帽
                Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .2f, -s * .04f), new Vector3(s * .4f, s * .3f, s * .4f), new Color(.24f, .4f, .26f), 0f, .35f);
                // 长弓（竖在左手）
                var bow = Gfx.Prim(PrimitiveType.Cube, rig.armL, new Vector3(-s * .05f, -s * .4f, s * .2f),
                         new Vector3(.05f, s * 1.4f, .05f), Pal.WoodD, 0f, .3f);
                bow.transform.localRotation = Quaternion.Euler(0, 0, 8);
                Gfx.Prim(PrimitiveType.Cube, rig.armL, new Vector3(-s * .02f, -s * .4f, s * .23f),
                         new Vector3(.015f, s * 1.3f, .015f), new Color(.9f, .9f, .85f), 0f, .4f);
                // 箭袋
                Gfx.Prim(PrimitiveType.Cylinder, rig.torso, new Vector3(s * .15f, s * .55f, -s * .2f),
                         new Vector3(s * .14f, s * .4f, s * .14f), Pal.Leather, 0f, .3f);
            }
            else
            {
                // 亡灵射手：暗紫斗篷 + 骨弓
                HumanoidBase(rig, def, Pal.UBone, new Color(.3f, .2f, .4f), Pal.UStone, false);
                // 兜帽披风
                Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .2f, -s * .03f), new Vector3(s * .4f, s * .3f, s * .4f), new Color(.3f, .18f, .42f), 0f, .35f);
                var cape = Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(0, s * .35f, -s * .2f),
                         new Vector3(s * .5f, s * .8f, .04f), new Color(.28f, .16f, .4f), 0f, .3f);
                cape.transform.localRotation = Quaternion.Euler(8, 0, 0);
                // 骨弓
                var bow = Gfx.Prim(PrimitiveType.Cube, rig.armL, new Vector3(-s * .05f, -s * .4f, s * .2f),
                         new Vector3(.05f, s * 1.35f, .05f), Pal.UBone, 0f, .35f);
                bow.transform.localRotation = Quaternion.Euler(0, 0, 8);
                // 紫光眼
                foreach (float ex in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(ex * s * .09f, s * .14f, s * .16f),
                             new Vector3(s * .07f, s * .07f, .03f), Pal.UGlow2, 0f, .8f, true);
            }
        }

        static void BuildCavalry(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            var horseC = h ? new Color(.45f, .3f, .18f) : new Color(.25f, .25f, .3f);
            var horseD = h ? new Color(.35f, .22f, .12f) : new Color(.18f, .18f, .22f);

            // 马身（用横向胶囊+胸脯+臀部塑出马的轮廓）
            var horse = new GameObject("horse").transform;
            horse.SetParent(rig.root, false);
            horse.localPosition = Vector3.zero;
            rig.torso = horse;
            Gfx.Prim(PrimitiveType.Capsule, horse, new Vector3(0, s * .85f, 0),
                     new Vector3(s * .62f, s * 1.1f, s * .62f), horseC, 0f, .35f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            Gfx.Prim(PrimitiveType.Sphere, horse, new Vector3(0, s * .9f, s * .45f), new Vector3(s * .6f, s * .62f, s * .5f), horseC, 0f, .35f);
            Gfx.Prim(PrimitiveType.Sphere, horse, new Vector3(0, s * .88f, -s * .45f), new Vector3(s * .62f, s * .66f, s * .55f), horseC, 0f, .35f);
            // 马颈 + 马头
            var neck = Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.3f, s * .55f),
                     new Vector3(s * .28f, s * .7f, s * .3f), horseC, 0f, .35f);
            neck.transform.localRotation = Quaternion.Euler(30, 0, 0);
            Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.6f, s * .85f),
                     new Vector3(s * .26f, s * .24f, s * .55f), horseC, 0f, .35f);
            // 马耳
            foreach (float ex in new[] { -1, 1 })
                Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(ex * s * .08f, s * 1.78f, s * .72f),
                         new Vector3(s * .06f, s * .16f, .04f), horseD, 0f, .3f);
            // 鬃毛 + 尾巴
            Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.42f, s * .42f),
                     new Vector3(.06f, s * .55f, s * .3f), horseD, 0f, .3f).transform.localRotation = Quaternion.Euler(35, 0, 0);
            var tail = Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * .95f, -s * .95f),
                     new Vector3(s * .12f, s * .7f, s * .12f), horseD, 0f, .3f);
            tail.transform.localRotation = Quaternion.Euler(30, 0, 0);
            // 四条马腿（挂点用于摆动）
            rig.horseLegs = new Transform[4];
            int li = 0;
            foreach (float lx in new[] { -1, 1 }) foreach (float lz in new[] { -1, 1 })
            {
                var leg = new GameObject("hleg" + li).transform;
                leg.SetParent(horse, false);
                leg.localPosition = new Vector3(lx * s * .22f, s * .7f, lz * s * .55f);
                Gfx.Prim(PrimitiveType.Cube, leg, new Vector3(0, -s * .32f, 0), new Vector3(s * .14f, s * .64f, s * .14f), horseC, 0f, .35f);
                Gfx.Prim(PrimitiveType.Cube, leg, new Vector3(0, -s * .6f, 0), new Vector3(s * .16f, s * .1f, s * .16f), horseD, 0f, .3f); // 蹄
                rig.horseLegs[li++] = leg;
            }
            // 马鞍
            Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.32f, 0), new Vector3(s * .55f, .1f, s * .6f), h ? Pal.HCloth : Pal.UCloth, 0f, .4f);
            Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.28f, 0), new Vector3(s * .4f, .12f, s * .45f), Pal.Leather, 0f, .3f);

            // 骑手（小型人形，坐在鞍上）
            var rider = new GameObject("rider").transform;
            rider.SetParent(horse, false);
            rider.localPosition = new Vector3(0, s * 1.35f, 0);
            rig.head = rider;
            var skin = h ? new Color(.9f, .72f, .55f) : Pal.UBone;
            var cloth = h ? new Color(.25f, .38f, .68f) : new Color(.3f, .2f, .4f);
            // 骑手腿（跨马）
            foreach (float side in new[] { -1, 1 })
            {
                var leg = Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(side * s * .32f, -s * .05f, 0),
                         new Vector3(s * .14f, s * .5f, s * .14f), cloth, 0f, .3f);
                leg.transform.localRotation = Quaternion.Euler(0, 0, side * -18);
            }
            // 骑手脚干 + 头
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .3f, 0), new Vector3(s * .5f, s * .6f, s * .34f), h ? Pal.Iron : Pal.UStone, h ? .7f : .2f, h ? .6f : .35f);
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .72f, 0), new Vector3(s * .32f, s * .3f, s * .32f), skin, 0f, .35f);
            if (h)
            {
                // 骑士头盔 + 盔缨
                Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .8f, 0), new Vector3(s * .36f, s * .18f, s * .36f), Pal.Iron, .8f, .7f);
                Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .98f, -s * .05f), new Vector3(s * .08f, s * .2f, s * .4f), new Color(.85f, .25f, .25f), 0f, .4f);
                // 骑枪（右手斜持）
                var armR = new GameObject("armR").transform;
                armR.SetParent(rider, false);
                armR.localPosition = new Vector3(s * .3f, s * .55f, 0);
                rig.armR = armR;
                Gfx.Prim(PrimitiveType.Cube, armR, new Vector3(0, -s * .2f, 0), new Vector3(s * .14f, s * .4f, s * .14f), cloth, 0f, .3f);
                var lance = Gfx.Prim(PrimitiveType.Cylinder, armR, new Vector3(0, 0, s * .5f),
                         new Vector3(.06f, s * 1.1f, .06f), Pal.WoodL, 0f, .3f);
                lance.transform.localRotation = Quaternion.Euler(90, 0, 0);
                Gfx.MeshGo(Gfx.Frustum(0f, 4), armR, new Vector3(0, 0, s * 1.55f), new Vector3(.16f, .3f, .16f), Pal.Iron, .8f, .7f)
                   .transform.localRotation = Quaternion.Euler(90, 0, 0);
            }
            else
            {
                // 死亡骑士：黑甲 + 符文剑 + 角盔
                Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .8f, 0), new Vector3(s * .36f, s * .16f, s * .36f), Pal.IronD, .8f, .6f);
                foreach (float ex in new[] { -1, 1 })
                {
                    var horn = Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(ex * s * .2f, s * .9f, 0),
                             new Vector3(s * .06f, s * .22f, s * .06f), Pal.UBone, 0f, .35f);
                    horn.transform.localRotation = Quaternion.Euler(0, 0, ex * -35);
                }
                foreach (float ex in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(ex * s * .08f, s * .7f, s * .17f),
                             new Vector3(s * .07f, s * .07f, .03f), new Color(1f, .3f, .2f), 0f, .8f, true);
                var armR = new GameObject("armR").transform;
                armR.SetParent(rider, false);
                armR.localPosition = new Vector3(s * .3f, s * .55f, 0);
                rig.armR = armR;
                Gfx.Prim(PrimitiveType.Cube, armR, new Vector3(0, -s * .2f, 0), new Vector3(s * .14f, s * .4f, s * .14f), cloth, 0f, .3f);
                Gfx.Prim(PrimitiveType.Cube, armR, new Vector3(0, -s * .4f, s * .4f), new Vector3(s * .1f, .03f, s * .9f), Pal.UGlow2, .6f, .8f, true);
            }
        }

        static void BuildWorker(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            if (h)
            {
                HumanoidBase(rig, def, new Color(.9f, .72f, .55f), new Color(.6f, .5f, .35f), new Color(.5f, .42f, .3f), false);
                // 草帽
                Gfx.Prim(PrimitiveType.Cylinder, rig.head, new Vector3(0, s * .3f, 0), new Vector3(s * .55f, .05f, s * .55f), new Color(.85f, .75f, .4f), 0f, .35f);
                Gfx.Prim(PrimitiveType.Sphere, rig.head, new Vector3(0, s * .33f, 0), new Vector3(s * .3f, s * .15f, s * .3f), new Color(.85f, .75f, .4f), 0f, .35f);
                // 肩上扛的锄头
                var tool = Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(0, -s * .1f, s * .1f), new Vector3(.05f, s * 1.1f, .05f), Pal.WoodL, 0f, .3f);
                tool.transform.localRotation = Quaternion.Euler(0, 0, 40);
                Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(s * .3f, s * .32f, s * .1f), new Vector3(.04f, .2f, .15f), Pal.Iron, .7f, .5f);
            }
            else
            {
                // 侍僧：罩袍（用锥台当袍子，无腿）+ 兜帽
                var robe = Gfx.MeshGo(Gfx.Frustum(.32f, 8), rig.root, Vector3.zero,
                         new Vector3(s * .8f, s * 1.3f, s * .8f), new Color(.32f, .25f, .4f), 0f, .3f);
                rig.torso = robe.transform;
                var head = new GameObject("head").transform;
                head.SetParent(rig.root, false);
                head.localPosition = new Vector3(0, s * 1.25f, 0);
                rig.head = head;
                Gfx.Prim(PrimitiveType.Cube, head, new Vector3(0, s * .1f, 0), new Vector3(s * .32f, s * .3f, s * .32f), Pal.UBone, 0f, .35f);
                Gfx.Prim(PrimitiveType.Cube, head, new Vector3(0, s * .16f, -s * .03f), new Vector3(s * .4f, s * .3f, s * .4f), new Color(.28f, .2f, .38f), 0f, .35f);
                // 袖中绿光
                Gfx.Prim(PrimitiveType.Sphere, rig.root, new Vector3(0, s * .7f, s * .3f), Vector3.one * s * .15f, Pal.UGlow, 0f, .9f, true);
                // 悬浮特效（底部魂火）
                Gfx.Prim(PrimitiveType.Sphere, rig.root, new Vector3(0, .1f, 0), new Vector3(s * .4f, .12f, s * .4f), Pal.UGlow, 0f, .9f, true);
            }
        }

        static void AddHeroDecor(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            var gold = h ? Pal.HGold : Pal.UGlow2;
            // 王冠/头饰
            if (rig.head != null)
            {
                Gfx.Prim(PrimitiveType.Cylinder, rig.head, new Vector3(0, s * .32f, 0),
                         new Vector3(s * .3f, s * .08f, s * .3f), gold, .85f, .8f, !h);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * .5f;
                    Gfx.Prim(PrimitiveType.Cube, rig.head,
                             new Vector3(Mathf.Cos(a) * s * .13f, s * .4f, Mathf.Sin(a) * s * .13f),
                             new Vector3(.04f, s * .12f, .04f), gold, .85f, .8f, !h);
                }
            }
            // 披风（英雄标志）
            if (rig.torso != null)
            {
                var cape = Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(0, s * .3f, -s * .22f),
                         new Vector3(s * .55f, s * 1.0f, .04f), h ? new Color(.7f, .15f, .2f) : Pal.UCloth, 0f, .4f);
                cape.transform.localRotation = Quaternion.Euler(10, 0, 0);
                // 肩甲
                foreach (float side in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Sphere, rig.torso, new Vector3(side * s * .38f, s * .78f, 0),
                             new Vector3(s * .28f, s * .2f, s * .28f), gold, .85f, .75f, !h);
            }
        }

        // =================================================================
        //  资源 / 植被 / 装饰
        // =================================================================

        /// <summary>树：分叉树干 + 多层锥形树冠（低多边形风格）。</summary>
        public static void Tree(Transform r, System.Random rnd)
        {
            var trunk = new Color(.38f, .27f, .16f);
            var leaf = new Color(.16f, .42f, .2f);
            var leaf2 = new Color(.22f, .5f, .24f);
            float h = Mathf.Lerp(2.4f, 3.4f, (float)rnd.NextDouble());
            // 主干（锥台，上细下粗）
            Gfx.MeshGo(Gfx.Frustum(.32f, 6), r, Vector3.zero, new Vector3(.55f, h * .55f, .55f), trunk, 0f, .25f);
            // 一根侧枝
            var br = Gfx.Prim(PrimitiveType.Cylinder, r, new Vector3(.3f, h * .42f, 0),
                     new Vector3(.12f, .6f, .12f), trunk * 1.1f, 0f, .25f);
            br.transform.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 45);
            // 三层锥形树冠（颜色上浅下深）
            float w = Mathf.Lerp(1.7f, 2.3f, (float)rnd.NextDouble());
            Gfx.MeshGo(Gfx.Frustum(.05f, 7), r, new Vector3(0, h * .34f, 0), new Vector3(w, h * .5f, w), leaf * .9f, 0f, .3f);
            Gfx.MeshGo(Gfx.Frustum(.05f, 7), r, new Vector3(0, h * .58f, 0), new Vector3(w * .78f, h * .45f, w * .78f), leaf, 0f, .3f);
            Gfx.MeshGo(Gfx.Frustum(.05f, 7), r, new Vector3(0, h * .8f, 0), new Vector3(w * .55f, h * .4f, w * .55f), leaf2, 0f, .3f);
        }

        /// <summary>魔法水晶簇：主晶 + 环绕小晶，自发光。</summary>
        public static void ManaCrystal(Transform r, System.Random rnd)
        {
            var glow = new Color(.3f, .8f, 1f);
            var glow2 = new Color(.5f, .9f, 1f);
            // 基岩
            Gfx.MeshGo(Gfx.Frustum(.6f, 6), r, new Vector3(0, -.1f, 0), new Vector3(1.6f, .5f, 1.6f), Pal.Stone * .9f, 0f, .25f);
            // 主晶
            var main = Gfx.MeshGo(Gfx.Crystal(), r, new Vector3(0, .6f, 0), new Vector3(.8f, 1.9f, .8f), glow, .1f, .85f, true);
            main.transform.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360, (float)(rnd.NextDouble() * 10 - 5));
            // 环绕小晶 ×4
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f + (float)rnd.NextDouble() * .6f;
                float rr = .7f + (float)rnd.NextDouble() * .2f;
                var c = Gfx.MeshGo(Gfx.Crystal(), r,
                         new Vector3(Mathf.Cos(a) * rr, .35f, Mathf.Sin(a) * rr),
                         new Vector3(.4f, .9f + (float)rnd.NextDouble() * .5f, .4f), glow2, .1f, .85f, true);
                c.transform.localRotation = Quaternion.Euler((float)(rnd.NextDouble() * 24 - 12), 0, (float)(rnd.NextDouble() * 24 - 12));
            }
        }

        /// <summary>草簇：三个斜置薄片交叉。</summary>
        public static void GrassTuft(Transform r, System.Random rnd)
        {
            var g1 = new Color(.3f, .5f, .22f);
            var g2 = new Color(.4f, .58f, .26f);
            float h = Mathf.Lerp(.25f, .5f, (float)rnd.NextDouble());
            for (int i = 0; i < 3; i++)
            {
                var blade = Gfx.MeshGo(Gfx.Quad(), r, Vector3.up * h * .5f,
                         new Vector3(.5f, 1, h), i == 1 ? g2 : g1, 0f, .3f);
                blade.transform.localRotation = Quaternion.Euler((float)(rnd.NextDouble() * 20 - 10), i * 60f + (float)rnd.NextDouble() * 30, (float)(rnd.NextDouble() * 20 - 10));
            }
        }

        /// <summary>灌木：两个叠放的绿色球。</summary>
        public static void Bush(Transform r, System.Random rnd)
        {
            var c = new Color(.18f, .4f, .2f);
            float s = Mathf.Lerp(.5f, .9f, (float)rnd.NextDouble());
            Gfx.Prim(PrimitiveType.Sphere, r, Vector3.up * s * .5f, Vector3.one * s, c, 0f, .3f);
            Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(s * .3f, s * .4f, s * .2f), Vector3.one * s * .7f, c * 1.15f, 0f, .3f);
        }

        /// <summary>岩石：压扁的球体组合。</summary>
        public static void Rock(Transform r, System.Random rnd)
        {
            var c = new Color(.48f, .47f, .5f);
            float s = Mathf.Lerp(.4f, .9f, (float)rnd.NextDouble());
            var rock = Gfx.Prim(PrimitiveType.Sphere, r, Vector3.up * s * .3f, new Vector3(s, s * .7f, s * .9f), c, 0f, .25f);
            rock.transform.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 0);
            if (rnd.NextDouble() > .5)
                Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(s * .5f, s * .2f, s * .3f),
                         new Vector3(s * .5f, s * .4f, s * .45f), c * 1.1f, 0f, .25f);
        }

        /// <summary>小花：细茎 + 彩色小花头。</summary>
        public static void Flower(Transform r, System.Random rnd)
        {
            var petals = new[] { new Color(.9f, .4f, .5f), new Color(.95f, .85f, .3f), new Color(.9f, .55f, .85f), new Color(.95f, .95f, .95f) };
            var pc = petals[rnd.Next(petals.Length)];
            float h = Mathf.Lerp(.3f, .5f, (float)rnd.NextDouble());
            Gfx.Prim(PrimitiveType.Cylinder, r, Vector3.up * h * .5f, new Vector3(.04f, h * .5f, .04f), new Color(.3f, .5f, .22f), 0f, .3f);
            Gfx.Prim(PrimitiveType.Sphere, r, Vector3.up * h, Vector3.one * .18f, pc, 0f, .4f);
            Gfx.Prim(PrimitiveType.Sphere, r, Vector3.up * (h + .05f), Vector3.one * .08f, new Color(1f, .9f, .4f), 0f, .5f);
        }

        /// <summary>倒木：横放的原木 + 苔藓斑点。</summary>
        public static void FallenLog(Transform r, System.Random rnd)
        {
            var log = Gfx.Prim(PrimitiveType.Cylinder, r, Vector3.up * .3f, new Vector3(.5f, 1.4f, .5f), new Color(.4f, .3f, .18f), 0f, .25f);
            log.transform.localRotation = Quaternion.Euler(90, 0, (float)rnd.NextDouble() * 360);
            Gfx.Prim(PrimitiveType.Sphere, r, new Vector3(.3f, .55f, .1f), new Vector3(.3f, .1f, .2f), new Color(.3f, .45f, .2f), 0f, .3f);
        }

        /// <summary>远山（地图边界外）：剪影式低多边形山体。</summary>
        public static void Mountain(Transform r, float w, float h, Color c)
        {
            Gfx.MeshGo(Gfx.Frustum(.15f, 5), r, Vector3.zero, new Vector3(w, h, w * .8f), c, 0f, .2f);
            // 雪顶
            Gfx.MeshGo(Gfx.Frustum(.12f, 5), r, Vector3.up * h * .72f, new Vector3(w * .32f, h * .3f, w * .25f), new Color(.92f, .94f, .98f), 0f, .4f);
        }
    }

    /// <summary>单位动画挂点集合（行走/攻击时摆动）。</summary>
    public class BodyRig
    {
        public Transform root;
        public Transform torso, head;
        public Transform legL, legR;
        public Transform armL, armR;
        public Transform[] horseLegs;
    }
}
