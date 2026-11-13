using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 三种小队长（2026-11-13 owner「小队长你也设计一下，可以不是 boss，但是有独特的机制和战斗方式」）。归刷怪 owner：
    /// 带守货岗、或从签过两柜起随押运车厢下来。不是 Boss（没有 Boss 血条、不掉专属物），血比押运员厚，各有一招只有他会的：
    /// - 信号工长「红灯点名」（找人的）：看得见玩家时举手提红灯，一道红光照向玩家，1.4 秒内一直照着就点上名——
    ///   附近 40 m 内围坐、站岗的守货岗全醒，附近没交火的押运直扑玩家，玩家脚下跟一圈红光 6 秒。躲到墙、车厢或检修蒸汽后面红光就断。先打他。
    /// - 调车长「溜放」（封路的）：站定举红旗、轮缘一声尖啸，从脚下朝玩家铺一条黄黑预警带，1.1 秒后放出一辆溜放推车顺着带子冲过去，
    ///   撞到谁算谁——玩家掉一截血，押运员（包括他自己手下）掉得更多。墙、掩体和进站的车厢都能把车拦下。侧移出带；或者站到他手下身后让车替你打。
    /// - 押运班长「车挡 / 压上来」（推进的）：看得见玩家时把一辆轨道检查车推到身前当掩体（挡子弹，22 秒后撤走，玩家也能借它躲）；
    ///   玩家躲在掩体后 2.5 秒不露头，他吼一声，周围的押运从两边包抄、直扑玩家。打掉他，手下就不会包抄。
    /// 表现：红灯、红旗与预警带复用押运长的表现件（<see cref="LastShiftChiefFx"/>），红光与脚下红圈在 <see cref="LastShiftLeaderFx"/>；
    /// 推车与检查车克隆场景包车辆模板。只在小队长活着、醒着、没挂起、离玩家 45 m 内时逐帧跑，其余时间每人 O(1) 早返（§4.12）。
    /// 预警画不出来（共享材质缺失）就不出那一招：预警是公平的前提。
    /// </summary>
    internal sealed partial class LastShiftEnemies
    {
        internal const float LeaderRange = 45f;
        // 信号工长
        internal const float LampReach = 30f, LampCharge = 1.4f, LampCooldown = 16f, LampFizzleCooldown = 5f, CallRadius = 40f, CallSeconds = 12f, MarkSeconds = 6f;
        // 调车长
        internal const float ShuntMin = 6f, ShuntReach = 28f, ShuntTelegraph = 1.1f, ShuntMaxLength = 34f, ShuntCooldown = 8f;
        internal const float CartSpeed = 15f, CartLead = 1.2f, CartHitRadius = 1.15f, CartDamagePlayer = 35f, CartDamageEnemy = 60f, CartHalf = .75f;
        // 押运班长
        internal const float TrolleyMin = 7f, TrolleyMax = 28f, ShoveWindup = .7f, TrolleyLife = 22f, TrolleyCooldown = 14f, TrolleyAhead = 2.4f;
        internal const float PushAfter = 2.5f, PushCooldown = 12f, PushRadius = 25f, PushSide = 6f;
        internal const int PushCount = 5;
        private const string CartTemplate = "Teaching cart", TrolleyTemplate = "Siding 06 · railway inspection trolley";

        private enum LeaderKind { Signal, Shunt, Sergeant }
        private enum LeaderMove { None, Charge, Telegraph, Roll }

        private sealed class LeaderState
        {
            internal LeaderKind Kind;
            internal LeaderMove Move;
            internal LastShiftChiefFx Chief;
            internal LastShiftLeaderFx Fx;
            internal AI_PathControl Path;
            internal float Start, Cooldown, NextLook, HiddenSince = -1f, MarkUntil = -1f, PushReady;
            internal Vector3 Origin, Dir;
            internal float Length, Shown;
            internal bool Planted, Visible, HitPlayer;
            internal readonly List<Guard> Hit = new List<Guard>(4);
            internal GameObject Cart;
        }

        private sealed class Trolley
        {
            internal Guard Owner;
            internal GameObject Root;
            internal float Expire;
        }

        private readonly List<Guard> leaders = new List<Guard>(4);
        private readonly List<Trolley> trolleys = new List<Trolley>(2);
        private Transform cartTemplate, trolleyTemplate;
        private int leaderWallLayer;
        private float nextCallLine;

        internal static bool IsLeaderPost(LastShiftPost post)
        {
            return post == LastShiftPost.Signalman || post == LastShiftPost.Shunter || post == LastShiftPost.Sergeant;
        }

        private static LeaderKind KindOf(LastShiftPost post)
        {
            return post == LastShiftPost.Signalman ? LeaderKind.Signal : post == LastShiftPost.Shunter ? LeaderKind.Shunt : LeaderKind.Sergeant;
        }

        /// <summary>押运车厢送下来的小队长轮着来（信号工长 → 调车长 → 押运班长）。</summary>
        private LastShiftPost NextLeaderPost()
        {
            switch (leaderSerial++ % 3)
            {
                case 0: return LastShiftPost.Signalman;
                case 1: return LastShiftPost.Shunter;
                default: return LastShiftPost.Sergeant;
            }
        }

        private static void MakeLeader(Guard g, LastShiftPost post)
        {
            g.Post = post;
            g.Quality = Math.Max(g.Quality, 3);
        }

        // ---------------- 生命周期 ----------------
        private void BuildLeaderProps()
        {
            Transform templates = world.Root.transform.Find("Templates/VIS_vehicles");
            if (templates != null)
            {
                cartTemplate = templates.Find(CartTemplate);
                trolleyTemplate = templates.Find(TrolleyTemplate);
            }
            if (cartTemplate == null) Debug.LogWarning(LastShiftRaidScene.LogTag + " 找不到溜放推车模板，调车长不放推车");
            if (trolleyTemplate == null) Debug.LogWarning(LastShiftRaidScene.LogTag + " 找不到轨道检查车模板，押运班长不推车挡");
            int mask = GameplayDataSettings.Layers.wallLayerMask.value;
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { leaderWallLayer = i; break; }
        }

        private void BindLeader(Guard g)
        {
            if (g.Lead != null) return;
            g.Lead = new LeaderState { Kind = KindOf(g.Post), Cooldown = Time.time + 2.5f, PushReady = Time.time + 4f };
            g.Lead.Path = g.Character.GetComponentInChildren<AI_PathControl>(true);
            if (!leaders.Contains(g)) leaders.Add(g);
            Debug.Log(LastShiftRaidScene.LogTag + " LEADER_READY id=" + g.Id + " kind=" + g.Lead.Kind);
        }

        /// <summary>小队长倒下、收岗或整趟收尾：招式当场作废，表现件收起；他推出来的车挡照常到时撤走。</summary>
        private void DisposeLeader(Guard g)
        {
            LeaderState st = g.Lead;
            leaders.Remove(g);
            if (st == null) return;
            g.Lead = null;
            try
            {
                Interrupt(st, Time.time);
                if (st.Chief != null) { st.Chief.Lamp(false, 0f); st.Chief.Dispose(); }
                if (st.Fx != null) st.Fx.Dispose();
                if (st.Cart != null) UnityEngine.Object.Destroy(st.Cart);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 小队长收尾失败 " + g.Id + "：" + e.Message); }
        }

        private void DisposeLeaders()
        {
            foreach (Guard g in leaders.ToArray()) DisposeLeader(g);
            leaders.Clear();
            foreach (Trolley t in trolleys) if (t.Root != null) UnityEngine.Object.Destroy(t.Root);
            trolleys.Clear();
        }

        // ---------------- 每帧 ----------------
        private void TickLeaders(float now)
        {
            for (int i = leaders.Count - 1; i >= 0; i--)
            {
                Guard g = leaders[i];
                if (g.Lead == null) { leaders.RemoveAt(i); continue; }
                if (!IsAlive(g)) { DisposeLeader(g); continue; }
                try { TickLeader(g, g.Lead, now); }
                catch (Exception e)
                {
                    Interrupt(g.Lead, now);
                    g.Lead.Cooldown = now + 4f;
                    Debug.LogWarning(LastShiftRaidScene.LogTag + " 小队长招式取消 " + g.Id + "：" + e.Message);
                }
            }
            for (int i = trolleys.Count - 1; i >= 0; i--)
            {
                Trolley t = trolleys[i];
                if (t.Root != null && now < t.Expire) continue;
                if (t.Root != null)
                {
                    SkyIslandImpactFx.Puff(world.Root.transform, t.Root.transform.position, .8f, 6);
                    UnityEngine.Object.Destroy(t.Root);
                }
                trolleys.RemoveAt(i);
            }
        }

        private void TickLeader(Guard g, LeaderState st, float now)
        {
            CharacterMainControl me = g.Character;
            bool down = player == null || player.Health == null || player.Health.IsDead;
            Vector3 to = down ? Vector3.zero : player.transform.position - me.transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (down || g.Suspended || g.Frozen || g.Ai == null || dist > LeaderRange || dist < .3f)
            {
                if (st.Move != LeaderMove.None) Interrupt(st, now);
                if (st.Chief != null) st.Chief.Lamp(false, 0f);
                // 点名红圈只跟着看得见的点名走：小队长挂起、睡着或玩家跑远，红圈当场收起，不留在原地。
                if (st.Fx != null && st.MarkUntil >= 0f) { st.Fx.HideMark(); st.MarkUntil = -1f; }
                return;
            }
            bool engaged = g.Ai.searchedEnemy != null && g.Ai.searchedEnemy == player.mainDamageReceiver;
            switch (st.Kind)
            {
                case LeaderKind.Signal: TickSignal(g, st, now, to / dist, dist, engaged); break;
                case LeaderKind.Shunt: TickShunt(g, st, now, to / dist, dist, engaged); break;
                default: TickSergeant(g, st, now, to / dist, dist, engaged); break;
            }
        }

        // ---------------- 信号工长：红灯点名 ----------------
        private void TickSignal(Guard g, LeaderState st, float now, Vector3 dir, float dist, bool engaged)
        {
            LastShiftChiefFx chiefFx = ChiefFxOf(g, st);
            LastShiftLeaderFx fx = FxOf(st);
            if (chiefFx != null) chiefFx.Lamp(engaged || st.Move == LeaderMove.Charge, now);
            if (st.MarkUntil >= 0f)
            {
                if (now < st.MarkUntil) fx.Mark(player.transform.position, (st.MarkUntil - now) / MarkSeconds, now);
                else { fx.HideMark(); st.MarkUntil = -1f; }
            }
            if (st.Move == LeaderMove.Charge)
            {
                float charge = (now - st.Start) / LampCharge;
                fx.Beam(LampPoint(g), Chest(player.transform.position), Mathf.Clamp01(charge), now);
                if (now >= st.NextLook)
                {
                    st.NextLook = now + .1f;
                    if (dist > LampReach + 4f || SightBlocked(Chest(g.Character.transform.position), Chest(player.transform.position)))
                    {
                        Interrupt(st, now);
                        st.Cooldown = now + LampFizzleCooldown;
                        Bark(g, L10n.T("躲哪去了？", "Where'd they go?"));
                        return;
                    }
                }
                if (charge >= 1f) CallOut(g, st, now);
                return;
            }
            if (!engaged || now < st.Cooldown || dist > LampReach || now < st.NextLook || !fx.Available) return;
            st.NextLook = now + .2f;
            if (SightBlocked(Chest(g.Character.transform.position), Chest(player.transform.position))) return;
            st.Move = LeaderMove.Charge;
            st.Start = now;
            st.NextLook = now + .1f;
            Bark(g, L10n.T("红灯！别动！", "Red lamp! Hold it!"));
        }

        /// <summary>点上名：附近的守货岗全醒、没交火的押运直扑玩家，玩家脚下跟一圈红光。</summary>
        private void CallOut(Guard g, LeaderState st, float now)
        {
            st.Move = LeaderMove.None;
            st.Cooldown = now + LampCooldown;
            st.MarkUntil = now + MarkSeconds;
            if (st.Fx != null) st.Fx.HideBeam();
            LastShiftSounds.Play(LastShiftSounds.SignalBell, g.Character.gameObject);
            Bark(g, L10n.T("红灯！都过来！", "Red lamp! Everyone, over here!"));
            Vector3 at = player.transform.position;
            int woke = SquadsHearCall(at, CallRadius), sent = 0;
            foreach (Guard other in guards)
            {
                if (other == g || !IsAlive(other) || other.Boss || other.Squad != null || other.Ai == null || other.Suspended) continue;
                if (FlatDistance(other.Character.transform.position, at) > CallRadius + 5f) continue;
                PointAtPlayer(other.Ai);
                Lure(other, Snap(at + new Vector3((other.Index % 3 - 1) * 3f, 0f, (other.Index % 2) * 3f - 1.5f)), now + CallSeconds);
                sent++;
            }
            if (now >= nextCallLine)
            {
                nextCallLine = now + 20f;
                status(L10n.T("红灯照住了你。四下里有人在往这边跑。", "A red lamp has you pinned. People are running this way from all sides."), true);
            }
            Debug.Log(LastShiftRaidScene.LogTag + " LEADER_CALL id=" + g.Id + " squads=" + woke + " guards=" + sent);
        }

        private static Vector3 LampPoint(Guard g)
        {
            CharacterMainControl c = g.Character;
            Transform model = c.characterModel != null ? c.characterModel.transform : c.transform;
            // 与押运长红灯同一个挂点（LastShiftChiefFx 把灯挂在模型本地 (-.38, 1.05, .18)）。
            return model.TransformPoint(new Vector3(-.38f, 1.05f, .18f));
        }

        // ---------------- 调车长：溜放 ----------------
        private void TickShunt(Guard g, LeaderState st, float now, Vector3 dir, float dist, bool engaged)
        {
            LastShiftChiefFx chiefFx = ChiefFxOf(g, st);
            if (st.Move == LeaderMove.Telegraph)
            {
                float age = now - st.Start;
                if (now >= st.NextLook) { st.NextLook = now + .1f; st.Shown = CartRun(st.Origin, st.Dir, st.Length); }
                chiefFx.Lane(0, st.Origin, st.Origin + st.Dir * (st.Shown * Mathf.Clamp01(age / .35f)), CartHalf, Mathf.Clamp01(age / ShuntTelegraph), false, 1f, now);
                chiefFx.Flag(true, now);
                chiefFx.Tick(now);
                if (age >= ShuntTelegraph) ReleaseCart(g, st, now);
                return;
            }
            if (st.Move == LeaderMove.Roll)
            {
                TickCart(g, st, now, chiefFx);
                return;
            }
            if (!engaged || now < st.Cooldown || dist < ShuntMin || dist > ShuntReach || now < st.NextLook || cartTemplate == null) return;
            st.NextLook = now + .2f;
            if (chiefFx == null || !chiefFx.Available) return;
            Vector3 feet = g.Character.transform.position;
            if (Physics.Linecast(feet + Vector3.up * .6f, player.transform.position + Vector3.up * .6f, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) return;
            st.Move = LeaderMove.Telegraph;
            st.Start = now;
            st.Dir = dir;
            st.Origin = feet + dir * .6f;
            st.Length = Mathf.Min(ShuntMaxLength, dist + 8f);
            st.Shown = CartRun(st.Origin, st.Dir, st.Length);
            st.NextLook = now + .1f;
            Plant(g, st, true);
            LastShiftSounds.Play(LastShiftSounds.ChiefWhistleLong, g.Character.gameObject);
            Bark(g, L10n.T("溜放！闪开！", "Runaway coming! Out of the way!"));
        }

        /// <summary>推车能走多远：齐膝高往前打一条射线，碰到墙层（墙、掩体、进站的车厢）就停在它前面。</summary>
        private static float CartRun(Vector3 origin, Vector3 dir, float length)
        {
            RaycastHit hit;
            if (Physics.Raycast(origin + Vector3.up * .6f, dir, out hit, length, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore))
                return Mathf.Max(.6f, hit.distance - .5f);
            return length;
        }

        private void ReleaseCart(Guard g, LeaderState st, float now)
        {
            if (st.Cart == null)
            {
                st.Cart = UnityEngine.Object.Instantiate(cartTemplate.gameObject, world.Root.transform, true);
                st.Cart.name = "LastShiftRunawayCart_" + g.Id;
            }
            st.Move = LeaderMove.Roll;
            st.Start = now;
            st.Hit.Clear();
            st.HitPlayer = false;
            st.Cart.SetActive(true);
            LastShiftSounds.Play(LastShiftSounds.CartScreech, st.Cart);
            PlaceCart(st, CartLead);
        }

        private void PlaceCart(LeaderState st, float travelled)
        {
            Vector3 p = st.Origin + st.Dir * travelled;
            float ground;
            if (world.TryGroundAt(p, out ground)) p.y = ground;
            st.Cart.transform.SetPositionAndRotation(p + Vector3.up * .06f, Quaternion.LookRotation(st.Dir, Vector3.up) * cartTemplate.rotation);
        }

        private void TickCart(Guard g, LeaderState st, float now, LastShiftChiefFx chiefFx)
        {
            float travelled = CartLead + CartSpeed * (now - st.Start);
            if (now >= st.NextLook) { st.NextLook = now + .1f; st.Shown = Mathf.Min(st.Shown, CartRun(st.Origin, st.Dir, st.Length)); }
            float fade = Mathf.Clamp01((st.Shown - travelled) / 4f + .3f);
            if (chiefFx != null) { chiefFx.Lane(0, st.Origin, st.Origin + st.Dir * st.Shown, CartHalf, 1f, true, fade, now); chiefFx.Flag(false, now); }
            if (travelled >= st.Shown)
            {
                Vector3 stop = st.Origin + st.Dir * st.Shown;
                SkyIslandImpactFx.Puff(world.Root.transform, stop, .9f, 10);
                SkyIslandImpactFx.Sparks(world.Root.transform, stop + Vector3.up * .5f, LastShiftChiefFx.Amber, 8);
                SkyIslandImpactFx.Shake(stop, .3f);
                if (st.Cart != null) st.Cart.SetActive(false);
                Interrupt(st, now);
                st.Cooldown = now + ShuntCooldown;
                return;
            }
            PlaceCart(st, travelled);
            Vector3 at = st.Cart.transform.position;
            if (!st.HitPlayer && player != null && player.Health != null && !player.Health.IsDead && Near(at, player.transform.position))
            {
                st.HitPlayer = true;
                HurtFrom(g.Character, player.Health, CartDamagePlayer, at, st.Dir, false);
                SkyIslandImpactFx.Shake(at, .45f);
                SkyIslandImpactFx.Puff(world.Root.transform, player.transform.position, .6f, 6);
            }
            foreach (Guard other in guards)
            {
                if (other == g || st.Hit.Contains(other) || !IsAlive(other) || other.Suspended || !Near(at, other.Character.transform.position)) continue;
                st.Hit.Add(other);
                HurtFrom(null, other.Character.Health, CartDamageEnemy, at, st.Dir, true);
                SkyIslandImpactFx.Puff(world.Root.transform, other.Character.transform.position, .6f, 6);
                Debug.Log(LastShiftRaidScene.LogTag + " CART_FRIENDLY_FIRE leader=" + g.Id + " hit=" + other.Id);
            }
        }

        private static bool Near(Vector3 cart, Vector3 who)
        {
            Vector3 d = who - cart;
            if (Mathf.Abs(d.y) > 1.6f) return false;
            d.y = 0f;
            return d.sqrMagnitude <= CartHitRadius * CartHitRadius;
        }

        // ---------------- 押运班长：车挡 / 压上来 ----------------
        private void TickSergeant(Guard g, LeaderState st, float now, Vector3 dir, float dist, bool engaged)
        {
            if (st.Move == LeaderMove.Telegraph)
            {
                if (now - st.Start >= ShoveWindup)
                {
                    PlaceTrolley(g, st, now);
                    Interrupt(st, now);
                    st.Cooldown = now + TrolleyCooldown;
                }
                return;
            }
            if (!engaged) { st.HiddenSince = -1f; return; }
            if (now >= st.NextLook)
            {
                st.NextLook = now + .2f;
                st.Visible = !SightBlocked(Chest(g.Character.transform.position), Chest(player.transform.position));
            }
            if (st.Visible) st.HiddenSince = -1f;
            else if (st.HiddenSince < 0f) st.HiddenSince = now;
            if (st.HiddenSince >= 0f && now - st.HiddenSince >= PushAfter && now >= st.PushReady) { Push(g, st, now); return; }
            if (!st.Visible || now < st.Cooldown || dist < TrolleyMin || dist > TrolleyMax || trolleyTemplate == null || HasTrolley(g)) return;
            st.Move = LeaderMove.Telegraph;
            st.Start = now;
            st.Dir = dir;
            Plant(g, st, true);
            Bark(g, L10n.T("上车挡！", "Get the trolley up!"));
        }

        private bool HasTrolley(Guard g)
        {
            foreach (Trolley t in trolleys) if (t.Owner == g && t.Root != null) return true;
            return false;
        }

        /// <summary>
        /// 把一辆轨道检查车推到身前 <see cref="TrolleyAhead"/> m、横着挡在他和玩家之间：墙层碰撞挡子弹（顶高在半掩体区间时照全图口径登记半掩体），
        /// <see cref="TrolleyLife"/> 秒后撤走。压在股道上、压进墙里或贴着玩家就不推。
        /// </summary>
        private void PlaceTrolley(Guard g, LeaderState st, float now)
        {
            Vector3 center = g.Character.transform.position + st.Dir * TrolleyAhead;
            float ground;
            if (!world.TryGroundAt(center, out ground)) return;
            center.y = ground;
            if (LastShiftTimetable.InEnvelope(center.x, -center.z, 'W') || LastShiftTimetable.InEnvelope(center.x, -center.z, 'E')) return;
            if (player != null && FlatDistance(center, player.transform.position) < 1.8f) return;
            var root = new GameObject("LastShiftTrolley_" + g.Id);
            root.transform.SetParent(world.Root.transform, false);
            root.transform.position = center;
            GameObject model = UnityEngine.Object.Instantiate(trolleyTemplate.gameObject, root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = trolleyTemplate.rotation;
            model.SetActive(true);
            // 车身外框按渲染器量（未转向时），碰撞盒照它摆；长边横在火线上。
            var bounds = new Bounds(center, Vector3.zero);
            bool any = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!any || bounds.size.x < .3f || bounds.size.z < .3f || bounds.size.y < .4f) { UnityEngine.Object.Destroy(root); return; }
            // 模型原点不一定在车身正中：水平挪回来，让车身和碰撞盒都正好压在推出去的那一点上。
            Vector3 offset = bounds.center - center;
            offset.y = 0f;
            model.transform.position -= offset;
            bounds.center -= offset;
            var col = new GameObject("COL_Trolley");
            col.layer = leaderWallLayer;
            col.transform.SetParent(root.transform, false);
            BoxCollider box = col.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
            bool longZ = bounds.size.z > bounds.size.x;
            root.transform.rotation = Quaternion.LookRotation(st.Dir, Vector3.up) * Quaternion.Euler(0f, longZ ? 90f : 0f, 0f);
            if (OverlapsOtherWall(box))
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
            try { RaidHalfCover.Register(root.transform, leaderWallLayer, world.TryGroundAt); }
            catch (Exception e) { ModBehaviour.DevLog(LastShiftRaidScene.LogTag + " 车挡半掩体登记失败：" + e.Message); }
            trolleys.Add(new Trolley { Owner = g, Root = root, Expire = now + TrolleyLife });
            LastShiftSounds.Play(LastShiftSounds.TrolleyShove, root);
            SkyIslandImpactFx.Puff(world.Root.transform, center, .8f, 8);
            Debug.Log(LastShiftRaidScene.LogTag + " LEADER_TROLLEY id=" + g.Id + " at=" + center + " size=" + bounds.size);
        }

        /// <summary>车挡的碰撞盒有没有压进别的墙层碰撞（自己那只除外）。</summary>
        private static bool OverlapsOtherWall(BoxCollider box)
        {
            Transform t = box.transform;
            Collider[] hits = Physics.OverlapBox(t.TransformPoint(box.center), Vector3.Scale(box.size, t.lossyScale) * .45f, t.rotation,
                GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits) if (c != null && c != box) return true;
            return false;
        }

        /// <summary>压上来：周围 <see cref="PushRadius"/> m 内最多 <see cref="PushCount"/> 个押运从玩家两侧包过去、直扑玩家。</summary>
        private void Push(Guard g, LeaderState st, float now)
        {
            st.PushReady = now + PushCooldown;
            st.HiddenSince = -1f;
            Bark(g, L10n.T("压上去！两边包！", "Push up! Flank both sides!"));
            Vector3 at = player.transform.position, me = g.Character.transform.position;
            Vector3 back = me - at;
            back.y = 0f;
            back = back.sqrMagnitude > .01f ? back.normalized : Vector3.forward;
            Vector3 side = new Vector3(-back.z, 0f, back.x);
            int sent = 0;
            foreach (Guard other in guards)
            {
                if (sent >= PushCount) break;
                if (other == g || !IsAlive(other) || other.Boss || other.Ai == null || other.Suspended) continue;
                if (FlatDistance(other.Character.transform.position, me) > PushRadius) continue;
                if (other.Squad != null && other.Squad.State != SquadState.Alert) AlertSquad(other.Squad, now, true, other, "leader_push");
                if (other.Frozen) continue; // 同队其余人按位次错开醒，醒了自己追
                Vector3 flank = Snap(at + side * (sent % 2 == 0 ? PushSide : -PushSide) + back * 2f);
                PointAtPlayer(other.Ai);
                other.Ai.patrolPosition = flank;
                try { other.Ai.MoveToPos(flank); } catch (Exception) { /* 官方行为树接手 */ }
                sent++;
            }
            Debug.Log(LastShiftRaidScene.LogTag + " LEADER_PUSH id=" + g.Id + " guards=" + sent);
        }

        // ---------------- 小工具 ----------------
        private LastShiftChiefFx ChiefFxOf(Guard g, LeaderState st)
        {
            if (st.Chief == null)
            {
                CharacterMainControl c = g.Character;
                st.Chief = new LastShiftChiefFx(world.Root.transform, c.transform, c.characterModel != null ? c.characterModel.transform : c.transform);
            }
            return st.Chief;
        }

        private LastShiftLeaderFx FxOf(LeaderState st)
        {
            if (st.Fx == null) st.Fx = new LastShiftLeaderFx(world.Root.transform);
            return st.Fx;
        }

        /// <summary>出招时站定：停掉官方寻路组件并清移动输入（AI 本身照常瞄准、开枪）；收招时还回去。</summary>
        private static void Plant(Guard g, LeaderState st, bool on)
        {
            if (on == st.Planted) return;
            st.Planted = on;
            if (st.Path == null) return;
            if (on)
            {
                try { g.Ai.StopMove(); } catch (Exception e) { ModBehaviour.DevLog(LastShiftRaidScene.LogTag + " 小队长站定失败：" + e.Message); }
                st.Path.enabled = false;
                if (g.Character != null) g.Character.SetMoveInput(Vector3.zero);
            }
            else st.Path.enabled = true;
        }

        private static void Interrupt(LeaderState st, float now)
        {
            st.Move = LeaderMove.None;
            if (st.Planted)
            {
                st.Planted = false;
                if (st.Path != null) st.Path.enabled = true;
            }
            if (st.Chief != null) st.Chief.Clear();
            if (st.Fx != null) st.Fx.HideBeam();
            if (st.Cart != null && st.Cart.activeSelf) st.Cart.SetActive(false);
            st.Cooldown = Mathf.Max(st.Cooldown, now + 1.5f);
        }

        private static Vector3 Chest(Vector3 feet) { return feet + Vector3.up * 1.1f; }

        /// <summary>起手要看得见：墙层（含车厢）或检修蒸汽（官方 FowBlock 层）挡着都算看不见。</summary>
        private static bool SightBlocked(Vector3 a, Vector3 b)
        {
            int mask = GameplayDataSettings.Layers.wallLayerMask.value | GameplayDataSettings.Layers.fowBlockLayers.value;
            return Physics.Linecast(a, b, mask, QueryTriggerInteraction.Collide);
        }

        /// <summary>一次招式伤害：<paramref name="real"/> 为真时是真实伤害（推车撞押运，不吃护甲），否则照官方护甲结算。</summary>
        private static void HurtFrom(CharacterMainControl from, Health health, float damage, Vector3 at, Vector3 dir, bool real)
        {
            if (health == null || health.IsDead) return;
            var info = new DamageInfo(from);
            info.damageValue = damage;
            info.fromWeaponItemID = 0;
            info.damagePoint = at + Vector3.up;
            info.damageNormal = -dir;
            if (real) { info.damageType = DamageTypes.realDamage; info.ignoreArmor = true; }
            health.Hurt(info);
        }
    }

    /// <summary>
    /// 小队长招式的表现层（§4.15 表现写独立类型；判定全在 <see cref="LastShiftEnemies"/>）：信号工长的红光（从红灯射向玩家胸口，蓄力越满越粗越亮）
    /// 与点名后玩家脚下跟着走的一圈红光。材质走共享工厂（<see cref="SkyIslandBossForge.StraightLine"/> / 贴地预警圈），懒建、复用，画不出来就报不可用。
    /// </summary>
    internal sealed class LastShiftLeaderFx : IDisposable
    {
        private static readonly Color BeamTint = new Color(1f, .16f, .1f, 1f);
        private readonly Transform root;
        private LineRenderer beam, mark;
        private bool broken, disposed;
        private readonly Vector3[] two = new Vector3[2];

        internal LastShiftLeaderFx(Transform worldRoot) { root = worldRoot; }

        internal bool Available
        {
            get
            {
                if (disposed || broken) return false;
                if (beam != null) return true;
                try { beam = SkyIslandBossForge.StraightLine(root, "LastShiftSignalBeam", .08f, BeamTint); beam.enabled = false; return true; }
                catch (Exception e) { broken = true; Debug.LogWarning(LastShiftRaidScene.LogTag + " 信号工长红光不可用（不出这一招）：" + e.Message); return false; }
            }
        }

        internal void Beam(Vector3 from, Vector3 to, float charge, float now)
        {
            if (!Available) return;
            two[0] = from; two[1] = to;
            beam.SetPositions(two);
            float pulse = .75f + .25f * Mathf.Sin(now * (8f + 14f * charge));
            beam.widthMultiplier = Mathf.Lerp(.05f, .16f, charge);
            Color c = new Color(BeamTint.r, BeamTint.g, BeamTint.b, Mathf.Lerp(.3f, .95f, charge) * pulse);
            beam.startColor = c;
            beam.endColor = new Color(c.r, c.g, c.b, c.a * .7f);
            beam.enabled = true;
        }

        internal void HideBeam() { if (beam != null) beam.enabled = false; }

        /// <summary>点名后的红圈：跟着玩家脚下走，<paramref name="remaining"/>（1→0）越少闪得越慢。</summary>
        internal void Mark(Vector3 feet, float remaining, float now)
        {
            if (disposed || broken) return;
            try
            {
                if (mark == null) mark = SkyIslandBossForge.CreateGroundRing(root, feet);
                SkyIslandBossForge.PlaceRing(mark, root, feet);
                SkyIslandBossForge.SetRing(mark, 1.3f, .55f + .45f * Mathf.Abs(Mathf.Sin(now * 5f)) * Mathf.Clamp01(remaining), BeamTint);
            }
            catch (Exception e) { broken = true; Debug.LogWarning(LastShiftRaidScene.LogTag + " 点名红圈不可用：" + e.Message); }
        }

        internal void HideMark()
        {
            if (mark == null) return;
            SkyIslandBossForge.ReleaseRing(mark);
            mark = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            HideMark();
            if (beam != null) UnityEngine.Object.Destroy(beam.gameObject);
            beam = null;
        }
    }
}
