using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private void DrawMarriageTestUI()
        {
            if (!DevModeEnabled || !marriageTestUIVisible) return;
            marriageTestWindowRect = GUI.Window(
                19870613,
                marriageTestWindowRect,
                DrawMarriageTestWindow,
                "婚姻系统测试面板 (DevMode)");
        }

        private void DrawMarriageTestWindow(int windowId)
        {
            GUILayout.BeginVertical();

            string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            string spouseText = string.IsNullOrEmpty(spouseNpcId) ? "无" : spouseNpcId;
            GUILayout.Label("当前配偶: " + spouseText);
            GUILayout.Label("哥布林: Lv." + AffinityManager.GetLevel(GoblinAffinityConfig.NPC_ID)
                + " / 点数 " + AffinityManager.GetPoints(GoblinAffinityConfig.NPC_ID)
                + " / 已婚=" + AffinityManager.IsMarriedToPlayer(GoblinAffinityConfig.NPC_ID)
                + " / 5级剧情=" + AffinityManager.HasTriggeredStory5(GoblinAffinityConfig.NPC_ID)
                + " / 10级剧情=" + AffinityManager.HasTriggeredStory10(GoblinAffinityConfig.NPC_ID));
            GUILayout.Label("护士: Lv." + AffinityManager.GetLevel(NurseAffinityConfig.NPC_ID)
                + " / 点数 " + AffinityManager.GetPoints(NurseAffinityConfig.NPC_ID)
                + " / 已婚=" + AffinityManager.IsMarriedToPlayer(NurseAffinityConfig.NPC_ID)
                + " / 5级剧情=" + AffinityManager.HasTriggeredStory5(NurseAffinityConfig.NPC_ID)
                + " / 10级剧情=" + AffinityManager.HasTriggeredStory10(NurseAffinityConfig.NPC_ID));

            GUILayout.Space(8);
            GUILayout.Label("--- 基础工具 ---");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("给玩家1枚戒指", GUILayout.Height(28)))
            {
                SpawnDebugItemForMarriageTest(DiamondRingConfig.TYPE_ID, 1);
            }
            if (GUILayout.Button("给玩家5枚戒指", GUILayout.Height(28)))
            {
                SpawnDebugItemForMarriageTest(DiamondRingConfig.TYPE_ID, 5);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("输出背包详细日志", GUILayout.Height(28)))
            {
                LogInventoryDetailsForF3Debug();
            }
            if (GUILayout.Button("统计背包戒指数量", GUILayout.Height(28)))
            {
                int ringCount = CountItemInPlayerInventory(DiamondRingConfig.TYPE_ID);
                AppendMarriageTestLog("背包钻石戒指数量: " + ringCount);
                NotificationText.Push("[MarriageTest] 钻石戒指 x" + ringCount);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("--- 好感与每日限制 ---");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("哥布林=10级", GUILayout.Height(28)))
            {
                AffinityManager.SetPoints(GoblinAffinityConfig.NPC_ID, AffinityManager.UNIFIED_MAX_POINTS);
                AppendMarriageTestLog("已设置哥布林为 10 级");
            }
            if (GUILayout.Button("哥布林=1级", GUILayout.Height(28)))
            {
                AffinityManager.SetPoints(GoblinAffinityConfig.NPC_ID, 0);
                AppendMarriageTestLog("已设置哥布林为 1 级");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("护士=10级", GUILayout.Height(28)))
            {
                AffinityManager.SetPoints(NurseAffinityConfig.NPC_ID, AffinityManager.UNIFIED_MAX_POINTS);
                AppendMarriageTestLog("已设置护士为 10 级");
            }
            if (GUILayout.Button("护士=1级", GUILayout.Height(28)))
            {
                AffinityManager.SetPoints(NurseAffinityConfig.NPC_ID, 0);
                AppendMarriageTestLog("已设置护士为 1 级");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重置今日礼物限制", GUILayout.Height(28)))
            {
                AffinityManager.SetLastGiftDay(GoblinAffinityConfig.NPC_ID, -1);
                AffinityManager.SetLastGiftDay(NurseAffinityConfig.NPC_ID, -1);
                AppendMarriageTestLog("已重置哥布林/护士今日赠礼限制");
            }
            if (GUILayout.Button("重置今日聊天限制", GUILayout.Height(28)))
            {
                AffinityManager.SetLastChatDay(GoblinAffinityConfig.NPC_ID, -1);
                AffinityManager.SetLastChatDay(NurseAffinityConfig.NPC_ID, -1);
                AppendMarriageTestLog("已重置哥布林/护士今日聊天限制");
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("重置故事大对话标记(允许重新触发5/10级大对话)", GUILayout.Height(28)))
            {
                AffinityManager.ResetStoryTriggers(GoblinAffinityConfig.NPC_ID);
                AffinityManager.ResetStoryTriggers(NurseAffinityConfig.NPC_ID);
                AffinityManager.FlushSave();
                AppendMarriageTestLog("已重置哥布林/护士的5级和10级故事大对话触发标记");
            }

            GUILayout.Space(8);
            GUILayout.Label("--- NPC与婚礼教堂 ---");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("强制刷新哥布林", GUILayout.Height(28)))
            {
                SpawnGoblinNPC(null, false, true);
                AppendMarriageTestLog("请求强制刷新哥布林");
            }
            if (GUILayout.Button("强制刷新护士", GUILayout.Height(28)))
            {
                SpawnNurseNPC(null, false, true);
                AppendMarriageTestLog("请求强制刷新护士");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("恢复婚礼教堂NPC", GUILayout.Height(28)))
            {
                RestoreWeddingBuildingNPC();
                AppendMarriageTestLog("已请求恢复婚礼教堂配偶刷新");
            }
            if (GUILayout.Button("关闭面板", GUILayout.Height(28)))
            {
                marriageTestUIVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("--- 婚姻与花心流程 ---");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("强制与哥布林结婚", GUILayout.Height(28)))
            {
                TriggerMarriageSequenceForNpc(GoblinAffinityConfig.NPC_ID);
            }
            if (GUILayout.Button("强制与护士结婚", GUILayout.Height(28)))
            {
                TriggerMarriageSequenceForNpc(NurseAffinityConfig.NPC_ID);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("给当前配偶+1花心事件", GUILayout.Height(28)))
            {
                string spouseId = AffinityManager.GetCurrentSpouseNpcId();
                if (string.IsNullOrEmpty(spouseId))
                {
                    AppendMarriageTestLog("操作失败：当前没有配偶");
                }
                else
                {
                    AffinityManager.RecordCheatingIncidentForSpouse(spouseId);
                    AppendMarriageTestLog("已记录花心事件，配偶=" + spouseId);
                }
            }
            if (GUILayout.Button("与当前配偶离婚", GUILayout.Height(28)))
            {
                TriggerDivorceForCurrentSpouse();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("--- 测试日志 ---");
            marriageTestLogScroll = GUILayout.BeginScrollView(marriageTestLogScroll, GUILayout.Height(180));
            GUILayout.Label(string.IsNullOrEmpty(marriageTestLog) ? "(无日志)" : marriageTestLog);
            GUILayout.EndScrollView();
            if (GUILayout.Button("清空日志", GUILayout.Height(24)))
            {
                marriageTestLog = "";
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, marriageTestWindowRect.width, 24f));
        }

        private void AppendMarriageTestLog(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            if (string.IsNullOrEmpty(marriageTestLog))
            {
                marriageTestLog = line;
            }
            else
            {
                marriageTestLog += "\n" + line;
            }

            const int maxLogChars = 14000;
            if (marriageTestLog.Length > maxLogChars)
            {
                marriageTestLog = marriageTestLog.Substring(marriageTestLog.Length - maxLogChars);
            }
        }

        private void SpawnDebugItemForMarriageTest(int typeId, int count)
        {
            if (count <= 0) return;

            int successCount = 0;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    Item item = ItemAssetsCollection.InstantiateSync(typeId);
                    if (item == null) continue;
                    ItemUtilities.SendToPlayer(item);
                    successCount++;
                }
                catch (Exception e)
                {
                    AppendMarriageTestLog("生成物品失败(typeId=" + typeId + "): " + e.Message);
                    break;
                }
            }

            AppendMarriageTestLog("已发放物品 typeId=" + typeId + " x" + successCount);
            NotificationText.Push("[MarriageTest] 已发放 typeId=" + typeId + " x" + successCount);
        }

        private int CountItemInPlayerInventory(int typeId)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.CharacterItem == null) return 0;

                Inventory inventory = player.CharacterItem.Inventory;
                if (inventory == null) return 0;

                int count = 0;
                foreach (Item item in inventory)
                {
                    if (item != null && item.TypeID == typeId)
                    {
                        count++;
                    }
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        private void TriggerMarriageSequenceForNpc(string npcId)
        {
            try
            {
                if (string.IsNullOrEmpty(npcId))
                {
                    AppendMarriageTestLog("触发结婚失败：npcId 为空");
                    return;
                }

                if (npcId == GoblinAffinityConfig.NPC_ID && goblinNPCInstance == null)
                {
                    SpawnGoblinNPC(null, false, true);
                }
                else if (npcId == NurseAffinityConfig.NPC_ID && nurseNPCInstance == null)
                {
                    SpawnNurseNPC(null, false, true);
                }

                Transform npcTransform = GetNpcTransformForMarriageTest(npcId);
                INPCController npcController = GetNpcControllerForMarriageTest(npcId, npcTransform);
                NPCMarriageSystem.HandleRingGiftAccepted(npcId, npcTransform, npcController);
                AppendMarriageTestLog("已触发结婚流程: " + npcId);
            }
            catch (Exception e)
            {
                AppendMarriageTestLog("触发结婚流程异常: " + e.Message);
            }
        }

        private void TriggerDivorceForCurrentSpouse()
        {
            try
            {
                string spouseId = AffinityManager.GetCurrentSpouseNpcId();
                if (string.IsNullOrEmpty(spouseId))
                {
                    AppendMarriageTestLog("离婚失败：当前没有配偶");
                    return;
                }

                Transform npcTransform = GetNpcTransformForMarriageTest(spouseId);
                INPCController npcController = GetNpcControllerForMarriageTest(spouseId, npcTransform);
                NPCMarriageSystem.HandleDivorceRequested(spouseId, npcTransform, npcController);
                AppendMarriageTestLog("已触发离婚流程: " + spouseId);
            }
            catch (Exception e)
            {
                AppendMarriageTestLog("触发离婚流程异常: " + e.Message);
            }
        }

        private Transform GetNpcTransformForMarriageTest(string npcId)
        {
            if (npcId == GoblinAffinityConfig.NPC_ID)
            {
                return goblinNPCInstance != null ? goblinNPCInstance.transform : null;
            }

            if (npcId == NurseAffinityConfig.NPC_ID)
            {
                return nurseNPCInstance != null ? nurseNPCInstance.transform : null;
            }

            return null;
        }

        private INPCController GetNpcControllerForMarriageTest(string npcId, Transform fallbackTransform)
        {
            if (npcId == GoblinAffinityConfig.NPC_ID)
            {
                if (goblinController != null) return goblinController;
                if (fallbackTransform != null) return fallbackTransform.GetComponent<INPCController>();
                return null;
            }

            if (npcId == NurseAffinityConfig.NPC_ID)
            {
                if (nurseController != null) return nurseController;
                if (fallbackTransform != null) return fallbackTransform.GetComponent<INPCController>();
                return null;
            }

            if (fallbackTransform != null)
            {
                return fallbackTransform.GetComponent<INPCController>();
            }

            return null;
        }

        private void LogInventoryDetailsForF3Debug()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.CharacterItem == null)
                {
                    AppendMarriageTestLog("输出背包失败：玩家或 CharacterItem 为空");
                    return;
                }

                var inventory = player.CharacterItem.Inventory;
                if (inventory == null)
                {
                    AppendMarriageTestLog("输出背包失败：背包为空");
                    return;
                }

                DevLog("========== 背包物品详细信息 ==========");
                int index = 0;
                foreach (var item in inventory)
                {
                    if (item == null) continue;

                    int typeID = item.TypeID;
                    string displayName = item.DisplayName ?? "(无名称)";

                    string tagsStr = "(无Tags)";
                    try
                    {
                        var tags = item.Tags;
                        if (tags != null && tags.Count > 0)
                        {
                            var tagNames = new List<string>();
                            foreach (var tag in tags)
                            {
                                if (tag != null) tagNames.Add(tag.name);
                            }
                            tagsStr = string.Join(", ", tagNames);
                        }
                    }
                    catch
                    {
                    }

                    DevLog(string.Format("[{0}] TypeID={1}, Name={2}, Tags=[{3}]",
                        index, typeID, displayName, tagsStr));
                    index++;
                }

                DevLog("========== 共 " + index + " 个物品 ==========");
                AppendMarriageTestLog("背包详情已输出到日志，共 " + index + " 个物品");
                NotificationText.Push("[MarriageTest] 背包物品信息已输出到日志");
            }
            catch (Exception e)
            {
                AppendMarriageTestLog("输出背包详情异常: " + e.Message);
            }
        }
    }
}
