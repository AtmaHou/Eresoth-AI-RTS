using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>单位视觉系统：可复用构件库 + 分阵营兵种构建 + 英雄专属设计。
    /// 所有方法只负责视觉：不挂逻辑组件、不加碰撞体、不改战斗数值。
    /// 视觉入口 Models.BuildUnit（由 Models.cs partial 共享）。</summary>
    public static partial class Models
    {
        // =================================================================
        //  单位总入口
        // =================================================================

        /// <summary>单位总入口：阵营 + 兵种分派，英雄走专属方法。返回动画挂点（见 BodyRig）。</summary>
        public static BodyRig BuildUnit(Transform v, Team team, UnitDef def)
        {
            var rig = new BodyRig { root = v };
            bool h = Human(team);
            if (def.hero)
            {
                // 英雄走专属全身构建（不与普通兵种分支叠加，避免重复马体/武器）
                if (h) BuildLordKnightHero(rig, def);
                else BuildLichHero(rig, def);
            }
            else if (def.worker) BuildWorker(rig, def, h);
            else switch (def.kind)
            {
                case UnitKind.Infantry: BuildInfantry(rig, def, h); break;
                case UnitKind.Ranged:   BuildRanged(rig, def, h); break;
                case UnitKind.Cavalry:  BuildCavalry(rig, def, h); break;
            }
            // 动画基准快照（突刺复位 / 魂核脉动 / 碎片浮动都基于快照，绝不每帧累加）
            if (rig.armR != null) rig.armRHome = rig.armR.localPosition;
            if (rig.soulCore != null) rig.soulCoreBase = rig.soulCore.localScale;
            if (rig.floatingParts != null)
            {
                rig.floatBase = new Vector3[rig.floatingParts.Length];
                for (int i = 0; i < rig.floatingParts.Length; i++)
                    if (rig.floatingParts[i] != null) rig.floatBase[i] = rig.floatingParts[i].localPosition;
            }
            return rig;
        }

        /// <summary>挂点节点：视觉层级下的空 Transform，供动画/特效定位。</summary>
        static Transform Node(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        // =================================================================
        //  基础人形
        // =================================================================

        /// <summary>人形骨架：腿×2、躯干(枢轴在髋部 y=size*.75)、头、臂×2(挂肩点)。
        /// 与 Unit.Anim 的躯干起伏不变量兼容：有腿 torso 枢轴 y = def.size*.75。</summary>
        static void HumanoidBase(BodyRig rig, UnitDef def, Color skin, Color cloth, Color armor, bool heavy,
                                 bool chestPlate = true, bool legs = true)
        {
            float s = def.size;
            float legH = s * .75f, torsoH = s * .8f;
            float legW = s * (heavy ? .26f : .22f);

            if (legs)
                foreach (float side in new[] { -1, 1 })
                {
                    var hip = Node(rig.root, side < 0 ? "legL" : "legR", new Vector3(side * s * .14f, legH, 0));
                    Gfx.Prim(PrimitiveType.Cube, hip, new Vector3(0, -legH * .5f, 0), new Vector3(legW, legH, legW), cloth, 0f, .3f);
                    if (side < 0) rig.legL = hip; else rig.legR = hip;
                }
            var torso = Node(rig.root, "torso", new Vector3(0, legH, 0));
            Gfx.Prim(PrimitiveType.Cube, torso, new Vector3(0, torsoH * .45f, 0),
                     new Vector3(s * (heavy ? .62f : .52f), torsoH, s * (heavy ? .4f : .34f)), cloth, 0f, .3f);
            if (chestPlate)
                Gfx.Prim(PrimitiveType.Cube, torso, new Vector3(0, torsoH * .55f, s * .05f),
                         new Vector3(s * (heavy ? .66f : .56f), torsoH * .5f, s * .3f), armor, heavy ? .7f : .3f, heavy ? .6f : .35f);
            rig.torso = torso;

            var head = Node(torso, "head", new Vector3(0, torsoH, 0));
            Gfx.Prim(PrimitiveType.Cube, head, new Vector3(0, s * .16f, 0), new Vector3(s * .34f, s * .32f, s * .34f), skin, 0f, .35f);
            rig.head = head;

            foreach (float side in new[] { -1, 1 })
            {
                var shoulder = Node(torso, side < 0 ? "armL" : "armR", new Vector3(side * s * (heavy ? .38f : .32f), torsoH * .8f, 0));
                Gfx.Prim(PrimitiveType.Cube, shoulder, new Vector3(0, -s * .3f, 0),
                         new Vector3(s * .16f, s * .6f, s * .16f), cloth, 0f, .3f);
                if (side < 0) rig.armL = shoulder; else rig.armR = shoulder;
            }
        }

        // =================================================================
        //  构件库
        // =================================================================

        /// <summary>锥形尖（骨刺/角）：底粗顶尖，微随机倾斜。</summary>
        static GameObject Spike(Transform parent, Vector3 pos, float w, float h, Color c, float lean = 0f, float yaw = 0f)
        {
            var g = Gfx.MeshGo(Gfx.Frustum(0f, 4), parent, pos, new Vector3(w, h, w), c, 0f, .35f);
            g.transform.localRotation = Quaternion.Euler(lean, yaw, lean * .5f);
            return g;
        }

        /// <summary>风格化头骨：颅+下颌+颧骨；可选眼窝发光。elongate=true 为巫妖细长颅。</summary>
        static void CreateFacetedSkull(Transform parent, Vector3 pos, float s, Color bone, Color eyeGlow, bool glowEyes, bool elongate)
        {
            float ch = elongate ? .40f : .28f;                    // 颅高
            Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(0, s * ch * .5f + s * .06f, 0),
                     new Vector3(s * .30f, s * ch, s * .30f), bone, 0f, .4f);
            // 颧骨
            foreach (float side in new[] { -1, 1 })
                Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(side * s * .13f, s * .12f, s * .10f),
                         new Vector3(s * .09f, s * .08f, s * .14f), bone * .92f, 0f, .4f);
            // 下颌（前伸）
            Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(0, s * .02f, s * .05f),
                     new Vector3(s * .20f, s * .08f, s * .18f), bone * .9f, 0f, .4f);
            if (glowEyes)
                foreach (float side in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(side * s * .085f, s * .17f, s * .16f),
                             new Vector3(s * .07f, s * .06f, .03f), eyeGlow, 0f, .8f, true, .9f);
        }

        /// <summary>暴露肋骨架：脊柱 + n 根肋骨（正面弧段）。</summary>
        static void CreateBoneRibCage(Transform parent, Vector3 pos, float s, Color bone, int ribs = 3)
        {
            Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(0, s * .12f, -s * .1f),
                     new Vector3(s * .05f, s * .5f, s * .05f), bone * .85f, 0f, .35f);   // 脊柱
            for (int i = 0; i < ribs; i++)
                Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(0, s * (.02f + i * s * .0f) + i * s * .14f, s * .14f),
                         new Vector3(s * (.46f - i * .06f), s * .035f, s * .05f), bone, 0f, .35f);
        }

        /// <summary>人族纹章盾（鸢形）：双色盾面 + 金属包边 + 中央纹章凸台。盾面朝 +Z。</summary>
        static Transform CreateShieldWithEmblem(Transform parent, Vector3 pos, float s, Color c1, Color c2, Color trim)
        {
            var root = Node(parent, "heraldicShield", pos);
            root.localRotation = Quaternion.Euler(0, 0, 4);
            float w = s * .62f, h = s * .85f;
            // 双色盾面（左右分区）
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(-w * .25f, 0, 0), new Vector3(w * .5f, h, .06f), c1, .3f, .5f);
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(w * .25f, 0, 0), new Vector3(w * .5f, h, .06f), c2, .3f, .5f);
            // 下尖（鸢形底）
            var pt = Gfx.MeshGo(Gfx.Frustum(0f, 4), root, new Vector3(0, -h * .5f, 0),
                     new Vector3(w * .8f, s * .3f, .06f), c1, .3f, .5f);
            pt.transform.localRotation = Quaternion.Euler(0, 45, 180);
            // 金属包边
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(0, h * .5f, 0), new Vector3(w * 1.04f, s * .06f, .08f), trim, .85f, .7f);
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(-w * .5f, 0, 0), new Vector3(s * .05f, h, .08f), trim, .85f, .7f);
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(w * .5f, 0, 0), new Vector3(s * .05f, h, .08f), trim, .85f, .7f);
            // 纹章凸台（太阳圆盘）
            Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, s * .08f, .05f),
                     new Vector3(s * .3f, .04f, s * .3f), trim, .85f, .75f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            return root;
        }

        /// <summary>骨盾：圆骨盘 + 参差骨刺边 + 裂纹。</summary>
        static Transform CreateBoneShield(Transform parent, Vector3 pos, float s, Color bone)
        {
            var root = Node(parent, "boneShield", pos);
            var disc = Gfx.Prim(PrimitiveType.Cylinder, root, Vector3.zero,
                     new Vector3(s * .6f, .05f, s * .6f), bone * .9f, 0f, .35f);
            disc.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // 边缘参差骨刺
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 2.1f + .5f);
                Spike(root, new Vector3(Mathf.Cos(a) * s * .3f, Mathf.Sin(a) * s * .3f, 0),
                      s * .08f, s * .3f, bone, 0f, 0f).transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg - 90);
            }
            // 中央骨节
            Gfx.Prim(PrimitiveType.Sphere, root, new Vector3(0, 0, .04f), Vector3.one * s * .18f, bone, 0f, .4f);
            return root;
        }

        /// <summary>分层披风：n 片不规则分片，记录到 rig.capePieces 供摆动动画。</summary>
        static Transform[] CreateLayeredCape(Transform parent, Vector3 pos, float s, Color c, int pieces, BodyRig rig)
        {
            var list = new List<Transform>();
            for (int i = 0; i < pieces; i++)
            {
                float t = pieces == 1 ? 0 : i / (float)(pieces - 1);
                float px = Mathf.Lerp(-s * .24f, s * .24f, t);
                var p = Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(px, -s * .18f, -i * .008f),
                         new Vector3(s * .26f, s * .78f, .03f), i % 2 == 0 ? c : c * .92f, 0f, .4f);
                p.transform.localRotation = Quaternion.Euler(10, 0, px * -14f);
                list.Add(p.transform);
            }
            rig.capePieces = list.ToArray();
            return rig.capePieces;
        }

        /// <summary>弧形弓：3 段折线弓身 + 弓弦；bone=true 为骨弓（骨白+紫光节点）。</summary>
        static Transform CreateCurvedBow(Transform parent, Vector3 pos, float s, Color body, Color stringC, bool bone)
        {
            var root = Node(parent, bone ? "boneBow" : "longbow", pos);
            float h = s * 1.5f, spread = s * .22f;
            // 上臂/下臂向外弯
            var up = Gfx.Prim(PrimitiveType.Cube, root, new Vector3(0, h * .32f, spread * .5f),
                     new Vector3(.05f, h * .42f, .05f), body, 0f, bone ? .35f : .3f);
            up.transform.localRotation = Quaternion.Euler(0, 0, -14);
            var lo = Gfx.Prim(PrimitiveType.Cube, root, new Vector3(0, -h * .32f, spread * .5f),
                     new Vector3(.05f, h * .42f, .05f), body, 0f, bone ? .35f : .3f);
            lo.transform.localRotation = Quaternion.Euler(0, 0, 14);
            // 弓梢
            foreach (float sy in new[] { -1, 1 })
                Gfx.Prim(PrimitiveType.Cube, root, new Vector3(0, sy * h * .52f, 0),
                         new Vector3(.05f, h * .14f, .05f), body, 0f, .3f);
            // 弓弦（张紧直线）
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(0, 0, -spread * .02f),
                     new Vector3(.015f, h * 1.04f, .015f), stringC, 0f, .4f);
            if (bone)
                foreach (float sy in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Sphere, root, new Vector3(0, sy * h * .3f, spread * .5f),
                             Vector3.one * s * .06f, Pal.UGlow2, 0f, .8f, true, .8f);
            return root;
        }

        /// <summary>单手短枪：枪杆 + 叶形枪头 + 枪尾配重。持握点在原点，枪身沿 +Z。</summary>
        static Transform CreateSpear(Transform parent, Vector3 pos, float s, Color shaft, Color head, Color trim)
        {
            var root = Node(parent, "spear", pos);
            float len = s * 2.1f;
            Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0, len * .18f),
                     new Vector3(.05f, len * .5f, .05f), shaft, 0f, .3f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            // 叶形枪头（四棱锥）
            Gfx.MeshGo(Gfx.Frustum(0f, 4), root, new Vector3(0, 0, len * .68f),
                     new Vector3(s * .16f, s * .34f, s * .16f), head, .85f, .8f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            // 枪尾配重
            Gfx.Prim(PrimitiveType.Sphere, root, new Vector3(0, 0, -len * .32f), Vector3.one * s * .07f, trim, .8f, .6f);
            return root;
        }

        /// <summary>骑枪：更长更粗 + 大锥枪头 + 金色绑带。沿 +Z。</summary>
        static Transform CreateLance(Transform parent, Vector3 pos, float s, Color shaft, Color head, Color trim)
        {
            var root = Node(parent, "lance", pos);
            float len = s * 3.2f;
            Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0, len * .22f),
                     new Vector3(.07f, len * .52f, .07f), shaft, 0f, .3f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            foreach (float tz in new[] { .30f, .38f })
                Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0, len * tz),
                         new Vector3(.09f, .05f, .09f), trim, .85f, .7f)
                   .transform.localRotation = Quaternion.Euler(90, 0, 0);
            Gfx.MeshGo(Gfx.Frustum(0f, 4), root, new Vector3(0, 0, len * .76f),
                     new Vector3(s * .2f, s * .42f, s * .2f), head, .85f, .8f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            Gfx.Prim(PrimitiveType.Sphere, root, new Vector3(0, 0, -len * .3f), Vector3.one * s * .08f, trim, .8f, .6f);
            return root;
        }

        /// <summary>符文环（光环/法阵）：XZ 平面环，自发光。</summary>
        static GameObject CreateRuneRing(Transform parent, Vector3 pos, float radius, Color c, float emis = 1.2f)
        {
            var g = Gfx.MeshGo(Gfx.RingMesh(), parent, pos, Vector3.one * radius * 2f, c, 0f, .5f, true, emis);
            g.name = "runeRing";
            return g;
        }

        /// <summary>灵魂晶体：外晶 + 内芯发光。</summary>
        static Transform CreateSoulCrystal(Transform parent, Vector3 pos, float s, Color c)
        {
            var root = Node(parent, "soulCrystal", pos);
            Gfx.MeshGo(Gfx.Crystal(), root, Vector3.zero, Vector3.one * s, c, .1f, .85f, true, .8f)
               .transform.localRotation = Quaternion.Euler(0, 25, 0);
            Gfx.MeshGo(Gfx.Crystal(), root, Vector3.zero, Vector3.one * s * .5f, Color.white, .1f, .9f, true, 1.6f);
            return root;
        }

        /// <summary>悬浮碎片（不规则骨片/符文石）。</summary>
        static GameObject CreateFloatingShard(Transform parent, Vector3 pos, float s, Color c, float emis = 0f)
        {
            var g = Gfx.MeshGo(Gfx.Frustum(0f, 4), parent, pos, new Vector3(s * .5f, s, s * .5f), c, 0f, .4f, emis > 0, emis);
            g.name = "floatingShard";
            g.transform.localRotation = Quaternion.Euler(15, pos.x * 400, 10);
            return g;
        }

        /// <summary>带角头盔：盔体 + 护鼻 + 双角。</summary>
        static void CreateHornedHelmet(Transform parent, Vector3 pos, float s, Color metal, Color horn)
        {
            Gfx.Prim(PrimitiveType.Cube, parent, pos, new Vector3(s * .38f, s * .18f, s * .38f), metal, .8f, .7f);
            Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(0, -s * .08f, s * .17f),
                     new Vector3(s * .06f, s * .16f, .04f), metal, .8f, .7f);
            foreach (float side in new[] { -1, 1 })
            {
                var h = Gfx.Prim(PrimitiveType.Cube, parent, pos + new Vector3(side * s * .2f, s * .08f, 0),
                         new Vector3(s * .05f, s * .24f, s * .05f), horn, 0f, .35f);
                h.transform.localRotation = Quaternion.Euler(0, 0, side * -35);
            }
        }

        /// <summary>巫妖悬浮骨冠：n 片不对称悬浮碎骨，记入 floatingParts。</summary>
        static void CreateLichCrown(Transform parent, Vector3 pos, float s, Color bone, List<Transform> floats)
        {
            int n = 4;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * .5f + .35f;
                float r = s * (.16f + (i % 2) * .05f);
                var g = CreateFloatingShard(parent, pos + new Vector3(Mathf.Cos(a) * r, s * (.1f + i * .045f), Mathf.Sin(a) * r),
                                            s * .28f, bone, 0f);
                floats.Add(g.transform);
            }
        }

        /// <summary>巫妖法杖：杖身 + 顶部骨环 + 灵魂晶体。顶端为 projectileOrigin。</summary>
        static Transform CreateLichStaff(Transform parent, Vector3 pos, float s)
        {
            var root = Node(parent, "lichStaff", pos);
            float len = s * 2.0f;
            Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, len * .3f, 0),
                     new Vector3(.05f, len * .5f, .05f), Pal.UBone * .8f, 0f, .35f);
            // 骨环（顶端托架）
            var ring = Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, len * .78f, 0),
                     new Vector3(s * .22f, .04f, s * .22f), Pal.UBone, 0f, .4f);
            ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // 顶部晶体
            var crystal = CreateSoulCrystal(root, new Vector3(0, len * .92f, 0), s * .3f, Pal.UGlow);
            crystal.name = "staffCrystal";
            return Node(crystal, "projectileOrigin", Vector3.up * s * .18f);
        }

        /// <summary>战旗：旗杆 + 横杆 + 2 片飘带。</summary>
        static void CreateBanner(Transform parent, Vector3 pos, float s, Color cloth, Color trim, BodyRig rig)
        {
            var root = Node(parent, "banner", pos);
            Gfx.Prim(PrimitiveType.Cylinder, root, new Vector3(0, s * .55f, 0),
                     new Vector3(.04f, s * .55f, .04f), Pal.WoodD, 0f, .3f);
            Gfx.Prim(PrimitiveType.Cube, root, new Vector3(s * .14f, s * 1.02f, 0),
                     new Vector3(s * .3f, .04f, .04f), Pal.WoodD, 0f, .3f);
            var strips = new List<Transform>();
            for (int i = 0; i < 2; i++)
            {
                var p = Gfx.Prim(PrimitiveType.Cube, root, new Vector3(s * (.14f + i * .17f), s * .86f, 0),
                         new Vector3(s * .16f, s * .3f, .02f), i == 0 ? cloth : trim, 0f, .45f);
                p.transform.localRotation = Quaternion.Euler(0, 0, -6);
                strips.Add(p.transform);
            }
            rig.banner = root;
            // 复用 capePieces 通道做飘带摆动（没有披风时）
            if (rig.capePieces == null) rig.capePieces = strips.ToArray();
            else
            {
                var merged = new List<Transform>(rig.capePieces); merged.AddRange(strips);
                rig.capePieces = merged.ToArray();
            }
        }

        /// <summary>护甲片（肩甲/马铠）：压扁多面体。</summary>
        static void CreateArmorPlate(Transform parent, Vector3 pos, float s, Color c, bool gold)
        {
            Gfx.Prim(PrimitiveType.Sphere, parent, pos, new Vector3(s * .3f, s * .2f, s * .3f), c, gold ? .85f : .6f, gold ? .75f : .5f);
        }

        // =================================================================
        //  工人
        // =================================================================

        static void BuildWorker(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            if (h)
            {
                // 农夫：草帽 + 皮围裙 + 肩扛锄头 + 背木材
                HumanoidBase(rig, def, new Color(.9f, .72f, .55f), new Color(.6f, .5f, .35f), new Color(.5f, .42f, .3f), false, chestPlate: false);
                var hatC = new Color(.85f, .75f, .4f);
                Gfx.Prim(PrimitiveType.Cylinder, rig.head, new Vector3(0, s * .3f, 0), new Vector3(s * .55f, .05f, s * .55f), hatC, 0f, .35f);
                Gfx.Prim(PrimitiveType.Sphere, rig.head, new Vector3(0, s * .32f, 0), new Vector3(s * .3f, s * .15f, s * .3f), hatC, 0f, .35f);
                // 皮围裙 + 工具袋
                Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(0, s * .28f, s * .17f),
                         new Vector3(s * .4f, s * .5f, .03f), Pal.Leather, 0f, .3f);
                Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(s * .18f, s * .2f, s * .14f),
                         new Vector3(s * .16f, s * .18f, s * .1f), Pal.WoodD, 0f, .25f);
                // 肩扛锄头
                var tool = Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(0, -s * .1f, s * .1f), new Vector3(.05f, s * 1.1f, .05f), Pal.WoodL, 0f, .3f);
                tool.transform.localRotation = Quaternion.Euler(0, 0, 40);
                Gfx.Prim(PrimitiveType.Cube, rig.armR, new Vector3(s * .3f, s * .32f, s * .1f), new Vector3(.04f, .2f, .15f), Pal.Iron, .7f, .5f);
                rig.weaponMain = tool.transform;
                // 背负木材小捆
                foreach (float i in new[] { 0f, 1f, 2f })
                    Gfx.Prim(PrimitiveType.Cylinder, rig.torso, new Vector3(-s * .05f + i * s * .06f, s * .5f, -s * .24f),
                             new Vector3(.09f, s * .5f, .09f), i == 1 ? Pal.WoodL : Pal.WoodD, 0f, .3f)
                       .transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            else
            {
                // 侍僧：破损长袍 + 半遮面兜帽 + 悬浮魂灯 + 腰间灵魂瓶（无腿微悬浮）
                var robeC = new Color(.32f, .25f, .4f);
                var robe = Gfx.MeshGo(Gfx.Frustum(.34f, 8), rig.root, Vector3.zero,
                         new Vector3(s * .8f, s * 1.3f, s * .8f), robeC, 0f, .3f);
                robe.name = "acolyteRobe";
                rig.torso = robe.transform;
                // 下摆破片
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * .5f + .4f;
                    var sh = Gfx.Prim(PrimitiveType.Cube, rig.root, new Vector3(Mathf.Cos(a) * s * .3f, s * .12f, Mathf.Sin(a) * s * .3f),
                             new Vector3(s * .16f, s * .3f, .03f), robeC * .85f, 0f, .3f);
                    sh.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 12, a * Mathf.Rad2Deg, 0);
                }
                // 头 + 兜帽（只露眼窝冷光）挂袍身，随起伏整体运动
                var head = Node(rig.torso, "head", new Vector3(0, s * 1.3f, 0));
                Gfx.Prim(PrimitiveType.Cube, head, new Vector3(0, s * .1f, 0), new Vector3(s * .3f, s * .26f, s * .3f), Pal.UBone, 0f, .35f);
                Gfx.Prim(PrimitiveType.Cube, head, new Vector3(0, s * .16f, -s * .03f), new Vector3(s * .38f, s * .3f, s * .38f), robeC * .8f, 0f, .35f);
                foreach (float ex in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Cube, head, new Vector3(ex * s * .08f, s * .1f, s * .15f),
                             new Vector3(s * .06f, s * .05f, .03f), Pal.UGlow, 0f, .8f, true, .9f);
                rig.head = head;
                // 悬浮魂灯（手持引导灵魂能量）
                var lamp = Node(rig.root, "soulLamp", new Vector3(s * .3f, s * .75f, s * .25f));
                Gfx.Prim(PrimitiveType.Sphere, lamp, Vector3.zero, Vector3.one * s * .13f, Pal.UGlow, 0f, .9f, true, 1.2f);
                Gfx.Prim(PrimitiveType.Cylinder, lamp, new Vector3(0, s * .12f, 0),
                         new Vector3(s * .16f, .02f, s * .16f), Pal.UBone, 0f, .4f);
                rig.weaponMain = lamp;
                // 腰间灵魂瓶 ×2
                foreach (float side in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Sphere, rig.torso, new Vector3(side * s * .22f, s * .45f, s * .12f),
                             Vector3.one * s * .1f, side < 0 ? Pal.UGlow2 : Pal.UGlow, 0f, .85f, true, .8f);
                // 魂雾底座（极弱发光，不遮挡）
                Gfx.Prim(PrimitiveType.Sphere, rig.root, new Vector3(0, .06f, 0),
                         new Vector3(s * .4f, .1f, s * .4f), Pal.UGlow2, 0f, .9f, true, .5f);
            }
            // 工人弹道/采集引导起点
            rig.projectileOrigin = rig.weaponMain != null ? Node(rig.weaponMain, "projectileOrigin", Vector3.zero) : null;
        }

        // =================================================================
        //  步兵
        // =================================================================

        static void BuildInfantry(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            if (h)
            {
                // 枪盾步兵：副手大纹章盾 + 主手单手短枪，盾前置枪侧出
                HumanoidBase(rig, def, new Color(.9f, .72f, .55f), new Color(.22f, .34f, .62f), Pal.Iron, true);
                // 头盔：护鼻 + 短羽饰
                Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .26f, 0), new Vector3(s * .4f, s * .18f, s * .4f), Pal.Iron, .8f, .7f);
                Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .13f, s * .19f), new Vector3(s * .07f, s * .2f, .04f), Pal.Iron, .8f, .7f);
                var plume = Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .4f, -s * .06f),
                         new Vector3(s * .07f, s * .2f, s * .3f), new Color(.8f, .2f, .25f), 0f, .4f);
                plume.transform.localRotation = Quaternion.Euler(-12, 0, 0);
                // 护肩
                foreach (float side in new[] { -1, 1 })
                    CreateArmorPlate(rig.torso, new Vector3(side * s * .4f, s * .72f, 0), s, Pal.Iron, false);
                // 右手短枪（枪尖从盾侧探出）
                var spear = CreateSpear(rig.armR, new Vector3(0, -s * .55f, s * .3f), s, Pal.WoodL, Pal.Iron, Pal.HGold);
                spear.localRotation = Quaternion.Euler(-8, 0, 0);
                rig.weaponMain = spear;
                rig.thrustAttack = true;
                // 左手大纹章盾（最大识别元素，前置）
                var shield = CreateShieldWithEmblem(rig.armL, new Vector3(-s * .12f, -s * .3f, s * .22f), s * 1.25f,
                                                    new Color(.2f, .34f, .6f), new Color(.78f, .75f, .68f), Pal.HGold);
                rig.weaponOff = shield;
            }
            else
            {
                // 骷髅兵：裸露肋骨架 + 骨盾 + 锈刀 + 不对称骨刺肩
                HumanoidBase(rig, def, Pal.UBone, Pal.UBone * .9f, Pal.UStone, false, chestPlate: false);
                CreateFacetedSkull(rig.head, new Vector3(0, s * .05f, 0), s, Pal.UBone, new Color(1f, .3f, .2f), true, false);
                CreateBoneRibCage(rig.torso, new Vector3(0, s * .3f, 0), s, Pal.UBone, 3);
                // 盆骨
                Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(0, -s * .02f, 0),
                         new Vector3(s * .4f, s * .1f, s * .26f), Pal.UBone * .88f, 0f, .35f);
                // 左肩骨刺（不对称，表现拼接腐朽）
                Spike(rig.torso, new Vector3(-s * .36f, s * .75f, 0), s * .1f, s * .34f, Pal.UBone, 0f, 0f)
                    .transform.localRotation = Quaternion.Euler(0, 0, 38);
                // 右手锈刀（断裂重绑）
                var sword = Node(rig.armR, "rustedBlade", new Vector3(0, -s * .55f, s * .35f));
                Gfx.Prim(PrimitiveType.Cube, sword, new Vector3(0, 0, -s * .12f), new Vector3(.04f, .04f, s * .16f), Pal.WoodD, 0f, .3f);
                var blade = Gfx.Prim(PrimitiveType.Cube, sword, new Vector3(0, 0, s * .28f),
                         new Vector3(s * .09f, .03f, s * .55f), Pal.IronD * .8f, .6f, .45f);
                blade.transform.localRotation = Quaternion.Euler(6, 0, 0);
                // 刃口缺口（绑扎布条）
                Gfx.Prim(PrimitiveType.Cube, sword, new Vector3(0, 0, s * .1f), new Vector3(s * .11f, .04f, s * .08f), Pal.UCloth, 0f, .3f);
                rig.weaponMain = sword;
                // 左手骨盾
                var shield = CreateBoneShield(rig.armL, new Vector3(-s * .1f, -s * .3f, s * .16f), s * 1.1f, Pal.UBone);
                shield.localRotation = Quaternion.Euler(0, 0, 6);
                rig.weaponOff = shield;
            }
            rig.projectileOrigin = rig.weaponMain != null
                ? Node(rig.weaponMain, "projectileOrigin", new Vector3(0, 0, def.size * 1.6f)) : null;
        }

        // =================================================================
        //  远程
        // =================================================================

        static void BuildRanged(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            if (h)
            {
                // 长弓手：弧形长弓 + 箭袋 + 披肩 + 护腕
                HumanoidBase(rig, def, new Color(.9f, .72f, .55f), new Color(.3f, .5f, .3f), Pal.Leather, false, chestPlate: false);
                Gfx.Prim(PrimitiveType.Cube, rig.head, new Vector3(0, s * .2f, -s * .04f), new Vector3(s * .4f, s * .3f, s * .4f), new Color(.24f, .4f, .26f), 0f, .35f);
                // 披肩
                Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(0, s * .72f, 0),
                         new Vector3(s * .56f, s * .16f, s * .4f), new Color(.24f, .4f, .26f), 0f, .35f);
                var bow = CreateCurvedBow(rig.armL, new Vector3(-s * .04f, -s * .4f, s * .18f), s, Pal.WoodD, new Color(.9f, .9f, .85f), false);
                rig.weaponMain = bow;
                // 箭袋 + 箭羽
                Gfx.Prim(PrimitiveType.Cylinder, rig.torso, new Vector3(s * .15f, s * .55f, -s * .2f),
                         new Vector3(s * .14f, s * .4f, s * .14f), Pal.Leather, 0f, .3f);
                foreach (float i in new[] { 0f, 1f, 2f })
                    Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(s * .12f + i * s * .04f, s * .82f, -s * .2f),
                             new Vector3(.02f, s * .14f, .02f), i == 1 ? new Color(.85f, .25f, .25f) : Pal.UBone * .9f, 0f, .4f);
                // 护腕
                Gfx.Prim(PrimitiveType.Cube, rig.armL, new Vector3(0, -s * .42f, 0),
                         new Vector3(s * .19f, s * .1f, s * .19f), Pal.Leather, 0f, .3f);
            }
            else
            {
                // 亡灵射手：骨弓 + 破碎斗篷 + 眼窝胸腔冷光
                HumanoidBase(rig, def, Pal.UBone, new Color(.3f, .2f, .4f), Pal.UStone, false, chestPlate: false);
                CreateFacetedSkull(rig.head, new Vector3(0, s * .04f, 0), s, Pal.UBone, Pal.UGlow2, true, false);
                // 破碎斗篷（分片）
                var capeC = new Color(.28f, .16f, .4f);
                var pieces = new List<Transform>();
                for (int i = 0; i < 3; i++)
                {
                    float px = Mathf.Lerp(-s * .2f, s * .2f, i / 2f);
                    var p = Gfx.Prim(PrimitiveType.Cube, rig.torso, new Vector3(px, s * .3f, -s * .2f),
                             new Vector3(s * .22f, s * .7f - (i == 1 ? 0 : s * .12f), .03f), i == 1 ? capeC : capeC * .9f, 0f, .3f);
                    p.transform.localRotation = Quaternion.Euler(8, 0, px * -16f);
                    pieces.Add(p.transform);
                }
                rig.capePieces = pieces.ToArray();
                var bow = CreateCurvedBow(rig.armL, new Vector3(-s * .04f, -s * .4f, s * .18f), s, Pal.UBone, Pal.UGlow2, true);
                rig.weaponMain = bow;
                // 胸腔冷光
                Gfx.Prim(PrimitiveType.Sphere, rig.torso, new Vector3(0, s * .4f, s * .1f),
                         Vector3.one * s * .1f, Pal.UGlow2, 0f, .85f, true, .7f);
            }
            rig.projectileOrigin = rig.weaponMain != null
                ? Node(rig.weaponMain, "projectileOrigin", new Vector3(0, 0, def.size * .5f)) : null;
        }

        // =================================================================
        //  骑兵（含坐骑构建）
        // =================================================================

        /// <summary>马体：躯干/颈/头/耳/鬃/尾/四腿。bone=true 为不死骨马（骨白+黑曜甲+魂火眼）。
        /// 返回马身 Transform（rig.torso），马腿挂点写入 rig.horseLegs。</summary>
        static Transform CreateHorseBody(BodyRig rig, Transform parent, float s, bool bone)
        {
            var bodyC = bone ? Pal.UBone : new Color(.45f, .3f, .18f);
            var darkC = bone ? new Color(.2f, .2f, .25f) : new Color(.35f, .22f, .12f);
            var horse = Node(parent, bone ? "boneHorse" : "horse", Vector3.zero);
            rig.torso = horse;
            Gfx.Prim(PrimitiveType.Capsule, horse, new Vector3(0, s * .85f, 0),
                     new Vector3(s * .62f, s * 1.1f, s * .62f), bodyC, 0f, .35f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            var neck = Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.3f, s * .55f),
                     new Vector3(s * .28f, s * .7f, s * .3f), bodyC, 0f, .35f);
            neck.transform.localRotation = Quaternion.Euler(30, 0, 0);
            Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.6f, s * .85f),
                     new Vector3(s * .26f, s * .24f, s * .55f), bodyC, 0f, .35f);
            // 马耳
            foreach (float ex in new[] { -1, 1 })
                Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(ex * s * .08f, s * 1.78f, s * .72f),
                         new Vector3(s * .06f, s * .16f, .04f), darkC, 0f, .3f);
            // 眼（骨马为魂火）
            foreach (float ex in new[] { -1, 1 })
                Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(ex * s * .13f, s * 1.62f, s * 1.05f),
                         new Vector3(s * .05f, s * .05f, .03f), bone ? Pal.UGlow : new Color(.1f, .08f, .06f), 0f, bone ? .8f : .2f, bone, .8f);
            // 鬃毛 + 尾
            Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.42f, s * .42f),
                     new Vector3(.06f, s * .55f, s * .3f), darkC, 0f, .3f).transform.localRotation = Quaternion.Euler(35, 0, 0);
            var tail = Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * .95f, -s * .95f),
                     new Vector3(s * .12f, s * .7f, s * .12f), darkC, 0f, .3f);
            tail.transform.localRotation = Quaternion.Euler(30, 0, 0);
            // 四条马腿（挂点）
            rig.horseLegs = new Transform[4];
            int li = 0;
            foreach (float lx in new[] { -1, 1 }) foreach (float lz in new[] { -1, 1 })
            {
                var leg = Node(horse, "hleg" + li, new Vector3(lx * s * .22f, s * .7f, lz * s * .55f));
                Gfx.Prim(PrimitiveType.Cube, leg, new Vector3(0, -s * .32f, 0), new Vector3(s * .14f, s * .64f, s * .14f), bodyC, 0f, .35f);
                // 蹄（骨马为黑曜蹄+微光）
                Gfx.Prim(PrimitiveType.Cube, leg, new Vector3(0, -s * .62f, 0),
                         new Vector3(s * .16f, s * .1f, s * .16f), bone ? Pal.IronD : darkC, bone ? .5f : 0f, .3f);
                if (bone)
                    Gfx.Prim(PrimitiveType.Cube, leg, new Vector3(0, -s * .56f, 0),
                             new Vector3(s * .18f, .02f, s * .18f), Pal.UGlow, 0f, .8f, true, .6f);
                rig.horseLegs[li++] = leg;
            }
            // 鞍垫
            Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * 1.32f, 0), new Vector3(s * .55f, .1f, s * .6f),
                     bone ? Pal.UCloth : Pal.HCloth, 0f, .4f);
            Gfx.Prim(PrimitiveType.Cube, horse, new Vector3(0, s * .62f, s * .85f),
                     new Vector3(s * .3f, s * .5f, .04f), bone ? Pal.UCloth : Pal.HCloth, 0f, .4f); // 胸前马衣
            return horse;
        }

        static void BuildCavalry(BodyRig rig, UnitDef def, bool h)
        {
            float s = def.size;
            var horse = CreateHorseBody(rig, rig.root, s, !h);
            // 骑手
            var rider = Node(horse, "rider", new Vector3(0, s * 1.35f, 0));
            rig.head = rider;
            var skin = h ? new Color(.9f, .72f, .55f) : Pal.UBone;
            var cloth = h ? new Color(.25f, .38f, .68f) : new Color(.3f, .2f, .4f);
            foreach (float side in new[] { -1, 1 })
            {
                var leg = Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(side * s * .32f, -s * .05f, 0),
                         new Vector3(s * .14f, s * .5f, s * .14f), cloth, 0f, .3f);
                leg.transform.localRotation = Quaternion.Euler(0, 0, side * -18);
            }
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .3f, 0), new Vector3(s * .5f, s * .6f, s * .34f),
                     h ? Pal.Iron : Pal.IronD, h ? .7f : .8f, h ? .6f : .5f);
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .72f, 0), new Vector3(s * .32f, s * .3f, s * .32f), skin, 0f, .35f);
            var armR = Node(rider, "armR", new Vector3(s * .3f, s * .55f, 0));
            rig.armR = armR;
            Gfx.Prim(PrimitiveType.Cube, armR, new Vector3(0, -s * .2f, 0), new Vector3(s * .14f, s * .4f, s * .14f), cloth, 0f, .3f);
            if (h)
            {
                // 骑士：盔 + 盔缨 + 骑枪
                Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .8f, 0), new Vector3(s * .36f, s * .18f, s * .36f), Pal.Iron, .8f, .7f);
                Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .98f, -s * .05f), new Vector3(s * .08f, s * .2f, s * .4f),
                         new Color(.85f, .25f, .25f), 0f, .4f);
                var lance = CreateLance(armR, new Vector3(0, -s * .3f, s * .2f), s, Pal.WoodL, Pal.Iron, Pal.HGold);
                lance.localRotation = Quaternion.Euler(-6, 0, 0);
                rig.weaponMain = lance;
                // 马衣纹章
                Gfx.Prim(PrimitiveType.Cylinder, horse, new Vector3(0, s * .88f, 0),
                         new Vector3(s * .66f, s * .5f, s * .66f), Pal.HCloth, 0f, .4f)
                   .transform.localRotation = Quaternion.Euler(90, 0, 0);
            }
            else
            {
                // 死亡骑士：骨冠尖角盔 + 符文剑 + 破碎小旗
                CreateHornedHelmet(rider, new Vector3(0, s * .82f, 0), s, Pal.IronD, Pal.UBone);
                foreach (float ex in new[] { -1, 1 })
                    Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(ex * s * .08f, s * .72f, s * .17f),
                             new Vector3(s * .06f, s * .05f, .03f), new Color(1f, .3f, .2f), 0f, .8f, true, .9f);
                var sword = Node(armR, "runeBlade", new Vector3(0, -s * .3f, s * .3f));
                Gfx.Prim(PrimitiveType.Cube, sword, new Vector3(0, 0, s * .35f),
                         new Vector3(s * .1f, .03f, s * .8f), Pal.IronD, .7f, .5f);
                Gfx.Prim(PrimitiveType.Cube, sword, new Vector3(0, 0, s * .35f),
                         new Vector3(s * .04f, .04f, s * .7f), Pal.UGlow2, .5f, .8f, true, .8f);
                rig.weaponMain = sword;
                // 破碎小旗（马尾侧）
                var flagPole = Gfx.Prim(PrimitiveType.Cylinder, horse, new Vector3(s * .18f, s * 1.5f, -s * .7f),
                         new Vector3(.03f, s * .5f, .03f), Pal.UBone * .8f, 0f, .35f);
                var rag = Gfx.Prim(PrimitiveType.Cube, flagPole.transform, new Vector3(s * .18f, s * .35f, 0),
                         new Vector3(s * .34f, s * .18f, .02f), Pal.UCloth, 0f, .35f);
                rag.transform.localRotation = Quaternion.Euler(0, 0, -12);
                // 黑曜马铠
                CreateArmorPlate(horse, new Vector3(0, s * 1.15f, s * .55f), s * 1.2f, new Color(.15f, .14f, .18f), false);
                CreateArmorPlate(horse, new Vector3(0, s * 1.1f, -s * .5f), s * 1.2f, new Color(.15f, .14f, .18f), false);
            }
            rig.projectileOrigin = rig.weaponMain != null
                ? Node(rig.weaponMain, "projectileOrigin", new Vector3(0, 0, def.size * 2.4f)) : null;
        }

        // =================================================================
        //  英雄专属
        // =================================================================

        /// <summary>人族英雄 骑士团长：军团视觉核心。专属重盔/三层胸甲徽记/分层肩甲/
        /// 披风分片+战旗/圣辉重骑枪/纹章马甲/金色光环。</summary>
        static void BuildLordKnightHero(BodyRig rig, UnitDef def)
        {
            float s = def.size;   // 1.5：体量经更大马体/盔冠/旗帜体现，不靠整体缩放
            var horse = CreateHorseBody(rig, rig.root, s, false);
            // 专属纹章马衣（蓝底金边）
            Gfx.Prim(PrimitiveType.Cylinder, horse, new Vector3(0, s * .88f, 0),
                     new Vector3(s * .68f, s * .5f, s * .68f), new Color(.16f, .28f, .6f), 0f, .45f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            Gfx.Prim(PrimitiveType.Cylinder, horse, new Vector3(0, s * .88f, 0),
                     new Vector3(s * .72f, s * .34f, s * .72f), Pal.HGold, .85f, .7f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            // 骑手
            var rider = Node(horse, "rider", new Vector3(0, s * 1.35f, 0));
            rig.head = rider;
            var cloth = new Color(.2f, .3f, .6f);
            foreach (float side in new[] { -1, 1 })
            {
                var leg = Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(side * s * .32f, -s * .05f, 0),
                         new Vector3(s * .15f, s * .5f, s * .15f), Pal.Iron, .7f, .55f);
                leg.transform.localRotation = Quaternion.Euler(0, 0, side * -18);
            }
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .3f, 0), new Vector3(s * .54f, s * .62f, s * .36f), cloth, 0f, .35f);
            // 三层胸甲徽记：底板 + 纹章 + 发光细线
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .34f, s * .16f), new Vector3(s * .44f, s * .4f, .06f), Pal.Iron, .8f, .7f);
            Gfx.Prim(PrimitiveType.Cylinder, rider, new Vector3(0, s * .38f, s * .2f), new Vector3(s * .22f, .04f, s * .22f), Pal.HGold, .85f, .75f)
               .transform.localRotation = Quaternion.Euler(90, 0, 0);
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .38f, s * .22f), new Vector3(s * .05f, s * .26f, .02f), new Color(1f, .9f, .5f), 0f, .6f, true, 1.2f);
            // 分层肩甲（金）
            foreach (float side in new[] { -1, 1 })
                CreateArmorPlate(rider, new Vector3(side * s * .38f, s * .62f, 0), s * 1.15f, Pal.HGold, true);
            // 头 + 专属高耸羽冠重盔
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .74f, 0), new Vector3(s * .34f, s * .3f, s * .34f), new Color(.92f, .74f, .58f), 0f, .35f);
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .92f, 0), new Vector3(s * .4f, s * .2f, s * .4f), Pal.Iron, .85f, .75f);
            Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * .88f, s * .19f), new Vector3(s * .08f, s * .18f, .04f), Pal.HGold, .85f, .7f);
            var crest = Gfx.Prim(PrimitiveType.Cube, rider, new Vector3(0, s * 1.12f, -s * .08f),
                     new Vector3(s * .1f, s * .38f, s * .5f), new Color(.75f, .15f, .2f), 0f, .4f);
            crest.transform.localRotation = Quaternion.Euler(-14, 0, 0);
            // 手臂
            var armR = Node(rider, "armR", new Vector3(s * .32f, s * .55f, 0));
            rig.armR = armR;
            Gfx.Prim(PrimitiveType.Cube, armR, new Vector3(0, -s * .2f, 0), new Vector3(s * .15f, s * .42f, s * .15f), cloth, 0f, .3f);
            var lance = CreateLance(armR, new Vector3(0, -s * .3f, s * .25f), s * 1.15f, Pal.WoodL, Pal.HGold, Pal.HGold);
            lance.name = "holyLance";
            lance.localRotation = Quaternion.Euler(-5, 0, 0);
            rig.weaponMain = lance;
            // 短披风（红蓝金分片）+ 背部战旗
            CreateLayeredCape(rider, new Vector3(0, s * .6f, -s * .2f), s, new Color(.7f, .15f, .2f), 3, rig);
            CreateBanner(rider, new Vector3(-s * .28f, s * .55f, -s * .26f), s * 1.1f, new Color(.16f, .28f, .6f), Pal.HGold, rig);
            // 金色攻击光环（双层符文环，低亮度）
            rig.auraOrigin = Node(rig.root, "auraOrigin", new Vector3(0, .05f, 0));
            CreateRuneRing(rig.auraOrigin, Vector3.zero, def.size * 1.15f, new Color(1f, .85f, .4f), 1.0f);
            CreateRuneRing(rig.auraOrigin, new Vector3(0, .03f, 0), def.size * .85f, new Color(1f, .7f, .3f), .8f);
            rig.projectileOrigin = Node(lance, "projectileOrigin", new Vector3(0, 0, s * 3.8f));
        }

        /// <summary>不死英雄 巫妖（DeathRanger 视觉重定义）：细长头骨 + 悬浮骨冠 + 破碎法袍 +
        /// 灵魂核心 + 巫妖法杖 + 背部悬浮符文碎片 + 紫绿双层光环。无腿悬浮。</summary>
        static void BuildLichHero(BodyRig rig, UnitDef def)
        {
            float s = def.size;   // 1.1
            var floats = new List<Transform>();
            // 破碎法袍（倒三角轮廓，5 分片下摆）
            var robeC = new Color(.24f, .18f, .34f);
            var robe = Gfx.MeshGo(Gfx.Frustum(.3f, 8), rig.root, Vector3.zero,
                     new Vector3(s * .95f, s * 1.5f, s * .95f), robeC, 0f, .3f);
            robe.name = "lichRobe";
            rig.torso = robe.transform;
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2 / 5 + .3f;
                var sh = Gfx.Prim(PrimitiveType.Cube, rig.root,
                         new Vector3(Mathf.Cos(a) * s * .38f, s * .18f, Mathf.Sin(a) * s * .38f),
                         new Vector3(s * .15f, s * .4f, .03f), i % 2 == 0 ? robeC : robeC * .85f, 0f, .3f);
                sh.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 14, a * Mathf.Rad2Deg, 0);
            }
            // 内层：脊柱 + 肋骨 + 灵魂核心（胸口可见，不被遮挡）
            Gfx.Prim(PrimitiveType.Cube, rig.root, new Vector3(0, s * .75f, s * .02f),
                     new Vector3(s * .05f, s * .5f, s * .05f), Pal.UBone * .85f, 0f, .35f);
            CreateBoneRibCage(rig.root, new Vector3(0, s * .72f, 0), s, Pal.UBone, 2);
            var core = CreateSoulCrystal(rig.root, new Vector3(0, s * .82f, s * .16f), s * .34f, Pal.UGlow);
            core.name = "lichSoulCore";
            rig.soulCore = core;
            // 头：细长头骨 + 眼窝幽绿/灵魂紫双层发光 + 悬浮骨冠
            var head = Node(rig.root, "head", new Vector3(0, s * 1.55f, 0));
            CreateFacetedSkull(head, Vector3.zero, s * 1.1f, Pal.UBone, Pal.UGlow, true, true);
            foreach (float ex in new[] { -1, 1 })
                Gfx.Prim(PrimitiveType.Cube, head, new Vector3(ex * s * .09f, s * .19f, s * .17f),
                         new Vector3(s * .08f, s * .05f, .03f), Pal.UGlow2, 0f, .8f, true, .8f);
            rig.head = head;
            CreateLichCrown(head, new Vector3(0, s * .3f, 0), s, Pal.UBone, floats);
            // 尖长骨肩甲
            foreach (float side in new[] { -1, 1 })
            {
                var p = Spike(rig.root, new Vector3(side * s * .42f, s * 1.3f, 0), s * .16f, s * .55f, Pal.UBone, 0f, 0f);
                p.transform.localRotation = Quaternion.Euler(0, 0, side * -52);
            }
            // 双臂（骨手前伸持杖）
            var armR = Node(rig.root, "armR", new Vector3(s * .38f, s * 1.15f, s * .1f));
            var armL = Node(rig.root, "armL", new Vector3(-s * .38f, s * 1.15f, s * .1f));
            foreach (var arm in new[] { armR, armL })
            {
                Gfx.Prim(PrimitiveType.Cube, arm, new Vector3(0, -s * .22f, s * .12f),
                         new Vector3(s * .12f, s * .45f, s * .12f), robeC * .9f, 0f, .3f)
                   .transform.localRotation = Quaternion.Euler(35, 0, 0);
                Gfx.Prim(PrimitiveType.Sphere, arm, new Vector3(0, -s * .42f, s * .28f),
                         Vector3.one * s * .09f, Pal.UBone, 0f, .4f);   // 骨手
            }
            rig.armR = armR; rig.armL = armL;
            // 主法器：巫妖法杖（右手），顶端晶体为弹道起点
            var staffTip = CreateLichStaff(armR, new Vector3(0, -s * .42f, s * .3f), s);
            rig.staff = armR;
            rig.weaponMain = staffTip.parent.parent;   // staff 根节点
            rig.projectileOrigin = staffTip;
            // 背部悬浮符文碎片（英雄远景识别轮廓）
            for (int i = 0; i < 3; i++)
            {
                var g = CreateFloatingShard(rig.root, new Vector3((i - 1) * s * .3f, s * (1.1f + (i % 2) * .3f), -s * .45f),
                                            s * .3f, i == 1 ? Pal.UGlow2 : Pal.UBone, i == 1 ? .8f : 0f);
                floats.Add(g.transform);
            }
            rig.floatingParts = floats.ToArray();
            // 紫绿双层攻击光环
            rig.auraOrigin = Node(rig.root, "auraOrigin", new Vector3(0, .05f, 0));
            CreateRuneRing(rig.auraOrigin, Vector3.zero, def.size * 1.3f, Pal.UGlow2, 1.0f);
            CreateRuneRing(rig.auraOrigin, new Vector3(0, .03f, 0), def.size * .95f, Pal.UGlow, .8f);
        }
    }

    /// <summary>单位动画挂点集合（行走/攻击时摆动）。新增挂点仅用于视觉与动画，
    /// 不承担碰撞、路径或战斗判定；全部允许为 null，消费方必须判空。</summary>
    public class BodyRig
    {
        public Transform root;
        public Transform torso, head;
        public Transform legL, legR;
        public Transform armL, armR;
        public Transform[] horseLegs;
        // ---- 扩展挂点（视觉/动画专用） ----
        public Transform weaponMain, weaponOff;   // 主/副武器
        public Transform staff, relic;            // 法器/圣物
        public Transform banner, cape;            // 战旗/披风根
        public Transform auraOrigin;              // 光环旋转中心
        public Transform projectileOrigin;        // 弹道起点
        public Transform soulCore;                // 灵魂核心（脉动）
        public Transform[] floatingParts;         // 悬浮碎片（环绕/浮动）
        public Transform[] capePieces;            // 披风/飘带分片（逐片摆动）
        // ---- 动画基准快照（BuildUnit 结束时记录，动画内绝对赋值防漂移） ----
        public bool thrustAttack;                 // true=枪盾短刺，false=挥砍
        public Vector3 armRHome;                  // armR 初始位置（突刺复位）
        public Vector3 soulCoreBase = Vector3.one;
        public Vector3[] floatBase;               // floatingParts 初始局部坐标
    }
}
