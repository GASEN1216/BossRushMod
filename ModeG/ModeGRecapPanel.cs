using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// Mode G 终局呈现层（任务 #9 可玩性 UX 补强）。
    ///
    /// 职责：
    /// - 失败横幅/失败 recap 的宿敌预告文案组装（复仇钩子；§15 三分支：
    ///   已写入下局宿敌 Rank / 宿敌记录受版本保护 / 未形成新宿敌）；
    /// - 结算 recap 面板（复用 ZombieModeUIHelper 模态件，不新建 UI 框架）：
    ///   奖励档 near-miss、三轴尝试/破解计数、印章/图鉴 near-miss、累计纪录；
    /// - 弹药禁令归因文案组装（上局弹种威胁占比）。
    ///
    /// 硬约束：
    /// - 全部新文本走 L10n.T("BossRush_ModeG_*") key，代码只持 key；
    /// - 全程防御式 try/catch，呈现失败不得拖崩宿主（静默降级无面板）；
    /// - recap 面板仅终局一次性构建（非热路径），自动倒计时关闭不阻塞官方死亡流程。
    /// </summary>
    internal static class ModeGRecapPanel
    {
        #region Tunables（owner tunable）

        /// <summary>面板宽</summary>
        private const float PanelWidth = 820f;
        /// <summary>面板高</summary>
        private const float PanelHeight = 620f;
        /// <summary>失败 recap 自动关闭秒数（不阻塞死亡/回城流程）</summary>
        private const float DefeatAutoCloseSeconds = 14f;
        /// <summary>胜利 recap 自动关闭秒数</summary>
        private const float VictoryAutoCloseSeconds = 24f;
        /// <summary>图鉴里程碑（累计宿敌击败数；owner tunable，纯呈现）</summary>
        private static readonly int[] CodexMilestones = { 1, 3, 5, 10 };

        #endregion

        #region Nemesis Attribution Outcome（失败宿敌预告）

        /// <summary>宿敌归因结果（ModeGDeathRouting 写入/升级后返回）。</summary>
        internal struct NemesisAttribution
        {
            /// <summary>是否成功写入/升级宿敌记录</summary>
            public bool written;
            /// <summary>归因是否因存储故障被阻断（击杀者已知但记录未变更）</summary>
            public bool storeBlocked;
            /// <summary>击杀者 Boss preset key（归因成功或 storeBlocked 时非空）</summary>
            public string bossKey;
            /// <summary>写入后的持久 Rank（仅 written 时有效；读取内存提交后的新值）</summary>
            public int rank;
        }

        /// <summary>
        /// 组装宿敌预告尾行（失败横幅与失败 recap 共用）。
        /// written  -> 下局宿敌 &lt;Boss&gt; Rank N；
        /// blocked  -> 击杀者 &lt;Boss&gt; · 宿敌记录受版本保护，未变更；
        /// 其余     -> 未形成新宿敌。
        /// </summary>
        internal static string ComposeNemesisPreviewLine(NemesisAttribution attribution)
        {
            try
            {
                if (attribution.written && !string.IsNullOrEmpty(attribution.bossKey))
                {
                    return L10n.T("BossRush_ModeG_NextNemesis") + " "
                        + ModeGEncounterVariation.GetManagedBossDisplayName(attribution.bossKey)
                        + " " + L10n.T("BossRush_ModeG_RankWord") + " " + attribution.rank;
                }
                if (attribution.storeBlocked && !string.IsNullOrEmpty(attribution.bossKey))
                {
                    return L10n.T("BossRush_ModeG_KillerWord") + " "
                        + ModeGEncounterVariation.GetManagedBossDisplayName(attribution.bossKey)
                        + " · " + L10n.T("BossRush_ModeG_NemesisProtected");
                }
                return L10n.T("BossRush_ModeG_NoNewNemesis");
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 组装失败横幅全文（宿敌未竟 · 第 X/9 波 · Resolve Y/11 · 宿敌预告行）。
        /// </summary>
        internal static string ComposeDefeatBanner(int waveNumber, int resolve, NemesisAttribution attribution)
        {
            try
            {
                string line1 = "<color=#B22222>" + L10n.T("BossRush_ModeG_DefeatTitle") + "</color> "
                    + L10n.T("BossRush_ModeG_WaveWord") + " " + waveNumber + L10n.T("BossRush_ModeG_WaveOfNine")
                    + " · Resolve " + resolve + "/" + ModeGAdaptiveCombat.MaxResolveTotal;
                string line2 = ComposeNemesisPreviewLine(attribution);
                return string.IsNullOrEmpty(line2) ? line1 : line1 + "\n" + line2;
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region Ammo Ban Attribution（弹药禁令归因文案）

        /// <summary>
        /// 弹药禁令归因行：「上局你的 &lt;弹药名&gt; 贡献了 X% 威胁」。
        /// threatSharePercent &lt;=0 时返回空串（调用方跳过归因行）。
        /// </summary>
        internal static string ComposeBanAttributionLine(string ammoName, int threatSharePercent)
        {
            try
            {
                if (string.IsNullOrEmpty(ammoName) || threatSharePercent <= 0) return string.Empty;
                return L10n.T("BossRush_ModeG_BanAttrPrefix") + ammoName
                    + L10n.T("BossRush_ModeG_BanAttrMid") + threatSharePercent
                    + L10n.T("BossRush_ModeG_BanAttrTail");
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 弹药本地化展示名（metadata 缺失时稳定回退「弹药 #TypeID」，不持有 Item）。
        /// </summary>
        internal static string GetAmmoDisplayName(int ammoTypeId)
        {
            try
            {
                ItemStatsSystem.ItemMetaData meta = ItemStatsSystem.ItemAssetsCollection.GetMetaData(ammoTypeId);
                if (meta.id == ammoTypeId && !string.IsNullOrEmpty(meta.DisplayName))
                {
                    return meta.DisplayName;
                }
            }
            catch { /* metadata 读取失败走稳定回退 */ }
            return L10n.T("BossRush_ModeG_AmmoFallback") + " #" + ammoTypeId;
        }

        #endregion

        #region Recap Panel（终局结算页）

        private static GameObject _activeRoot;

        /// <summary>
        /// 显示终局 recap 面板（Victory/Defeat 各一次；幂等单实例）。
        /// 失败静默降级（无面板不阻塞结算）。
        /// </summary>
        internal static void Show(ModeGRuntimeModule module, ModeGBattleResult result, string nemesisPreviewLine)
        {
            try
            {
                if (module == null) return;
                if (result != ModeGBattleResult.Victory && result != ModeGBattleResult.Defeat) return;

                DismissActive();

                GameObject root = new GameObject("ModeG_Recap");
                UnityEngine.Object.DontDestroyOnLoad(root);

                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 940; // 低于入口确认页(950)，高于 HUD(900)
                CanvasScaler scaler = root.AddComponent<CanvasScaler>();
                ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
                root.AddComponent<GraphicRaycaster>();

                GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                    "ModeG_RecapSurface", root.transform, new Vector2(PanelWidth, PanelHeight),
                    new Color(0.72f, 0.53f, 0.04f, 1f));
                Transform st = surface.transform;

                // 标题
                bool victory = result == ModeGBattleResult.Victory;
                ZombieModeUIHelper.CreateText("Title", st,
                    victory ? L10n.T("BossRush_ModeG_Recap_VictoryTitle") : L10n.T("BossRush_ModeG_Recap_DefeatTitle"),
                    32f, new Vector2(0f, 262f), new Vector2(PanelWidth - 60f, 54f),
                    TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);

                // 概要：第 X/9 波 · Resolve Y/11（± 新纪录）
                int wave = module.State != null ? module.State.waveEpoch + 1 : 0;
                int resolve = module.Adaptive != null ? module.Adaptive.TotalResolve : 0;
                string summary = L10n.T("BossRush_ModeG_WaveWord") + " " + wave + L10n.T("BossRush_ModeG_WaveOfNine")
                    + " · Resolve " + resolve + "/" + ModeGAdaptiveCombat.MaxResolveTotal;
                if (IsNewBestWave(module, wave))
                {
                    summary += " · <color=#B8860B>" + L10n.T("BossRush_ModeG_NewRecord") + "</color>";
                }
                ZombieModeUIHelper.CreateText("Summary", st, summary,
                    20f, new Vector2(0f, 214f), new Vector2(PanelWidth - 80f, 34f),
                    TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);

                // 奖励档 near-miss：当前档件数 + 距下一档还差 X Resolve
                ZombieModeUIHelper.CreateText("RewardGap", st, ComposeRewardGapLine(resolve),
                    18f, new Vector2(0f, 178f), new Vector2(PanelWidth - 80f, 30f),
                    TextAlignmentOptions.Center, ZombieModeUIHelper.TextSecondaryColor);

                // 失败 recap：宿敌预告行（复仇钩子）
                if (!victory && !string.IsNullOrEmpty(nemesisPreviewLine))
                {
                    ZombieModeUIHelper.CreateText("NemesisPreview", st, nemesisPreviewLine,
                        18f, new Vector2(0f, 144f), new Vector2(PanelWidth - 80f, 30f),
                        TextAlignmentOptions.Center, new Color(1f, 0.55f, 0f, 1f));
                }

                // 三轴本局尝试/破解计数
                ZombieModeUIHelper.CreateText("Axes", st, ComposeAxisLines(module),
                    17f, new Vector2(0f, 72f), new Vector2(PanelWidth - 80f, 96f),
                    TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);

                // 本局契约：名称 · 达成状态
                ZombieModeUIHelper.CreateText("Contract", st, ComposeContractLine(module),
                    17f, new Vector2(0f, 2f), new Vector2(PanelWidth - 80f, 28f),
                    TextAlignmentOptions.Center, ZombieModeUIHelper.TextPrimaryColor);

                // 印章目标 + 图鉴 near-miss（读 profile 累计数据）
                ZombieModeUIHelper.CreateText("Seal", st, ComposeSealLine(),
                    16f, new Vector2(0f, -32f), new Vector2(PanelWidth - 80f, 26f),
                    TextAlignmentOptions.Center, ZombieModeUIHelper.TextSecondaryColor);
                ZombieModeUIHelper.CreateText("Codex", st, ComposeCodexLine(),
                    16f, new Vector2(0f, -60f), new Vector2(PanelWidth - 80f, 26f),
                    TextAlignmentOptions.Center, ZombieModeUIHelper.TextSecondaryColor);

                // 累计纪录摘要
                ZombieModeUIHelper.CreateText("Profile", st, ComposeProfileLine(),
                    16f, new Vector2(0f, -96f), new Vector2(PanelWidth - 80f, 26f),
                    TextAlignmentOptions.Center, ZombieModeUIHelper.TextSecondaryColor);

                // 关闭按钮（另有自动倒计时关闭）
                Button closeButton = ZombieModeUIHelper.CreateButton(
                    "Close", st, L10n.T("BossRush_ModeG_Recap_Close"),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -230f), new Vector2(200f, 52f),
                    ZombieModeUIHelper.ModalSurfaceColor, 20f, new Vector2(180f, 44f),
                    DismissActive, true);
                ZombieModeUIHelper.ApplyButtonColors(closeButton,
                    ZombieModeUIHelper.ModalSurfaceColor, ZombieModeUIHelper.WarningHoverColor,
                    ZombieModeUIHelper.DisabledColor);

                // 自动关闭（不阻塞官方死亡/回城流程）
                RecapAutoClose autoClose = root.AddComponent<RecapAutoClose>();
                autoClose.remainingSeconds = victory ? VictoryAutoCloseSeconds : DefeatAutoCloseSeconds;

                _activeRoot = root;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] recap 面板创建失败（降级无面板）: " + e.Message);
            }
        }

        /// <summary>关闭当前 recap 面板（幂等 no-throw）。</summary>
        internal static void DismissActive()
        {
            try
            {
                if (_activeRoot != null) UnityEngine.Object.Destroy(_activeRoot);
            }
            catch { /* no-throw */ }
            _activeRoot = null;
        }

        /// <summary>自动倒计时关闭组件（面板 root 自驱动，无外部 owner 依赖）。</summary>
        internal sealed class RecapAutoClose : MonoBehaviour
        {
            internal float remainingSeconds;

            private void Update()
            {
                try
                {
                    remainingSeconds -= Time.unscaledDeltaTime;
                    if (remainingSeconds <= 0f) DismissActive();
                }
                catch { /* no-throw */ }
            }
        }

        #endregion

        #region Line Composition（near-miss 呈现；仅终局调用一次）

        /// <summary>本局波次是否刷新个人最佳波次（profile 已含本局记账）。</summary>
        private static bool IsNewBestWave(ModeGRuntimeModule module, int wave)
        {
            try
            {
                if (wave <= 0) return false;
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.Current;
                return profile != null && wave >= profile.bestWaveReached;
            }
            catch { return false; }
        }

        /// <summary>
        /// 奖励档 near-miss：奖励件数分带 0-2→6 / 3-5→7 / 6-8→8 / 9→9 / 10-11→10。
        /// </summary>
        private static string ComposeRewardGapLine(int resolve)
        {
            int current = ModeGRewardTransaction.GetRewardItemCount(resolve);
            int nextBoundary = GetNextBandBoundary(resolve);
            if (nextBoundary < 0)
            {
                return L10n.T("BossRush_ModeG_Recap_Rewards") + " " + current
                    + " " + L10n.T("BossRush_ModeG_Recap_ItemsUnit")
                    + " · " + L10n.T("BossRush_ModeG_Recap_RewardMax");
            }
            int next = ModeGRewardTransaction.GetRewardItemCount(nextBoundary);
            return L10n.T("BossRush_ModeG_Recap_Rewards") + " " + current
                + " " + L10n.T("BossRush_ModeG_Recap_ItemsUnit")
                + " · " + L10n.T("BossRush_ModeG_Recap_ResolveGap") + " "
                + (nextBoundary - resolve) + " Resolve → "
                + next + " " + L10n.T("BossRush_ModeG_Recap_ItemsUnit");
        }

        /// <summary>下一奖励档的 Resolve 门槛（3/6/9/10）；已满档返回 -1。</summary>
        private static int GetNextBandBoundary(int resolve)
        {
            if (resolve < 3) return 3;
            if (resolve < 6) return 6;
            if (resolve < 9) return 9;
            if (resolve < 10) return 10;
            return -1;
        }

        /// <summary>三轴本局尝试/破解计数（尝试=该轴反制生效的波数；破解=该轴 Resolve 数）。</summary>
        private static string ComposeAxisLines(ModeGRuntimeModule module)
        {
            ModeGAdaptiveCombat adaptive = module.Adaptive;
            int distanceAttempts = module.AxisAttemptDistance;
            int ammoAttempts = module.AxisAttemptAmmo;
            int attributeAttempts = module.AxisAttemptAttribute;
            int distanceBreaks = adaptive != null ? adaptive.ResolveDistance : 0;
            int ammoBreaks = adaptive != null ? adaptive.ResolveAmmo : 0;
            int attributeBreaks = adaptive != null ? adaptive.ResolveAttribute : 0;

            string attemptsWord = " " + L10n.T("BossRush_ModeG_Axis_Attempts");
            string breaksWord = " " + L10n.T("BossRush_ModeG_Axis_Breaks");
            return L10n.T("BossRush_ModeG_AxisDistance") + ": " + distanceAttempts + attemptsWord
                    + " · " + distanceBreaks + breaksWord
                + "\n" + L10n.T("BossRush_ModeG_AxisAmmo") + ": " + ammoAttempts + attemptsWord
                    + " · " + ammoBreaks + breaksWord
                + "\n" + L10n.T("BossRush_ModeG_AxisAttribute") + ": " + attributeAttempts + attemptsWord
                    + " · " + attributeBreaks + breaksWord;
        }

        /// <summary>本局契约达成状态（终局进度快照评估）。</summary>
        private static string ComposeContractLine(ModeGRuntimeModule module)
        {
            try
            {
                int contractId = module.State != null ? module.State.fateContractId : -1;
                if (contractId < 0) return string.Empty;
                ModeGFateContract.ContractDef def = ModeGFateContract.GetById(contractId);
                bool fulfilled = ModeGFateContract.Evaluate(contractId, module.BuildContractProgress());
                return L10n.T("BossRush_ModeG_Recap_Contract") + ": " + def.GetDisplayName()
                    + " · " + (fulfilled
                        ? "<color=#2E8B57>" + L10n.T("BossRush_ModeG_Recap_ContractDone") + "</color>"
                        : "<color=#B22222>" + L10n.T("BossRush_ModeG_Recap_ContractFailed") + "</color>");
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// 下一枚印章目标行（入口确认页与 recap 共用：印章条件 + 当前契约连胜）。
        /// 失败返回空串（调用方隐藏该行）。
        /// </summary>
        internal static string ComposeEntrySealLine()
        {
            try
            {
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.LoadOrInit();
                int streak = profile != null ? profile.contractStreak : 0;
                return L10n.T("BossRush_ModeG_Seal_EntryCondition")
                    + " · " + L10n.T("BossRush_ModeG_Seal_Streak") + " " + streak;
            }
            catch { return string.Empty; }
        }

        /// <summary>印章目标（契约连胜；下一枚印章 = 再达成 1 次契约）。</summary>
        private static string ComposeSealLine()
        {
            try
            {
                string condition = ComposeEntrySealLine();
                if (string.IsNullOrEmpty(condition)) return string.Empty;
                return L10n.T("BossRush_ModeG_Seal_NextLabel") + ": " + condition;
            }
            catch { return string.Empty; }
        }

        /// <summary>图鉴 near-miss（累计宿敌击败 -> 里程碑；纯呈现，不授予任何奖励）。</summary>
        private static string ComposeCodexLine()
        {
            try
            {
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.LoadOrInit();
                int kills = profile != null ? profile.totalNemesisDefeated : 0;
                int nextMilestone = -1;
                for (int i = 0; i < CodexMilestones.Length; i++)
                {
                    if (kills < CodexMilestones[i]) { nextMilestone = CodexMilestones[i]; break; }
                }
                if (nextMilestone < 0)
                {
                    return L10n.T("BossRush_ModeG_Codex") + ": " + L10n.T("BossRush_ModeG_Codex_Complete");
                }
                return L10n.T("BossRush_ModeG_Codex") + ": "
                    + L10n.T("BossRush_ModeG_Codex_Need") + " " + (nextMilestone - kills)
                    + " " + L10n.T("BossRush_ModeG_Codex_More");
            }
            catch { return string.Empty; }
        }

        /// <summary>累计纪录摘要（总场次/胜利/最佳波次/宿敌击败）。</summary>
        private static string ComposeProfileLine()
        {
            try
            {
                ModeGProfilePersistence.ProfileDto profile = ModeGProfilePersistence.LoadOrInit();
                if (profile == null) return string.Empty;
                return L10n.T("BossRush_ModeG_TotalRuns") + " " + profile.totalRuns
                    + " · " + L10n.T("BossRush_ModeG_TotalVictories") + " " + profile.totalVictories
                    + " · " + L10n.T("BossRush_ModeG_BestWave") + " " + profile.bestWaveReached
                    + " · " + L10n.T("BossRush_ModeG_NemesisKills") + " " + profile.totalNemesisDefeated;
            }
            catch { return string.Empty; }
        }

        #endregion
    }
}
