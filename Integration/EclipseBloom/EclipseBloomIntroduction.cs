using System;
using Duckov.MiniMaps;
using Duckov.Quests;
using Saves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>Jeff 官方任务页的花冠引导。Mod 分槽事实为权威，死亡即撤销本次接取。</summary>
    internal sealed class EclipseBloomIntroduction : BossRushRuntimeModuleBase, IOfficialQuestClient
    {
        internal const int QuestId = 590215;
        private const string NameKey = "BossRush_EclipseBloom_Introduction_Name";
        private const string DescriptionKey = "BossRush_EclipseBloom_Introduction_Description";
        private const string ObjectiveKey = "BossRush_EclipseBloom_Introduction_Objective";
        private static EclipseBloomIntroduction current;
        private ModBehaviour owner;
        private OfficialQuestProjection projection;
        private EclipseBloomIntroductionPersistence persistence;
        private EclipseBloomIntroductionData data;
        private Health playerHealth;
        private QuestGiver jeff;
        private SimplePointOfInterest marker;
        private int openedSlot = -1;
        private bool abandonPending, committing, deliveryActive;
        private float nextJeffAttempt, nextMarkerAttempt;

        public override string ModuleName { get { return "EclipseBloomIntroduction"; } }
        /// <summary>接了引导、还没击败：这趟 37 号的花冠是任务目标，必须出现。</summary>
        internal static bool EncounterIsQuestObjective
        {
            get
            {
                return current != null && current.EnsureData() && !current.abandonPending
                    && current.data.Accepted && !current.data.Defeated && !current.data.Completed;
            }
        }

        internal static bool CanSpawnEncounter
        {
            get
            {
                return current != null && current.EnsureData() && current.persistence.Writable && !current.abandonPending
                    && (current.data.Completed || (current.data.Accepted && !current.data.Defeated));
            }
        }

        public override void OnAwake(ModBehaviour host)
        {
            owner = host;
            current = this;
            persistence = new EclipseBloomIntroductionPersistence(OnSlotChanged, BeforeCollectSaveData, CanFlushFacts);
            projection = host != null && host.OfficialQuestRuntime != null ? host.OfficialQuestRuntime.Projection : null;
            InjectLocalizations();
            if (projection != null) projection.Register(BuildBinding());
            else Debug.LogWarning("[EclipseBloomIntroduction] 官方任务核心尚未准备，花冠引导未注册");
            RefreshPlayerDeathSubscription();
        }

        internal static void InjectLocalizations()
        {
            LocalizationHelper.InjectLocalization(NameKey, L10n.T("雪原上的蚀日", "An Eclipse in the Snow"));
            LocalizationHelper.InjectLocalization(DescriptionKey, L10n.T(
                "杰夫发现 37 号实验区有一株异常花冠。普通出击前往地图圈出的区域，击败蚀日花冠，再回基地向杰夫报告。残日盾片归你，不用交走。死亡会自动放弃本次任务，可回基地重新接取。",
                "Jeff has located an unusual bloom in Test Area 37. Take a normal raid to the marked area, defeat the Eclipse Bloom, and report to Jeff at base. Keep the Residual Sun Shield. Death abandons this attempt; speak to Jeff again at base to retry."));
            LocalizationHelper.InjectLocalization(ObjectiveKey, L10n.T("蚀日花冠出没区域", "Eclipse Bloom area"));
        }

        private OfficialQuestBinding BuildBinding()
        {
            return new OfficialQuestBinding
            {
                QuestId = QuestId, GiverId = (int)QuestGiverID.Jeff,
                ObjectName = "BossRush_EclipseBloom_Introduction_OfficialQuest",
                NameKey = NameKey, DescriptionKey = DescriptionKey, Client = this,
                CanOffer = CanOffer, CanDeliver = CanDeliver,
                IsAccepted = () => data != null && data.Accepted && !abandonPending,
                IsDelivered = () => data != null && data.Completed,
                RewardPaid = () => data != null && data.Completed,
                StateStamp = () => data == null ? 0 : (data.Accepted && !abandonPending ? 1 : 0)
                    | (data.Defeated && !abandonPending ? 2 : 0) | (data.Completed ? 4 : 0),
                Accept = Accept, Deliver = Deliver, BeginDelivery = BeginDelivery, EndDelivery = EndDelivery,
                DeliverBlocked = () => L10n.T("先在普通出击中击败 37 号实验区的蚀日花冠，再回基地找杰夫。",
                    "Defeat the Eclipse Bloom during a normal raid to Test Area 37, then return to Jeff at base."),
                Tasks = new[] { new OfficialQuestTaskBinding
                {
                    TaskId = 1, Done = () => data != null && (data.Completed || data.Defeated) && !abandonPending,
                    Description = () => data != null && (data.Completed || data.Defeated) && !abandonPending
                        ? L10n.T("回基地向杰夫报告；残日盾片留在你身上。", "Report to Jeff at base. Keep the Residual Sun Shield.")
                        : L10n.T("普通出击前往 37 号实验区，击败蚀日花冠。", "Defeat the Eclipse Bloom in a normal raid to Test Area 37."),
                    ExtraHint = () => L10n.T("进入 37 后打开地图查看目标圈。死亡自动放弃，可回基地重新接取。",
                        "Open the map in Test Area 37 to find the objective ring. Death abandons this attempt; accept it again at base.")
                } }
            };
        }

        private static bool InBase()
        {
            LevelManager level = LevelManager.Instance;
            return level != null && level.IsBaseLevel && !SceneLoader.IsSceneLoading && LevelManager.AfterInit
                && EclipseBloomEncounterPlacement.IsLoaded(level.gameObject.scene);
        }
        private static bool PlayerAlive()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            return player != null && player.Health != null && !player.Health.IsDead;
        }
        private bool CanOffer()
        {
            return EnsureData() && persistence.Writable && !abandonPending && !data.Accepted && !data.Completed
                && InBase() && PlayerAlive();
        }
        private bool CanDeliver()
        {
            return EnsureData() && persistence.Writable && !abandonPending && data.Accepted && data.Defeated
                && !data.Completed && InBase() && PlayerAlive();
        }
        private bool Accept(out string message)
        {
            message = L10n.T("花冠任务暂时无法接取，请稍后再试。", "The bloom quest is unavailable. Please try again shortly.");
            if (!CanOffer() || !Commit(new EclipseBloomIntroductionData { Accepted = true })) return false;
            message = L10n.T("普通出击前往 37 号实验区，打开地图寻找目标圈。", "Take a normal raid to Test Area 37 and open the map to find the objective ring.");
            return true;
        }
        private bool Deliver(out string message)
        {
            message = L10n.T("还不能交付这项任务。", "This quest is not ready to turn in.");
            if (!CanDeliver() || !Commit(new EclipseBloomIntroductionData { Completed = true })) return false;
            message = L10n.T("调查完成。今后普通出击前往 37 号实验区，仍可遇到蚀日花冠。", "Investigation complete. The Eclipse Bloom can appear in future normal raids to Test Area 37.");
            return true;
        }
        private bool BeginDelivery(Func<bool> collectAssets, out string message)
        {
            message = L10n.T("花冠任务暂时无法交付，请稍后再试。", "The bloom quest cannot be turned in yet. Please try again shortly.");
            if (deliveryActive || !CanDeliver()) return false;
            // 本任务无现金、奖品或提交物；只锁住自己的事实采集，不额外保存玩家战利品。
            deliveryActive = true;
            message = null;
            return true;
        }
        private void EndDelivery(bool committed)
        {
            deliveryActive = false;
            if (persistence != null) persistence.RequestFlush();
        }
        private bool Commit(EclipseBloomIntroductionData value)
        {
            if (persistence == null || committing) return false;
            bool stored;
            committing = true;
            try { stored = persistence.Store(value, !deliveryActive); }
            finally { committing = false; }
            if (!stored) return false;
            data = value;
            if (projection != null) projection.NotifyProgressChanged(this);
            RefreshMapMarker();
            return true;
        }
        internal static void NotifyDefeated()
        {
            EclipseBloomIntroduction value = current;
            Scene target;
            if (value == null || !value.EnsureData() || value.abandonPending || !PlayerAlive()
                || !value.data.Accepted || value.data.Defeated || value.data.Completed
                || !EclipseBloomEncounterPlacement.TryGetExplorationScene(out target)
                || EclipseBloomRuntimeModule.Current == null || EclipseBloomRuntimeModule.Current.OtherModeActive()) return;
            if (value.Commit(new EclipseBloomIntroductionData { Accepted = true, Defeated = true }) && value.owner != null)
                value.owner.ShowMessage(L10n.T("蚀日花冠已击败，回基地向杰夫报告。残日盾片不用交走。",
                    "The Eclipse Bloom is defeated. Report to Jeff at base; keep the Residual Sun Shield."));
        }

        private bool EnsureData()
        {
            if (persistence == null || !LevelManager.AfterInit) return false;
            int slot;
            try { slot = SavesSystem.CurrentSlot; }
            catch (Exception) { return false; }
            if (slot < 0) return false;
            if (data == null || openedSlot != slot)
            {
                EclipseBloomIntroductionData loaded = persistence.Load();
                data = loaded;
                openedSlot = slot;
            }
            return data != null && !persistence.HasWriteBarrier;
        }
        private void OnSlotChanged()
        {
            data = null;
            openedSlot = -1;
            abandonPending = false;
            deliveryActive = false;
            jeff = null;
            nextJeffAttempt = 0f;
            RemoveMapMarker();
        }
        private bool HasCurrentData()
        {
            if (data == null || openedSlot < 0) return false;
            try { return openedSlot == SavesSystem.CurrentSlot; }
            catch (Exception) { return false; }
        }
        private void RefreshPlayerDeathSubscription()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Health next = player == null ? null : player.Health;
            if (playerHealth == next) return;
            // 死亡切图可能先换主角，再到模块回调；换订阅前消费旧主角的死亡事实。
            if (playerHealth != null && playerHealth.IsDead && HasCurrentData()) AbandonOnDeath();
            if (playerHealth != null) playerHealth.OnDeadEvent.RemoveListener(OnPlayerDied);
            playerHealth = next;
            if (playerHealth != null) playerHealth.OnDeadEvent.AddListener(OnPlayerDied);
        }
        private void OnPlayerDied(DamageInfo info)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && player.Health == playerHealth && HasCurrentData()) AbandonOnDeath();
        }
        private void AbandonOnDeath()
        {
            // 已打开的本槽事实在切图/写屏障期间仍可撤销内存投影，不依赖 AfterInit 再次为真。
            if ((!HasCurrentData() && !EnsureData()) || data.Completed || (!data.Accepted && !abandonPending)) return;
            bool first = !abandonPending;
            abandonPending = true;
            // 先关闭内存中的遭遇和官方 accepted 投影，写失败仍保留重试义务。
            if (Commit(new EclipseBloomIntroductionData())) abandonPending = false;
            if (projection != null) projection.NotifyProgressChanged(this);
            RemoveMapMarker();
            if (first && owner != null) owner.ShowMessage(L10n.T("本次花冠任务已自动放弃。回基地找杰夫可重新接取。",
                "The bloom quest was abandoned. Speak to Jeff at base to accept it again."));
        }
        private bool BeforeCollectSaveData()
        {
            if (deliveryActive) return false;
            if (abandonPending || (playerHealth != null && playerHealth.IsDead)) AbandonOnDeath();
            return !abandonPending;
        }
        private bool CanFlushFacts() { return !deliveryActive && !abandonPending; }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            RefreshPlayerDeathSubscription();
            if (marker != null && !EclipseBloomEncounterPlacement.IsLoaded(marker.gameObject.scene)) RemoveMapMarker();
            if (!InBase()) jeff = null;
        }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            RefreshPlayerDeathSubscription();
            if (abandonPending || (playerHealth != null && playerHealth.IsDead)) AbandonOnDeath();
            if (persistence != null && !deliveryActive) persistence.Tick();
            if (!EnsureData()) return;
            if (InBase() && jeff == null && Time.unscaledTime >= nextJeffAttempt)
            {
                nextJeffAttempt = Time.unscaledTime + 1f;
                if (OfficialQuestGiverLocator.TryFind(QuestGiverID.Jeff, out jeff) && projection != null) projection.PrepareGiver(jeff);
            }
            RefreshMapMarker();
        }

        private void RefreshMapMarker()
        {
            Scene target;
            bool visible = data != null && data.Accepted && !data.Defeated && !data.Completed && !abandonPending
                && PlayerAlive() && !SceneLoader.IsSceneLoading && EclipseBloomRuntimeModule.Current != null
                && !EclipseBloomRuntimeModule.Current.OtherModeActive()
                && EclipseBloomEncounterPlacement.TryGetExplorationScene(out target);
            if (!visible) { RemoveMapMarker(); return; }
            if (!EclipseBloomEncounterPlacement.TryGetExplorationScene(out target)) return;
            if (marker != null && marker.gameObject.scene.handle == target.handle) return;
            RemoveMapMarker();
            if (Time.unscaledTime < nextMarkerAttempt) return;
            nextMarkerAttempt = Time.unscaledTime + 2f;
            try
            {
                marker = SimplePointOfInterest.Create(EclipseBloomEncounterPlacement.ObjectiveAnchor,
                    MapPointSceneResolver.Resolve(EclipseBloomEncounterPlacement.SceneName), ObjectiveKey, null, false);
                if (marker == null) return;
                EclipseBloomEncounterPlacement.BindToScene(marker.gameObject, target);
                marker.Color = BossRushUIColors.WarningText;
                marker.ScaleFactor = 1.35f;
                marker.IsArea = true;
                marker.AreaRadius = 22f;
            }
            catch (Exception e)
            {
                RemoveMapMarker();
                ModBehaviour.DevLog("[EclipseBloomIntroduction] 地图目标圈创建失败，将重试: " + e.Message);
            }
        }
        private void RemoveMapMarker()
        {
            if (marker != null) UnityEngine.Object.Destroy(marker.gameObject);
            marker = null;
        }
        string IOfficialQuestClient.LogTag { get { return "[EclipseBloomIntroduction]"; } }
        bool IOfficialQuestClient.Ready { get { return EnsureData() || (abandonPending && HasCurrentData()); } }
        int IOfficialQuestClient.Slot { get { return openedSlot; } }
        void IOfficialQuestClient.BeginTick()
        {
            RefreshPlayerDeathSubscription();
            if (abandonPending || (playerHealth != null && playerHealth.IsDead)) AbandonOnDeath();
        }
        void IOfficialQuestClient.EndTick(bool dirty) { if (dirty) OfficialQuestGiverLocator.RefreshMarker(jeff); }
        void IOfficialQuestClient.RefreshMarkers() { OfficialQuestGiverLocator.RefreshMarker(jeff); }
        public override void OnDestroy()
        {
            if (playerHealth != null && playerHealth.IsDead) AbandonOnDeath();
            if (playerHealth != null) playerHealth.OnDeadEvent.RemoveListener(OnPlayerDied);
            playerHealth = null;
            if (projection != null) projection.UnregisterClient(this);
            RemoveMapMarker();
            if (persistence != null) persistence.Dispose();
            persistence = null;
            projection = null;
            data = null;
            jeff = null;
            owner = null;
            if (current == this) current = null;
        }
    }
}
