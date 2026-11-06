using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using Duckov.Utilities;
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
    /// 潮闸镇的刷怪 owner。每趟在作者摆的点位中按地区抽守备队，选中的点只刷一次，玩家走到 25–75 m 之间才刷
    /// （官方 `CharacterSpawnerRoot` 同样不在玩家 25 m 内出怪，避免背后刷怪）。拾荒者、水厂与灯塔岬的守卫、淘沙帮都用官方拾荒者预设，
    /// 守卫另按天空岛精英档配枪（`SkyIslandEnemyArmory`，品质 4–5），沙老大从官方 Boss 池按固定 key 抽一位、保留官方 Boss 掉落。
    /// 刷出后的六步与天空岛相同：关距离激活 → 官方创建 → 导航限定本图 → 解除距离休眠 → 敌对性安全网 `SetTeam(Teams.wolf)` → 挂到世界根下。
    /// </summary>
    internal sealed partial class TidegateEnemies : IDisposable
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
#if BOSSRUSH_DEV
            // 演练还原可先撤销仍在官方异步创建中的增援；迟到结果由 Spawn 的 finally 销毁。
            internal bool DevSpawning, DevCancelled;
            internal float DevLegDistance, DevLegAdvance;
#endif
            internal CharacterMainControl Character;
            internal AICharacterController Ai;
            internal Vector3 Home;
            internal float RetryAfter;
            // 镇街和渔港的同伴在附近首哨落位后才从巡逻点出场；一趟只来一次。
            internal Slot Lead;
            internal float JoinedAt = -1f, FollowDelay;
            internal TidegateEnemyRole Role;
            internal bool HasPatrol, PatrolOut;
            internal Vector3 PatrolPost, PatrolGoal;
            internal float HoldUntil = -1f, GiveUpAt, NextMove;
            internal int PatrolLeg, LastLeg = -1;
            internal Vector3 Origin { get { return Lead != null ? Home : Marker.position; } }
            internal int Waypoint;
            internal float DeepSince = -1f;
            internal GameObject MapPoint;
        }

        private readonly List<Slot> slots = new List<Slot>();
        private readonly TidegateWorld world;
        private readonly TidegateTide tide;
        private readonly ArenaPrototypeNavigation navigation;
        private readonly CharacterMainControl player;
        private readonly Func<bool> valid;
        private CharacterRandomPreset scav, lightScav;
        private float nextTick, nextTide, revealUntil = -1f;
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
            var candidates = new List<Slot>();
            foreach (Transform marker in world.SpawnMarkers)
            {
                // 进图时导航探路够不着的点本趟不刷（会话已记日志）。
                if (unreachable != null && unreachable.Contains(marker)) continue;
                string[] parts = marker.name.Split('_');
                candidates.Add(new Slot { Marker = marker, Id = marker.name, Group = parts.Length > 1 ? parts[1] : "town", Kind = parts.Length > 2 ? parts[2] : "scav",
                    Home = marker.position, Waypoint = unchecked(SkyIslandLootTables.StableHash(marker.name) + seed) & 0x7fffffff });
            }
            SelectRoster(candidates, seed);
            List<Vector3> posts;
            if (world.Waypoints.TryGetValue("town", out posts)) AddPartners("town", posts, 4, seed);
            if (world.Waypoints.TryGetValue("docks", out posts)) AddPartners("docks", posts, 3, seed);
            AssignRoles();
        }

        // 镇街与渔港以拾荒者小组守街，水厂和灯塔才出现精英；淘沙帮守船，沙老大每趟必在。
        // 候选点洗牌后按组取名额；未入选的标记整趟为空，不会在玩家搜完同一条街后补刷。
        private void SelectRoster(List<Slot> candidates, int seed)
        {
            candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            var random = SkyIslandLootTables.CreateStream(seed, "TidegateRoster");
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Slot other = candidates[j]; candidates[j] = candidates[i]; candidates[i] = other;
            }
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            // 8 号在控制楼外有掩体侧路；其它水厂候选里有室内点，不能随便拿来放远射哨。
            Slot overlook = candidates.Find(s => s.Id == "SPAWN_ww_guard_8");
            if (overlook != null) { slots.Add(overlook); counts["ww:guard"] = 1; }
            foreach (Slot slot in candidates)
            {
                if (slot == overlook) continue;
                string squad = slot.Group + ":" + slot.Kind;
                int max;
                switch (squad)
                {
                    case "town:scav": max = 5; break;
                    case "dig:digger": max = 4; break;
                    case "ww:guard": case "docks:scav": max = 3; break;
                    case "reeds:scav": max = 2; break;
                    case "cape:guard": case "cape:scav": case "dig:boss": max = 1; break;
                    default: max = 1; break;
                }
                int count;
                counts.TryGetValue(squad, out count);
                if (count >= max) continue;
                slots.Add(slot);
                counts[squad] = count + 1;
            }
        }

        private void AssignRoles()
        {
            bool townDog = false, dockDog = false, gunner = false;
            foreach (Slot slot in slots)
            {
                if (slot.Id == "SPAWN_ww_guard_8") slot.Role = TidegateEnemyRole.Marksman;
                else if (slot.Group == "dig" && slot.Kind == "digger" && !gunner)
                { slot.Role = TidegateEnemyRole.Gunner; gunner = true; }
                else if (slot.Lead != null && slot.Group == "town" && !townDog)
                { slot.Role = TidegateEnemyRole.Hound; townDog = true; }
                else if (slot.Lead != null && slot.Group == "docks" && !dockDog)
                { slot.Role = TidegateEnemyRole.Hound; dockDog = true; }
            }
        }

        // 从作者巡逻点挑出离首哨 5–26 m 的侧位，优先每个首哨配一人；不给进图点加伏兵。
        private void AddPartners(string group, List<Vector3> posts, int cap, int seed)
        {
            var order = new List<Vector3>(posts);
            var random = SkyIslandLootTables.CreateStream(seed, "TidegatePartners:" + group);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Vector3 other = order[j]; order[j] = order[i]; order[i] = other;
            }
            var assigned = new Dictionary<Slot, int>();
            int added = 0;
            for (int pass = 0; pass < 2 && added < cap; pass++)
            {
                foreach (Vector3 post in order)
                {
                    if ((world.PlayerSpawn.position - post).sqrMagnitude < 27f * 27f) continue;
                    Slot lead = null;
                    float best = 26f * 26f;
                    bool clear = true;
                    foreach (Slot slot in slots)
                    {
                        if (slot.Group != group) continue;
                        float sqr = (slot.Origin - post).sqrMagnitude;
                        if (sqr < 5f * 5f) { clear = false; break; }
                        int count;
                        assigned.TryGetValue(slot, out count);
                        if (slot.Lead == null && count <= pass && sqr < best) { lead = slot; best = sqr; }
                    }
                    if (!clear || lead == null) continue;
                    int previous;
                    assigned.TryGetValue(lead, out previous);
                    assigned[lead] = previous + 1;
                    if (!lead.HasPatrol || best > (lead.PatrolPost - lead.Origin).sqrMagnitude)
                    { lead.HasPatrol = true; lead.PatrolPost = post; }
                    slots.Add(new Slot { Marker = lead.Marker, Id = "Patrol_" + group + "_" + added,
                        Group = group, Kind = "scav", Home = post, Lead = lead, FollowDelay = 4f + 3f * previous });
                    if (++added == cap) break;
                }
            }
        }

        internal void Tick()
        {
            if (closed || valid == null || !valid() || player == null) return;
            if (Time.time >= nextTide) { nextTide = Time.time + TideInterval; TickTide(); }
            if (revealUntil > 0f && Time.time >= revealUntil) { revealUntil = -1f; ClearReveal(); }
            if (inFlight > 0 || Time.time < nextTick) return;
            nextTick = Time.time + TickInterval;
            Vector3 origin = player.transform.position;
            Slot pick = null;
            float best = MaxSpawnDistance * MaxSpawnDistance;
            foreach (Slot slot in slots)
            {
                if (slot.Started || slot.Failed || Time.time < slot.RetryAfter) continue;
                if (!CanJoin(slot, Time.time)) continue;
                if (slot.Group == "vault")
                {
                    if (!InSpawnRing(slot.Origin, true)) continue; // 玩家守在海堤就等，不发起创建也不挡其它点。
                    pick = slot; break;
                }
                float distance = (slot.Origin - origin).sqrMagnitude;
                if (distance < MinSpawnDistance * MinSpawnDistance || distance >= best) continue;
                // 刷怪点泡在深水里时先不刷：等水退了再出来，免得一出生就在水底。
                if (slot.Group == "dig" && tide != null && tide.IsDeepAt(slot.Marker.position)) continue;
                best = distance;
                pick = slot;
            }
            if (pick != null) Spawn(pick);
        }

        private CharacterRandomPreset SourceFor(Slot slot)
        {
            if (slot.Kind == "boss") return SkyIslandEnemySources.ForMinion("Tidegate_SandBoss");
            if (scav == null) scav = SkyIslandEnemySources.ScavReference();
            if (slot.Kind == "scav" && (slot.Group == "town" || slot.Group == "reeds"))
            {
                if (lightScav == null) lightScav = SkyIslandEnemySources.Named("EnemyPreset_Scav") ?? scav;
                return lightScav;
            }
            return scav; // 普通点绝不退回到 Boss 池；没有官方拾荒者才明确报刷怪失败。
        }

        private async void Spawn(Slot slot)
        {
            slot.Started = true;
            inFlight++;
#if BOSSRUSH_DEV
            // Dev 演练核对「25–75 m 才刷」：记下开刷那一刻玩家离刷怪点多远（TidegateDrillHooks.cs）。
            slot.DevSpawning = true;
#endif
            CharacterRandomPreset clone = null;
            CharacterMainControl created = null;
            bool retained = false;
            try
            {
                // 先吸到本图导航最近的可走面上：有几个刷怪点落在车辆 / 设备的实心碰撞盒里（墙层），
                // 地面射线会穿过盒子落到盒底，敌人就生在盒子里头（ArtSource/Tidegate/README.md 的「运行时要 GetNearest」）。
                Vector3 position = slot.Origin, snapped;
                if (!navigation.TryNearestWalkable(position, out snapped) || !TryNearbyFooting(snapped, out position)
                    || !InSpawnRing(position, slot.Group == "vault")
                    || (slot.Lead != null && !OnLeadRoute(slot, position)))
                {
                    slot.Started = false;
                    slot.RetryAfter = Time.time + 3f;
                    return;
                }
#if BOSSRUSH_DEV
                DevNoteSpawn(slot, position);
#endif
                CharacterRandomPreset source = TidegateEnemyActors.Source(slot.Role, SourceFor(slot));
                if (source == null) throw new InvalidOperationException("找不到官方敌人预设");
                if (slot.Role == TidegateEnemyRole.Hound && !TidegateEnemyActors.IsPatrolWolf(source)) slot.Role = TidegateEnemyRole.Regular;
                clone = UnityEngine.Object.Instantiate(source);
                clone.name = "BossRush_Tidegate_" + slot.Id;
                clone.dropBoxOnDead = true;
                clone.setActiveByPlayerDistance = false;
                // 缩小岸哨无事时的漫游，保留官方选敌、交火与追击。
                if (slot.Group != "dig" && slot.Group != "vault") clone.patrolRange = Mathf.Min(clone.patrolRange, 3.5f);
                TidegateEnemyActors.Tune(slot.Role, clone);
                // 拾荒者及其猎犬同阵营；武装守卫和淘沙帮敌视他们，玩家可以借两边的交火转移。
                clone.team = ScavDistrict(slot.Group) ? Teams.scav : Teams.wolf;
                created = await clone.CreateCharacterAsync(position, slot.Marker.forward, -1, null, false);
#if BOSSRUSH_DEV
                if (slot.DevCancelled) return;
#endif
                if (closed || valid == null || !valid() || world.Root == null) return;
                if (created == null) throw new InvalidOperationException("官方角色创建失败");
                // 官方异步创建期间玩家可能接近、潮水也可能涨；最终脚点仍要干燥且在普通刷怪安全环内。
                if (tide.IsDeepAt(created.transform.position) || !InSpawnRing(created.transform.position, slot.Group == "vault"))
                {
                    slot.Started = false;
                    slot.RetryAfter = Time.time + 3f;
                    return;
                }
                Seeker[] seekers = created.GetComponentsInChildren<Seeker>(true);
                AICharacterController ai = created.GetComponentInChildren<AICharacterController>();
                if (seekers.Length == 0 || ai == null) throw new InvalidOperationException("角色缺少战斗 AI / 导航");
                foreach (Seeker seeker in seekers) { seeker.CancelCurrentPathRequest(); seeker.graphMask = navigation.Mask; }
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created);
                created.SetTeam(clone.team);
                if (!Team.IsEnemy(created.Team, Teams.player)) created.SetTeam(Teams.wolf);
                created.transform.SetParent(world.Root.transform, true);
                // 水厂与灯塔岬的守卫比镇上的拾荒者难打一档：按天空岛精英档换枪补弹（池子进图读条时已预热，可失败的装饰）。
                if (!TidegateEnemyActors.Equip(slot.Role, created, tide, valid) && slot.Kind == "guard") SkyIslandEnemyArmory.Arm(created, SkyIslandEnemyTier.Elite);
                // 选敌全交给官方感知。不改 forceTracePlayerDistance：官方 AI 在这个距离内无视视线每帧锁定玩家，等于透视。
                slot.Ai = ai;
                slot.Home = position;
                // 克隆 preset 要活到角色销毁：官方死亡掉落、经验与魂都还会读它（同天空岛 SkyIslandEnemyLife.OwnPreset）。
                created.gameObject.AddComponent<TidegatePresetOwner>().Preset = clone;
                clone = null;
                slot.Character = created;
                slot.JoinedAt = Time.time;
                if (revealUntil > 0f) MarkOnMap(slot);
                retained = true;
                SpawnedCount++;
                Debug.Log(TidegateRaidScene.LogTag + " ENEMY_READY id=" + slot.Id + " preset=" + source.name + " role=" + slot.Role + " team=" + created.Team);
            }
            catch (Exception e)
            {
                slot.Failed = true;
                Debug.LogWarning(TidegateRaidScene.LogTag + " 刷怪失败 " + slot.Id + "：" + e.Message);
            }
            finally
            {
#if BOSSRUSH_DEV
                slot.DevSpawning = false;
#endif
                if (!retained && created != null) { created.gameObject.SetActive(false); UnityEngine.Object.Destroy(created.gameObject); }
                if (clone != null) UnityEngine.Object.Destroy(clone, created != null ? 0.1f : 0f);
                inFlight--;
            }
        }

        private bool InSpawnRing(Vector3 position, bool seawall = false)
        {
            float sqr = (position - player.transform.position).sqrMagnitude;
            return sqr >= MinSpawnDistance * MinSpawnDistance && (seawall || sqr < MaxSpawnDistance * MaxSpawnDistance);
        }

        private static bool ScavDistrict(string group) { return group == "town" || group == "docks" || group == "reeds"; }

        private static bool CanJoin(Slot slot, float now)
        {
            return slot.Lead == null || (slot.Lead.JoinedAt >= 0f && now >= slot.Lead.JoinedAt + slot.FollowDelay);
        }

        private bool OnLeadRoute(Slot slot, Vector3 position)
        {
            // 不让岸面投影把同伴吸到另一街 / 楼层；只在本图可走分区与已落位首哨连通时采用。
            if ((position - slot.Home).sqrMagnitude > 6f * 6f || (position - slot.Lead.Home).sqrMagnitude < 4f * 4f) return false;
            return SamePatrolArea(slot.Lead.Home, position);
        }

        private bool SamePatrolArea(Vector3 from, Vector3 to)
        {
            AstarPath astar = AstarPath.active;
            if (astar == null || astar.isScanning) return false;
            NNConstraint constraint = NNConstraint.Walkable;
            constraint.graphMask = navigation.Mask;
            NNInfo a = astar.GetNearest(from, constraint), b = astar.GetNearest(to, constraint);
            return a.node != null && b.node != null && PathUtilities.IsPathPossible(a.node, b.node);
        }

        // 地形射线、导航投影和潮位可能给出不同答案。先验最终站位；只有该点失败时才搜附近岸面。
        private bool TryNearbyFooting(Vector3 near, out Vector3 position)
        {
            if (CanStand(near, out position)) return true;
            for (int radius = 2; radius <= 8; radius += 3)
            {
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * 0.25f;
                    Vector3 probe = near + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    Vector3 snapped;
                    if (navigation.TryNearestWalkable(probe, out snapped) && (snapped - near).sqrMagnitude <= 81f
                        && CanStand(snapped, out position)) return true;
                }
            }
            return false;
        }

        private bool CanStand(Vector3 point, out Vector3 position)
        {
            position = point;
            float ground;
            if (!world.TryGroundAt(point, out ground) || TidegateTideRules.IsDeep(tide.WaterLevelAt(point), ground)) return false;
            position.y = ground + 0.05f;
            return !Physics.CheckCapsule(position + Vector3.up * 0.45f, position + Vector3.up * 1.25f, 0.25f,
                GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore);
        }

        private void RecoverShorePatrol(Slot slot, CharacterMainControl character)
        {
            Vector3 here = character.transform.position;
            if (!tide.IsDeepAt(here)) return;
            Vector3 snapped, safe;
            if (!navigation.TryNearestWalkable(here, out snapped) || !TryNearbyFooting(snapped, out safe))
            {
                if (!navigation.TryNearestWalkable(slot.Home, out snapped) || !TryNearbyFooting(snapped, out safe)) return;
            }
            character.SetPosition(safe);
            slot.Ai.patrolPosition = safe;
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
                else { RecoverShorePatrol(slot, character); TickSquadPatrol(slot); continue; }
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
#if BOSSRUSH_DEV
            DevNoteSweep(slot);
#endif
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
        /// 点灯时仅在灯塔发一次无人物声源。官方 AI 按自己的听觉察觉该位置；玩家转移后，脚步与枪声仍由官方感知处理。
        /// </summary>
        internal void Lure(Vector3 origin, float radius)
        {
            if (closed) return;
            try
            {
                AIMainBrain.MakeSound(new AISound { pos = origin, radius = radius, fromTeam = Teams.player,
                    soundType = SoundTypes.unknowNoise, fromCharacter = null, fromObject = null });
            }
            catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 灯塔声源未能送达：" + e.Message); }
        }

        /// <summary>剧情增援可从远处海堤入场；若玩家贴着来路，就登记等待，仍然保留 25 m 出生下限。</summary>
        internal void SpawnExtra(Vector3 position, string kind)
        {
            if (closed) return;
            GameObject anchor = new GameObject("TidegateExtraSpawn_" + slots.Count);
            anchor.transform.SetParent(world.Root.transform, false);
            anchor.transform.position = position;
            var slot = new Slot { Marker = anchor.transform, Id = anchor.name, Group = "vault", Kind = kind, Home = position, Waypoint = slots.Count };
            slots.Add(slot);
            if (InSpawnRing(position, true)) Spawn(slot);
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
