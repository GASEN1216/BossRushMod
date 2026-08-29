// ============================================================================
// PetNestDebugProbe.cs - 遗种巢 PoC 闸门实机探针（实施计划 步骤 0）
// ============================================================================
// 只服务于步骤 0 的单点闸门验证，不参与正式玩法链路：
//   1) 跟随（含远距传送）
//   2) 不伤主人、不被主人伤（玩家方阵营）
//   3) 自动索敌作战（原生行为树）
//   4) 致死钳 1 血（致死钳制链第四消费者）
//   5) 反射借席激活 PetProxy 背包链（inventory 跟随幼体、容量随玩家 PetCapcity）
// 附带探测：ItemPicker.Instance 存在性、当前场景官方宠物席位占用情况。
//
// 纪律：
//   - 单例 handle，重复召唤先回收旧的（不留孤儿）；
//   - 只在 BOSSRUSH 调试入口触发，不订阅任何全局事件、不常驻 Update；
//   - 全程 no-throw，探测失败只返回文本，不影响宿主。
// ============================================================================

using System;
using System.Text;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 遗种巢 PoC 探针。步骤 0 闸门通过后本文件保留为调试工具，不进入正式管线。
    /// </summary>
    internal static class PetNestDebugProbe
    {
        #region 状态

        private static PetNestCompanionHandle _probeHandle;
        private static string _lastStatus = "尚未召唤 PoC 幼体";
        private static bool _spawnInFlight;

        /// <summary>最近一次操作结果文本。</summary>
        internal static string LastStatus { get { return _lastStatus; } }

        /// <summary>当前是否有 PoC 幼体在场。</summary>
        internal static bool HasProbeCompanion
        {
            get { return _probeHandle != null && _probeHandle.Character != null; }
        }

        #endregion

        #region 召唤 / 回收

        /// <summary>
        /// 召唤一只 PoC 幼体。lineageKey 为空时取 Boss 池过滤结果的第一个官方 Boss。
        /// </summary>
        internal static void SpawnProbeCompanion(ModBehaviour owner, string lineageKey)
        {
            if (_spawnInFlight)
            {
                _lastStatus = "PoC 幼体正在生成中，请稍候";
                return;
            }
            // 同步先把状态写成"正在召唤"：下面是 fire-and-forget 异步，
            // F3 按钮回调紧接着就会读 LastStatus，不写这一行玩家会看到上一次的状态
            // （初始值"尚未召唤 PoC 幼体"），按钮看起来像没反应。
            _lastStatus = "正在召唤 PoC 幼体……（完成后按「输出探针报告」看结果）";
            SpawnProbeCompanionAsync(owner, lineageKey).Forget();
        }

        private static async UniTaskVoid SpawnProbeCompanionAsync(ModBehaviour owner, string lineageKey)
        {
            _spawnInFlight = true;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    _lastStatus = "玩家未就绪，无法召唤 PoC 幼体";
                    return;
                }

                string resolvedKey = lineageKey;
                if (string.IsNullOrEmpty(resolvedKey))
                {
                    resolvedKey = ResolveFirstAvailableLineageKey();
                }
                if (string.IsNullOrEmpty(resolvedKey))
                {
                    _lastStatus = "未找到任何可用血脉 preset";
                    return;
                }

                CharacterRandomPreset source = PetNestCompanionSpawner.ResolveCompanionSourcePreset(resolvedKey);
                if (source == null)
                {
                    _lastStatus = "血脉 preset 解析失败（fail-closed）: " + resolvedKey;
                    return;
                }

                DespawnProbeCompanion();

                Vector3 stagingPos = player.transform.position + PetNestCompanionSpawner.StagingOffset;
                PetNestCompanionHandle handle = await PetNestCompanionSpawner.CreateIsolatedAsync(
                    source, resolvedKey, PetNestCompanionSpawner.DefaultModelScale, stagingPos);

                if (handle == null)
                {
                    _lastStatus = "PoC 幼体创建失败: " + resolvedKey;
                    return;
                }

                // await 之后重验：玩家可能已切图/死亡
                CharacterMainControl playerNow = CharacterMainControl.Main;
                if (playerNow == null || playerNow != player)
                {
                    PetNestCompanionSpawner.CleanupOnce(handle);
                    _lastStatus = "生成期间玩家状态变化，已回收 PoC 幼体";
                    return;
                }

                string failureReasonId;
                Vector3 spawnPos = playerNow.transform.position + PetNestCompanionSpawner.SpawnOffset;
                if (!PetNestCompanionSpawner.TryActivate(handle, spawnPos, playerNow, owner, null, out failureReasonId))
                {
                    PetNestCompanionSpawner.CleanupOnce(handle);
                    _lastStatus = "PoC 幼体激活失败: " + failureReasonId;
                    return;
                }

                _probeHandle = handle;

                string yieldReason;
                bool borrowed = PetNestPetProxyBridge.TryBorrowSeat(handle.Character, out yieldReason);
                _lastStatus = "已召唤 PoC 幼体: " + resolvedKey
                    + "，捡漏背包借席=" + (borrowed ? "成功" : ("让席/失败(" + yieldReason + ")"));
            }
            catch (Exception e)
            {
                _lastStatus = "PoC 幼体召唤异常: " + e.Message;
                ModBehaviour.DevLog("[PetNest] PoC 幼体召唤异常: " + e);
            }
            finally
            {
                _spawnInFlight = false;
            }
        }

        /// <summary>回收 PoC 幼体并还席。幂等。</summary>
        internal static void DespawnProbeCompanion()
        {
            try
            {
                PetNestPetProxyBridge.ReleaseSeat();
                if (_probeHandle != null)
                {
                    PetNestCompanionSpawner.CleanupOnce(_probeHandle);
                    _probeHandle = null;
                    _lastStatus = "已回收 PoC 幼体并还席";
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] PoC 幼体回收异常: " + e.Message);
            }
        }

        /// <summary>
        /// 取第一个可用血脉。探针不经 ModBehaviour 门面（步骤 3 之前还没有 PetNest
        /// 只读门面），直接读 ObjectCache 的 preset 缓存挑一个 Boss preset。
        /// 正式管线的资格口径在步骤 1 由 PetNestLineageCatalog 承接。
        /// </summary>
        private static string ResolveFirstAvailableLineageKey()
        {
            try
            {
                CharacterRandomPreset[] all = ObjectCache.GetCharacterPresets();
                if (all == null) return null;
                for (int i = 0; i < all.Length; i++)
                {
                    CharacterRandomPreset p = all[i];
                    if (p == null || string.IsNullOrEmpty(p.nameKey)) continue;
                    if (!p.isBoss) continue;
                    return p.nameKey;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 解析首个可用血脉失败: " + e.Message);
            }
            return null;
        }

        #endregion

        #region 探测报告

        /// <summary>
        /// PoC 闸门报告：五件事的可观测量 + 两项附带探测。返回多行中文文本。
        /// </summary>
        internal static string BuildProbeReport()
        {
            StringBuilder sb = new StringBuilder(512);
            sb.AppendLine("===== 遗种巢 PoC 闸门探针 =====");
            sb.AppendLine("最近操作: " + _lastStatus);

            // 场景与官方设施
            sb.AppendLine("场景: " + SafeSceneName() + "，基地图=" + SafeIsBaseLevel());
            sb.AppendLine("ItemPicker.Instance: " + SafeItemPicker());
            sb.AppendLine("宠物席位: " + PetNestPetProxyBridge.DescribeSeat());
            sb.AppendLine("玩家 PetCapcity: " + SafePetCapcity());

            // 随从现状
            if (!HasProbeCompanion)
            {
                sb.AppendLine("随从: 不在场");
                return sb.ToString();
            }

            CharacterMainControl companion = _probeHandle.Character;
            CharacterMainControl player = CharacterMainControl.Main;
            sb.AppendLine("随从血脉: " + _probeHandle.LineageKey);
            sb.AppendLine("随从阵营: " + SafeTeam(companion)
                + "，对玩家敌对=" + SafeIsEnemyToPlayer(companion));
            sb.AppendLine("随从血量: " + SafeHealth(companion));
            sb.AppendLine("与玩家距离: " + SafeDistance(companion, player));
            sb.AppendLine("leader 已写: " + SafeLeaderBound(companion, player));
            sb.AppendLine("当前目标: " + SafeSearchedEnemy(companion));
            sb.AppendLine("modelRoot 缩放: " + SafeModelScale(companion));
            sb.AppendLine("致死钳制 armed: " + PetNestCompanionAgent.IsCompanionArmed
                + "，累计钳血次数=" + PetNestCompanionAgent.LethalClampHitCount);
            return sb.ToString();
        }

        private static string SafeSceneName()
        {
            try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
            catch (Exception) { return "unknown"; }
        }

        private static string SafeIsBaseLevel()
        {
            try
            {
                return LevelManager.Instance != null
                    ? (LevelManager.Instance.IsBaseLevel ? "true" : "false")
                    : "no-levelmanager";
            }
            catch (Exception) { return "unknown"; }
        }

        private static string SafeItemPicker()
        {
            try { return ItemPicker.Instance != null ? "存在" : "不存在"; }
            catch (Exception) { return "查询失败"; }
        }

        private static string SafePetCapcity()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                return player != null ? player.PetCapcity.ToString() : "玩家未就绪";
            }
            catch (Exception) { return "查询失败"; }
        }

        private static string SafeTeam(CharacterMainControl c)
        {
            try { return c.Team.ToString(); }
            catch (Exception) { return "unknown"; }
        }

        private static string SafeIsEnemyToPlayer(CharacterMainControl c)
        {
            try { return Team.IsEnemy(Teams.player, c.Team) ? "是(异常)" : "否(正确)"; }
            catch (Exception) { return "unknown"; }
        }

        private static string SafeHealth(CharacterMainControl c)
        {
            try
            {
                Health h = c.Health;
                if (h == null) return "无 Health";
                return h.CurrentHealth.ToString("F0") + "/" + h.MaxHealth.ToString("F0")
                    + (h.Invincible ? "(无敌)" : "");
            }
            catch (Exception) { return "查询失败"; }
        }

        private static string SafeDistance(CharacterMainControl c, CharacterMainControl player)
        {
            try
            {
                if (player == null) return "玩家未就绪";
                return Vector3.Distance(c.transform.position, player.transform.position).ToString("F1") + "m";
            }
            catch (Exception) { return "查询失败"; }
        }

        private static string SafeLeaderBound(CharacterMainControl c, CharacterMainControl player)
        {
            try
            {
                AICharacterController ai = c.GetComponentInChildren<AICharacterController>();
                if (ai == null) return "无 AICharacterController";
                if (ai.leader == null) return "未写";
                return ai.leader == player ? "是(玩家)" : ("是(" + ai.leader.gameObject.name + ")");
            }
            catch (Exception) { return "查询失败"; }
        }

        private static string SafeSearchedEnemy(CharacterMainControl c)
        {
            try
            {
                AICharacterController ai = c.GetComponentInChildren<AICharacterController>();
                if (ai == null) return "无 AICharacterController";
                return ai.searchedEnemy != null ? ai.searchedEnemy.gameObject.name : "无";
            }
            catch (Exception) { return "查询失败"; }
        }

        private static string SafeModelScale(CharacterMainControl c)
        {
            try
            {
                Transform modelRoot = c.modelRoot;
                return modelRoot != null ? modelRoot.localScale.x.ToString("F2") : "无 modelRoot";
            }
            catch (Exception) { return "查询失败"; }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            _probeHandle = null;
            _spawnInFlight = false;
            _lastStatus = "尚未召唤 PoC 幼体";
        }

        #endregion
    }
}
