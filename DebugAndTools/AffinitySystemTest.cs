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
        }
        
        void OnGUI()
        {
            // 仅在DevMode开启且测试UI可见时显示
            if (!ModBehaviour.DevModeEnabled || !testUIVisible) return;
            
            // 绘制测试UI
            GUILayout.BeginArea(new Rect(10, 10, 400, 600));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label("=== 好感度系统测试工具 (DevMode) ===", GUILayout.Height(25));
            GUILayout.Label("按 F11 关闭此窗口", GUILayout.Height(20));
            GUILayout.Space(10);
            
            // 当前状态
            GUILayout.Label("--- 当前状态 ---");
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            int points = AffinityManager.GetPoints(TEST_NPC_ID);
            float progress = AffinityManager.GetLevelProgress(TEST_NPC_ID);
            float discount = AffinityManager.GetDiscount(TEST_NPC_ID);
            
            GUILayout.Label($"NPC: {TEST_NPC_ID}");
            GUILayout.Label($"等级: {level} / 10");
            GUILayout.Label($"点数: {points} / 1000");
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
            GUILayout.Space(10);
            
            // 测试日志
            GUILayout.Label("--- 测试日志 ---");
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
            GUILayout.Label(testLog);
            GUILayout.EndScrollView();
            
            if (GUILayout.Button("清空日志"))
            {
                testLog = "";
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
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
            
            // 验证
            int expectedPoints = Math.Min(1000, oldPoints + TEST_POINTS_AMOUNT);
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
            
            AffinityManager.SetPoints(TEST_NPC_ID, 1000);
            
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            int points = AffinityManager.GetPoints(TEST_NPC_ID);
            float discount = AffinityManager.GetDiscount(TEST_NPC_ID);
            
            Log($"等级: {level}");
            Log($"点数: {points}");
            Log($"折扣: {discount:P0}");
            
            if (level == 10 && points == 1000)
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
            
            if (level == 0 && points == 0)
            {
                Log("✓ 重置正确");
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
            int level = AffinityManager.GetLevel(TEST_NPC_ID);
            
            Log($"商店解锁: {isUnlocked}");
            Log($"当前折扣: {discount:P0}");
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
