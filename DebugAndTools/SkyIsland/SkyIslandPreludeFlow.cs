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
        /// <summary>官方地图上圈出的目标半径（米）：够把坠落点和守卫一起圈进去，又不至于盖住半张零号区。</summary>
        private const float ObjectiveMarkerRadius = 22f;
        /// <summary>交付「云上的坐标」时 Jeff 付的钱。官方完成面板照 Reward_Money 显示，实际发放随交付事实一次完成。</summary>
        internal const int DeliveryMoney = 5000;
        /// <summary>「航向仪在不在玩家身上」的采样间隔：只在任务进行中的那段时间里采，其余时候一次都不扫。</summary>
        private const float InstrumentPollInterval = 1f;
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
        /// <summary>上一次采样时玩家（背包 + 仓库）手上有没有航向仪。任务窗口之外恒为 false，不做无谓扫描。</summary>
        private bool holdsInstrument;
        /// <summary>本次故事打开后是否已经采过一次样。没采过就不拿 <see cref="holdsInstrument"/> 去推翻剧情位，
        /// 否则每次切场景的头一秒官方目标都会先变回未完成、再完成一次，白响一次「目标完成」。</summary>
        private bool instrumentSampled;
        private float nextInstrumentPoll;
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
            SkyIslandResidentInteractable.InjectLocalizations();
            // 交互体只在 Awake / Start 读取 key；已有目标随全局语言注入刷新，不重建场景对象。
            LocalizationHelper.InjectLocalization(DepartureNameKey,
                L10n.T("前往天空岛 · 晴岚群岛", "Depart for Sky Islands · Qinglan"));
            LocalizationHelper.InjectLocalization(ObjectiveNameKey,
                L10n.T("失落的航向仪", "Lost Navigation Instrument"));
            LocalizationHelper.InjectLocalization(InstrumentNameKey,
                L10n.T("从残骸里拆一具航向仪", "Salvage a navigation instrument"));
            LocalizationHelper.InjectLocalization(QuestNameKey, L10n.T("云上的坐标", "Coordinates Above the Clouds"));
            LocalizationHelper.InjectLocalization(QuestDescriptionKey,
                L10n.T("零号区掉下来一台航向仪，不是地面上的东西。守着它的那个家伙也不像本地拾荒者。帮我把航向仪带回来，我想看看它记了什么。",
                    "A navigation instrument fell into Ground Zero, and it is not from down here. Whatever guards it is no local scavenger either. Bring the instrument back to me. I want to see what it recorded."));
        }

        /// <summary>序章在官方任务表里的定义：给予者官方 Jeff，只在基地可接，交付要求人在基地（见 TryCompleteOfficialQuest）。</summary>
        private SkyIslandOfficialQuestDefinition BuildDefinition()
        {
            return new SkyIslandOfficialQuestDefinition
            {
                QuestId = SkyIslandOfficialQuestTable.PreludeQuestId, GiverId = (int)QuestGiverID.Jeff,
                ObjectName = QuestObjectName, NameKey = QuestNameKey, DescriptionKey = QuestDescriptionKey,
                Name = () => L10n.T("云上的坐标", "Coordinates Above the Clouds"),
                Description = () => L10n.T("零号区掉下来一台航向仪，守着它的家伙不是本地人。把航向仪带回来给 Jeff。",
                    "A navigation instrument fell into Ground Zero, and its guard is no local. Bring the instrument back to Jeff."),
                AcceptedFlag = SkyIslandStoryFlag.PreludeAccepted, DeliveredFlag = SkyIslandStoryFlag.RouteUnlocked,
                AcceptAction = SkyIslandStoryAction.AcceptPrelude, DeliverAction = SkyIslandStoryAction.UnlockRoute,
                Gate = context => !context.OnIsland && context.InBaseHub && context.BundleDeployed && context.NoConflictingMode,
                Accept = TryAcceptOfficialQuest, Deliver = TryCompleteOfficialQuest,
                // 官方任务页的「所需物品」栏与完成面板的奖励行都只是投影：真正的账记在本槽剧情事实上，
                // 钱在交付那一拍随 RouteUnlocked 一起发，读档重建投影不会再发第二次（见 SkyIslandOfficialQuestBridge）。
                RequiredItemId = SkyIslandNavInstrumentConfig.TYPE_ID, RequiredItemCount = 1, RewardMoney = DeliveryMoney,
                Tasks = new[]
                {
                    new SkyIslandOfficialQuestTaskDefinition
                    {
                        TaskId = 1,
                        // 「交付物在身上」才算目标完成：丢了就把完成按钮收回去，让玩家看到的判据和点下去的判据是同一份。
                        // 已交付的那一份投影在 history 里，这时 holdsInstrument 已经是 false，用航线位兜住。
                        Done = data => data.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered) &&
                            (data.SkyIslandRouteUnlocked || !instrumentSampled || holdsInstrument),
                        Description = data => data.SkyIslandRouteUnlocked
                            ? L10n.T("坐标已经交给 Jeff 了。", "The coordinates are with Jeff now.")
                            : !data.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered)
                                ? L10n.T("去零号区，干掉看守的家伙，把失落的航向仪带回来。",
                                    "Head to Ground Zero, put down the guard, and bring back the lost navigation instrument.")
                                : !instrumentSampled || holdsInstrument
                                    ? L10n.T("航向仪在你身上了，回基地找 Jeff。",
                                        "You have the instrument. Take it back to Jeff at base.")
                                    : L10n.T("航向仪不在你身上了。回零号区，从残骸里再拆一具。",
                                        "You are no longer carrying the instrument. Go back to Ground Zero and salvage another one from the wreck."),
                        ExtraHint = data => data.SkyIslandRouteUnlocked ? null
                            : data.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered) && (!instrumentSampled || holdsInstrument)
                                ? L10n.T("交付的时候 Jeff 会把航向仪收走。", "Jeff takes the instrument when you hand it in.")
                                : L10n.T("地图上圈出来的那一带，就是它掉下来的地方。", "The ring on your map is where it came down."),
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
            reason = L10n.T("先在基地找 Jeff 接下「云上的坐标」，把零号区那台航向仪带回去交给他。",
                "Take Coordinates Above the Clouds from Jeff at base first, then bring him the instrument from Ground Zero.");
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
            nextInstrumentPoll = 0f;
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
            RefreshInstrumentState();

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
            nextInstrumentPoll = 0f;
            Report(L10n.T("Jeff 在地图上给你圈了个地方。零号区，圈里头。东西在谁手上，你到了就知道。",
                "Jeff circles a spot on your map. Ground Zero, inside the ring. You will find out who is holding it when you get there."), false);
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
                reason = L10n.T("带着航向仪回基地找 Jeff。", "Bring the instrument back to Jeff at base.");
                return false;
            }
            // 交付物在身上才算交得掉。丢了不是死局：零号区那具残骸旁边还能再拆一具。
            if (SkyIslandNavInstrumentConfig.CountOwned() <= 0)
            {
                reason = L10n.T("航向仪不在身上。零号区那堆残骸旁边还能再拆一具。",
                    "You are not carrying the instrument. There is another one to salvage beside the wreck in Ground Zero.");
                return false;
            }
            if (!story.TryApply(SkyIslandStoryAction.UnlockRoute, out reason)) return false;
            story.Tick(true);
            // 先落事实再收物品：收不走最多是玩家手里多一具卖不掉的仪器，反过来就是白扣一件交付物。
            if (!SkyIslandNavInstrumentConfig.TryConsumeOne())
                ModBehaviour.DevLog("[SkyIslandPrelude] [WARNING] 航向仪没有被收走，交付事实已落下。");
            routeUnlocked = true;
            holdsInstrument = false;
            ClearJeff();
            Report(L10n.T("Jeff 把坐标读完了，让船工在航路表上添了一条晴岚。想上去看看，船点见。",
                "Jeff finishes reading the coordinates and has the crew add Qinglan to the route table. If you want to see it for yourself, the boat is waiting."), false);
            if (routeOpened != null) routeOpened();
            reason = null;
            return true;
        }

        /// <summary>
        /// 任务进行中才采样「航向仪在不在玩家身上」，并在第一次拿到时把剧情事实落下来。
        /// 窗口之外一次都不扫：官方 ItemUtilities 每次都会遍历仓库与背包并新建一个 List。
        /// </summary>
        private void RefreshInstrumentState()
        {
            SkyIslandStoryData data = story == null ? null : story.Current;
            if (data == null || !data.Has(SkyIslandStoryFlag.PreludeAccepted) || data.SkyIslandRouteUnlocked)
            {
                holdsInstrument = false;
                return;
            }
            if (!story.IsCurrentSlot) return;
            if (Time.unscaledTime < nextInstrumentPoll) return;
            nextInstrumentPoll = Time.unscaledTime + InstrumentPollInterval;
            holdsInstrument = SkyIslandNavInstrumentConfig.CountOwned() > 0;
            instrumentSampled = true;
            if (!holdsInstrument || data.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered)) return;
            if (!story.CanWrite) return;
            string message;
            if (!story.TryApply(SkyIslandStoryAction.RecoverPreludeInstrument, out message)) return;
            story.Tick(SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name));
            if (officialQuest != null) officialQuest.NotifyProgressChanged();
            Report(L10n.T("航向仪到手了。把它带回基地交给 Jeff。",
                "You have the instrument. Take it back to Jeff at base."), false);
        }

        /// <summary>
        /// 零号区那具残骸与守卫要不要在场：接了任务、还没交付、手上也没有航向仪时才摆。
        /// 判据看的是「手上有没有」而不是剧情位：阵亡丢包之后守卫会照常回来，玩家不会被卡在半路。
        /// </summary>
        private bool ShouldRunObjective()
        {
            SkyIslandStoryData data = story == null ? null : story.Current;
            return data != null && data.Has(SkyIslandStoryFlag.PreludeAccepted) &&
                !data.SkyIslandRouteUnlocked && !holdsInstrument;
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
                Report(L10n.T("圈里有东西。航向仪就摔在那儿，旁边还站着一个。",
                    "Something is inside the ring. The instrument came down there, and it is not lying alone."), false);
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
                // 官方要的是场景表里的场景 ID，不是 Unity 场景名（见 Utilities/MapPointSceneResolver.cs）。
                // 零号区两者恰好同名，所以这里换成共享解析属于加固，不是本轮「地图上没标出来」的成因。
                string sceneId = MapPointSceneResolver.Resolve(GroundZeroScene);
                mapMarker = SimplePointOfInterest.Create(position, sceneId, ObjectiveNameKey, null, false);
                if (mapMarker != null)
                {
                    mapMarker.Color = new Color(0.35f, 0.90f, 0.95f, 1f);
                    mapMarker.ScaleFactor = 1.35f;
                    // 画成一个圈：坠落点 + 守卫要占一小片地方，只画一个点在零号区那张图上很容易被漏看
                    // （owner 2026-09-20 反馈「地图上好像没标出来」）。撤离点那种 3 米触发区不适用，仍然只画点。
                    mapMarker.IsArea = true;
                    mapMarker.AreaRadius = ObjectiveMarkerRadius;
                    ModBehaviour.DevLog("[SkyIslandPrelude] 航向仪地图标记已创建 sceneId=" + sceneId +
                        " activeScene=" + SceneManager.GetActiveScene().name + " pos=" + position);
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
                Report(L10n.T("风声不对了。看着航向仪的那个——断风游猎·守，醒了。",
                    "The wind turns wrong. The one watching the instrument, the Galebreaker Warden, is awake."), false);
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
            nextInstrumentPoll = 0f;
            Report(L10n.T("断风游猎倒下了。航向仪在它的箱子里。",
                "The Galebreaker is down. The instrument is in its box."), false);
        }

        /// <summary>
        /// 残骸兜底：航向仪本来在守卫的尸体箱里（<see cref="SkyIslandPreludeBossOwner"/> 在建箱前塞进去）。
        /// 箱子被炸掉、背包塞满、或者仪器被卖掉时，才在这里再拆一具，条件是手上一具都没有——
        /// 仪器卖价为 0，所以这条兜底不构成刷钱口。
        /// </summary>
        internal bool CanRecoverInstrument
        {
            get { return bossDead && story != null && story.IsCurrentSlot && ShouldRunObjective(); }
        }

        internal void RecoverInstrument()
        {
            if (!CanRecoverInstrument) return;
            if (!SkyIslandNavInstrumentConfig.TryGiveToPlayer())
            {
                Report(L10n.T("残骸里拆不出完整的航向仪。清点一下背包再来。",
                    "You cannot pull an intact instrument out of the wreck. Make room in your pack and try again."), true);
                return;
            }
            // 剧情事实由下一次采样落下（RefreshInstrumentState），发物品与写存档因此永远只有一个 owner。
            nextInstrumentPoll = 0f;
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
            holdsInstrument = false;
            instrumentSampled = false;
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

    /// <summary>
    /// 前置头目的生命周期 owner：死亡回调、克隆 preset 的回收，以及交付物的掉落。
    ///
    /// 【掉落挂在 BeforeCharacterSpawnLootOnDead】官方 <c>CharacterMainControl.OnDead</c> 对非主角先发这个实例事件，
    /// 紧接着按 <c>dropBoxOnDead</c> 调 <c>InteractableLootbox.CreateFromItem</c> 收走「全部槽位 + 全部背包」。
    /// 在这一拍把航向仪塞进头目库存，它就和头目自己的掉落一起躺在同一个尸体箱里，不另建箱子、不搬运物品
    /// （与 <see cref="SkyIslandBossLoot"/> 同一条纪律）。<see cref="dropped"/> 挡住重入。
    /// </summary>
    internal sealed class SkyIslandPreludeBossOwner : MonoBehaviour
    {
        private Health health;
        private CharacterMainControl boss;
        private CharacterRandomPreset preset;
        private Action died;
        private bool subscribed, lootSubscribed, dropped;

        internal void Bind(CharacterMainControl character, CharacterRandomPreset ownedPreset, Action onDead)
        {
            health = character == null ? null : character.Health;
            boss = character;
            preset = ownedPreset;
            died = onDead;
            if (health == null) throw new InvalidOperationException("前置头目缺少生命组件");
            health.OnDeadEvent.AddListener(OnDead);
            subscribed = true;
            boss.BeforeCharacterSpawnLootOnDead += OnBeforeLoot;
            lootSubscribed = true;
        }

        private void OnBeforeLoot(DamageInfo damage)
        {
            if (dropped) return;
            dropped = true;
            try { SkyIslandNavInstrumentConfig.TryDropInto(boss); }
            catch (Exception e)
            {
                // 掉落失败不阻断死亡结算：残骸那边还能再拆一具。
                Debug.LogWarning("[SkyIslandPrelude] 航向仪掉落结算失败: " + e.Message);
            }
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
            if (lootSubscribed && boss != null) boss.BeforeCharacterSpawnLootOnDead -= OnBeforeLoot;
            lootSubscribed = false;
            if (preset != null) Destroy(preset, 0.1f);
            preset = null;
            died = null;
            health = null;
            boss = null;
        }
    }
}
