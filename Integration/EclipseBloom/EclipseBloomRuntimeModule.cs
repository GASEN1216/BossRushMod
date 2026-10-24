using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>37 号普通探索的单次遭遇。名额属于关卡 owner，代际令牌覆盖异步生成。</summary>
    internal sealed class EclipseBloomRuntimeModule : BossRushRuntimeModuleBase
    {
        internal static EclipseBloomRuntimeModule Current { get; private set; }
        internal static bool IsEncounterActive { get { return Current != null && Current.active; } }
        internal static void NotifyOwnedBossDefeated(CharacterMainControl defeated)
        {
            EclipseBloomRuntimeModule current = Current;
            if (current == null || defeated == null || current.boss != defeated
                || current.spawning || !current.IsCurrent(current.generation)) return;
            EclipseBloomIntroduction.NotifyDefeated();
        }
        public override string ModuleName { get { return "EclipseBloom"; } }
        private ModBehaviour owner;
        private CharacterMainControl boss;
        private LevelManager levelOwner;
        private Scene levelScene;
        private Scene encounterScene;
        private int generation;
        private int candidateIndex;
        private bool active;
        private bool spawning;
        private bool consumed;
        private bool relaxedPlacement;
        private bool encounterRolled, encounterAllowed;
        /// <summary>完成引导后的随机遇敌掷点；离线夹具可替换成定值。</summary>
        internal static Func<float> EncounterRoll = () => UnityEngine.Random.Range(0f, 1f);
        private float retryAt;

        public override void OnAwake(ModBehaviour host)
        {
            owner = host;
            Current = this;
        }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            // 同一关卡的主/子场景、资源 additive 回调都不构成新出击。
            RefreshLevelOwner();
            if (active && !IsCurrent(generation)) Cleanup();
        }

        internal bool OtherModeActive()
        {
            if (owner == null || owner.IsActive || owner.IsCampaignFinalBossActive
                || owner.CampaignArenaActiveForRuntime || WavesArenaRuntimeModule.ArenaPlanned
                || WavesArenaRuntimeModule.ArenaActive
                || BossRushMapSelectionHelper.GetPendingEntryKind() != BossRushPendingEntryKind.None
                || owner.CampaignModeDActiveForRuntime || owner.CampaignModeEActiveForRuntime
                || owner.CampaignModeFActiveForRuntime || owner.CampaignModeGActiveForRuntime) return true;
            if (owner.CampaignZombieRunForRuntime != null
                && owner.CampaignZombieRunForRuntime.LifecyclePhase != ZombieModeLifecyclePhase.None) return true;
            return owner.ModeHRuntime != null && owner.ModeHRuntime.RunState != null
                && owner.ModeHRuntime.RunState.Lifecycle != ModeHLifecycle.None;
        }

        private bool RefreshLevelOwner()
        {
            Scene current;
            if (!EclipseBloomEncounterPlacement.TryGetLevelOwnerScene(out current))
            {
                if (levelOwner != null || levelScene.IsValid())
                {
                    Cleanup();
                    levelOwner = null;
                    levelScene = default(Scene);
                    consumed = false;
                    candidateIndex = 0;
                    relaxedPlacement = false;
                    encounterRolled = false;
                    retryAt = 0f;
                }
                return false;
            }
            if (levelOwner != LevelManager.Instance || levelScene.handle != current.handle)
            {
                Cleanup();
                levelOwner = LevelManager.Instance;
                levelScene = current;
                consumed = false;
                candidateIndex = 0;
                relaxedPlacement = false;
                encounterRolled = false;
                retryAt = 0f;
            }
            return true;
        }

        private bool CanRunEncounter()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            return owner != null && Current == this && player != null && player.Health != null && !player.Health.IsDead
                && EclipseBloomIntroduction.CanSpawnEncounter && !OtherModeActive()
                && EclipseBloomEncounterPlacement.IsCurrent(encounterScene, levelScene, levelOwner);
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!RefreshLevelOwner()) return;
            if (active)
            {
                if (!spawning && boss != null && boss.Health != null && boss.Health.IsDead)
                {
                    // 死亡与掉落已归角色 owner；本次出击的成功名额不因清理或切子场景返还。
                    NotifyOwnedBossDefeated(boss);
                    boss = null;
                    active = false;
                    generation++;
                    return;
                }
                if (!IsCurrent(generation)) { Cleanup(); return; }
                if (!spawning && (boss == null || boss.Health == null)) Cleanup();
                return;
            }
            if (consumed || SceneLoader.IsSceneLoading || EclipseBloomEncounterPlacement.IsLoadingSubScene
                || !LevelManager.AfterInit || OtherModeActive()
                || !EclipseBloomIntroduction.CanSpawnEncounter || Time.unscaledTime < retryAt) return;
            Scene target;
            if (!EclipseBloomEncounterPlacement.TryGetExplorationScene(out target)) return;
            // 引导任务进行中必刷（任务目标）；完成之后每趟 37 号只按概率遇到一次，不再进图必刷。
            if (!EclipseBloomIntroduction.EncounterIsQuestObjective)
            {
                if (!encounterRolled)
                {
                    encounterRolled = true;
                    encounterAllowed = EncounterRoll() < EclipseBloomTuning.RepeatEncounterChance;
                }
                if (!encounterAllowed) return;
            }
            encounterScene = target;
            if (!CanRunEncounter()) return;
            Vector3 position;
            bool found = EclipseBloomEncounterPlacement.TryResolveCandidate(candidateIndex,
                CharacterMainControl.Main.transform.position, relaxedPlacement, out position);
            candidateIndex = (candidateIndex + 1) % EclipseBloomEncounterPlacement.CandidateCount;
            // 一帧最多验证一个位置；一轮失败后放宽一档并退避，等待导航或玩家位置变化。
            retryAt = Time.unscaledTime + (candidateIndex == 0 ? 2f : 0.2f);
            if (!found && candidateIndex == 0)
            {
                ModBehaviour.DevLog("[EclipseBloom] 37 号目标圈本轮 " + EclipseBloomEncounterPlacement.CandidateCount
                    + " 个候选都未通过净空检查" + (relaxedPlacement ? "（已放宽）" : "，下一轮放宽门槛"));
                relaxedPlacement = true;
            }
            if (!found) return;
            active = true;
            spawning = true;
            int token = ++generation;
            owner.ShowMessage(L10n.T("蚀日花冠正在绽放。花瓣护着核心，先击碎花瓣再打日核。",
                "The Eclipse Bloom awakens. Its petals shield the core: break them before striking the sun."));
            Spawn(token, position, target).Forget();
        }

        private bool IsCurrent(int token)
        {
            return active && token == generation && Current == this && CanRunEncounter();
        }

        private async UniTask Spawn(int token, Vector3 position, Scene target)
        {
            CharacterMainControl created = await EclipseBloomBoss.SpawnAsync(owner, position, () => IsCurrent(token), target);
            // 旧工厂结果只能回收自己的角色，不能清掉新代际的 active/spawning/名额。
            if (!IsCurrent(token))
            {
                if (created != null) UnityEngine.Object.Destroy(created.gameObject);
                return;
            }
            boss = created;
            spawning = false;
            if (created != null)
            {
                consumed = true;
                return;
            }
            Cleanup();
            retryAt = Time.unscaledTime + 2f;
        }

        private void Cleanup()
        {
            generation++;
            active = false;
            spawning = false;
            if (boss != null)
            {
                EclipseBloomAbilityController controller = boss.GetComponent<EclipseBloomAbilityController>();
                if (controller != null) controller.StopBattle(false);
                if (boss.Health == null || !boss.Health.IsDead) UnityEngine.Object.Destroy(boss.gameObject);
            }
            boss = null;
        }

        public override void OnDestroy()
        {
            Cleanup();
            // 部位减伤登记（花冠护核、宝箱铁皮）握着 DamageReceiver 引用；战斗 owner 正常会撤销，探索模块卸载时兜底整表清空。
            ReceiverDamageScaling.ResetStaticCaches();
            if (Current == this) Current = null;
            owner = null;
            levelOwner = null;
        }
    }
}
