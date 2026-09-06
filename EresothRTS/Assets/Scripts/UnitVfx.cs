using System.Collections.Generic;
using UnityEngine;

namespace Eresoth
{
    /// <summary>特效全局质量档位。Off 不产生任何特效对象；Low 仅战斗反馈；
    /// High 为完整职业化特效。关闭特效绝不影响单位逻辑。</summary>
    public enum VfxQuality { Off, Low, High }

    /// <summary>普通兵职业化战场特效（任务书 F2）：低成本、可读、符合阵营职业语言。
    /// 人族偏实体/暖色/金属火花；不死偏灵魂/骨片/冷色漂浮。
    /// 共享材质走 Gfx.Mat 缓存；短生命对象走静态池（上限 MaxActive），禁止每帧创建。
    /// 所有方法纯视觉：不修改伤害、资源或任何玩法数值。</summary>
    public static class UnitVfx
    {
        public static VfxQuality quality = VfxQuality.High;
        const int MaxActive = 80;
        static readonly List<VfxToken> pool = new();

        public static void SetQuality(VfxQuality q) => quality = q;

        static bool Human(Unit u) => u.team == Team.Player;

        // ---------------- 动作反馈 ----------------

        /// <summary>移动：人族脚步尘土（暖棕、低亮度）；不死袍下冷雾/灰烬（弱紫光）。仅 High 档。</summary>
        public static void PlayMoveEffect(Unit u)
        {
            if (quality != VfxQuality.High) return;
            var pos = u.transform.position + new Vector3(Random.Range(-.1f, .1f), .06f, Random.Range(-.1f, .1f));
            if (Human(u))
                Vel(Spawn(pos, PrimitiveType.Sphere, new Color(.55f, .47f, .36f), u.def.size * .16f, .5f, .3f), new Vector3(0, .35f, 0));
            else
                Vel(Spawn(pos, PrimitiveType.Sphere, Models.Pal.UGlow2 * .8f, u.def.size * .13f, .6f, .5f),
                    new Vector3(Random.Range(-.15f, .15f), .3f, Random.Range(-.15f, .15f)));
        }

        /// <summary>出手前摇：枪/剑尖端金点闪光（人族）或暗紫魂火（不死）；远程为拉弦处亮点。仅 High 档。</summary>
        public static void PlayAttackStartEffect(Unit u)
        {
            if (quality != VfxQuality.High) return;
            var rig = u.Rig;
            Vector3 at = rig != null && rig.weaponMain != null
                ? rig.weaponMain.position
                : u.transform.position + Vector3.up * u.def.size;
            if (Human(u))
                Spawn(at, PrimitiveType.Sphere, Models.Pal.HGold, u.def.size * .2f, .18f, 1.8f);
            else
                Spawn(at, PrimitiveType.Sphere, new Color(.6f, .25f, .9f), u.def.size * .18f, .22f, 1.4f);
        }

        // ---------------- 战斗反馈 ----------------

        /// <summary>命中点闪光（攻击方视角）：人族金色火花，不死暗红魂火碎屑。Low/High 档触发。</summary>
        public static void PlayImpactEffect(Unit u, Vector3 at)
        {
            if (quality == VfxQuality.Off) return;
            if (Human(u))
                Spawn(at, PrimitiveType.Sphere, Models.Pal.HGold, .22f, .2f, 1.8f);
            else
            {
                Spawn(at, PrimitiveType.Sphere, new Color(.7f, .15f, .1f), .2f, .25f, 1.2f);
                Vel(Spawn(at + Random.insideUnitSphere * .15f, PrimitiveType.Cube, Models.Pal.UBone, .07f, .4f, .2f),
                    new Vector3(Random.Range(-.5f, .5f), 1.2f, Random.Range(-.5f, .5f)));
            }
        }

        /// <summary>受击反馈：枪盾步兵从盾面迸出金色格挡火花；其余单位短促受击脉冲。</summary>
        public static void PlayHitEffect(Unit u)
        {
            if (quality == VfxQuality.Off) return;
            var rig = u.Rig;
            if (u.def.kind == UnitKind.Infantry && rig != null && rig.weaponOff != null)
            {
                // 盾牌格挡：盾面半圈火花（短促、从盾面位置发出）
                Spawn(rig.weaponOff.position + Vector3.forward * .1f,
                      PrimitiveType.Sphere, Human(u) ? Models.Pal.HGold : Models.Pal.UGlow, u.def.size * .3f, .22f, 2f);
                return;
            }
            var at = u.transform.position + Vector3.up * u.def.size * .8f;
            Spawn(at, PrimitiveType.Sphere,
                  Human(u) ? new Color(1f, .9f, .6f) : new Color(.75f, .2f, .15f),
                  u.def.size * .24f, .18f, 1.2f);
        }

        /// <summary>采集反馈（High 档）：农夫木屑/金光；侍僧魂丝绕体。</summary>
        public static void PlayGatherEffect(Unit u, string kind)
        {
            if (quality != VfxQuality.High) return;
            var at = u.transform.position + Vector3.up * u.def.size * .9f;
            if (Human(u))
            {
                if (kind == "wood")
                    for (int i = 0; i < 3; i++)
                        Vel(Spawn(at + Random.insideUnitSphere * .2f, PrimitiveType.Cube, Models.Pal.WoodL, .06f, .45f, .2f),
                            new Vector3(Random.Range(-.6f, .6f), 1.4f, Random.Range(-.6f, .6f)));
                else
                    Spawn(at, PrimitiveType.Sphere, Models.Pal.HGold, .18f, .4f, 1.6f);   // 魔法矿金色引导光点
            }
            else
                // 灵魂抽取：幽绿光点缓缓升向魂灯
                Vel(Spawn(at, PrimitiveType.Sphere, Models.Pal.UGlow, .16f, .7f, 1.4f), new Vector3(0, .9f, 0));
        }

        /// <summary>死亡反馈（Low/High 档）：不死骨屑散落 + 魂火熄灭；人族短促倒地尘土。</summary>
        public static void PlayDeathEffect(Unit u)
        {
            if (quality == VfxQuality.Off) return;
            var at = u.transform.position + Vector3.up * .3f;
            if (Human(u))
                Vel(Spawn(at, PrimitiveType.Sphere, new Color(.6f, .52f, .4f), u.def.size * .4f, .6f, .4f), new Vector3(0, .6f, 0));
            else
            {
                for (int i = 0; i < 3; i++)
                    Vel(Spawn(at + Random.insideUnitSphere * .25f, PrimitiveType.Cube, Models.Pal.UBone, .08f, .7f, .2f),
                        new Vector3(Random.Range(-.8f, .8f), 1.6f, Random.Range(-.8f, .8f)));
                Vel(Spawn(at + Vector3.up * .4f, PrimitiveType.Sphere, Models.Pal.UGlow2, u.def.size * .3f, .5f, 1.6f),
                    new Vector3(0, 1f, 0));
            }
        }

        // ---------------- 对象池 ----------------

        /// <summary>池满时 Spawn 返回 null，此处统一判空。</summary>
        static void Vel(VfxToken t, Vector3 v) { if (t != null) t.vel = v; }

        static VfxToken Spawn(Vector3 pos, PrimitiveType type, Color c, float size, float life, float emisMul)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var t = pool[i];
                if (t == null) { pool.RemoveAt(i); i--; continue; }
                if (!t.gameObject.activeSelf)
                {
                    t.Reset(pos, type, c, size, life, emisMul);
                    return t;
                }
            }
            if (pool.Count >= MaxActive) return null;   // 超预算直接跳过，绝不无限增长
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var token = go.AddComponent<VfxToken>();
            token.Reset(pos, type, c, size, life, emisMul);
            pool.Add(token);
            return token;
        }
    }

    /// <summary>短生命特效令牌：自驱缩放淡出 + 速度位移，结束后隐藏回池。</summary>
    public class VfxToken : MonoBehaviour
    {
        public Vector3 vel;
        float life, maxLife, baseSize;
        Renderer rd;

        public void Reset(Vector3 pos, PrimitiveType type, Color c, float size, float dur, float emisMul)
        {
            if (rd == null) rd = GetComponent<Renderer>();
            gameObject.SetActive(true);
            transform.position = pos;
            baseSize = size;
            life = maxLife = dur;
            vel = Vector3.zero;
            var mf = GetComponent<MeshFilter>();
            if (mf != null)
                mf.sharedMesh = type == PrimitiveType.Cube ? CubeMesh() : SphereMesh();
            rd.sharedMaterial = Gfx.Mat(c, 0f, .6f, true, emisMul);
            transform.localScale = Vector3.one * size;
        }

        static Mesh sphere, cube;
        static Mesh PrimMesh(PrimitiveType type, ref Mesh cache)
        {
            if (cache != null) return cache;
            var t = GameObject.CreatePrimitive(type);            // 借内置图元的共享网格
            cache = t.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(t); else DestroyImmediate(t);
            return cache;
        }
        static Mesh SphereMesh() => PrimMesh(PrimitiveType.Sphere, ref sphere);
        static Mesh CubeMesh() => PrimMesh(PrimitiveType.Cube, ref cube);

        void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f) { gameObject.SetActive(false); return; }
            transform.position += vel * dt;
            vel *= Mathf.Max(0f, 1f - dt * 2f);
            float k = life / maxLife;
            transform.localScale = Vector3.one * baseSize * Mathf.Lerp(.25f, 1f, k);   // 缩小淡出
        }
    }
}
