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

        /// <summary>染色用的属性块。复用一份，避免每个 renderer 新建。</summary>
        private readonly MaterialPropertyBlock campaignFinalBossColorBlock = new MaterialPropertyBlock();

        private static readonly int CampaignBossColorProperty = Shader.PropertyToID("_Color");
        private static readonly int CampaignBossTintColorProperty = Shader.PropertyToID("_TintColor");
        private static readonly int CampaignBossBaseColorProperty = Shader.PropertyToID("_BaseColor");

        #endregion

        #region 只读

        /// <summary>决战是否进行中。</summary>
        internal bool IsCampaignFinalBossActive { get { return campaignFinalBossActive; } }

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

                if (!IsCurrentSceneValidBossRushArena()) return false;

                return !IsAnyGameplayModeActiveForCampaign();
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

        private bool ShouldCampaignFinalBossAltarExist()
        {
            if (!IsCampaignConfiguredEnabled()) return false;
            if (campaignFinalBossActive) return false;
            if (!IsCurrentSceneValidBossRushArena()) return false;
            if (IsAnyGameplayModeActiveForCampaign()) return false;

            CampaignChapterDef def = CampaignProgressService.GetActiveChapterDef();
            return def != null
                && string.Equals(def.Mode, CampaignContentCatalog.ModeFinal, StringComparison.Ordinal);
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
                CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeFinal);
                StartCampaignFinalBossAsync().Forget();
            }
            catch (Exception e)
            {
                campaignFinalBossActive = false;
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战启动失败: " + e.Message);
            }
        }

        private async UniTask StartCampaignFinalBossAsync()
        {
            try
            {
                ShowMessage(L10n.T("冠军之影现身了……", "The Shadow of the Champion appears..."));

                Vector3 position = ResolveCampaignFinalBossSpawnPosition();

                // notifyBossRushOnFailure:false —— 失败不能去通知标准竞技场流程，
                // 那会在没有波次的情况下推进它的状态机
                CharacterMainControl boss = await SpawnPhantomWitch(position, false);
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

                BossRushAudioManager.Instance?.PlayBossBGM(BossBgmKeys.PhantomWitch);
            }
            catch (Exception e)
            {
                campaignFinalBossActive = false;
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

            // 2) 体型：纯视觉放大，不动碰撞判定口径
            try
            {
                boss.transform.localScale = boss.transform.localScale * CampaignTuning.FinalBossScale;
            }
            catch (Exception e)
            {
                DevLog(CampaignTuning.LogPrefix + "[WARNING] 决战体型缩放失败: " + e.Message);
            }

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
            try
            {
                DevLog(CampaignTuning.LogPrefix + "冠军之影已被击败");
                ShowMessage(L10n.T("冠军之影已被击败", "The Shadow of the Champion has fallen"));

                CampaignObjectiveTracker.ReportFinalBossKill();
                BossRushAudioManager.Instance?.StopBossBGM(BossBgmKeys.PhantomWitch);
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
        /// 每帧检查是否需要给既有模式让路。
        /// 玩家在决战途中开了任何模式：主动中止并清掉自己的 Boss。
        /// </summary>
        internal void TickCampaignFinalBossYield()
        {
            if (!campaignFinalBossActive) return;
            try
            {
                if (!IsAnyGameplayModeActiveForCampaign()) return;

                DevLog(CampaignTuning.LogPrefix + "检测到既有模式启动，决战主动让路");
                ShowMessage(L10n.T("决战已中止（有其他模式开始）", "Showdown aborted (another mode started)"));
                CleanupCampaignFinalBoss(true);
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
                        BossRushAudioManager.Instance?.StopBossBGM(BossBgmKeys.PhantomWitch);
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
            }
        }

        #endregion
    }
}
