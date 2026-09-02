// ============================================================================
// CampaignFinalBoss.cs - 终章「冠军之影」决战编排
// ============================================================================
// 零新增 3D 资产：复用现有自定义 Boss 的公开生成 API，生成后叠三层改造——
//   数值倍率（ApplyBossStatMultiplier）+ 体型放大 + MaterialPropertyBlock 染色。
// 官方 preset 在生成流程里已被克隆过一份，改 nameKey 只影响这一只，不污染 Boss 池。
//
// 【门禁：为什么必须「无任何模式激活」才能开战】
//   生成走的是 legacy 路径，会写 currentBoss / currentWaveBosses 这些标准竞技场
//   的静态流程状态。标准流程只在 bossRushArenaActive 为真时消费它们，
//   所以空手开战是安全的；但反过来，如果玩家已经在跑某个模式，
//   我们的 Boss 就会被那个模式的波次逻辑当成自己的敌人算进去。
//   因此开战前逐一检查六个模式标志，任一激活即拒绝。
//
// 【让路策略】
//   玩家在决战途中开了标准竞技场：战役主动中止并清理自己的 Boss，
//   而不是去改路牌的 IsInteractable——那属于侵入既有模式，违反零重构原则。
// ============================================================================

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        #region 状态

        /// <summary>当前决战中的 Boss 实例；null 表示没有在打。</summary>
        private CharacterMainControl campaignFinalBossInstance;

        /// <summary>决战是否正在进行（生成中也算，避免重复触发）。</summary>
        private bool campaignFinalBossActive;

        /// <summary>
        /// 本场决战的编号。每次开战自增，每次收尾也自增——
        /// 异步生成期间玩家可能已经切场景/死亡，回来时这一场早就作废了。
        /// 生成协程回来后比对它，不一致就把生成出来的 Boss 销毁，
        /// 否则会留下一只没人记账的强化女巫。
        /// </summary>
        private int campaignFinalBossRunId;

        /// <summary>
        /// 生成流程是否已出结果（拿到实例或确认失败）。
        /// 用于区分「还在异步生成中」与「Boss 已经不在场上了」：
        /// 两种情况下 campaignFinalBossInstance 都是 null。
        /// </summary>
        private bool campaignFinalBossSpawnResolved;

        /// <summary>竞技场场景判定的缓存代数（-1 = 尚未计算）。</summary>
        private int campaignArenaSceneGeneration = -1;

        /// <summary>上次计算出的「当前场景是竞技场」结果。</summary>
        private bool campaignArenaSceneIsValid;

        /// <summary>染色用的属性块。复用一份，避免每个 renderer 新建。</summary>
        private readonly MaterialPropertyBlock campaignFinalBossColorBlock = new MaterialPropertyBlock();

        private static readonly int CampaignBossColorProperty = Shader.PropertyToID("_Color");
        private static readonly int CampaignBossTintColorProperty = Shader.PropertyToID("_TintColor");
        private static readonly int CampaignBossBaseColorProperty = Shader.PropertyToID("_BaseColor");
        private int campaignFinalBossDeathPresentationCount;

        #endregion

        #region 只读

        /// <summary>决战是否进行中。</summary>
        internal bool IsCampaignFinalBossActive { get { return campaignFinalBossActive; } }
        internal int CampaignFinalBossDeathPresentationCount { get { return campaignFinalBossDeathPresentationCount; } }
        internal CharacterMainControl CampaignFinalBossInstanceForValidation { get { return campaignFinalBossInstance; } }

        #endregion

        #region 门禁

        /// <summary>
        /// 现在能否开启终章决战。要求：战役已启用、终章契约进行中、
        /// 身处竞技场、且**没有任何模式在跑**。
        /// </summary>
        internal bool CanStartCampaignFinalBoss()
        {
            try
            {
                if (!IsCampaignConfiguredEnabled()) return false;
                if (campaignFinalBossActive) return false;

                CampaignChapterDef def = CampaignProgressService.GetActiveChapterDef();
                if (def == null) return false;
                if (!string.Equals(def.Mode, CampaignContentCatalog.ModeFinal, StringComparison.Ordinal)) return false;

                if (!IsCampaignArenaSceneCached()) return false;

                return !IsAnyGameplayModeActiveForCampaign();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 当前场景是不是竞技场，按场景代数缓存。
        ///
        /// 【为什么要缓存】IsCurrentSceneValidBossRushArena 内部走
        /// SceneManager.GetActiveScene().name，**每次调用分配一个托管字符串**。
        /// 召唤石维护是每帧路径，直接调它等于每帧产生垃圾（AGENTS.md 4.12）。
        /// 场景只在 OnSceneLoaded 时变，用模块的 scene generation 做失效键即可。
        /// </summary>
        private bool IsCampaignArenaSceneCached()
        {
            try
            {
                CampaignRuntimeModule runtime = CampaignRuntime;
                int generation = runtime != null ? runtime.SceneGeneration : 0;
                if (generation != campaignArenaSceneGeneration)
                {
                    campaignArenaSceneGeneration = generation;
                    campaignArenaSceneIsValid = IsCurrentSceneValidBossRushArena();
                }
                return campaignArenaSceneIsValid;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 是否有任何既有模式正在跑。决战必须独占场地——
        /// legacy 生成会写标准流程的静态状态，混跑会让 Boss 被别的模式记账。
        /// </summary>
        private bool IsAnyGameplayModeActiveForCampaign()
        {
            try
            {
                if (bossRushArenaActive) return true;
                if (modeDActive) return true;
                if (modeEActive) return true;
                if (modeFActive) return true;
                // 宿命回响也在竞技场里跑并自己刷 Boss，漏了它决战就会和它抢场地
                if (modeGActive) return true;
                if (zombieModeRunState != null
                    && zombieModeRunState.LifecyclePhase != ZombieModeLifecyclePhase.None)
                {
                    return true;
                }
                // 黑市鸭王杯同样占用竞技场（玩家在看台，场上是签约的斗士）
                ModeHRuntimeModule modeH = ModeHRuntime;
                if (modeH != null && modeH.RunState != null
                    && modeH.RunState.Lifecycle != ModeHLifecycle.None)
                {
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                // 读不到就当作「有模式在跑」：拒绝开战是更保守的一侧
                return true;
            }
        }

        #endregion

        #region 召唤石维护

        /// <summary>场上的召唤石实例。随场景销毁，按需重建。</summary>
        private GameObject campaignFinalBossAltar;

        /// <summary>
        /// 每帧维护召唤石：终章契约进行中且身处竞技场时确保它在场，否则清掉。
        /// 由 TickCampaignModeBridge 驱动，未启用战役时不会走到这里。
        ///
        /// 放在 tick 而不是场景回调里，是因为「玩家进场时角色还没生成」——
        /// 召唤石要摆在玩家附近，必须等拿得到玩家位置的那一帧。
        /// </summary>
        internal void TickCampaignFinalBossAltar()
        {
            try
            {
                bool shouldExist = ShouldCampaignFinalBossAltarExist();

                if (!shouldExist)
                {
                    if (campaignFinalBossAltar != null)
                    {
                        UnityEngine.Object.Destroy(campaignFinalBossAltar);
                        campaignFinalBossAltar = null;
                    }
                    return;
                }

                if (campaignFinalBossAltar != null) return;

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return;

                CreateCampaignFinalBossAltar(main.transform.position + main.transform.forward * 3f);
            }
            catch (Exception)
            {
                // 每帧路径：不抛也不打日志
            }
        }

        /// <summary>
        /// 召唤石现在该不该在场。
        ///
        /// 【判定顺序是性能约束，不是风格】这是每帧路径。终章契约查询是纯内存的
        /// （字典命中 + 整数比较，零分配），而场景判定会分配字符串。
        /// 绝大多数玩家在绝大多数时间里没有进行中的终章契约，
        /// 因此必须让零分配的那个先短路掉（AGENTS.md 4.12）。
        /// </summary>
        private bool ShouldCampaignFinalBossAltarExist()
        {
            if (!IsCampaignConfiguredEnabled()) return false;
            if (campaignFinalBossActive) return false;

            CampaignChapterDef def = CampaignProgressService.GetActiveChapterDef();
            if (def == null) return false;
            if (!string.Equals(def.Mode, CampaignContentCatalog.ModeFinal, StringComparison.Ordinal)) return false;
            // 目标达成后只应回公告板交付，不能再次生成祭坛、重复召唤终章 Boss。
            if (CampaignProgressService.GetState(def.ChapterId) != CampaignChapterState.ContractActive) return false;

            if (!IsCampaignArenaSceneCached()) return false;
            return !IsAnyGameplayModeActiveForCampaign();
        }

        /// <summary>造一块程序化召唤石：黑曜石基座 + 悬浮的绯红核心。</summary>
        private void CreateCampaignFinalBossAltar(Vector3 position)
        {
            try
            {
                campaignFinalBossAltar = new GameObject("BossRushCampaignFinalBossAltar");
                campaignFinalBossAltar.transform.position = position;

                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");

                CreateCampaignAltarPart(campaignFinalBossAltar, PrimitiveType.Cube, "Base",
                    new Vector3(1.0f, 0.25f, 1.0f), new Vector3(0f, 0.12f, 0f),
                    new Color(0.10f, 0.10f, 0.13f, 1f), shader);
                CreateCampaignAltarPart(campaignFinalBossAltar, PrimitiveType.Cube, "Pillar",
                    new Vector3(0.34f, 0.85f, 0.34f), new Vector3(0f, 0.60f, 0f),
                    new Color(0.16f, 0.15f, 0.19f, 1f), shader);
                CreateCampaignAltarPart(campaignFinalBossAltar, PrimitiveType.Sphere, "Core",
                    new Vector3(0.34f, 0.34f, 0.34f), new Vector3(0f, 1.20f, 0f),
                    CampaignTuning.FinalBossTint, shader);

                BoxCollider collider = campaignFinalBossAltar.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(1.4f, 1.8f, 1.4f);
                collider.center = new Vector3(0f, 0.9f, 0f);

                campaignFinalBossAltar.AddComponent<CampaignFinalBossInteractable>();

                DevLog(CampaignTuning.LogPrefix + "决战召唤石已生成");
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 召唤石生成失败: " + e.Message);
                campaignFinalBossAltar = null;
            }
        }

        private void CreateCampaignAltarPart(
            GameObject parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 localPos, Color color, Shader shader)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = scale;

            // 装饰件的碰撞体会跟交互 trigger 打架，必须删掉
            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider != null) UnityEngine.Object.Destroy(partCollider);

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null && shader != null)
            {
                Material material = new Material(shader);
                material.color = color;
                renderer.material = material;
            }
        }

        #endregion

        #region 开战

        /// <summary>启动终章决战。前置条件不满足时静默返回。</summary>
        internal void StartCampaignFinalBoss()
        {
            try
            {
                if (!CanStartCampaignFinalBoss()) return;
                campaignFinalBossActive = true;
                campaignFinalBossSpawnResolved = false;
                int runId = ++campaignFinalBossRunId;
                CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeFinal);
                StartCampaignFinalBossAsync(runId).Forget();
            }
            catch (Exception e)
            {
                campaignFinalBossActive = false;
                campaignFinalBossSpawnResolved = false;
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战启动失败: " + e.Message);
            }
        }

        internal bool DebugStartCampaignFinalBossForValidation()
        {
            if (!DevModeEnabled || campaignFinalBossActive || !IsCampaignArenaSceneCached()) return false;
            campaignFinalBossDeathPresentationCount = 0;
            campaignFinalBossActive = true;
            campaignFinalBossSpawnResolved = false;
            int runId = ++campaignFinalBossRunId;
            StartCampaignFinalBossAsync(runId).Forget();
            return true;
        }

        private async UniTask StartCampaignFinalBossAsync(int runId)
        {
            try
            {
                ShowMessage(L10n.T("冠军之影现身了……", "The Shadow of the Champion appears..."));

                Vector3 position = ResolveCampaignFinalBossSpawnPosition();

                // notifyBossRushOnFailure:false —— 失败不能去通知标准竞技场流程，
                // 那会在没有波次的情况下推进它的状态机
                // 体型倍率必须在生成时传入，不能生成后再改 localScale：
                // localScale 会缩放碰撞体，而属性/AI 初始化会缓存碰撞器半径。
                CharacterMainControl boss = await SpawnPhantomWitch(
                    position, false, false, PhantomWitchDeathPresentation.CampaignFinal,
                    CampaignTuning.FinalBossScale);

                // 生成是异步的：等待期间玩家可能已经切场景、死亡或开了别的模式，
                // 那一场已经被收尾过了。此时绝不能把 Boss 认领回来——
                // 它会变成一只没人记账的强化女巫留在场上。
                if (runId != campaignFinalBossRunId)
                {
                    DevLog(CampaignTuning.LogPrefix + "决战已在生成期间中止，销毁迟到的 Boss");
                    if (boss != null)
                    {
                        try
                        {
                            UnityEngine.Object.Destroy(boss.gameObject);
                        }
                        catch (Exception e)
                        {
                            DevLog(CampaignTuning.LogPrefix + "[WARNING] 销毁迟到的决战 Boss 失败: " + e.Message);
                        }
                    }
                    return;
                }

                campaignFinalBossSpawnResolved = true;

                if (boss == null)
                {
                    campaignFinalBossActive = false;
                    ShowMessage(L10n.T("决战未能开始，请稍后重试", "The showdown could not begin; try again"));
                    return;
                }

                campaignFinalBossInstance = boss;
                ApplyCampaignFinalBossVariant(boss);

                if (boss.Health != null)
                {
                    boss.Health.OnDeadEvent.AddListener(OnCampaignFinalBossDead);
                }

            }
            catch (Exception e)
            {
                campaignFinalBossActive = false;
                campaignFinalBossSpawnResolved = false;
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战生成异常: " + e.Message);
            }
        }

        /// <summary>决战 Boss 的生成位置：玩家前方一段距离，拿不到玩家时退回原点。</summary>
        private Vector3 ResolveCampaignFinalBossSpawnPosition()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return Vector3.zero;
                return main.transform.position + main.transform.forward * 8f;
            }
            catch (Exception)
            {
                return Vector3.zero;
            }
        }

        #endregion

        #region 变体改造

        /// <summary>
        /// 把普通幽灵女巫改造成「冠军之影」：数值倍率 + 体型放大 + 绯红染色 + 变体名。
        /// 三层都是可失败的装饰，任一失败都不影响这场战斗能不能打完。
        /// </summary>
        private void ApplyCampaignFinalBossVariant(CharacterMainControl boss)
        {
            if (boss == null) return;

            // 1) 数值：在 Boss 自身倍率之上再叠战役倍率
            try
            {
                ApplyBossStatMultiplier(boss, CampaignTuning.FinalBossStatMultiplier);
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战数值倍率失败: " + e.Message);
            }

            // 2) 体型：已由 SpawnPhantomWitch 的 extraModelScale 在碰撞器缓存之前应用，
            //    这里不再二次缩放（事后改 localScale 会让碰撞体与模型口径不一致）。

            // 3) 染色：走 MaterialPropertyBlock，绝不碰 sharedMaterial（会污染同款所有敌人）
            try
            {
                Renderer[] renderers = boss.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    SetCampaignFinalBossRendererColor(renderers[i], CampaignTuning.FinalBossTint);
                }
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战染色失败: " + e.Message);
            }

            // 4) 变体名：生成流程已把 preset 克隆过一份，改它不会污染 Boss 池
            try
            {
                string nameKey = "BossRush_Campaign_FinalBoss_Name";
                LocalizationHelper.InjectLocalization(
                    nameKey, L10n.T("冠军之影", "Shadow of the Champion"));
                if (boss.characterPreset != null)
                {
                    boss.characterPreset.nameKey = nameKey;
                }
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战变体名失败: " + e.Message);
            }
        }

        private void SetCampaignFinalBossRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;

            renderer.GetPropertyBlock(campaignFinalBossColorBlock);
            Material sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial != null && sharedMaterial.HasProperty(CampaignBossColorProperty))
            {
                campaignFinalBossColorBlock.SetColor(CampaignBossColorProperty, color);
            }
            else if (sharedMaterial != null && sharedMaterial.HasProperty(CampaignBossTintColorProperty))
            {
                campaignFinalBossColorBlock.SetColor(CampaignBossTintColorProperty, color);
            }
            else
            {
                campaignFinalBossColorBlock.SetColor(CampaignBossBaseColorProperty, color);
            }

            renderer.SetPropertyBlock(campaignFinalBossColorBlock);
        }

        #endregion

        #region 结束

        /// <summary>决战 Boss 死亡：上报目标并收尾。</summary>
        private void OnCampaignFinalBossDead(DamageInfo damageInfo)
        {
            // 同一帧的重复死亡回调只允许第一个取得表现 owner。
            if (!campaignFinalBossActive) return;
            campaignFinalBossActive = false;
            campaignFinalBossDeathPresentationCount++;
            try
            {
                DevLog(CampaignTuning.LogPrefix + "冠军之影已被击败");
                ShowMessage(L10n.T("冠军之影已被击败", "The Shadow of the Champion has fallen"));

                CampaignObjectiveTracker.ReportFinalBossKill();
                BossRushAudioManager.Instance?.StopBossBGM(
                    BossBgmKeys.PhantomWitch, campaignFinalBossInstance);
                BossRushAudioManager.Instance?.PlayStinger(BossBgmEvents.RunVictory);
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战结算异常: " + e.Message);
            }
            finally
            {
                CleanupCampaignFinalBoss(false);
            }
        }

        /// <summary>
        /// 每帧检查决战是否该收尾：给既有模式让路，或者 Boss 已经不在场上。
        ///
        /// 【为什么必须有「Boss 不在场」这一条】
        ///   玩家打输（死亡回基地）或中途离场时，Boss 随场景销毁，
        ///   OnDeadEvent 永远不会来。少了这条收尾，campaignFinalBossActive
        ///   会永久卡在 true：召唤石不再生成、CanStart 恒 false、局内 HUD 常驻，
        ///   玩家再也没法重打终章——而终章恰恰是最可能打输的一场。
        /// </summary>
        internal void TickCampaignFinalBossYield()
        {
            if (!campaignFinalBossActive) return;
            try
            {
                if (IsAnyGameplayModeActiveForCampaign())
                {
                    DevLog(CampaignTuning.LogPrefix + "检测到既有模式启动，决战主动让路");
                    ShowMessage(L10n.T("决战已中止（有其他模式开始）", "Showdown aborted (another mode started)"));
                    CleanupCampaignFinalBoss(true);
                    return;
                }

                // 生成已出结果、实例却没了 = Boss 已被销毁（玩家死亡回基地、切场景、
                // 被别的系统清掉）。死亡回调不会再来，必须自己收尾。
                if (campaignFinalBossSpawnResolved && campaignFinalBossInstance == null)
                {
                    DevLog(CampaignTuning.LogPrefix + "决战 Boss 已不在场，自动收尾");
                    CleanupCampaignFinalBoss(false);
                }
            }
            catch (Exception)
            {
                // 每帧 tick：记日志会刷屏
            }
        }

        /// <summary>
        /// 决战收尾。destroyBoss 为真时连 Boss 实例一起销毁（让路场景）；
        /// 正常击杀时只退订与复位，尸体与掉落交给既有流程。
        /// </summary>
        internal void CleanupCampaignFinalBoss(bool destroyBoss)
        {
            try
            {
                if (campaignFinalBossInstance != null)
                {
                    try
                    {
                        if (campaignFinalBossInstance.Health != null)
                        {
                            campaignFinalBossInstance.Health.OnDeadEvent.RemoveListener(OnCampaignFinalBossDead);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 清理终章决战失败: " + e.Message);
                    }

                    if (destroyBoss)
                    {
                        try
                        {
                            UnityEngine.Object.Destroy(campaignFinalBossInstance.gameObject);
                        }
                        catch (Exception e)
                        {
                            ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 清理终章决战失败: " + e.Message);
                        }
                        BossRushAudioManager.Instance?.StopBossBGM(
                            BossBgmKeys.PhantomWitch, campaignFinalBossInstance);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战清理异常: " + e.Message);
            }
            finally
            {
                campaignFinalBossInstance = null;
                campaignFinalBossActive = false;
                campaignFinalBossSpawnResolved = false;
                // 作废还在飞的异步生成：它回来时会看到编号对不上，自行销毁产物
                campaignFinalBossRunId++;
                ResetCampaignFinalBossTracking();
            }
        }

        /// <summary>
        /// 决战收尾后清掉终章的局内追踪。
        ///
        /// 终章不经模式桥武装（它不依赖任何模式标志），因此桥上那条
        /// 「离开模式即 ResetSession」的路径永远轮不到它——不清的话，
        /// 打完决战回基地，HUD 会一直挂着「契约 · 冠军之影」。
        /// 「目标已达成待交付」的状态存在 CampaignProgressService 里，
        /// 不受这次复位影响，玩家照常能交付。
        ///
        /// 只清终章那一份：别的章节的进度是模式桥在管的，不能顺手抹掉。
        /// </summary>
        private void ResetCampaignFinalBossTracking()
        {
            try
            {
                string armed = CampaignObjectiveTracker.ArmedChapterId;
                if (string.IsNullOrEmpty(armed)) return;

                CampaignChapterDef def = CampaignContentCatalog.GetChapter(armed);
                if (def == null) return;
                if (!string.Equals(def.Mode, CampaignContentCatalog.ModeFinal, StringComparison.Ordinal)) return;

                CampaignObjectiveTracker.ResetSession();
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 复位终章追踪失败: " + e.Message);
            }
        }

        #endregion
    }
}
