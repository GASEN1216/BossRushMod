// ============================================================================
// PermanentDuckNpcDebug.cs - 永久捏脸 NPC 的 F3 测试入口
// ============================================================================
// 模块说明：
//   永久 NPC 正常是靠蓝图里的 `scenes` 白名单在指定场景自动生成的。
//   但在还没决定它住哪张图之前，需要一条「当场生成来验证」的路子。
//
//   另一个必要性：现有的婚姻/好感度调试 UI
//   （DebugAndTools/MarriageTestDebugUI.cs、F3DebugCheatMenu 的 NPC/剧情页）
//   **全部写死了叮当和护士的 NPC_ID**，新永久 NPC 用不上。
//   所以这里配一套自足的：生成 / 回收 / 好感度拉满 / 好感度清零 / 输出状态。
//
//   单独成文件而不是塞进 DuckNpcDebugProbe：那个文件已经 1100+ 行，
//   接近 LargeFileBudgetGuard 对新文件 1200 行的预算。
//
//   仅供 F3 调用，不参与任何正常玩法路径。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 永久捏脸 NPC 调试工具。
    /// </summary>
    internal static class PermanentDuckNpcDebug
    {
        private const string LogPrefix = "[PermanentDuckNpcDebug]";

        /// <summary>生成时相对玩家的前方偏移。</summary>
        private const float SpawnForwardOffset = 2.5f;

        private static string _lastStatus = "尚未操作永久 NPC";
        private static bool _spawnInFlight;

        internal static string LastStatus
        {
            get { return _lastStatus; }
        }

        // ====================================================================
        // 目标解析
        // ====================================================================

        /// <summary>
        /// 取要测试的永久 NPC 蓝图。当前取第一条 —— 示例阶段只有一只。
        /// </summary>
        private static DuckNpcBlueprint ResolveTarget()
        {
            List<DuckNpcBlueprint> permanents = PermanentDuckNpcRegistry.GetAllPermanent();
            if (permanents.Count == 0)
            {
                return null;
            }
            return permanents[0];
        }

        // ====================================================================
        // 生成 / 回收
        // ====================================================================

        /// <summary>在玩家前方生成永久 NPC（无视蓝图的 scenes 白名单）。</summary>
        internal static void SpawnHere()
        {
            if (_spawnInFlight)
            {
                _lastStatus = "永久 NPC 正在生成中，请稍候";
                return;
            }

            DuckNpcBlueprint blueprint = ResolveTarget();
            if (blueprint == null)
            {
                _lastStatus = "DuckNpcs.json 里没有 isPermanent 的蓝图";
                return;
            }

            if (PermanentDuckNpcRegistry.GetInstance(blueprint.id) != null)
            {
                _lastStatus = "永久 NPC 已存在（" + blueprint.id + "），请先回收";
                return;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                _lastStatus = "找不到玩家，无法生成";
                return;
            }

            Vector3 forward = main.transform.forward;
            Vector3 pos = main.transform.position + forward * SpawnForwardOffset;

            // 贴地，避免生在斜坡里
            try
            {
                RaycastHit hit;
                if (Physics.Raycast(pos + Vector3.up, Vector3.down, out hit, 5f))
                {
                    pos = hit.point + new Vector3(0f, 0.1f, 0f);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 落点贴地失败: " + e.Message);
            }

            _lastStatus = "正在生成永久 NPC（" + blueprint.id + "）……";
            _spawnInFlight = true;
            SpawnHereAsync(blueprint, pos).Forget();
        }

        private static async UniTaskVoid SpawnHereAsync(DuckNpcBlueprint blueprint, Vector3 pos)
        {
            try
            {
                // 模块可能还没跑过（比如当前场景不在白名单里），
                // 这里补一次注册，否则好感度/对话/送礼全部拿不到配置。
                PermanentDuckNpcModule.RegisterAllAffinityConfigs();

                CharacterMainControl npc =
                    await PermanentDuckNpcModule.ForceSpawnAtAsync(blueprint.id, pos, false);

                if (npc == null)
                {
                    _lastStatus = "永久 NPC 生成失败，详见日志";
                    ModBehaviour.LogWarning(LogPrefix + " 生成失败: " + blueprint.id);
                    return;
                }

                try
                {
                    npc.gameObject.name = "PermanentDuckNpc_" + blueprint.id;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 改名失败: " + e.Message);
                }

                string displayName = blueprint.permanent != null ? blueprint.permanent.displayNameCn : blueprint.id;
                _lastStatus = "已生成永久 NPC：" + displayName + "（" + blueprint.id + "），走过去按交互键";
                ModBehaviour.LogInfo(LogPrefix + " 已生成 " + blueprint.id + " @ " + pos);
            }
            catch (Exception e)
            {
                _lastStatus = "生成永久 NPC 异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 生成异常: " + e);
            }
            finally
            {
                _spawnInFlight = false;
            }
        }

        /// <summary>回收永久 NPC。</summary>
        internal static void Despawn()
        {
            DuckNpcBlueprint blueprint = ResolveTarget();
            if (blueprint == null)
            {
                _lastStatus = "没有永久 NPC 蓝图";
                return;
            }

            CharacterMainControl npc = PermanentDuckNpcRegistry.GetInstance(blueprint.id);
            if (npc == null)
            {
                _lastStatus = "当前没有永久 NPC 实例";
                return;
            }

            DuckNpcSpawner.Despawn(npc);
            PermanentDuckNpcRegistry.UnregisterInstance(blueprint.id);
            _lastStatus = "永久 NPC 已回收（" + blueprint.id + "）";
            ModBehaviour.LogInfo(LogPrefix + " 已回收 " + blueprint.id);
        }

        // ====================================================================
        // 好感度
        // ====================================================================

        /// <summary>
        /// 好感度拉满到 10 级，并补打 3/5/8/10 级剧情标记。
        /// </summary>
        /// <remarks>
        /// **必须一并打 10 级标记**：婚礼教堂的解锁判据是
        /// AffinityManager.HasAnyNPCEverReachedMaxLevel()，而它查的是
        /// hasTriggeredStory10 标记而不是当前点数。只把点数拉满，教堂照样开不了，
        /// 于是"结婚测不了"会被误判成婚姻功能有 bug。
        /// </remarks>
        internal static void MaxAffinity()
        {
            DuckNpcBlueprint blueprint = ResolveTarget();
            if (blueprint == null)
            {
                _lastStatus = "没有永久 NPC 蓝图";
                return;
            }

            try
            {
                PermanentDuckNpcModule.RegisterAllAffinityConfigs();

                AffinityManager.SetPoints(blueprint.id, AffinityManager.UNIFIED_MAX_POINTS);

                int[] milestones = new int[] { 3, 5, 8, 10 };
                for (int i = 0; i < milestones.Length; i++)
                {
                    AffinityManager.MarkStoryTriggered(blueprint.id, milestones[i]);
                }

                AffinityManager.FlushSave();

                _lastStatus = "已把 " + blueprint.id + " 拉到 Lv."
                    + AffinityManager.GetLevel(blueprint.id) + "，并打上 3/5/8/10 级剧情标记（教堂可解锁）";
                ModBehaviour.LogInfo(LogPrefix + " " + _lastStatus);
            }
            catch (Exception e)
            {
                _lastStatus = "拉满好感度异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 拉满好感度异常: " + e);
            }
        }

        /// <summary>好感度清零并清掉剧情标记，方便反复测试。</summary>
        internal static void ResetAffinity()
        {
            DuckNpcBlueprint blueprint = ResolveTarget();
            if (blueprint == null)
            {
                _lastStatus = "没有永久 NPC 蓝图";
                return;
            }

            try
            {
                AffinityManager.SetPoints(blueprint.id, 0);
                AffinityManager.ResetStoryTriggers(blueprint.id);
                AffinityManager.SetLastChatDay(blueprint.id, -1);
                AffinityManager.SetLastGiftDay(blueprint.id, -1);
                AffinityManager.FlushSave();

                _lastStatus = "已清零 " + blueprint.id + " 的好感度与剧情标记";
                ModBehaviour.LogInfo(LogPrefix + " " + _lastStatus);
            }
            catch (Exception e)
            {
                _lastStatus = "清零好感度异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 清零好感度异常: " + e);
            }
        }

        // ====================================================================
        // 状态报告
        // ====================================================================

        /// <summary>输出永久 NPC 的完整状态，落盘 + 日志。</summary>
        internal static void DumpState()
        {
            try
            {
                string report = BuildStateReport();
                ModBehaviour.LogInfo(LogPrefix + " 永久 NPC 状态报告:\n" + report);

                string path = DuckNpcDebugProbe.WriteDebugReport("PermanentDuckNpcState", ".md", report);
                _lastStatus = path != null ? ("已输出永久 NPC 状态: " + path) : "状态报告落盘失败，内容见日志";
            }
            catch (Exception e)
            {
                _lastStatus = "输出状态异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 输出状态异常: " + e);
            }
        }

        private static string BuildStateReport()
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("# 永久捏脸 NPC 状态");
            sb.AppendLine();
            sb.AppendLine("- 时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try
            {
                sb.AppendLine("- 场景: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
            catch (Exception e)
            {
                sb.AppendLine("- 场景: (读取失败: " + e.GetType().Name + ")");
            }
            sb.AppendLine();

            List<DuckNpcBlueprint> permanents = PermanentDuckNpcRegistry.GetAllPermanent();
            sb.AppendLine("## 蓝图");
            sb.AppendLine();
            sb.AppendLine("永久蓝图数量: " + permanents.Count);
            if (permanents.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("> DuckNpcs.json 里没有 isPermanent 的蓝图，或整表解析失败回落了兜底。");
                return sb.ToString();
            }

            DuckNpcBlueprint blueprint = permanents[0];
            sb.AppendLine();
            sb.AppendLine("| 项 | 值 |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine("| id | " + blueprint.id + " |");
            sb.AppendLine("| faceMode | " + blueprint.faceMode + " |");
            sb.AppendLine("| faceJson 长度 | " + (blueprint.faceJson != null ? blueprint.faceJson.Length : 0) + " |");
            sb.AppendLine("| 底模 | " + (string.IsNullOrEmpty(blueprint.baseModel) ? "(默认鸭模)" : blueprint.baseModel) + " |");
            sb.AppendLine("| canWander | " + blueprint.canWander + " |");
            sb.AppendLine("| scenes | " + DescribeScenes(blueprint) + " |");
            sb.AppendLine("| permanent 子对象 | " + (blueprint.permanent != null ? "有" : "**无**") + " |");
            if (blueprint.permanent != null)
            {
                sb.AppendLine("| 显示名 | " + blueprint.permanent.displayNameCn
                    + " / " + blueprint.permanent.displayNameEn + " |");
            }

            AppendAffinitySection(sb, blueprint);
            AppendInstanceSection(sb, blueprint);

            sb.AppendLine();
            sb.AppendLine("## 需要人工目视确认的项");
            sb.AppendLine();
            sb.AppendLine("- [ ] NPC 长相与 `faceJson` 一致（青瓷色羽毛、深色短发、偏大的眼睛）");
            sb.AppendLine("- [ ] 头顶有名字标签");
            sb.AppendLine("- [ ] 走过去能出现「聊天」交互，且**能挡住/不能穿过去的手感与原版 NPC 一致**");
            sb.AppendLine("- [ ] 聊天有气泡台词，好感度会涨（连点两次第二次不再涨——每日限一次）");
            sb.AppendLine("- [ ] 切到「送礼」选项能打开礼物容器");
            sb.AppendLine("- [ ] NPC 会自己走动，对话时停下、说完继续走");
            sb.AppendLine("- [ ] **角色物理没坏**：NPC 不会抖动/被自己顶开，玩家推它的手感正常");
            sb.AppendLine("- [ ] 好感度拉满后，婚礼教堂可以建造（教堂查的是 10 级剧情标记）");

            return sb.ToString();
        }

        private static string DescribeScenes(DuckNpcBlueprint blueprint)
        {
            if (blueprint.scenes == null || blueprint.scenes.Length == 0)
            {
                return "(空——不会自动生成，只能用本页按钮召唤)";
            }
            return string.Join(", ", blueprint.scenes);
        }

        private static void AppendAffinitySection(StringBuilder sb, DuckNpcBlueprint blueprint)
        {
            sb.AppendLine();
            sb.AppendLine("## 好感度");
            sb.AppendLine();
            sb.AppendLine("| 项 | 值 |");
            sb.AppendLine("| --- | --- |");

            AppendRow(sb, "配置已注册", () =>
                AffinityManager.GetNPCConfig(blueprint.id) != null ? "是" : "**否（对话/送礼会回落通用文案）**");
            AppendRow(sb, "等级", () => AffinityManager.GetLevel(blueprint.id).ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "点数", () => AffinityManager.GetPoints(blueprint.id).ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "已婚", () => AffinityManager.IsMarriedToPlayer(blueprint.id).ToString());
            AppendRow(sb, "10 级剧情标记", () =>
            {
                bool flag = AffinityManager.HasTriggeredStory(blueprint.id, 10);
                return flag ? "已打（教堂可解锁）" : "未打（**教堂开不了**）";
            });
            AppendRow(sb, "任一 NPC 到过满级", () => AffinityManager.HasAnyNPCEverReachedMaxLevel().ToString());
        }

        private static void AppendInstanceSection(StringBuilder sb, DuckNpcBlueprint blueprint)
        {
            sb.AppendLine();
            sb.AppendLine("## 场上实例");
            sb.AppendLine();

            CharacterMainControl npc = PermanentDuckNpcRegistry.GetInstance(blueprint.id);
            if (npc == null)
            {
                sb.AppendLine("> 当前没有实例。点「永久 NPC：在此生成」。");
                return;
            }

            sb.AppendLine("| 项 | 值 |");
            sb.AppendLine("| --- | --- |");
            AppendRow(sb, "GameObject", () => npc.gameObject.name);
            AppendRow(sb, "位置", () => npc.transform.position.ToString());
            AppendRow(sb, "Team", () => npc.Team.ToString());
            AppendRow(sb, "根节点 layer", () =>
            {
                int layer = npc.gameObject.layer;
                string name = LayerMask.LayerToName(layer);
                // 这是本轮最关键的一条：交互挂错地方会把角色根节点的层
                // 从 Character 改成 Interactable，静默打坏物理。
                return layer + " (" + name + ")" + (name == "Character" ? " 正确" : " **错误！应为 Character**");
            });
            AppendRow(sb, "CustomFace 实例", () =>
                (npc.characterModel != null && npc.characterModel.CustomFace != null) ? "有" : "**无**");
            AppendRow(sb, "DuckNpcMovement", () =>
            {
                DuckNpcMovement move = npc.GetComponent<DuckNpcMovement>();
                if (move == null) return "无（站桩）";
                return "有, enabled=" + move.enabled + ", 移动中=" + move.IsMoving + ", 挂起=" + move.IsHeld;
            });
            AppendRow(sb, "交互子物体", () => DescribeInteractRoot(npc));
            AppendRow(sb, "交互选项", () => DescribeInteractOptions(npc));
        }

        private static string DescribeInteractRoot(CharacterMainControl npc)
        {
            Transform root = npc.transform.Find("InteractRoot");
            if (root == null)
            {
                return "**未找到 InteractRoot（交互不会工作）**";
            }

            string layerName = LayerMask.LayerToName(root.gameObject.layer);
            Collider collider = root.GetComponent<Collider>();
            string colliderDesc = collider == null
                ? "**无碰撞体**"
                : collider.GetType().Name + " enabled=" + collider.enabled + " trigger=" + collider.isTrigger;

            return "有, layer=" + layerName
                + (layerName == "Interactable" ? " 正确" : " **应为 Interactable**")
                + ", " + colliderDesc;
        }

        private static string DescribeInteractOptions(CharacterMainControl npc)
        {
            Transform root = npc.transform.Find("InteractRoot");
            if (root == null)
            {
                return "-";
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (sb.Length > 0)
                {
                    sb.Append("; ");
                }
                sb.Append(child.name).Append(child.gameObject.activeSelf ? "(显示)" : "(隐藏)");
            }
            return sb.Length == 0 ? "(无子选项)" : sb.ToString();
        }

        private static void AppendRow(StringBuilder sb, string label, Func<string> getter)
        {
            string value;
            try
            {
                value = getter();
            }
            catch (Exception e)
            {
                value = "读取异常: " + e.GetType().Name;
            }
            sb.AppendLine("| " + label + " | " + (string.IsNullOrEmpty(value) ? "(空)" : value) + " |");
        }

        // ====================================================================
        // 缓存
        // ====================================================================

        internal static void ResetStaticCaches()
        {
            _lastStatus = "尚未操作永久 NPC";
            _spawnInFlight = false;
        }
    }
}
