using System;
using System.Collections.Generic;
using BossRush.Utils;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>本次出击的固定普通巡守；不消费 Boss/剧情组预算，不写剧情或玩家存档。</summary>
    internal sealed class SkyIslandPatrols : IDisposable
    {
        private sealed class Cell
        {
            internal SkyIslandPatrolSlot Slot;
            internal SkyIslandPatrolProfile Profile;
            internal CharacterMainControl Character;
            internal SkyIslandPatrolLife Life;
            internal int Failures, SpawnGeneration;
            internal float NextRetry;
        }

        private readonly GameObject root;
        private readonly CharacterMainControl player;
        private readonly GraphMask graphMask;
        private readonly int groundMask;
        private readonly Vector3 origin;
        private readonly Func<bool> valid;
        private readonly Func<SkyIslandStoryData> story;
        private readonly SkyIslandContentData content;
        private readonly Cell[] cells;
        private readonly SkyIslandPatrolSchedule schedule;
        private readonly CharacterRandomPreset source;
        private readonly List<int> active = new List<int>(24);
        private bool closed;
        private bool namesChinese;
        private float nextTick, nextSpawn;
        private int tickCount;
        internal int PlannedSlots { get { return cells.Length; } }
        internal int ActiveCount { get { return schedule.ActiveCount; } }
        internal int PendingCount { get { return schedule.PendingCount; } }
        internal string DataSource { get; private set; }

        internal SkyIslandPatrols(GameObject map, CharacterMainControl mainPlayer, GraphMask mask, int floorMask,
            Vector3 offset, SkyIslandContentData definitions, Func<bool> sessionValid, Func<SkyIslandStoryData> currentStory)
        {
            if (map == null || mainPlayer == null || definitions == null || sessionValid == null || currentStory == null)
                throw new ArgumentException("巡守 owner 未就绪");
            root = map; player = mainPlayer; graphMask = mask; groundMask = floorMask;
            origin = offset; content = definitions; valid = sessionValid; story = currentStory;
            SkyIslandPatrolData data = SkyIslandPatrolRules.Load(ModBehaviour.GetModPath());
            DataSource = data.Source;
            cells = new Cell[data.Slots.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                SkyIslandPatrolSlot slot = data.Slots[i];
                SkyIslandPatrolProfile profile = data.FindProfile(slot.RegionId);
                if (profile == null) throw new InvalidOperationException("巡守档位缺失：" + slot.RegionId);
                cells[i] = new Cell { Slot = slot, Profile = profile };
            }
            schedule = new SkyIslandPatrolSchedule(cells.Length, SkyIslandPatrolRules.ActiveLimit);
            source = FindSource();
            namesChinese = L10n.IsChinese;
        }

        // 同一底模与同一原版基准供各区倍率使用，绝不按生成顺序或随机池改变难度。
        private static CharacterRandomPreset FindSource()
        {
            var choices = new List<CharacterRandomPreset>();
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                if (preset != null && !preset.isBoss && !preset.isZombie && !preset.isVehicle && preset.team == Teams.scav
                    && preset.health >= 25f && preset.name.IndexOf("Dummy", StringComparison.OrdinalIgnoreCase) < 0
                    && !preset.name.StartsWith("BossRush_", StringComparison.Ordinal)) choices.Add(preset);
            choices.Sort(delegate(CharacterRandomPreset a, CharacterRandomPreset b)
            {
                int health = a.health.CompareTo(b.health);
                return health != 0 ? health : string.CompareOrdinal(a.name, b.name);
            });
            if (choices.Count == 0) throw new InvalidOperationException("巡守缺少原版普通敌人底模");
            return choices[0];
        }

        private Vector3 Position(Cell cell) { return origin + new Vector3(cell.Slot.X, cell.Slot.Y, cell.Slot.Z); }
        private bool Allowed(Cell cell)
        {
            return cell.Slot.RegionId != "H" || content.IsGateOpen("BellCourt", story());
        }
        private bool Nearby(Cell cell, Vector3 at)
        {
            float radius = SkyIslandPatrolRules.ActivationRadius;
            return (Position(cell) - at).sqrMagnitude <= radius * radius
                || (cell.Character != null && (cell.Character.transform.position - at).sqrMagnitude <= radius * radius);
        }

        internal void Tick()
        {
            if (closed || root == null || player == null || !valid() || BossRushUI.IsGamePaused()) return;
            float now = Time.unscaledTime;
            if (now < nextTick) return;
            nextTick = now + SkyIslandPatrolRules.TickInterval;
            tickCount++;
            Vector3 at = player.transform.position;
            bool languageChanged = namesChinese != L10n.IsChinese;
            namesChinese = L10n.IsChinese;
            // 远区只停用同一活体，保留装备、生命和单趟死亡状态；不销毁并重新满血补刷。
            for (int n = active.Count - 1; n >= 0; n--)
            {
                int index = active[n]; Cell cell = cells[index];
                if (cell.Character == null || cell.Character.Health == null || cell.Character.Health.IsDead)
                { MarkDefeated(index, cell.SpawnGeneration); continue; }
                if (languageChanged) ApplyName(cell);
                float radius = SkyIslandPatrolRules.SuspensionRadius;
                if ((Position(cell) - at).sqrMagnitude > radius * radius
                    && (cell.Character.transform.position - at).sqrMagnitude > radius * radius)
                    Suspend(index);
            }
            // 优先恢复同一实体，再预留一个最近的未生成点；pending 也占名额。
            for (int i = 0; i < cells.Length; i++)
            {
                Cell cell = cells[i];
                if (!schedule.IsSpawned(i) || schedule.IsActive(i) || schedule.IsDefeated(i)) continue;
                if (cell.Character == null || cell.Character.Health == null || cell.Character.Health.IsDead)
                { MarkDefeated(i, cell.SpawnGeneration); continue; }
                if (now >= cell.NextRetry && Nearby(cell, at) && Allowed(cell) && schedule.TryActivate(i))
                {
                    try
                    {
                        ConfigureNavigation(cell.Character);
                        cell.Character.gameObject.SetActive(true);
                        active.Add(i);
                        ApplyName(cell);
                    }
                    catch (Exception e)
                    {
                        schedule.Suspend(i);
                        if (cell.Character != null) cell.Character.gameObject.SetActive(false);
                        cell.Failures++; cell.NextRetry = now + cell.Failures * 3f;
                        if (cell.Failures == 1) Debug.LogWarning("[SkyIslandPatrol] RESUME_FAILED slot=" + cell.Slot.Id + " reason=" + e.Message);
                        if (cell.Failures >= 3) schedule.MarkDefeated(i);
                    }
                }
            }
            if (now < nextSpawn || schedule.PendingCount != 0) return;
            int nearest = -1; float distance = float.MaxValue;
            for (int i = 0; i < cells.Length; i++)
            {
                Cell cell = cells[i];
                if (!schedule.CanSpawn(i) || now < cell.NextRetry || !Allowed(cell) || !Nearby(cell, at)) continue;
                float candidate = (Position(cell) - at).sqrMagnitude;
                if (candidate < distance) { distance = candidate; nearest = i; }
            }
            if (nearest >= 0 && schedule.TryReserve(nearest))
            {
                nextSpawn = now + SkyIslandPatrolRules.SpawnInterval;
                Spawn(nearest, schedule.Generation);
            }
        }

        private void Suspend(int index)
        {
            Cell cell = cells[index];
            if (!schedule.Suspend(index)) return;
            active.Remove(index);
            foreach (Seeker seeker in cell.Character.GetComponentsInChildren<Seeker>(true)) seeker.CancelCurrentPathRequest();
            foreach (AI_PathControl path in cell.Character.GetComponentsInChildren<AI_PathControl>(true)) path.StopMove();
            NPCNameTagHelper.UnregisterOriginalHealthBarName(cell.Character.transform);
            cell.Character.gameObject.SetActive(false);
        }

        private void ConfigureNavigation(CharacterMainControl character)
        {
            Seeker[] seekers = character.GetComponentsInChildren<Seeker>(true);
            AICharacterController ai = character.GetComponentInChildren<AICharacterController>();
            if (seekers.Length == 0 || ai == null) throw new InvalidOperationException("巡守缺少战斗 AI / 导航");
            foreach (Seeker seeker in seekers) { seeker.CancelCurrentPathRequest(); seeker.graphMask = graphMask; }
            foreach (AI_PathControl path in character.GetComponentsInChildren<AI_PathControl>(true)) path.StopMove();
            SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);
            character.SetTeam(Teams.wolf);
            ai.forceTracePlayerDistance = 0f;
        }

        private bool TryFooting(Cell cell, out Vector3 point)
        {
            Vector3 expected = Position(cell); point = expected;
            RaycastHit hit;
            if (!Physics.Raycast(expected + Vector3.up * 2f, Vector3.down, out hit, 4f, groundMask, QueryTriggerInteraction.Ignore)
                || hit.transform == null || !hit.transform.IsChildOf(root.transform)
                || Mathf.Abs(hit.point.y - expected.y) > .35f) return false;
            if (Physics.CheckCapsule(hit.point + Vector3.up * .6f, hit.point + Vector3.up * 1.5f, .45f,
                GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) return false;
            point = hit.point + Vector3.up * .15f;
            return true;
        }

        private static void Prepare(CharacterRandomPreset clone, CharacterRandomPreset baseline, SkyIslandPatrolProfile profile)
        {
            SkyIslandCombatPreset.Apply(clone, baseline, "Patrol_" + profile.RegionId, 0, SkyIslandEnemyTier.Scav);
            clone.health *= profile.HealthFactor;
            clone.damageMultiplier *= profile.DamageFactor;
            clone.meleeDamageMultiplier *= profile.DamageFactor;
            clone.sightDistance = profile.SightDistance;
            clone.hearingAbility = Mathf.Min(clone.hearingAbility, .35f + profile.Rank * .12f);
            clone.hasSkill = false;
            clone.reactionTime = profile.ReactionTime;
            clone.shootDelay = Mathf.Max(.16f, .55f - (profile.Rank - 1) * .045f);
            clone.showName = true;
            clone.dropBoxOnDead = true;
            clone.setActiveByPlayerDistance = false;
        }

        private async void Spawn(int index, int generation)
        {
            Cell cell = cells[index]; CharacterRandomPreset clone = null; CharacterMainControl created = null;
            bool retained = false, presetOwned = false;
            try
            {
                if (closed || root == null || !valid()) return;
                Vector3 point;
                if (!TryFooting(cell, out point)) throw new InvalidOperationException("固定点位地面或净空不合格");
                clone = UnityEngine.Object.Instantiate(source);
                clone.name = "BossRush_SkyIslandPatrol_" + cell.Slot.Id;
                Prepare(clone, source, cell.Profile);
                created = await clone.CreateCharacterAsync(point, Vector3.forward, -1, null, false);
                if (created == null) throw new InvalidOperationException("官方创建巡守失败");
                SkyIslandPatrolLife life = created.gameObject.AddComponent<SkyIslandPatrolLife>();
                life.OwnPreset(clone); presetOwned = true;
                if (closed || root == null || !valid() || generation != schedule.Generation) return;
                created.transform.SetParent(root.transform, true);
                if (created.Health == null) throw new InvalidOperationException("巡守生命组件缺失");
                if (created.Health.IsDead)
                {
                    schedule.AbortSpawn(index, generation, false);
                    cell.Character = created; cell.Life = life; cell.SpawnGeneration = generation; retained = true; return;
                }
                created.gameObject.SetActive(false);
                if (!SkyIslandPatrolAppearance.Apply(created, cell.Slot.RegionId, cell.Profile.Rank))
                    throw new InvalidOperationException("巡守固定外形装配失败");
                // 底模是最弱的官方拾荒者：武器按岛区等级换到品质 3 以上（高等级岛区 4 以上）。
                SkyIslandEnemyArmory.ArmPatrol(created, cell.Profile.Rank);
                if (closed || root == null || !valid() || generation != schedule.Generation) return;
                ConfigureNavigation(created);
                bool activate = !BossRushUI.IsGamePaused() && Nearby(cell, player.transform.position) && Allowed(cell);
                if (!schedule.CompleteSpawn(index, generation, activate)) return;
                cell.Character = created; cell.Life = life; cell.SpawnGeneration = generation;
                life.Bind(this, index, generation, created.Health);
                created.gameObject.SetActive(activate && schedule.IsActive(index));
                if (schedule.IsActive(index)) active.Add(index);
                ApplyName(cell);
                cell.Failures = 0; retained = true;
            }
            catch (Exception e)
            {
                if (!closed)
                {
                    cell.Failures++; cell.NextRetry = Time.unscaledTime + Mathf.Min(15f, cell.Failures * 3f);
                    if (cell.Failures == 1 || cell.Failures == 3)
                        Debug.LogWarning("[SkyIslandPatrol] SPAWN_FAILED slot=" + cell.Slot.Id + " reason=" + e.Message);
                }
            }
            finally
            {
                if (!retained)
                {
                    if (generation == schedule.Generation && schedule.IsSpawned(index)) MarkDefeated(index, generation);
                    else schedule.AbortSpawn(index, generation, cell.Failures < 3);
                    if (created != null) { created.gameObject.SetActive(false); UnityEngine.Object.Destroy(created.gameObject); }
                    if (!presetOwned && clone != null) UnityEngine.Object.Destroy(clone, .1f);
                }
            }
        }

        private static string NameOf(string region)
        {
            switch (region)
            {
                case "A": return L10n.T("港口拾荒者", "Harbor Scavenger");
                case "B": return L10n.T("集市巡守", "Market Guard");
                case "C": return L10n.T("谷仓守卫", "Granary Sentry");
                case "D": return L10n.T("林地哨兵", "Forest Scout");
                case "E": return L10n.T("风口猎手", "Windbreak Hunter");
                case "F": return L10n.T("旧寺卫兵", "Temple Guard");
                case "G": return L10n.T("星工守卫", "Starworks Guard");
                case "H": return L10n.T("钟庭禁卫", "Belltower Guard");
                case "S1": return L10n.T("池边巡守", "Poolside Guard");
                case "S2": return L10n.T("瞭台哨兵", "Lookout Sentry");
                case "S3": return L10n.T("远寺巡守", "Temple Outrider");
                case "S4": return L10n.T("星台卫兵", "Observatory Guard");
                default: return L10n.T("群岛巡守", "Island Guard");
            }
        }
        private static void ApplyName(Cell cell)
        {
            if (cell.Character == null) return;
            NPCNameTagHelper.RegisterOriginalHealthBarName(cell.Character.transform, NameOf(cell.Slot.RegionId), 2.2f, "[SkyIslandPatrol]");
        }

        internal void MarkDefeated(int index, int generation)
        {
            if (closed || index < 0 || index >= cells.Length || generation != cells[index].SpawnGeneration) return;
            schedule.MarkDefeated(index); active.Remove(index);
        }

        internal bool HasLivingEnemiesWithin(Vector3 position, float radius)
        {
            float squared = radius * radius;
            for (int n = 0; n < active.Count; n++)
            {
                CharacterMainControl character = cells[active[n]].Character;
                if (character != null && character.Health != null && !character.Health.IsDead
                    && (character.transform.position - position).sqrMagnitude <= squared) return true;
            }
            return false;
        }

        internal int PlannedFor(string region)
        {
            int count = 0; foreach (Cell cell in cells) if (cell.Slot.RegionId == region) count++; return count;
        }

        /// <summary>F3 只读：预算与真实 activeSelf 对照，装配失败不以无敌人或 SKIP 掩盖。</summary>
        internal bool ValidateRuntime(out string metrics, out string reason)
        {
            int actual = 0, created = 0, defeated = 0, failed = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                Cell cell = cells[i];
                if (cell.Failures >= 3) failed++;
                if (schedule.IsDefeated(i)) defeated++;
                if (cell.Character == null || cell.Character.Health == null || cell.Character.Health.IsDead) continue;
                created++;
                if (cell.Character.gameObject.activeInHierarchy) actual++;
            }
            metrics = "planned=" + cells.Length + ",A=" + PlannedFor("A") + ",B=" + PlannedFor("B")
                + ",active=" + schedule.ActiveCount + ",actual_active=" + actual + ",pending=" + schedule.PendingCount
                + ",limit=" + SkyIslandPatrolRules.ActiveLimit + ",created=" + created + ",defeated=" + defeated
                + ",failed=" + failed + ",ticks=" + tickCount + ",source=" + DataSource;
            reason = closed ? "patrol_owner_closed" : tickCount == 0 ? "patrol_owner_not_ticked"
                : PlannedFor("A") < 6 || PlannedFor("B") < 8 ? "entry_island_density_missing"
                : schedule.ActiveCount + schedule.PendingCount > SkyIslandPatrolRules.ActiveLimit ? "patrol_budget_exceeded"
                : actual != schedule.ActiveCount ? "patrol_activation_differs_from_schedule"
                : failed > 0 ? "fixed_patrol_spawn_failed" : null;
            return reason == null;
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true; schedule.Close(); active.Clear();
            foreach (Cell cell in cells)
            {
                if (cell.Life != null) cell.Life.Unbind();
                if (cell.Character != null)
                {
                    NPCNameTagHelper.UnregisterOriginalHealthBarName(cell.Character.transform);
                    cell.Character.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(cell.Character.gameObject);
                }
                cell.Character = null; cell.Life = null;
            }
        }
    }

    /// <summary>每个角色持有自己的 preset 与可退订死亡回调，远区停用不触发死亡。</summary>
    internal sealed class SkyIslandPatrolLife : MonoBehaviour
    {
        private SkyIslandPatrols owner;
        private Health health;
        private CharacterRandomPreset preset;
        private int index, generation;
        private bool subscribed;
        internal void OwnPreset(CharacterRandomPreset value) { preset = value; }
        internal void Bind(SkyIslandPatrols service, int slot, int stamp, Health value)
        {
            owner = service; index = slot; generation = stamp; health = value;
            if (health == null) throw new InvalidOperationException("巡守生命订阅未就绪");
            if (!subscribed) { health.OnDeadEvent.AddListener(OnDead); subscribed = true; }
            if (health.IsDead) OnDead(default(DamageInfo));
        }
        private void OnDead(DamageInfo damage) { if (owner != null) owner.MarkDefeated(index, generation); }
        internal void Unbind()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false; owner = null; health = null;
        }
        private void OnDestroy()
        {
            OnDead(default(DamageInfo)); Unbind();
            NPCNameTagHelper.UnregisterOriginalHealthBarName(transform);
            if (preset != null) Destroy(preset, .1f);
            preset = null;
        }
    }
}
