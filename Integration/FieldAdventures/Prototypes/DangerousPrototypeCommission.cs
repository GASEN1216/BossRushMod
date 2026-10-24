using System;
using System.Collections.Generic;
using System.Threading;
using BossRush.Utils;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal sealed class DangerousPrototypeCommissionData
    {
        internal bool Completed;
    }

    /// <summary>
    /// 叮当的一次性委托「危险试用品」：好感 10 级后在基地的叮当身上挂出，接下即播剧情，对话结束即完成。
    /// 完成事实按存档槽保存；之后普通出击才会随机出现叮当的危险试验台（DangerousPrototypeRuntimeModule 读 IsUnlocked）。
    /// 不接官方 Quest：只有一句接下和一段对话，没有进行中状态，官方任务页反而多一层空壳。
    /// </summary>
    internal sealed class DangerousPrototypeCommission : BossRushRuntimeModuleBase
    {
        internal const int RequiredAffinityLevel = 10;
        private static DangerousPrototypeCommission current;
        private DangerousPrototypeCommissionPersistence persistence;
        private DangerousPrototypeCommissionData data;
        private ModBehaviour owner;
        private GoblinInteractable host;
        private DangerousPrototypeCommissionInteractable option;
        private CancellationTokenSource dialogue;
        private int openedSlot = -1;
        private float nextScan;
        private bool committing;

        public override string ModuleName { get { return "DangerousPrototypeCommission"; } }

        /// <summary>本存档已经接下（即完成）叮当的委托。</summary>
        internal static bool IsUnlocked
        {
            get { return current != null && current.EnsureData() && current.data.Completed; }
        }

        public override void OnAwake(ModBehaviour hostBehaviour)
        {
            owner = hostBehaviour;
            current = this;
            persistence = new DangerousPrototypeCommissionPersistence(OnSlotChanged, () => true, () => !committing);
        }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            DetachOption();
            nextScan = 0f;
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (persistence != null) persistence.Tick();
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 1f;
            try { SyncOption(); }
            catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototypeCommission] 选项同步失败: " + e.Message); }
        }

        internal bool CanAccept
        {
            get
            {
                LevelManager level = LevelManager.Instance;
                CharacterMainControl player = CharacterMainControl.Main;
                return dialogue == null && EnsureData() && persistence.Writable && !data.Completed
                    && level != null && level.IsBaseLevel && LevelManager.AfterInit && !SceneLoader.IsSceneLoading
                    && player != null && player.Health != null && !player.Health.IsDead
                    && AffinityManager.GetLevel(GoblinAffinityConfig.NPC_ID) >= RequiredAffinityLevel;
            }
        }

        private void SyncOption()
        {
            bool eligible = CanAccept;
            if (!eligible)
            {
                if (option != null && dialogue == null) DetachOption();
                return;
            }
            if (option != null && host != null) return;
            GoblinInteractable[] goblins = UnityEngine.Object.FindObjectsOfType<GoblinInteractable>();
            for (int i = 0; i < goblins.Length; i++)
            {
                GoblinInteractable goblin = goblins[i];
                if (goblin == null || !goblin.gameObject.activeInHierarchy) continue;
                List<InteractableBase> group = NPCInteractionGroupHelper.GetOrCreateGroupList(goblin, "[DangerousPrototypeCommission]");
                option = NPCInteractionGroupHelper.AddSubInteractable<DangerousPrototypeCommissionInteractable>(
                    goblin.transform, "DangerousPrototypeCommissionOption", group, x => { x.Owner = this; x.GroupMember = true; });
                if (option != null) host = goblin;
                return;
            }
        }

        private void DetachOption()
        {
            if (option != null)
            {
                if (host != null)
                {
                    List<InteractableBase> group = NPCInteractionGroupHelper.GetOrCreateGroupList(host, "[DangerousPrototypeCommission]");
                    if (group != null) group.Remove(option);
                }
                option.Owner = null;
                UnityEngine.Object.Destroy(option.gameObject);
            }
            option = null;
            host = null;
        }

        internal async void Accept(DangerousPrototypeCommissionInteractable source)
        {
            if (!CanAccept || source == null || host == null) return;
            var cancellation = new CancellationTokenSource();
            dialogue = cancellation;
            GoblinNPCController controller = host.GetComponent<GoblinNPCController>();
            try
            {
                if (controller != null) controller.StartDialogue();
                var actor = DialogueActorFactory.CreateBilingual(host.gameObject, "goblin_dingdang", "叮当", "Dingdang", new Vector3(0f, 2f, 0f));
                await DialogueManager.ShowDialogueSequenceBilingual(actor, Story, "BossRush_PrototypeCommission_Story", cancellation.Token);
                if (cancellation.IsCancellationRequested || dialogue != cancellation) return;
                if (!Commit())
                {
                    FieldAdventureSupport.Notify("委托暂时没能记下，稍后再找叮当一次。", "The commission could not be recorded. Talk to Dingdang again shortly.");
                    return;
                }
                FieldAdventureSupport.Notify("叮当的委托完成：危险试验台会随机出现在普通出击的地图里，地图上会标出来。",
                    "Dingdang's commission complete: her prototype bench now turns up at random on ordinary raid maps, marked on the map.");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototypeCommission] 委托对话失败: " + e.Message); }
            finally
            {
                if (controller != null)
                {
                    try { controller.EndDialogueWithStay(6f); }
                    catch (Exception e) { ModBehaviour.DevLog("[DangerousPrototypeCommission] 叮当收尾动作失败: " + e.Message); }
                }
                if (ReferenceEquals(dialogue, cancellation)) dialogue = null;
                cancellation.Dispose();
                if (data != null && data.Completed) DetachOption();
            }
        }

        // 一句一屏；叮当自称「叮当」，和她的好感剧情口吻一致。
        private static readonly string[][] Story =
        {
            new[] { "你来得正好！叮当有个委托，只想拜托你一个人。", "Perfect timing! Dingdang has a commission, and only you will do." },
            new[] { "最近叮当在后院拼了几台……嗯，稍微有那么一点点超标的试验装置。", "Lately Dingdang's been building a few test rigs out back... um, just a teeny bit over spec." },
            new[] { "倒鸭一号开一枪能把人推出三米，折射太阳在墙上弹得停不下来。", "Duckback Mk.I shoves the shooter three metres per shot. Ricochet Sun bounces off walls and won't stop." },
            new[] { "还有一口气打穿一整排的轨道炮、一劈连六个的雷霆鸭嘴，和会在头顶开花的巨型烟花筒。", "Then there's a rail cannon that punches through a whole line, a thunderbill that chains six at once, and a giant firework tube that blooms overhead." },
            new[] { "叮当自己试的话，锤子总是先飞出去……所以想请你在出击的时候帮叮当实测！", "Whenever Dingdang tests them, the hammer flies off first... So please field-test them for Dingdang on your raids!" },
            new[] { "叮当会把试验台偷偷藏进不同的地图，每次只放一件。碰到了就借来用，打出数据，叮当拿冷淬液谢你！", "Dingdang will sneak the bench into different maps, one rig at a time. Borrow it when you find it, get the data, and Dingdang will thank you with Cold Quench Fluid!" },
            new[] { "说好啦！叮当这就去打包装置。下次出击，留意地图上的试验台记号哦！", "It's a deal! Dingdang's off to pack the rigs. Next raid, watch the map for the bench marker!" },
        };

        private bool Commit()
        {
            if (persistence == null || committing) return false;
            bool stored;
            committing = true;
            try { stored = persistence.Store(new DangerousPrototypeCommissionData { Completed = true }); }
            finally { committing = false; }
            if (!stored) return false;
            data = new DangerousPrototypeCommissionData { Completed = true };
            return true;
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
                data = persistence.Load();
                openedSlot = slot;
            }
            return data != null && !persistence.HasWriteBarrier;
        }

        private void OnSlotChanged()
        {
            data = null;
            openedSlot = -1;
            DetachOption();
        }

        public override void OnDestroy()
        {
            if (dialogue != null) { dialogue.Cancel(); dialogue = null; }
            DetachOption();
            if (persistence != null) persistence.Dispose();
            persistence = null;
            data = null;
            owner = null;
            if (current == this) current = null;
        }
    }

    /// <summary>叮当组内的「叮当的委托」选项；只在可接时挂出，不挂灰掉的占位项。</summary>
    internal sealed class DangerousPrototypeCommissionInteractable : BossRushBuildingInteractableBase
    {
        internal DangerousPrototypeCommission Owner;
        internal bool GroupMember;
        private string lastLabel;
        protected override void Awake() { base.Awake(); HideGroupCollider(); }
        protected override void Start() { base.Start(); HideGroupCollider(); }
        private void HideGroupCollider()
        {
            if (!GroupMember) return;
            MarkerActive = false;
            if (interactCollider != null) interactCollider.enabled = false;
        }
        protected override string InteractNameKey
        {
            get
            {
                RefreshLabel();
                return "BossRush_PrototypeCommission";
            }
        }
        protected override string LogPrefix { get { return "[DangerousPrototypeCommission] "; } }
        protected override string InteractionGroupLabel { get { return "[DangerousPrototypeCommission]"; } }
        protected override float InteractMarkerHeight { get { return 1.4f; } }
        protected override bool IsBuildingInteractable()
        {
            RefreshLabel();
            return Owner != null && Owner.CanAccept;
        }
        private void RefreshLabel()
        {
            string label = L10n.T("叮当的委托：危险试用品", "Dingdang's commission: dangerous prototypes");
            if (label == lastLabel) return;
            lastLabel = label;
            LocalizationHelper.InjectLocalization("BossRush_PrototypeCommission", label);
        }
        protected override void OnInteractCompleted()
        {
            if (Owner != null) Owner.Accept(this);
        }
    }

    /// <summary>只保存「叮当的委托已完成」这一条事实；存槽、写屏障与物理保存复用共享管线。</summary>
    internal sealed class DangerousPrototypeCommissionPersistence : IBossRushSaveBatchSource, IDisposable
    {
        internal const string StorageKey = "BossRush_DangerousPrototype_Commission_v1";
        private readonly BossRushSlotJsonStore<DangerousPrototypeCommissionData> store;
        private readonly BossRushSaveCoordinatorEngine coordinator;
        private readonly Action slotChanged;
        private readonly Func<bool> canFlush;

        internal DangerousPrototypeCommissionPersistence(Action onSlotChanged, Func<bool> beforeCollect, Func<bool> flushAllowed)
        {
            slotChanged = onSlotChanged;
            canFlush = flushAllowed;
            coordinator = new BossRushSaveCoordinatorEngine(this, true);
            store = new BossRushSlotJsonStore<DangerousPrototypeCommissionData>(new BossRushSlotJsonStoreSpec<DangerousPrototypeCommissionData>
            {
                StorageKey = StorageKey, SchemaVersion = 1, LogPrefix = "[DangerousPrototypeCommission] ", DisplayName = "叮当的委托",
                Encode = Encode, Decode = Decode, ReadSchemaVersion = ReadSchemaVersion,
                CreateDefault = () => new DangerousPrototypeCommissionData(),
                NotifySlotChanged = OnSlotChanged,
                BeforeCollectSaveData = beforeCollect,
                AfterPendingWriteFlushed = coordinator.NotifyPendingWriteFlushed
            });
            store.EnsureSubscribed();
        }

        internal bool HasWriteBarrier { get { return store.HasWriteBarrier; } }
        internal bool Writable { get { return !store.HasWriteBarrier && !store.IsStoreFaulted; } }
        internal DangerousPrototypeCommissionData Load() { return store.LoadOrInit(); }
        internal bool Store(DangerousPrototypeCommissionData value)
        {
            if (!store.Store(value)) return false;
            string error;
            coordinator.RequestFlush(out error);
            return true;
        }
        internal void Tick() { coordinator.Tick(); }
        private void OnSlotChanged()
        {
            coordinator.NotifySlotChanged();
            if (slotChanged != null) slotChanged();
        }
        public void Dispose()
        {
            coordinator.TryFlushOnHostDestroy();
            store.ShutdownSubscription();
            store.ResetAll();
            coordinator.Reset();
        }

        string IBossRushSaveBatchSource.LogPrefix { get { return "[DangerousPrototypeCommission] "; } }
        bool IBossRushSaveBatchSource.HasPendingWrite { get { return store != null && store.HasPendingWrite; } }
        bool IBossRushSaveBatchSource.IsStoreFaulted { get { return store != null && store.IsStoreFaulted; } }
        bool IBossRushSaveBatchSource.HasSnapshotObligation { get { return false; } }
        string IBossRushSaveBatchSource.LastError { get { return store == null ? null : store.LastError; } }
        bool IBossRushSaveBatchSource.CollectSnapshot(out string error)
        {
            bool allowed = canFlush == null || canFlush();
            error = allowed ? null : "prototype_commission_pending";
            return allowed;
        }
        bool IBossRushSaveBatchSource.FlushPending() { return store.FlushPending(); }
        void IBossRushSaveBatchSource.OnPhysicalSaveSucceeded() { }

        internal static int ReadSchemaVersion(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int version;
            return root != null && root.TryGetInt("schemaVersion", out version) ? version : -1;
        }
        internal static DangerousPrototypeCommissionData Decode(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int schema;
            bool completed;
            if (root == null || !root.TryGetInt("schemaVersion", out schema) || schema != 1
                || !root.TryGetBool("completed", out completed)) return null;
            return new DangerousPrototypeCommissionData { Completed = completed };
        }
        internal static string Encode(DangerousPrototypeCommissionData value)
        {
            if (value == null) return null;
            return new BossRushJsonWriter().BeginObject().Int("schemaVersion", 1).Bool("completed", value.Completed).EndObject().ToString();
        }
    }
}
