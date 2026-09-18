using System;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Duckov.MiniMaps;
using Duckov.Quests;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 晴岚群岛的官方世界序章：官方 Jeff 的 Quest 页面负责接取 / 交付，零号区放一具航向仪与一名群岛头目，
    /// 状态以 BossRush 自己的分槽存档为权威；官方任务投影由 SkyIslandOfficialQuestBridge 管理。
    /// </summary>
    internal sealed class SkyIslandPreludeFlow : IDisposable
    {
        private const string GroundZeroScene = "Level_GroundZero_1";
        private const float TickInterval = 0.25f;
        private const float BossActivationRange = 70f;
        private const string QuestObjectName = "BossRush_SkyIslandPrelude_OfficialQuest";
        private const string QuestNameKey = "BossRush_SkyIslandPrelude_QuestName";
        private const string QuestDescriptionKey = "BossRush_SkyIslandPrelude_QuestDescription";
        internal const string DepartureNameKey = "BossRush_SkyIsland_Departure";
        internal const string ObjectiveNameKey = "BossRush_SkyIslandPrelude_Objective";
        internal const string InstrumentNameKey = "BossRush_SkyIslandPrelude_Instrument";
        private static readonly Vector3 ObjectiveAnchor = new Vector3(373.52f, 0.02f, 286.93f);
        private static readonly Vector3 BossOffset = new Vector3(6f, 0f, 0f);
        private static SkyIslandPreludeFlow active;

        private readonly ModBehaviour owner;
        private readonly Action routeOpened;
        private readonly SkyIslandOfficialQuestBridge officialQuest;
        private SkyIslandStoryService story;
        private QuestGiver jeff;
        private GameObject objectiveRoot;
        private SkyIslandPreludeArtifactInteractable artifact;
        private CharacterMainControl boss;
        private SimplePointOfInterest mapMarker;
        private bool disposed, routeUnlocked, bossDead, bossSpawning, objectiveAnnounced, poiWarned, bundleWarned;
        private bool bundleDeployed, storyReady;
        /// <summary>资源可用性由现有关卡调度刷新；常驻任务查询只读内存，真正登岛仍由租约核查文件。</summary>
        internal static bool BundleDeployed { get { return active != null && !active.disposed && active.bundleDeployed; } }
        private int openedSlot = -1, generation, jeffAttempts;
        private float nextTick, nextJeffAttempt, nextBossAttempt, nextPoiAttempt;

        internal SkyIslandPreludeFlow(ModBehaviour host, Action onRouteOpened, SkyIslandOfficialQuestBridge bridge)
        {
            owner = host;
            routeOpened = onRouteOpened;
            active = this;
            officialQuest = bridge;
            InjectLocalizations();
            if (officialQuest != null) officialQuest.Register(BuildDefinition());
        }

        internal static void InjectLocalizations()
        {
            // 交互体只在 Awake / Start 读取 key；已有目标随全局语言注入刷新，不重建场景对象。
            LocalizationHelper.InjectLocalization(DepartureNameKey,
                L10n.T("前往天空岛 · 晴岚群岛", "Depart for Sky Islands · Qinglan"));
            LocalizationHelper.InjectLocalization(ObjectiveNameKey,
                L10n.T("失落的航向仪", "Lost Navigation Instrument"));
            LocalizationHelper.InjectLocalization(InstrumentNameKey,
                L10n.T("读取失落的航向仪", "Read the lost navigation instrument"));
            LocalizationHelper.InjectLocalization(QuestNameKey, L10n.T("云上的坐标", "Coordinates Above the Clouds"));
            LocalizationHelper.InjectLocalization(QuestDescriptionKey,
                L10n.T("Jeff 请你调查零号区坠落的航向仪。击败看守仪器的陌生游猎，读出坐标，再回 Jeff 处完成任务。",
                    "Jeff asks you to investigate a navigation instrument that fell into Ground Zero. Defeat its strange guard, read the coordinates, then return to Jeff to complete the quest."));
        }

        /// <summary>序章在官方任务表里的定义：给予者官方 Jeff，只在基地可接，交付要求人在基地（见 TryCompleteOfficialQuest）。</summary>
        private SkyIslandOfficialQuestDefinition BuildDefinition()
        {
            return new SkyIslandOfficialQuestDefinition
            {
                QuestId = SkyIslandOfficialQuestTable.PreludeQuestId, GiverId = (int)QuestGiverID.Jeff,
                ObjectName = QuestObjectName, NameKey = QuestNameKey, DescriptionKey = QuestDescriptionKey,
                Name = () => L10n.T("云上的坐标", "Coordinates Above the Clouds"),
                Description = () => L10n.T("Jeff 请你调查零号区坠落的航向仪。", "Jeff asks you to investigate a navigation instrument that fell into Ground Zero."),
                AcceptedFlag = SkyIslandStoryFlag.PreludeAccepted, DeliveredFlag = SkyIslandStoryFlag.RouteUnlocked,
                AcceptAction = SkyIslandStoryAction.AcceptPrelude, DeliverAction = SkyIslandStoryAction.UnlockRoute,
                Gate = context => !context.OnIsland && context.InBaseHub && context.BundleDeployed && context.NoConflictingMode,
                Accept = TryAcceptOfficialQuest, Deliver = TryCompleteOfficialQuest,
                Tasks = new[]
                {
                    new SkyIslandOfficialQuestTaskDefinition
                    {
                        TaskId = 1, Done = data => data.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered),
                        Description = data => data.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered)
                            ? L10n.T("已从失落的航向仪读取坐标；返回基地向 Jeff 交付任务。",
                                "Coordinates recovered from the lost navigation instrument; return to Jeff at base to complete the quest.")
                            : L10n.T("前往零号区，击败看守者并读取失落航向仪的坐标。",
                                "Travel to Ground Zero, defeat the guard, and read the lost navigation instrument's coordinates."),
                        ExtraHint = data => L10n.T("打开官方地图寻找「失落的航向仪」。", "Open the official map and find Lost Navigation Instrument."),
                    },
                },
            };
        }

        internal bool RouteUnlocked
        {
            get
            {
                if (!routeUnlocked) return false;
                try { return openedSlot == Saves.SavesSystem.CurrentSlot; }
                catch (Exception) { return false; }
            }
        }

        internal static bool CanUseRoute(out string reason)
        {
            if (active != null && active.RouteUnlocked)
            {
                reason = null;
                return true;
            }
            reason = L10n.T("先在基地接受 Jeff 的任务『云上的坐标』，完成零号区调查并回去交付。",
                "Accept Jeff's Coordinates Above the Clouds quest at base, finish the Ground Zero investigation, and return to complete it.");
            return false;
        }

        internal void Schedule()
        {
            bundleDeployed = SkyIslandRaidLease.IsBundleDeployed();
            jeffAttempts = 12;
            nextJeffAttempt = Time.unscaledTime + 0.5f;
            nextTick = 0f;
        }

        internal void OnStartedLoading()
        {
            generation++;
            ClearJeff();
            ClearObjective();
            CloseStory();
        }

        internal void Tick()
        {
            if (disposed || owner == null || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickInterval;
            if (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || !LevelManager.LevelInited) return;
            if (owner.GetComponent<SkyIslandSession>() != null) return;
            // 场景包缺失时不把玩家引进一条无法完成的任务链；更新完整资源后，下次关卡就绪会自然恢复。
            if (!bundleDeployed)
            {
                ClearJeff();
                ClearObjective();
                if (!bundleWarned)
                {
                    bundleWarned = true;
                    ModBehaviour.CriticalLog("sky-island-bundle-missing",
                        "[SkyIslandPrelude] 缺少 " + SkyIslandRaidLease.BundleRelativePath + "，不开放 Jeff 前置任务与晴岚航线。");
                }
                return;
            }
            if (!EnsureStory()) return;

            Scene scene = SceneManager.GetActiveScene();
            if (SceneRuntimeGate.IsBaseHubSceneName(scene.name))
            {
                ClearObjective();
                if (RouteUnlocked) ClearJeff();
                else EnsureJeff();
                story.Tick(true);
                return;
            }

            ClearJeff();
            if (scene.name == GroundZeroScene && ShouldRunObjective() && NoConflictingMode()) EnsureObjective();
            else ClearObjective();
            story.Tick(bossDead);
        }

        private bool EnsureStory()
        {
            if (story != null && story.IsCurrentSlot && storyReady) return true;
            if (story != null && !story.IsCurrentSlot) CloseStory();
            try
            {
                if (story == null)
                {
                    story = new SkyIslandStoryService();
                    story.Open();
                    openedSlot = Saves.SavesSystem.CurrentSlot;
                }
                bool migrated;
                string message;
                if (!story.EnsureRouteCompatibility(out migrated, out message))
                {
                    // 临时写屏障下保留同一门面，下一拍重试；未完成兼容检查前不向任务桥发布半就绪故事。
                    story.Tick(SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name));
                    return false;
                }
                storyReady = true;
                SkyIslandOfficialQuestStory.SetBaseSource(story);
                routeUnlocked = story.Current != null && story.Current.SkyIslandRouteUnlocked;
                if (migrated)
                {
                    story.Tick(true);
                    ModBehaviour.DevLog("[SkyIslandPrelude] 既有群岛进度已迁移为航线解锁，slot=" + openedSlot);
                }
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SkyIslandPrelude] [WARNING] 序章记录未就绪: " + e.Message);
                CloseStory();
                return false;
            }
        }

        private bool NoConflictingMode()
        {
            string mode;
            return !owner.ValidationHasActiveMode(out mode);
        }

        internal bool TryAcceptOfficialQuest(out string message)
        {
            if (story == null || !story.IsCurrentSlot || story.Current == null || !story.CanWrite)
            {
                message = story == null ? L10n.T("晴岚任务尚未就绪。", "The Qinglan quest is not ready yet.") : story.SaveStatus;
                return false;
            }
            if (story.Current.Has(SkyIslandStoryFlag.PreludeAccepted))
            {
                message = null;
                return true;
            }
            if (!story.TryApply(SkyIslandStoryAction.AcceptPrelude, out message)) return false;
            story.Tick(true);
            Report(L10n.T("任务已接取：前往零号区，打开地图寻找『失落的航向仪』。",
                "Quest accepted: travel to Ground Zero and open the map to find the Lost Navigation Instrument."), false);
            return true;
        }

        internal bool TryCompleteOfficialQuest(out string reason)
        {
            if (RouteUnlocked) { reason = null; return true; }
            if (story == null || !story.IsCurrentSlot || story.Current == null || !story.CanWrite)
            {
                reason = story == null ? L10n.T("晴岚任务尚未就绪。", "The Qinglan quest is not ready yet.") : story.SaveStatus;
                return false;
            }
            if (!SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name))
            {
                reason = L10n.T("回到基地向 Jeff 交付坐标。", "Return to Jeff at base to deliver the coordinates.");
                return false;
            }
            if (!story.TryApply(SkyIslandStoryAction.UnlockRoute, out reason)) return false;
            story.Tick(true);
            routeUnlocked = true;
            ClearJeff();
            Report(L10n.T("任务完成：Jeff 已校准云上坐标，基地船点开放晴岚航线。",
                "Quest complete: Jeff calibrated the coordinates above the clouds, and the Qinglan route is now available at the base boat."), false);
            if (routeOpened != null) routeOpened();
            reason = null;
            return true;
        }

        private bool ShouldRunObjective()
        {
            SkyIslandStoryData data = story == null ? null : story.Current;
            return data != null && data.Has(SkyIslandStoryFlag.PreludeAccepted) &&
                !data.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered) && !data.SkyIslandRouteUnlocked;
        }

        private void EnsureJeff()
        {
            if (jeff != null || jeffAttempts <= 0 || Time.unscaledTime < nextJeffAttempt) return;
            nextJeffAttempt = Time.unscaledTime + 1f;
            jeffAttempts--;
            foreach (QuestGiver candidate in UnityEngine.Object.FindObjectsOfType<QuestGiver>(true))
            {
                if (candidate == null || candidate.ID != QuestGiverID.Jeff) continue;
                jeff = candidate;
                if (officialQuest != null) officialQuest.PrepareGiver(candidate);
                return;
            }
            if (jeffAttempts == 0)
                ModBehaviour.DevLog("[SkyIslandPrelude] 当前基地子场景尚未发现官方 Jeff，后续场景 / 关卡就绪事件会重试。");
        }

        private void ClearJeff()
        {
            jeff = null;
        }

        private void EnsureObjective()
        {
            Vector3 position = GroundPoint(ObjectiveAnchor);
            if (objectiveRoot == null) CreateObjective(position);
            if (mapMarker == null && Time.unscaledTime >= nextPoiAttempt) CreateMapMarker(position);
            if (!objectiveAnnounced)
            {
                objectiveAnnounced = true;
                Report(L10n.T("Jeff 标记的异常信号位于零号区；打开地图可查看『失落的航向仪』。",
                    "Jeff's strange signal is in Ground Zero; open the map to find the Lost Navigation Instrument."), false);
            }
            if (boss != null || bossDead || bossSpawning || Time.unscaledTime < nextBossAttempt) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || (player.transform.position - position).sqrMagnitude > BossActivationRange * BossActivationRange) return;
            SpawnBoss(position + BossOffset, generation);
        }

        private Vector3 GroundPoint(Vector3 point)
        {
            RaycastHit hit;
            int mask = GameplayDataSettings.Layers.groundLayerMask.value;
            if (Physics.Raycast(point + Vector3.up * 12f, Vector3.down, out hit, 30f, mask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;
            return point;
        }

        private void CreateObjective(Vector3 position)
        {
            objectiveRoot = new GameObject("BossRush_SkyIslandPrelude_Objective");
            objectiveRoot.SetActive(false);
            objectiveRoot.transform.position = position;
            SphereCollider trigger = objectiveRoot.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.up * 0.55f;
            trigger.radius = 0.7f;
            artifact = objectiveRoot.AddComponent<SkyIslandPreludeArtifactInteractable>();
            artifact.Bind(this);
            CreateInstrumentPiece(PrimitiveType.Cylinder, new Vector3(0f, 0.28f, 0f), new Vector3(0.55f, 0.12f, 0.55f), new Color(0.28f, 0.50f, 0.56f));
            CreateInstrumentPiece(PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(0.08f, 0.08f, 0.8f), new Color(0.82f, 0.70f, 0.34f));
            CreateInstrumentPiece(PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(0.8f, 0.08f, 0.08f), new Color(0.82f, 0.70f, 0.34f));
            Light signal = objectiveRoot.AddComponent<Light>();
            signal.type = LightType.Point;
            signal.range = 5f;
            signal.intensity = 1.2f;
            signal.color = new Color(0.38f, 0.84f, 0.90f);
            objectiveRoot.SetActive(true);
        }

        private void CreateInstrumentPiece(PrimitiveType type, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject piece = GameObject.CreatePrimitive(type);
            piece.name = "InstrumentPiece";
            piece.transform.SetParent(objectiveRoot.transform, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = localScale;
            Collider collider = piece.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            Renderer renderer = piece.GetComponent<Renderer>();
            if (renderer != null)
            {
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_Color", color);
                properties.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(properties);
            }
        }

        private void CreateMapMarker(Vector3 position)
        {
            nextPoiAttempt = Time.unscaledTime + 5f;
            try
            {
                mapMarker = SimplePointOfInterest.Create(position, GroundZeroScene, ObjectiveNameKey, null, false);
                if (mapMarker != null)
                {
                    mapMarker.Color = new Color(0.35f, 0.90f, 0.95f, 1f);
                    mapMarker.ScaleFactor = 1.35f;
                }
            }
            catch (Exception e)
            {
                if (!poiWarned)
                {
                    poiWarned = true;
                    ModBehaviour.DevLog("[SkyIslandPrelude] [WARNING] 航向仪地图标记创建失败，将重试: " + e.Message);
                }
            }
        }

        private CharacterRandomPreset FindPreset()
        {
            CharacterRandomPreset selected = null;
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
            {
                if (preset == null || preset.isBoss || preset.isZombie || preset.team != Teams.scav ||
                    preset.name.IndexOf("Dummy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    preset.name.StartsWith("BossRush_", StringComparison.Ordinal)) continue;
                if (selected == null || string.CompareOrdinal(preset.name, selected.name) < 0) selected = preset;
            }
            return selected;
        }

        private async void SpawnBoss(Vector3 position, int expectedGeneration)
        {
            bossSpawning = true;
            CharacterRandomPreset clone = null;
            CharacterMainControl created = null;
            bool retained = false, presetOwnedByCharacter = false;
            try
            {
                CharacterRandomPreset source = FindPreset();
                if (source == null) throw new InvalidOperationException("没有可用的官方拾荒者角色资源");
                clone = UnityEngine.Object.Instantiate(source);
                clone.name = "BossRush_SkyIslandPrelude_Galebreaker";
                clone.dropBoxOnDead = true;
                clone.setActiveByPlayerDistance = false;
                position = GroundPoint(position);
                created = await clone.CreateCharacterAsync(position, Vector3.left, -1, null, false);
                if (created == null) throw new InvalidOperationException("官方角色创建失败");
                if (!IsObjectiveGeneration(expectedGeneration)) return;
                created.transform.SetParent(objectiveRoot.transform, true);
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created);
                created.SetTeam(Teams.wolf);
                var life = created.gameObject.AddComponent<SkyIslandPreludeBossOwner>();
                life.Bind(created, clone, OnBossDead);
                presetOwnedByCharacter = true;
                var context = new SkyIslandBossContext
                {
                    Root = objectiveRoot.transform,
                    GroundMask = GameplayDataSettings.Layers.groundLayerMask.value,
                    Valid = () => IsObjectiveGeneration(expectedGeneration),
                    Report = Report,
                    CallGroup = null
                };
                if (!SkyIslandBossForge.TryApply(created, "K3_Relay", 0, context))
                    throw new InvalidOperationException("断风游猎档案装配失败");
                boss = created;
                retained = true;
                Report(L10n.T("异常风声逼近：断风游猎·守正在看守航向仪。",
                    "A strange wind closes in: Galebreaker Ranger (Warden) guards the instrument."), false);
            }
            catch (Exception e)
            {
                nextBossAttempt = Time.unscaledTime + 10f;
                Debug.LogWarning("[SkyIslandPrelude] 前置头目生成失败，将重试: " + e);
            }
            finally
            {
                bossSpawning = false;
                if (!retained && created != null) { created.gameObject.SetActive(false); UnityEngine.Object.Destroy(created.gameObject); }
                if (!presetOwnedByCharacter && clone != null) UnityEngine.Object.Destroy(clone, created != null ? 0.1f : 0f);
            }
        }

        private bool IsObjectiveGeneration(int expected)
        {
            return !disposed && expected == generation && objectiveRoot != null && ShouldRunObjective() &&
                SceneManager.GetActiveScene().name == GroundZeroScene;
        }

        private void OnBossDead()
        {
            bossDead = true;
            boss = null;
            Report(L10n.T("断风游猎倒下了。现在可以读取失落的航向仪。",
                "The Galebreaker is down. You can now read the lost navigation instrument."), false);
        }

        internal bool CanRecoverInstrument
        {
            get { return bossDead && story != null && story.IsCurrentSlot && ShouldRunObjective(); }
        }

        internal void RecoverInstrument()
        {
            if (!CanRecoverInstrument) return;
            string message;
            if (!story.TryApply(SkyIslandStoryAction.RecoverPreludeInstrument, out message))
            { Report(message, true); return; }
            story.Tick(true);
            if (mapMarker != null) UnityEngine.Object.Destroy(mapMarker.gameObject);
            mapMarker = null;
            if (artifact != null) artifact.enabled = false;
            if (officialQuest != null) officialQuest.NotifyProgressChanged();
            Report(message + L10n.T(" 返回基地，把坐标交给 Jeff。", " Return to the base and give the coordinates to Jeff."), false);
        }

        internal bool TryPrepareDeparture(out string reason)
        {
            if (!RouteUnlocked)
            {
                CanUseRoute(out reason);
                return false;
            }
            reason = null;
            if (story == null) return true;
            if (!story.TryClose())
            {
                reason = story.SaveStatus;
                return false;
            }
            story = null;
            SkyIslandOfficialQuestStory.SetBaseSource(null);
            return true;
        }

        private void ClearObjective()
        {
            if (mapMarker == null && objectiveRoot == null && boss == null && !bossDead && !bossSpawning && !objectiveAnnounced) return;
            generation++;
            if (mapMarker != null) UnityEngine.Object.Destroy(mapMarker.gameObject);
            if (objectiveRoot != null) UnityEngine.Object.Destroy(objectiveRoot);
            mapMarker = null;
            objectiveRoot = null;
            artifact = null;
            boss = null;
            bossDead = false;
            bossSpawning = false;
            objectiveAnnounced = false;
            poiWarned = false;
        }

        private void CloseStory()
        {
            storyReady = false;
            routeUnlocked = false;
            openedSlot = -1;
            if (story != null) story.Close();
            story = null;
            SkyIslandOfficialQuestStory.SetBaseSource(null);
        }

        private void Report(string message, bool error)
        {
            if (owner != null && !string.IsNullOrEmpty(message)) owner.ShowMessage(message);
            if (error && !string.IsNullOrEmpty(message)) Debug.LogWarning("[SkyIslandPrelude] " + message);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            generation++;
            ClearJeff();
            ClearObjective();
            if (officialQuest != null) officialQuest.Unregister(SkyIslandOfficialQuestTable.PreludeQuestId);
            CloseStory();
            if (ReferenceEquals(active, this)) active = null;
        }
    }

    public sealed class SkyIslandPreludeArtifactInteractable : BossRushBuildingInteractableBase
    {
        private SkyIslandPreludeFlow flow;
        internal void Bind(SkyIslandPreludeFlow value) { flow = value; }
        protected override string InteractNameKey
        {
            get { return SkyIslandPreludeFlow.InstrumentNameKey; }
        }
        protected override string LogPrefix { get { return "[SkyIslandPrelude] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIslandPrelude]"; } }
        protected override float InteractMarkerHeight { get { return 1.25f; } }
        protected override bool IsBuildingInteractable() { return flow != null && flow.CanRecoverInstrument; }
        protected override void OnInteractCompleted() { if (flow != null) flow.RecoverInstrument(); }
    }

    internal sealed class SkyIslandPreludeBossOwner : MonoBehaviour
    {
        private Health health;
        private CharacterRandomPreset preset;
        private Action died;
        private bool subscribed;

        internal void Bind(CharacterMainControl character, CharacterRandomPreset ownedPreset, Action onDead)
        {
            health = character == null ? null : character.Health;
            preset = ownedPreset;
            died = onDead;
            if (health == null) throw new InvalidOperationException("前置头目缺少生命组件");
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
        }

        private void OnDead(DamageInfo damage)
        {
            Action callback = died;
            died = null;
            if (callback != null) callback();
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) health.OnDeadEvent.RemoveListener(OnDead);
            subscribed = false;
            if (preset != null) Destroy(preset, 0.1f);
            preset = null;
            died = null;
            health = null;
        }
    }
}
