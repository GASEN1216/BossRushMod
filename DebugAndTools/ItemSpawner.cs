// ============================================================================
// ItemSpawner.cs - 物品生成器（F2 快捷键呼出）
// ============================================================================
// 模块说明：
//   提供一个简单的 UI 界面，允许玩家通过输入物品 ID 获取物品
//   按 F2 键呼出/关闭界面
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 物品生成器模块
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 物品生成器 UI 状态
        // ============================================================================
        
        private bool itemSpawnerUIVisible = false;
        private string itemIdInput = "";
        private string itemSpawnerMessage = "";
        private float itemSpawnerMessageTimer = 0f;
        private Rect itemSpawnerWindowRect = new Rect(100, 100, 320, 180);
        
        // ============================================================================
        // Update 检测 F2 快捷键
        // ============================================================================
        
        /// <summary>
        /// 检测 F2 快捷键（在 Update 中调用）
        /// </summary>
        private void CheckItemSpawnerHotkey()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F2))
            {
                itemSpawnerUIVisible = !itemSpawnerUIVisible;
                DevLog("[BossRush] F2 按下，物品生成器UI可见性: " + itemSpawnerUIVisible);
                if (itemSpawnerUIVisible)
                {
                    itemIdInput = "";
                    itemSpawnerMessage = "";
                }
            }
        }
        
        // ============================================================================
        // OnGUI 绘制物品生成器窗口
        // ============================================================================
        
        /// <summary>
        /// 绘制物品生成器 UI（在 OnGUI 中调用）
        /// </summary>
        private void DrawItemSpawnerUI()
        {
            if (!itemSpawnerUIVisible) return;
            
            // 更新消息计时器
            if (itemSpawnerMessageTimer > 0f)
            {
                itemSpawnerMessageTimer -= Time.deltaTime;
                if (itemSpawnerMessageTimer <= 0f)
                {
                    itemSpawnerMessage = "";
                }
            }
            
            // 绘制窗口
            itemSpawnerWindowRect = GUI.Window(
                19870001,  // 唯一窗口 ID
                itemSpawnerWindowRect,
                DrawItemSpawnerWindowContent,
                L10n.T("物品生成器", "Item Spawner")
            );
        }
        
        /// <summary>
        /// 绘制窗口内容
        /// </summary>
        private void DrawItemSpawnerWindowContent(int windowId)
        {
            GUILayout.BeginVertical();
            
            // 标题提示
            GUILayout.Label(L10n.T("输入物品 ID 获取物品：", "Enter Item ID to spawn:"));
            
            GUILayout.Space(5);
            
            // 输入框
            GUILayout.BeginHorizontal();
            GUILayout.Label("ID:", GUILayout.Width(30));
            itemIdInput = GUILayout.TextField(itemIdInput, GUILayout.Width(200));
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // 按钮行
            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button(L10n.T("确定", "Confirm"), GUILayout.Height(30)))
            {
                TrySpawnItemById();
            }
            
            if (GUILayout.Button(L10n.T("关闭", "Close"), GUILayout.Height(30)))
            {
                itemSpawnerUIVisible = false;
            }
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            
            // 显示消息
            if (!string.IsNullOrEmpty(itemSpawnerMessage))
            {
                GUIStyle messageStyle = new GUIStyle(GUI.skin.label);
                messageStyle.wordWrap = true;
                messageStyle.normal.textColor = itemSpawnerMessage.Contains("成功") || itemSpawnerMessage.Contains("Success") 
                    ? Color.green 
                    : Color.red;
                GUILayout.Label(itemSpawnerMessage, messageStyle);
            }
            
            GUILayout.Space(5);
            
            // 提示信息
            GUIStyle hintStyle = new GUIStyle(GUI.skin.label);
            hintStyle.fontSize = 10;
            hintStyle.normal.textColor = Color.gray;
            GUILayout.Label(L10n.T("提示：自定义头盔=600001，护甲=600002", "Hint: Custom Helmet=600001, Armor=600002"), hintStyle);
            
            GUILayout.EndVertical();
            
            // 允许拖动窗口
            GUI.DragWindow();
        }
        
        /// <summary>
        /// 尝试根据输入的 ID 生成物品
        /// </summary>
        private void TrySpawnItemById()
        {
            // 验证输入
            if (string.IsNullOrEmpty(itemIdInput))
            {
                itemSpawnerMessage = L10n.T("请输入物品 ID", "Please enter an Item ID");
                itemSpawnerMessageTimer = 3f;
                return;
            }
            
            int itemId;
            if (!int.TryParse(itemIdInput.Trim(), out itemId))
            {
                itemSpawnerMessage = L10n.T("无效的 ID 格式", "Invalid ID format");
                itemSpawnerMessageTimer = 3f;
                return;
            }
            
            try
            {
                // 使用游戏 API 生成物品
                Item item = ItemAssetsCollection.InstantiateSync(itemId);
                
                if (item == null)
                {
                    itemSpawnerMessage = L10n.T("物品 ID " + itemId + " 不存在", "Item ID " + itemId + " does not exist");
                    itemSpawnerMessageTimer = 3f;
                    return;
                }
                
                // 发送给玩家
                ItemUtilities.SendToPlayer(item);
                
                string itemName = item.DisplayName;
                if (string.IsNullOrEmpty(itemName))
                {
                    itemName = "Item #" + itemId;
                }
                
                itemSpawnerMessage = L10n.T(
                    "成功获取：" + itemName,
                    "Success: " + itemName
                );
                itemSpawnerMessageTimer = 3f;
                
                DevLog("[BossRush] 物品生成器：已生成物品 " + itemName + " (ID=" + itemId + ")");
            }
            catch (Exception e)
            {
                itemSpawnerMessage = L10n.T(
                    "生成失败：" + e.Message,
                    "Failed: " + e.Message
                );
                itemSpawnerMessageTimer = 3f;
                Debug.LogError("[BossRush] 物品生成器错误: " + e.Message);
            }
        }
    }
}
