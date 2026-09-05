using UnityEngine;

namespace Eresoth
{
    /// <summary>采集状态机：前往资源点 → 采集 → 回主基地交付 → 循环。</summary>
    public class Worker : MonoBehaviour
    {
        public enum State { Idle, ToNode, Gathering, Returning }
        public State state = State.Idle;
        public ResourceNode node;
        public int carry;
        string curKind = "wood";
        float t;
        Unit u;

        void Awake() { u = GetComponent<Unit>(); }

        public void GatherAt(ResourceNode n) { node = n; state = State.ToNode; }
        public void StopGather() { node = null; state = State.Idle; }

        void Update()
        {
            if (Game.I.over) return;
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
                        if (node.amount <= 0) { var dead = node; node = null; Destroy(dead.gameObject); }
                        state = State.Returning;
                    }
                    break;

                case State.Returning:
                    var hall = Game.I.NearestHall(u.team, transform.position);
                    if (hall == null) { state = State.Idle; break; }
                    if (u.MoveStep(hall.transform.position, dt, hall.radius))
                    {
                        Game.I.Deposit(u.team, curKind, carry);
                        carry = 0;
                        state = node != null ? State.ToNode : State.Idle;
                    }
                    break;
            }

            // 往返途中驱动走路动画（Unit.Update 在 busy 时不再调用 Anim）
            u.AnimWalk(state == State.ToNode || state == State.Returning, dt);
        }
    }
}
