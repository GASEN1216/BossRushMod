// ============================================================================
// AffinitySystemTest.cs - 好感度系统测试工具
// ============================================================================
// 模块说明：
//   用于测试好感度系统的调试工具。
//   提供快捷键测试各项功能，验证系统正确性。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 好感度系统测试工具
    /// </summary>
    public class AffinitySystemTest : MonoBehaviour
    {
        // 测试配置
        private const string TEST_NPC_ID = GoblinAffinityConfig.NPC_ID;
        private const int TEST_POINTS_AMOUNT = 50;
        
        // 测试状态
        private bool testUIVisible = false;
        private string testLog = "";
        private Vector2 scrollPosition;
        
        // UI窗口配置（可拖动）
        private Rect windowRect = new Rect(10, 10, 420, 750);
        
        // ============================================================================
        // Unity生命周期
        // ============================================================================
        
        void Update()
        {
            // 仅在DevMode开启时响应快捷键
            if (!ModBehaviour.DevModeEnabled) return;
            
            // F11: 切换测试UI（仅DevMode下可用）
            if (Input.GetKeyDown(KeyCode.F11))
            {
                testUIVisible = !testUIVisible;
                if (testUIVisible)
                {
                    Log("好感度系统测试UI已打开 (DevMode)");
                }
            }
            
            // 仅在测试UI可见时响应快捷键
            if (!testUIVisible) return;
            
            // 数字键1-9: 快捷测试
            if (Input.GetKeyDown(KeyCode.Alpha1)) TestGetLevel();
            if (Input.GetKeyDown(KeyCode.Alpha2)) TestAddPoints();
            if (Input.GetKeyDown(KeyCode.Alpha3)) TestSubtractPoints();
            if (Input.GetKeyDown(KeyCode.Alpha4)) TestMaxLevel();
            if (Input.GetKeyDown(KeyCode.Alpha5)) TestResetPoints();
            if (Input.GetKeyDown(KeyCode.Alpha6)) TestGiftSystem();
            if (Input.GetKeyDown(KeyCode.Alpha7)) TestShopSystem();
            if (Input.GetKeyDown(KeyCode.Alpha8)) TestEventSystem();
            if (Input.GetKeyDown(KeyCode.Alpha9)) TestStoryTriggerStatus();
            if (Input.GetKeyDown(KeyCode.Alpha0)) TestDeferredSave();
            if (Input.GetKeyDown(KeyCode.R)) ResetTodayChat();
            if (Input.GetKeyDown(KeyCode.D)) TestDailyDecay();
            if (Input.GetKeyDown(KeyCode.F)) SimulateDecay();
        }
        
        void OnGUI()
        {
            // 仅在DevMode开启且测试UI可见时显示
            if (!ModBehaviour.DevModeEnabled || !testUIVisible) return;
            
            // 使用GUI.Window实现可拖动窗口
            windowRect = GUI.Window(19870611, windowRect, DrawTestWindow, "好感度系统测试工具 (DevMode) - 可拖动");
        }
        
        /// <summary>
        /// 绘制测试窗口内容
        /// </summary>
        private void DrawTestWindow(int windowId)
        {
            GUILayout.Label("按 F11 关闭此窗口", GUILayout.Height(20));
            GUILayout.Space(5);
            
            // 当前状态
            GUILayout.Label("--- 当前状态 ---");
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            int points = AffinityManager.GetPoints(TEST_NPC_ID);
            float progress = AffinityManager.GetLevelProgress(TEST_NPC_ID);
            float discount = AffinityManager.GetDiscount(TEST_NPC_ID);
            
            GUILayout.Label($"NPC: {TEST_NPC_ID}");
            GUILayout.Label($"等级: {level} / {AffinityManager.UNIFIED_MAX_LEVEL}");
            GUILayout.Label($"点数: {points} / {AffinityManager.UNIFIED_MAX_POINTS}");
            GUILayout.Label($"进度: {progress:P1}");
            GUILayout.Label($"折扣: {discount:P0}");
            GUILayout.Space(10);
            
            // 测试按钮
            GUILayout.Label("--- 测试功能 (数字键1-9) ---");
            if (GUILayout.Button("[1] 获取等级信息")) TestGetLevel();
            if (GUILayout.Button("[2] 增加50点好感度")) TestAddPoints();
            if (GUILayout.Button("[3] 减少50点好感度")) TestSubtractPoints();
            if (GUILayout.Button("[4] 设置满级")) TestMaxLevel();
            if (GUILayout.Button("[5] 重置好感度")) TestResetPoints();
            if (GUILayout.Button("[6] 测试礼物系统")) TestGiftSystem();
            if (GUILayout.Button("[7] 测试商店系统")) TestShopSystem();
            if (GUILayout.Button("[8] 测试事件系统")) TestEventSystem();
            if (GUILayout.Button("[9] 故事触发状态")) TestStoryTriggerStatus();
            if (GUILayout.Button("[0] 延迟保存测试")) TestDeferredSave();
            if (GUILayout.Button("[R] 重置今日聊天")) ResetTodayChat();
            if (GUILayout.Button("[D] 测试每日衰减")) TestDailyDecay();
            if (GUILayout.Button("[F] 模拟衰减(跳过2天)")) SimulateDecay();
            GUILayout.Space(10);
            
            // 测试日志
            GUILayout.Label("--- 测试日志 ---");
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(250));
            GUILayout.Label(testLog);
            GUILayout.EndScrollView();
            
            if (GUILayout.Button("清空日志"))
            {
                testLog = "";
            }
            
            // 使窗口可拖动（拖动标题栏区域）
            GUI.DragWindow(new Rect(0, 0, windowRect.width, 25));
        }
        
        // ============================================================================
        // 测试方法
        // ============================================================================
        
        /// <summary>
        /// 测试1: 获取等级信息
        /// </summary>
        private void TestGetLevel()
        {
            Log("=== 测试: 获取等级信息 ===");
            
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            int points = AffinityManager.GetPoints(TEST_NPC_ID);
            float progress = AffinityManager.GetLevelProgress(TEST_NPC_ID);
            
            Log($"等级: {level}");
            Log($"点数: {points}");
            Log($"进度: {progress:P1}");
            
            // 验证计算正确性（使用递增式等级配置）
            int expectedLevel = AffinityManager.GetLevelFromPoints(points);
            
            if (level == expectedLevel)
            {
                Log("✓ 等级计算正确");
            }
            else
            {
                Log($"✗ 等级计算错误! 期望: {expectedLevel}, 实际: {level}");
            }
        }
        
        /// <summary>
        /// 测试2: 增加好感度
        /// </summary>
        private void TestAddPoints()
        {
            Log("=== 测试: 增加好感度 ===");
            
            int oldPoints = AffinityManager.GetPoints(TEST_NPC_ID);
            int oldLevel = AffinityManager.GetLevel(TEST_NPC_ID);
            
            AffinityManager.AddPoints(TEST_NPC_ID, TEST_POINTS_AMOUNT);
            
            int newPoints = AffinityManager.GetPoints(TEST_NPC_ID);
            int newLevel = AffinityManager.GetLevel(TEST_NPC_ID);
            
            Log($"点数: {oldPoints} -> {newPoints} (+{TEST_POINTS_AMOUNT})");
            Log($"等级: {oldLevel} -> {newLevel}");
            
            // 验证（使用统一配置的最大点数）
            int expectedPoints = Math.Min(AffinityManager.UNIFIED_MAX_POINTS, oldPoints + TEST_POINTS_AMOUNT);
            if (newPoints == expectedPoints)
            {
                Log("✓ 点数增加正确");
            }
            else
            {
                Log($"✗ 点数增加错误! 期望: {expectedPoints}, 实际: {newPoints}");
            }
        }
        
        /// <summary>
        /// 测试3: 减少好感度
        /// </summary>
        private void TestSubtractPoints()
        {
            Log("=== 测试: 减少好感度 ===");
            
            int oldPoints = AffinityManager.GetPoints(TEST_NPC_ID);
            
            AffinityManager.AddPoints(TEST_NPC_ID, -TEST_POINTS_AMOUNT);
            
            int newPoints = AffinityManager.GetPoints(TEST_NPC_ID);
            
            Log($"点数: {oldPoints} -> {newPoints} (-{TEST_POINTS_AMOUNT})");
            
            // 验证
            int expectedPoints = Math.Max(0, oldPoints - TEST_POINTS_AMOUNT);
            if (newPoints == expectedPoints)
            {
                Log("✓ 点数减少正确");
            }
            else
            {
                Log($"✗ 点数减少错误! 期望: {expectedPoints}, 实际: {newPoints}");
            }
        }
        
        /// <summary>
        /// 测试4: 设置满级
        /// </summary>
        private void TestMaxLevel()
        {
            Log("=== 测试: 设置满级 ===");
            
            // 使用统一配置的最大点数
            AffinityManager.SetPoints(TEST_NPC_ID, AffinityManager.UNIFIED_MAX_POINTS);
            
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            int points = AffinityManager.GetPoints(TEST_NPC_ID);
            float discount = AffinityManager.GetDiscount(TEST_NPC_ID);
            
            Log($"等级: {level}");
            Log($"点数: {points}");
            Log($"折扣: {discount:P0}");
            
            if (level == AffinityManager.UNIFIED_MAX_LEVEL && points == AffinityManager.UNIFIED_MAX_POINTS)
            {
                Log("✓ 满级设置正确");
            }
            else
            {
                Log("✗ 满级设置错误!");
            }
        }
        
        /// <summary>
        /// 测试5: 重置好感度
        /// </summary>
        private void TestResetPoints()
        {
            Log("=== 测试: 重置好感度 ===");
            
            AffinityManager.SetPoints(TEST_NPC_ID, 0);
            
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            int points = AffinityManager.GetPoints(TEST_NPC_ID);
            
            Log($"等级: {level}");
            Log($"点数: {points}");
            
            // 注意：0点对应1级（起始等级），不是0级
            if (level == 1 && points == 0)
            {
                Log("✓ 重置正确（0点=1级）");
            }
            else
            {
                Log("✗ 重置错误!");
            }
        }
        
        /// <summary>
        /// 测试6: 礼物系统
        /// </summary>
        private void TestGiftSystem()
        {
            Log("=== 测试: 礼物系统 ===");
            
            bool canGift = NPCGiftSystem.CanGiftToday(TEST_NPC_ID);
            int lastGiftDay = AffinityManager.GetLastGiftDay(TEST_NPC_ID);
            int currentDay = NPCGiftSystem.GetCurrentGameDay();
            
            Log($"今日可赠送: {canGift}");
            Log($"上次赠送日: {lastGiftDay}");
            Log($"当前游戏日: {currentDay}");
            
            // 测试重置每日礼物
            NPCGiftSystem.ResetDailyGift(TEST_NPC_ID);
            bool canGiftAfterReset = NPCGiftSystem.CanGiftToday(TEST_NPC_ID);
            
            Log($"重置后可赠送: {canGiftAfterReset}");
            
            if (canGiftAfterReset)
            {
                Log("✓ 礼物系统重置正确");
            }
            else
            {
                Log("✗ 礼物系统重置错误!");
            }
        }
        
        /// <summary>
        /// 测试7: 商店系统
        /// </summary>
        private void TestShopSystem()
        {
            Log("=== 测试: 商店系统 ===");
            
            bool isUnlocked = NPCShopSystem.IsShopUnlocked(TEST_NPC_ID);
            float discount = NPCShopSystem.GetDiscount(TEST_NPC_ID);
            float sellFactor = NPCShopSystem.GetSellFactor(TEST_NPC_ID);
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            
            Log($"商店解锁: {isUnlocked}");
            Log($"当前折扣: {discount:P0}");
            Log($"卖出系数: {sellFactor:P0} (原版50%)");
            Log($"当前等级: {level}");
            
            // 验证解锁逻辑
            bool expectedUnlock = level >= GoblinAffinityConfig.COLD_QUENCH_UNLOCK_LEVEL;
            if (isUnlocked == expectedUnlock)
            {
                Log("✓ 商店解锁逻辑正确");
            }
            else
            {
                Log($"✗ 商店解锁逻辑错误! 期望: {expectedUnlock}, 实际: {isUnlocked}");
            }
            
            // 验证卖出加成逻辑
            float expectedSellFactor = 0.5f + (discount / 2f);
            if (Mathf.Approximately(sellFactor, expectedSellFactor))
            {
                Log("✓ 卖出加成逻辑正确");
            }
            else
            {
                Log($"✗ 卖出加成逻辑错误! 期望: {expectedSellFactor:P0}, 实际: {sellFactor:P0}");
            }
        }
        
        /// <summary>
        /// 测试8: 事件系统
        /// </summary>
        private void TestEventSystem()
        {
            Log("=== 测试: 事件系统 ===");
            
            bool eventFired = false;
            int eventOldPoints = 0;
            int eventNewPoints = 0;
            
            // 订阅事件
            Action<string, int, int> handler = (npcId, oldP, newP) =>
            {
                if (npcId == TEST_NPC_ID)
                {
                    eventFired = true;
                    eventOldPoints = oldP;
                    eventNewPoints = newP;
                }
            };
            
            AffinityManager.OnAffinityChanged += handler;
            
            // 触发变化
            int beforePoints = AffinityManager.GetPoints(TEST_NPC_ID);
            AffinityManager.AddPoints(TEST_NPC_ID, 10);
            
            // 取消订阅
            AffinityManager.OnAffinityChanged -= handler;
            
            Log($"事件触发: {eventFired}");
            Log($"旧点数: {eventOldPoints}");
            Log($"新点数: {eventNewPoints}");
            
            if (eventFired && eventOldPoints == beforePoints && eventNewPoints == beforePoints + 10)
            {
                Log("✓ 事件系统正确");
            }
            else
            {
                Log("✗ 事件系统错误!");
            }
        }
        
        /// <summary>
        /// 测试9: 故事触发状态（持久化）
        /// </summary>
        private void TestStoryTriggerStatus()
        {
            Log("=== 测试: 故事触发状态 ===");
            
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            bool story5Triggered = AffinityManager.HasTriggeredStory5(TEST_NPC_ID);
            bool story10Triggered = AffinityManager.HasTriggeredStory10(TEST_NPC_ID);
            
            Log($"当前等级: {level}");
            Log($"5级故事已触发: {story5Triggered}");
            Log($"10级故事已触发: {story10Triggered}");
            
            // 显示触发条件
            Log("--- 触发条件 ---");
            Log($"5级故事: 等级≥5 ({(level >= 5 ? "✓" : "✗")}) 且 未触发 ({(!story5Triggered ? "✓" : "✗")})");
            Log($"10级故事: 等级≥10 ({(level >= 10 ? "✓" : "✗")}) 且 未触发 ({(!story10Triggered ? "✓" : "✗")})");
            
            // 提示如何测试
            if (!story5Triggered && level >= 5)
            {
                Log("提示: 5级故事可触发，靠近哥布林NPC 3米内即可");
            }
            if (!story10Triggered && level >= 10)
            {
                Log("提示: 10级故事可触发，靠近哥布林NPC 3米内即可");
            }
            
            Log("✓ 故事状态检查完成");
        }
        
        /// <summary>
        /// 测试10: 延迟保存机制
        /// </summary>
        private void TestDeferredSave()
        {
            Log("=== 测试: 延迟保存机制 ===");
            
            // 记录初始点数
            int initialPoints = AffinityManager.GetPoints(TEST_NPC_ID);
            Log($"初始点数: {initialPoints}");
            
            // 快速连续修改多次（模拟频繁操作）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                AffinityManager.AddPoints(TEST_NPC_ID, 1);
            }
            sw.Stop();
            
            Log($"100次 AddPoints 耗时: {sw.ElapsedMilliseconds}ms");
            Log($"当前点数: {AffinityManager.GetPoints(TEST_NPC_ID)}");
            
            // 强制保存
            sw.Restart();
            AffinityManager.FlushSave();
            sw.Stop();
            
            Log($"FlushSave 耗时: {sw.ElapsedMilliseconds}ms");
            
            // 恢复初始状态
            AffinityManager.SetPoints(TEST_NPC_ID, initialPoints);
            AffinityManager.FlushSave();
            
            Log("✓ 延迟保存测试完成");
        }
        
        /// <summary>
        /// 重置今日聊天状态
        /// </summary>
        private void ResetTodayChat()
        {
            Log("=== 重置今日聊天 ===");
            
            int lastChatDay = AffinityManager.GetLastChatDay(TEST_NPC_ID);
            int currentDay = NPCGiftSystem.GetCurrentGameDay();
            
            Log($"当前游戏日: {currentDay}");
            Log($"上次聊天日: {lastChatDay}");
            
            // 重置为-1，表示今日未聊天
            AffinityManager.SetLastChatDay(TEST_NPC_ID, -1);
            AffinityManager.FlushSave();
            
            int newLastChatDay = AffinityManager.GetLastChatDay(TEST_NPC_ID);
            Log($"重置后聊天日: {newLastChatDay}");
            Log("✓ 今日聊天状态已重置，下次聊天可获得好感度");
        }
        
        /// <summary>
        /// 测试每日衰减系统（累积衰减版本）
        /// </summary>
        private void TestDailyDecay()
        {
            Log("=== 测试: 每日累积衰减系统 ===");
            
            int currentDay = NPCGiftSystem.GetCurrentGameDay();
            int lastDecayDay = AffinityManager.GetLastDecayCheckDay(TEST_NPC_ID);
            int lastChatDay = AffinityManager.GetLastChatDay(TEST_NPC_ID);
            int lastGiftDay = AffinityManager.GetLastGiftDay(TEST_NPC_ID);
            int points = AffinityManager.GetPoints(TEST_NPC_ID);
            
            Log($"当前游戏日: {currentDay}");
            Log($"上次衰减检查日: {lastDecayDay}");
            Log($"上次聊天日: {lastChatDay}");
            Log($"上次送礼日: {lastGiftDay}");
            Log($"当前好感度: {points}");
            Log($"每日衰减值: {AffinityConfig.DAILY_DECAY_AMOUNT}");
            Log($"衰减启用: {AffinityConfig.ENABLE_DAILY_DECAY}");
            
            // 计算需要检查的天数范围
            if (lastDecayDay >= 0 && lastDecayDay < currentDay)
            {
                int startDay = lastDecayDay + 1;
                int endDay = currentDay - 1;  // 当天不算
                
                if (startDay <= endDay)
                {
                    Log($"待检查范围: 第{startDay}天 ~ 第{endDay}天");
                    
                    // 计算这些天中有多少天没有互动
                    int daysWithoutInteraction = 0;
                    for (int day = startDay; day <= endDay; day++)
                    {
                        bool hadInteraction = (lastChatDay == day) || (lastGiftDay == day);
                        if (!hadInteraction)
                        {
                            daysWithoutInteraction++;
                        }
                    }
                    
                    int potentialDecay = daysWithoutInteraction * AffinityConfig.DAILY_DECAY_AMOUNT;
                    Log($"未互动天数: {daysWithoutInteraction}");
                    Log($"预计衰减: {potentialDecay}点");
                    
                    if (daysWithoutInteraction > 0 && points > 0)
                    {
                        Log($"⚠ 警告: 有{daysWithoutInteraction}天未互动，下次遇到NPC将衰减{potentialDecay}点！");
                    }
                }
                else
                {
                    Log("✓ 连续两天都来了，无需衰减");
                }
            }
            else if (lastDecayDay >= currentDay)
            {
                Log("✓ 今天已检查过，不会重复衰减");
            }
            else
            {
                Log("ℹ 首次检查，将初始化日期");
            }
            
            // 检查今天是否有互动（影响明天的衰减）
            bool hadInteractionToday = (lastChatDay == currentDay) || (lastGiftDay == currentDay);
            if (hadInteractionToday)
            {
                Log("✓ 今日已互动，明天不会因今天而衰减");
            }
            else if (points > 0)
            {
                Log("⚠ 今日未互动，如果明天不来，后天会衰减！");
            }
            else
            {
                Log("✓ 好感度为0，不会衰减");
            }
        }
        
        /// <summary>
        /// 模拟衰减（将上次检查日期设为多天前，触发累积衰减）
        /// </summary>
        private void SimulateDecay()
        {
            Log("=== 模拟: 跳过2天触发累积衰减 ===");
            
            int currentDay = NPCGiftSystem.GetCurrentGameDay();
            int oldPoints = AffinityManager.GetPoints(TEST_NPC_ID);
            
            Log($"当前游戏日: {currentDay}");
            Log($"衰减前好感度: {oldPoints}");
            
            // 模拟场景：第1天互动，第2、3天没互动，第4天（今天）遇到NPC
            // 应该衰减2次（第2、3天各扣一次），当天不算
            int daysToSkip = 3;  // 上次检查日设为3天前
            int lastCheckDay = currentDay - daysToSkip;
            
            AffinityManager.SetLastDecayCheckDay(TEST_NPC_ID, lastCheckDay);
            
            // 设置上次互动日为检查日当天（模拟那天有互动）
            AffinityManager.SetLastChatDay(TEST_NPC_ID, lastCheckDay);
            AffinityManager.SetLastGiftDay(TEST_NPC_ID, -1);
            
            // 计算期望衰减：从 lastCheckDay+1 到 currentDay-1 的天数
            // 即第2天和第3天，共2天未互动
            int expectedDaysWithoutInteraction = daysToSkip - 1;  // 当天不算，所以是3-1=2天
            int expectedDecay = expectedDaysWithoutInteraction * AffinityConfig.DAILY_DECAY_AMOUNT;
            
            Log($"已设置: 上次检查日={lastCheckDay}, 上次聊天日={lastCheckDay}");
            Log($"检查范围: 第{lastCheckDay + 1}天 ~ 第{currentDay - 1}天 (共{expectedDaysWithoutInteraction}天)");
            Log($"期望衰减: {expectedDecay}点 ({expectedDaysWithoutInteraction}天 × {AffinityConfig.DAILY_DECAY_AMOUNT}点/天)");
            
            // 触发衰减检查
            int decayAmount = AffinityManager.CheckAndApplyDailyDecay(TEST_NPC_ID);
            
            int newPoints = AffinityManager.GetPoints(TEST_NPC_ID);
            
            Log($"衰减后好感度: {newPoints}");
            Log($"实际衰减: {decayAmount}");
            
            if (decayAmount == expectedDecay)
            {
                Log("✓ 累积衰减正确执行");
            }
            else if (decayAmount == 0 && oldPoints == 0)
            {
                Log("✓ 好感度为0，无需衰减");
            }
            else
            {
                Log($"⚠ 衰减值异常，期望: {expectedDecay}, 实际: {decayAmount}");
            }
            
            AffinityManager.FlushSave();
        }
        
        // ============================================================================
        // 辅助方法
        // ============================================================================
        
        private void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            testLog = $"[{timestamp}] {message}\n" + testLog;
            ModBehaviour.DevLog("[AffinityTest] " + message);
        }
    }
}
