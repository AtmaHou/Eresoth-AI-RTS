using UnityEngine;

namespace Eresoth
{
    /// <summary>采集状态机：前往资源点 → 采集 → 回主基地交付 → 循环。</summary>
    public class Worker : MonoBehaviour
    {
        public enum State { Idle, ToNode, Gathering, Returning, Constructing, Building }
        public State state = State.Idle;
        public ResourceNode node;
        public Building construction;        bool constructionOptOut;
        public int carry;
        string curKind = "wood";
        float t;
        Unit u;

        void Awake() { u = GetComponent<Unit>(); }

        public bool CanAutoBuild => !constructionOptOut && state == State.Idle;
        public void GatherAt(ResourceNode n) { StopGather(); constructionOptOut = true; node = n; state = State.ToNode; }
        public void BuildAt(Building b)
        {
            node = null;
            if (construction != null && construction != b) construction.RemoveBuilder(u);
            construction = b; constructionOptOut = false; state = State.Constructing;
        }
        public void FinishBuilding(Building b)
        {
            if (construction == b) { construction = null; constructionOptOut = false; state = State.Idle; }
        }
        public void StopGather()
        {
            if (construction != null) construction.RemoveBuilder(u);
            if (construction != null) constructionOptOut = true;
            node = null; construction = null; state = State.Idle;
        }

        void Update()
        {
            if (Game.I == null || Game.I.over) return;
            float dt = Time.deltaTime;
            u.busy = state != State.Idle;   // 采集中时 Unit.Update 让位，由本状态机驱动移动
            switch (state)
            {
                case State.ToNode:
                    if (node == null) { state = State.Idle; break; }
                    if (u.MoveStep(node.transform.position, dt, 0.6f)) { state = State.Gathering; t = 0; curKind = node.kind; }
                    break;

                case State.Gathering:
                    if (node == null) { state = State.Idle; break; }
                    t += dt;
                    if (t >= GameConfig.GatherTime)
                    {
                        int amt = Game.I.GatherAmt(u.team);
                        node.amount -= amt;
                        carry = amt;
                        UnitVfx.PlayGatherEffect(u, curKind);   // 采集反馈（纯视觉，不改数值）
                        if (node.amount <= 0) { var dead = node; node = null; Destroy(dead.gameObject); }
                        state = State.Returning;
                    }
                    break;

                case State.Returning:
                    var deposit = Game.I.NearestDeposit(u.team, transform.position);
                    if (deposit == null) { state = State.Idle; break; }
                    if (u.MoveStep(deposit.transform.position, dt, deposit.radius + u.Radius + .2f))
                    {
                        Game.I.Deposit(u.team, curKind, carry);
                        carry = 0;
                        state = node != null ? State.ToNode : State.Idle;
                    }
                    break;

                case State.Constructing:
                    if (construction == null || !construction.Alive || !construction.constructing)
                    { construction = null; state = State.Idle; break; }
                    float buildStopDist = construction.radius + u.Radius + 1.2f;
                    if (!u.MoveStep(construction.transform.position, dt, buildStopDist)) break;
                    if (construction.AddBuilder(u)) state = State.Building;
                    break;

                case State.Building:
                    if (construction == null || !construction.Alive || !construction.constructing)
                    { construction = null; state = State.Idle; break; }
                    // 已在建造位，保持静止并由 Building 推进进度；被推开时重新归位
                    float keepDist = construction.radius + u.Radius + 1.4f;
                    if (Vector3.Distance(transform.position, construction.transform.position) > keepDist + 0.3f)
                        state = State.Constructing;
                    break;
            }

            u.AnimWalk(state == State.ToNode || state == State.Returning || state == State.Constructing, dt);
            // 建造时播放工作动画（手臂小幅摆动）由 Unit.Anim 的行走混合驱动；此处保持静止即可
        }
    }
}
