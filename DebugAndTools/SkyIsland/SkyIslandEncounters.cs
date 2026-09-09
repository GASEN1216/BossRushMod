using System;
using System.Collections.Generic;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>COMPAT：区域接近生成，沿用官方装备、伤害、经验；地图拥有角色、preset 与战利品。</summary>
    internal sealed class SkyIslandEncounters : IDisposable
    {
        private sealed class Encounter
        {
            internal string Id;
            internal Transform Marker;
            internal int Count;
            internal SkyIslandEncounterDefinition Definition;
            internal bool Manual, Started, Cleared, Spawning;
            internal float RetryAt;
            internal SkyIslandEnemyRecord[] Actors;
            internal bool AllDead
            {
                get
                {
                    if (!Started || Spawning) return false;
                    foreach (SkyIslandEnemyRecord actor in Actors) if (!actor.Died) return false;
                    return true;
                }
            }
        }
        private readonly List<Encounter> encounters = new List<Encounter>();
        private readonly List<CharacterRandomPreset> sources = new List<CharacterRandomPreset>();
        private readonly GameObject root;
        private readonly CharacterMainControl player;
        private readonly GraphMask mask;
        private readonly int groundMask;
        private readonly Func<bool> valid;
        private readonly Func<string, bool> completed;
        private readonly Action<string> cleared;
        private readonly Action<string, bool> report;
        private readonly Action<Vector3> stormDefeated;
        private bool closed;
        private float nextTick;
        internal string ContentSource { get; private set; }

        /// <summary>内容表由会话加载一次后传入；同一次进岛不重复解析 World.json。</summary>
        internal SkyIslandEncounters(GameObject root, CharacterMainControl player, GraphMask mask, int groundMask,
            SkyIslandContentData content, Func<bool> valid, Func<string, bool> completed, Action<string> cleared,
            Action<string, bool> report, Action<Vector3> onStormDefeated)
        {
            if (content == null) throw new ArgumentNullException("content");
            this.root = root; this.player = player; this.mask = mask; this.groundMask = groundMask;
            this.valid = valid; this.completed = completed; this.cleared = cleared; this.report = report;
            this.stormDefeated = onStormDefeated;
            // 一次加载时缓存，不在每帧/每次遭遇扫描全局资源。
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                if (preset != null && !preset.isBoss && !preset.isZombie && preset.team == Teams.scav &&
                    preset.name.IndexOf("Dummy", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !preset.name.StartsWith("BossRush_", StringComparison.Ordinal)) sources.Add(preset);
            sources.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (sources.Count == 0) throw new InvalidOperationException("天空岛没有可用的官方战斗角色资源");
            ContentSource = content.Source;
            foreach (SkyIslandEncounterDefinition definition in content.Encounters) Add(definition);
        }

        private void Add(SkyIslandEncounterDefinition definition)
        {
            Transform marker = root.transform.Find(definition.Marker);
            if (marker == null) throw new InvalidOperationException("遭遇点位缺失：" + definition.Marker);
            var actors = new SkyIslandEnemyRecord[definition.Count];
            for (int i = 0; i < definition.Count; i++) actors[i] = new SkyIslandEnemyRecord();
            encounters.Add(new Encounter { Id = definition.Id, Marker = marker, Count = definition.Count,
                Manual = definition.Manual, Definition = definition, Actors = actors });
        }

        internal bool IsBusy(string id)
        {
            Encounter encounter = encounters.Find(e => e.Id == id);
            return encounter != null && encounter.Started && !encounter.Cleared && !encounter.AllDead;
        }

        /// <summary>
        /// 本局还能被「清场」记账的遭遇组数。
        ///
        /// 只数**自动**组：手动组（折翎 / 钟守 / 噬风）要额外的剧情前置，本局未必开得了，
        /// 算进去会高估。宁可低估——低估只是少派一单，高估会派出做不完的委托。
        ///
        /// 已存档清场的组在 `Tick` 里被直接短路成 `Cleared`，`cleared()` 回调根本不会触发，
        /// 所以它们对委托进度是彻底不可用的，必须排除在可完成量之外。
        /// </summary>
        internal int RemainingClearable
        {
            get
            {
                int count = 0;
                foreach (Encounter encounter in encounters)
                    if (!encounter.Manual && !encounter.Cleared && !completed(encounter.Id)) count++;
                return count;
            }
        }

        internal bool HasLivingEnemies
        {
            get
            {
                foreach (Encounter encounter in encounters)
                    if (encounter.Started && !encounter.Cleared && !encounter.AllDead) return true;
                return false;
            }
        }

        /// <summary>D/G 的装置要求整个区域两组均提交；其余 ID 返回单组的已接受结果。</summary>
        internal bool IsCleared(string id)
        {
            if (id == "D" || id == "G") return completed(id) && completed(id + "_02");
            return encounters.Exists(e => e.Id == id) && completed(id);
        }

        internal bool BeginChallenge(string id)
        {
            if (closed || !valid()) return false;
            Encounter encounter = encounters.Find(e => e.Id == id && e.Manual);
            if (encounter == null || encounter.Cleared || completed(id) || encounter.Started ||
                Time.time < encounter.RetryAt || Vector3.Distance(player.transform.position, encounter.Marker.position) > 90 ||
                encounters.Exists(e => e.Spawning) || CountActiveActors() + encounter.Count > 12) return false;
            Spawn(encounter);
            return true;
        }

        internal void Tick()
        {
            if (closed || !valid() || Time.time < nextTick) return;
            nextTick = Time.time + 0.25f;
            int active = 0;
            foreach (Encounter encounter in encounters)
            {
                if (encounter.Cleared) continue;
                if (!encounter.Started && completed(encounter.Id)) { encounter.Cleared = true; continue; }
                if (!encounter.Started) continue;
                foreach (SkyIslandEnemyRecord actor in encounter.Actors)
                {
                    // 死亡事实留在普通对象中，官方销毁角色后仍有效；活体丢失只补该槽，不重刷已死角色。
                    if (!actor.Died && actor.Life != null) active++;
                }
                if (encounter.AllDead && Time.time >= encounter.RetryAt)
                {
                    try
                    {
                        cleared(encounter.Id);
                        // 委托可能因写屏障/暂时失败未接受；保持物理清场事实并重复投递，不能重生敌群。
                        if (completed(encounter.Id))
                        {
                            encounter.Cleared = true;
                            report("航路已清理 · " + encounter.Id, false);
                        }
                    }
                    catch (Exception e) { report("清场记录提交失败，将重试：" + e.Message, true); }
                    encounter.RetryAt = Time.time + 1f;
                }
            }
            // 同时最多 12 名活跃敌人，一次只启动一组异步生成。既有遭遇只补失去 owner 的未死槽。
            if (encounters.Exists(e => e.Spawning)) return;
            foreach (Encounter encounter in encounters)
            {
                if ((encounter.Manual && !encounter.Started) || encounter.Cleared || Time.time < encounter.RetryAt) continue;
                int missing = CountMissing(encounter);
                if (missing == 0 || active + missing > 12) continue;
                if ((player.transform.position - encounter.Marker.position).sqrMagnitude > 85 * 85) continue;
                Spawn(encounter);
                break;
            }
        }

        private async void Spawn(Encounter encounter)
        {
            encounter.Started = encounter.Spawning = true;
            try
            {
                for (int i = 0; i < encounter.Count; i++)
                {
                    if (closed || !valid()) return;
                    SkyIslandEnemyRecord actor = encounter.Actors[i];
                    if (actor.Died || actor.Life != null) continue;
                    Vector3 point = FindGround(encounter.Marker, i);
                    CharacterRandomPreset clone = UnityEngine.Object.Instantiate(sources[i % sources.Count]);
                    clone.name = "BossRush_SkyIsland_" + encounter.Id;
                    // 正式独立出击沿用官方 CharacterMainControl.OnDead 箱子、经验与魂语义。
                    clone.dropBoxOnDead = true;
                    clone.setActiveByPlayerDistance = false;
                    CharacterMainControl created = null;
                    bool retained = false, presetOwnedByCharacter = false;
                    try
                    {
                        // 在途 preset 仅由当前 async 栈拥有，场景退出无权提前销毁。
                        created = await clone.CreateCharacterAsync(point, Vector3.forward, -1, null, false);
                        if (created == null) throw new InvalidOperationException("官方角色创建失败");
                        SkyIslandEnemyLife life = created.gameObject.AddComponent<SkyIslandEnemyLife>();
                        life.OwnPreset(clone);
                        presetOwnedByCharacter = true;
                        if (closed || !valid() || root == null) return;
                        created.transform.SetParent(root.transform, true);
                        Seeker[] seekers = created.GetComponentsInChildren<Seeker>(true);
                        AICharacterController ai = created.GetComponentInChildren<AICharacterController>();
                        if (seekers.Length == 0 || ai == null) throw new InvalidOperationException("角色缺少战斗 AI / 导航");
                        foreach (Seeker seeker in seekers) { seeker.CancelCurrentPathRequest(); seeker.graphMask = mask; }
                        SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created);
                        created.SetTeam(Teams.wolf);
                        SkyIslandEnemyTier tier = encounter.Definition.TierFor(i);
                        SkyIslandEnemyTiers.ApplyAi(ai, tier);
                        ApplyIdentity(created, encounter, i, tier);
                        life.Bind(created, actor);
                        actor.Life = life;
                        retained = true;
                    }
                    finally
                    {
                        if (!retained && created != null) { created.gameObject.SetActive(false); UnityEngine.Object.Destroy(created.gameObject); }
                        if (!presetOwnedByCharacter && clone != null) UnityEngine.Object.Destroy(clone, created != null ? 0.1f : 0f);
                    }
                }
            }
            catch (Exception e)
            {
                encounter.RetryAt = Time.time + 10;
                if (!closed) report("天空岛遭遇准备失败，10 秒后可重试：" + e.Message, true);
            }
            finally { encounter.Spawning = false; }
        }

        /// <summary>
        /// 身份层：具名剧情对手保留自己的脸与名字（只吃数值），其余按档次装饰。
        /// 噬风的相位编排挂在带队者身上，死亡回调由它自己派发，不与清场记账争 owner。
        /// </summary>
        private void ApplyIdentity(CharacterMainControl created, Encounter encounter, int index, SkyIslandEnemyTier tier)
        {
            if (encounter.Id == "Zheling" && index == 0)
            {
                SkyIslandResidents.ApplyBattleFace(created, "sky_zheling");
                SkyIslandEnemyTiers.ApplyStoryChampion(created, "zheling", "折翎", "Zheling");
                return;
            }
            if (encounter.Id == "BellKeeper" && index == 0)
            {
                SkyIslandResidents.ApplyBattleFace(created, "sky_bellkeeper");
                SkyIslandEnemyTiers.ApplyStoryChampion(created, "bellkeeper", "失控的守钟装置", "Runaway Bell Engine");
                return;
            }
            SkyIslandEnemyTiers.Apply(created, tier);
            if (tier != SkyIslandEnemyTier.Storm) return;
            Transform bossTransform = created.transform;
            created.gameObject.AddComponent<SkyIslandStormBoss>().Bind(created, valid, report, delegate
            {
                if (closed || stormDefeated == null || bossTransform == null) return;
                stormDefeated(bossTransform.position);
            });
        }

        private static int CountMissing(Encounter encounter)
        {
            int count = 0;
            foreach (SkyIslandEnemyRecord actor in encounter.Actors) if (!actor.Died && actor.Life == null) count++;
            return count;
        }
        private int CountActiveActors()
        {
            int count = 0;
            foreach (Encounter encounter in encounters)
                foreach (SkyIslandEnemyRecord actor in encounter.Actors)
                    if (!actor.Died && actor.Life != null) count++;
            return count;
        }

        private Vector3 FindGround(Transform marker, int index)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float angle = (index * 120 + attempt * 30) * Mathf.Deg2Rad;
                Vector3 point = marker.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (index == 0 ? 0 : 3);
                RaycastHit hit;
                if (Physics.Raycast(point + Vector3.up * 2, Vector3.down, out hit, 4, groundMask, QueryTriggerInteraction.Ignore) &&
                    hit.transform.IsChildOf(root.transform) && !Physics.CheckCapsule(hit.point + Vector3.up * 0.6f,
                    hit.point + Vector3.up * 1.5f, 0.5f, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore))
                    return hit.point + Vector3.up * 0.1f;
            }
            throw new InvalidOperationException("遭遇落点不可站立：" + marker.name);
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            foreach (Encounter encounter in encounters)
                foreach (SkyIslandEnemyRecord actor in encounter.Actors)
                    if (actor.Life != null) { actor.Life.gameObject.SetActive(false); UnityEngine.Object.Destroy(actor.Life.gameObject); }
        }
    }

    internal sealed class SkyIslandEnemyRecord
    {
        internal bool Died;
        internal SkyIslandEnemyLife Life;
    }

    internal sealed class SkyIslandEnemyLife : MonoBehaviour
    {
        private Health health;
        private CharacterRandomPreset preset;
        private SkyIslandEnemyRecord record;
        private bool subscribed;
        internal void OwnPreset(CharacterRandomPreset value) { preset = value; }
        internal void Bind(CharacterMainControl value, SkyIslandEnemyRecord state)
        {
            record = state; health = value.Health;
            if (health == null) throw new InvalidOperationException("遭遇角色生命组件缺失");
            record.Died = health.IsDead;
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
        }
        private void OnDead(DamageInfo damage) { if (record != null) record.Died = true; }
        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            // CharacterMainControl / 模型的 OnDestroy 仍可能读 preset；角色组件清理完成后再释放。
            if (preset != null) Destroy(preset, 0.1f);
            preset = null;
            record = null;
        }
    }
}
