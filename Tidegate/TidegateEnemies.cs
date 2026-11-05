using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>角色销毁时一并销毁它的克隆 preset。</summary>
    internal sealed class TidegatePresetOwner : MonoBehaviour
    {
        internal CharacterRandomPreset Preset;
        private void OnDestroy() { if (Preset != null) Destroy(Preset); Preset = null; }
    }

    /// <summary>
    /// 潮闸镇的刷怪 owner。每个刷怪点（`Markers/Spawns/SPAWN_<组>_<种类>_<序号>`）本趟只刷一次，玩家走到 25–75 m 之间才刷
    /// （官方 `CharacterSpawnerRoot` 同样不在玩家 25 m 内出怪，避免背后刷怪）。拾荒者、水厂与灯塔岬的守卫、淘沙帮都用官方拾荒者预设，
    /// 守卫另按天空岛精英档配枪（`SkyIslandEnemyArmory`，品质 4–5），沙老大从官方 Boss 池按固定 key 抽一位、保留官方 Boss 掉落。
    /// 刷出后的六步与天空岛相同：关距离激活 → 官方创建 → 导航限定本图 → 解除距离休眠 → 敌对性安全网 `SetTeam(Teams.wolf)` → 挂到世界根下。
    /// </summary>
    internal sealed class TidegateEnemies : IDisposable
    {
        private const float MinSpawnDistance = 25f, MaxSpawnDistance = 75f, TickInterval = 0.5f;
        /// <summary>淘沙帮跟潮走的推进间隔（秒）。只改官方 AI 的巡逻中心，不碰它的行为树与选敌。</summary>
        private const float TideInterval = 3f;
        /// <summary>涨潮前这么多现实秒就往挖沙船撤（原型「涨潮前 20 s」）。</summary>
        private const float RetreatLeadSeconds = 20f;
        /// <summary>泡在深水里连续这么久就算被潮水卷走（原型规则，只对淘沙帮）。</summary>
        private const float SweptSeconds = 8f;
        /// <summary>沙老大退潮时守着的地方：挖沙船跟前（three.js 原型 (4.5, 50.5)，Unity z 取反）。</summary>
        private static readonly Vector3 BossDigSpot = new Vector3(4.5f, 0f, -50.5f);

        private sealed class Slot
        {
            internal Transform Marker;
            internal string Id, Group, Kind;
            internal bool Started, Failed;
            internal CharacterMainControl Character;
            internal AICharacterController Ai;
            internal Vector3 Home;
            internal int Waypoint;
            internal float DeepSince = -1f;
            internal GameObject MapPoint;
            /// <summary>灯塔引怪前的官方 forceTracePlayerDistance；负数表示没被引。</summary>
            internal float LureOriginal = -1f;
        }

        private readonly List<Slot> slots = new List<Slot>();
        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private readonly ArenaPrototypeNavigation navigation;
        private readonly CharacterMainControl player;
        private readonly Func<bool> valid;
        private float nextTick, nextTide, revealUntil = -1f, lureUntil = -1f;
        private bool closed;
        /// <summary>
        /// 正在官方创建中的角色数。按距离的刷怪等它归零才挑下一个；海堤增援一次刷三个，计数器（不是布尔）
        /// 保证三个都回来之前不再开新的，也不会被先回来的那个提前清零（CR-2026-11-05-026）。
        /// </summary>
        private int inFlight;

        internal int SpawnedCount { get; private set; }

        internal TidegateEnemies(TidegateWorld world, TidegateTide tide, ArenaPrototypeNavigation navigation, CharacterMainControl player,
            int seed, Func<bool> valid, ICollection<Transform> unreachable)
        {
            this.world = world;
            this.tide = tide;
            this.navigation = navigation;
            this.player = player;
            this.valid = valid;
            foreach (Transform marker in world.SpawnMarkers)
            {
                // 进图时导航探路够不着的点本趟不刷（会话已记日志）。
                if (unreachable != null && unreachable.Contains(marker)) continue;
                string[] parts = marker.name.Split('_');
                slots.Add(new Slot { Marker = marker, Id = marker.name, Group = parts.Length > 1 ? parts[1] : "town", Kind = parts.Length > 2 ? parts[2] : "scav",
                    Home = marker.position, Waypoint = unchecked(SkyIslandLootTables.StableHash(marker.name) + seed) & 0x7fffffff });
            }
        }

        internal void Tick()
        {
            if (closed || valid == null || !valid() || player == null) return;
            if (Time.time >= nextTide) { nextTide = Time.time + TideInterval; TickTide(); }
            if (revealUntil > 0f && Time.time >= revealUntil) { revealUntil = -1f; ClearReveal(); }
            if (lureUntil > 0f && Time.time >= lureUntil) { lureUntil = -1f; EndLure(); }
            if (inFlight > 0 || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            Vector3 origin = player.transform.position;
            Slot pick = null;
            float best = MaxSpawnDistance * MaxSpawnDistance;
            foreach (Slot slot in slots)
            {
                if (slot.Started || slot.Failed) continue;
                float distance = (slot.Marker.position - origin).sqrMagnitude;
                if (distance < MinSpawnDistance * MinSpawnDistance || distance >= best) continue;
                // 刷怪点泡在深水里时先不刷：等水退了再出来，免得一出生就在水底。
                if (tide != null && tide.IsDeepAt(slot.Marker.position)) continue;
                best = distance;
                pick = slot;
            }
            if (pick != null) Spawn(pick);
        }

        private static CharacterRandomPreset SourceFor(Slot slot)
        {
            if (slot.Kind == "boss") return SkyIslandEnemySources.ForMinion("Tidegate_SandBoss");
            return SkyIslandEnemySources.ScavReference() ?? SkyIslandEnemySources.ForMinion("Tidegate_" + slot.Id);
        }

        private async void Spawn(Slot slot)
        {
            slot.Started = true;
            inFlight++;
            CharacterRandomPreset clone = null;
            CharacterMainControl created = null;
            bool retained = false;
            try
            {
                CharacterRandomPreset source = SourceFor(slot);
                if (source == null) throw new InvalidOperationException("找不到官方敌人预设");
                clone = UnityEngine.Object.Instantiate(source);
                clone.name = "BossRush_Tidegate_" + slot.Id;
                // 正式出击沿用官方 OnDead 箱子、经验与魂语义；距离激活关掉，由下面的安全网统一解除休眠。
                clone.dropBoxOnDead = true;
                clone.setActiveByPlayerDistance = false;
                // 先吸到本图导航最近的可走面上：有几个刷怪点落在车辆 / 设备的实心碰撞盒里（墙层），
                // 地面射线会穿过盒子落到盒底，敌人就生在盒子里头（ArtSource/Tidegate/README.md 的「运行时要 GetNearest」）。
                Vector3 position = slot.Marker.position, snapped;
                if (navigation.TryNearestWalkable(position, out snapped)) position = snapped;
                float ground;
                if (world.TryGroundAt(position, out ground)) position.y = ground + 0.05f;
                created = await clone.CreateCharacterAsync(position, slot.Marker.forward, -1, null, false);
                if (closed || valid == null || !valid() || world.Root == null) return;
                if (created == null) throw new InvalidOperationException("官方角色创建失败");
                Seeker[] seekers = created.GetComponentsInChildren<Seeker>(true);
                AICharacterController ai = created.GetComponentInChildren<AICharacterController>();
                if (seekers.Length == 0 || ai == null) throw new InvalidOperationException("角色缺少战斗 AI / 导航");
                foreach (Seeker seeker in seekers) { seeker.CancelCurrentPathRequest(); seeker.graphMask = navigation.Mask; }
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created);
                // 根 AGENTS §4.5：官方 preset 的阵营可能是中立，统一挂到 wolf，保证与玩家敌对、彼此不内斗。
                created.SetTeam(Teams.wolf);
                // 水厂与灯塔岬的守卫比镇上的拾荒者难打一档：按天空岛精英档换枪补弹（池子进图读条时已预热，可失败的装饰）。
                if (slot.Kind == "guard") SkyIslandEnemyArmory.Arm(created, SkyIslandEnemyTier.Elite);
                // 选敌全交给官方感知。不改 forceTracePlayerDistance：官方 AI 在这个距离内无视视线每帧锁定玩家，等于透视。
                created.transform.SetParent(world.Root.transform, true);
                slot.Ai = ai;
                if (revealUntil > 0f) MarkOnMap(slot);
                // 克隆 preset 要活到角色销毁：官方死亡掉落、经验与魂都还会读它（同天空岛 SkyIslandEnemyLife.OwnPreset）。
                created.gameObject.AddComponent<TidegatePresetOwner>().Preset = clone;
                clone = null;
                slot.Character = created;
                retained = true;
                SpawnedCount++;
                Debug.Log(TidegateRaidScene.LogTag + " ENEMY_READY id=" + slot.Id + " preset=" + source.name);
            }
            catch (Exception e)
            {
                slot.Failed = true;
                Debug.LogWarning(TidegateRaidScene.LogTag + " 刷怪失败 " + slot.Id + "：" + e.Message);
            }
            finally
            {
                if (!retained && created != null) { created.gameObject.SetActive(false); UnityEngine.Object.Destroy(created.gameObject); }
                if (clone != null) UnityEngine.Object.Destroy(clone, created != null ? 0.1f : 0f);
                inFlight--;
            }
        }

        /// <summary>
        /// 淘沙帮跟着潮水走（原型 game.js 的 enemyGoalIdle）：低潮下滩挖东西，涨潮前 20 秒往挖沙船撤；海堤来的增援等泻湖放干再进金库。
        /// 只改官方 AI 的 <c>patrolPosition</c>（闲逛中心），交火时官方行为树照旧。泡在深水里太久的淘沙帮被潮水卷走。
        /// </summary>
        private void TickTide()
        {
            if (tide == null) return;
            bool diggersOut = DiggersOut();
            List<Vector3> dig, vault;
            world.Waypoints.TryGetValue("dig", out dig);
            world.Waypoints.TryGetValue("vault", out vault);
            foreach (Slot slot in slots)
            {
                CharacterMainControl character = slot.Character;
                if (character == null || slot.Ai == null || character.Health == null || character.Health.IsDead) continue;
                Vector3 goal = slot.Home;
                if (slot.Group == "dig")
                {
                    if (diggersOut) goal = slot.Kind == "boss" ? BossDigSpot : Pick(dig, slot, slot.Home);
                }
                else if (slot.Group == "vault")
                {
                    if (tide.SluiceOpen && tide.LagoonLevel < -0.9f) goal = Pick(vault, slot, slot.Home);
                }
                else continue;
                slot.Ai.patrolPosition = goal;
                // 被潮水卷走：涨潮 / 高潮时还泡在深水里（脚下水深 > 0.5 m）超过 8 秒就消失，不掉东西。
                // 沙老大不卷：他是官方 Boss，掉落是打他的回报，不能让潮水白白带走。
                if (slot.Kind != "boss" && !diggersOut && tide.IsDeepAt(character.transform.position))
                {
                    if (slot.DeepSince < 0f) slot.DeepSince = Time.time;
                    else if (Time.time - slot.DeepSince > SweptSeconds) SweepAway(slot);
                }
                else slot.DeepSince = -1f;
            }
        }

        /// <summary>低潮、或退潮已过半时下滩；离涨潮不到 20 秒就往回撤。</summary>
        private bool DiggersOut()
        {
            TidegateTidePhase phase = tide.Phase;
            if (phase == TidegateTidePhase.Low) return TidegateTide.RealSecondsUntilNextPhase() > RetreatLeadSeconds;
            return phase == TidegateTidePhase.Ebb && tide.Level < (TidegateTideRules.HighLevel + TidegateTideRules.LowLevel) * 0.5f;
        }

        private static Vector3 Pick(List<Vector3> points, Slot slot, Vector3 fallback)
        {
            if (points == null || points.Count == 0) return fallback;
            return points[slot.Waypoint % points.Count];
        }

        private void SweepAway(Slot slot)
        {
            CharacterMainControl character = slot.Character;
            slot.Character = null;
            slot.Ai = null;
            if (slot.MapPoint != null) UnityEngine.Object.Destroy(slot.MapPoint);
            slot.MapPoint = null;
            if (character == null) return;
            character.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(character.gameObject);
            Debug.Log(TidegateRaidScene.LogTag + " SWEPT_AWAY id=" + slot.Id);
        }

        /// <summary>灯塔点亮：这么多秒里全图活着的敌人都显示在官方地图上（地图点挂在敌人身上，跟着走）。</summary>
        internal void RevealOnMap(float seconds)
        {
            if (closed) return;
            revealUntil = Time.time + seconds;
            foreach (Slot slot in slots) MarkOnMap(slot);
        }

        private void MarkOnMap(Slot slot)
        {
            CharacterMainControl character = slot.Character;
            if (slot.MapPoint != null || character == null || character.Health == null || character.Health.IsDead) return;
            try
            {
                var go = new GameObject("TidegateEnemyMapPoint");
                go.transform.SetParent(character.transform, false);
                SimplePointOfInterest poi = go.AddComponent<SimplePointOfInterest>();
                poi.Color = new Color(0.92f, 0.36f, 0.28f, 1f);
                poi.ScaleFactor = slot.Kind == "boss" ? 1.4f : 0.9f;
                poi.IsArea = false;
                poi.Setup(null, slot.Kind == "boss" ? TidegateText.MapBossKey : TidegateText.MapEnemyKey);
                slot.MapPoint = go;
            }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 敌人地图点创建失败：" + e.Message); }
        }

        private void ClearReveal()
        {
            foreach (Slot slot in slots)
            {
                if (slot.MapPoint != null) UnityEngine.Object.Destroy(slot.MapPoint);
                slot.MapPoint = null;
            }
        }

        /// <summary>
        /// 灯塔点亮这类动静：半径内已刷出的敌人在 <paramref name="seconds"/> 秒里咬住玩家（官方 forceTracePlayerDistance，
        /// 这个距离内无视视线锁定），到时还原成官方原值——和全图显示敌人同样长，不是整趟透视。
        /// </summary>
        internal void Lure(Vector3 origin, float radius, float seconds)
        {
            if (closed) return;
            foreach (Slot slot in slots)
            {
                CharacterMainControl character = slot.Character;
                AICharacterController ai = slot.Ai;
                if (character == null || ai == null || character.Health == null || character.Health.IsDead) continue;
                if ((character.transform.position - origin).sqrMagnitude > radius * radius) continue;
                if (slot.LureOriginal < 0f) slot.LureOriginal = Mathf.Max(0f, ai.forceTracePlayerDistance);
                ai.forceTracePlayerDistance = Mathf.Max(slot.LureOriginal, radius + 20f);
            }
            lureUntil = Time.time + Mathf.Max(1f, seconds);
        }

        private void EndLure()
        {
            foreach (Slot slot in slots)
            {
                if (slot.LureOriginal < 0f) continue;
                if (slot.Ai != null) slot.Ai.forceTracePlayerDistance = slot.LureOriginal;
                slot.LureOriginal = -1f;
            }
        }

        /// <summary>剧情增援（大闸提起后海堤来的淘沙帮）：不受 25–75 m 刷怪距离限制，立刻在给定点刷。</summary>
        internal void SpawnExtra(Vector3 position, string kind)
        {
            if (closed) return;
            GameObject anchor = new GameObject("TidegateExtraSpawn_" + slots.Count);
            anchor.transform.SetParent(world.Root.transform, false);
            anchor.transform.position = position;
            var slot = new Slot { Marker = anchor.transform, Id = anchor.name, Group = "vault", Kind = kind, Home = position, Waypoint = slots.Count };
            slots.Add(slot);
            Spawn(slot);
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            ClearReveal();
            foreach (Slot slot in slots)
            {
                if (slot.Character == null) continue;
                if (slot.Character.Health != null && slot.Character.Health.IsDead) continue;
                slot.Character.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(slot.Character.gameObject);
                slot.Character = null;
            }
            slots.Clear();
        }
    }
}
