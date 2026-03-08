// ============================================================================
// GoblinReforgeInteractable.cs - 哥布林重铸交互组件
// ============================================================================
// 模块说明：
//   为哥布林NPC添加"重铸"交互选项
//   玩家交互后打开重铸UI
// ============================================================================

using System;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 哥布林重铸交互组件
    /// 作为哥布林的子交互选项，玩家选择后打开重铸UI
    /// </summary>
    public class GoblinReforgeInteractable : InteractableBase
    {
        private GoblinNPCController controller;
        private bool isInitialized = false;
        
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Reforge";
                this.InteractName = "BossRush_Reforge";
            }
            catch { }
            
            try { this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f); } catch { }
            
            try { base.Awake(); } catch { }
            
            try { controller = GetComponentInParent<GoblinNPCController>(); } catch { }
            
            // 子选项不需要自己的 Collider，隐藏交互标记
            try { this.MarkerActive = false; } catch { }
            
            isInitialized = true;
        }
        
        protected override void Start()
        {
            try { base.Start(); } catch { }
        }
        
        protected override bool IsInteractable()
        {
            return isInitialized;
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                ModBehaviour.DevLog("[GoblinNPC] 玩家选择重铸服务");

                // 播放哥布林交互音效
                BossRushAudioManager.Instance?.PlayGoblinInteractSFX();
                
                // 让哥布林进入对话状态
                if (controller != null)
                {
                    controller.StartDialogue();
                }
                
                // 打开重铸UI
                ReforgeUIManager.OpenUI(controller);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 重铸服务交互出错: " + e.Message);
            }
        }
        
        protected override void OnInteractStop()
        {
            try { base.OnInteractStop(); } catch { }
        }
    }
    
    /// <summary>
    /// 哥布林主交互组件
    /// 使用交互组模式，支持多个交互选项（对话、商店、礼物、重铸）
    /// 主选项为"对话"，子选项为商店、礼物、重铸
    /// </summary>
    public class GoblinInteractable : InteractableBase
    {
        private GoblinNPCController controller;
        private GoblinReforgeInteractable reforgeInteractable;
        private NPCShopInteractable shopInteractable;  // 使用通用商店交互组件
        private NPCGiftInteractable giftInteractable;  // 使用通用礼物交互组件
        private NPCDivorceInteractable divorceInteractable;  // 离婚选项（仅配偶可见）
        
        protected override void Awake()
        {
            try
            {
                // 使用交互组模式，显示多个选项
                this.interactableGroup = true;
                
                // 设置主交互名称为"对话"（第一个选项）
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Chat";
                this.InteractName = "BossRush_Chat";
                
                // 设置交互标记偏移
                this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] GoblinInteractable.Awake 设置属性失败: " + e.Message);
            }

            // 确保有 Collider
            try
            {
                Collider col = GetComponent<Collider>();
                if (col == null)
                {
                    CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
                    capsule.height = 1.5f;  // 哥布林较矮
                    capsule.radius = 0.6f;
                    capsule.center = new Vector3(0f, 0.75f, 0f);
                    capsule.isTrigger = false;
                    this.interactCollider = capsule;
                }
                else
                {
                    this.interactCollider = col;
                }
                
                // 设置 Layer 为 Interactable
                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    gameObject.layer = interactableLayer;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] GoblinInteractable 设置 Collider 失败: " + e.Message);
            }

            try
            {
                NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[GoblinNPC]");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] GoblinInteractable 预创建交互组失败: " + e.Message);
            }

            // 在确保 Collider 就绪后再调用基类 Awake，避免 InteractableBase 内部空引用
            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] GoblinInteractable base.Awake 异常: " + e.Message);
            }
            
            // 获取控制器
            controller = GetComponent<GoblinNPCController>();
            
            ModBehaviour.DevLog("[GoblinNPC] GoblinInteractable.Awake 完成");
        }
        
        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] GoblinInteractable base.Start 异常: " + e.Message);
            }
            
            // 确保获取到控制器
            if (controller == null)
            {
                controller = GetComponent<GoblinNPCController>();
            }
            
            // 创建子交互选项
            CreateSubInteractables();
        }
        
        /// <summary>
        /// 创建子交互选项（商店、礼物、重铸、离婚）
        /// 使用反射将子选项注入到 otherInterablesInGroup 列表中（与快递员阿稳一致）
        /// 主选项为"对话"（由 OnTimeOut 处理）
        /// 子选项顺序：1.商店(好感度≥2级才显示) 2.赠送礼物 3.重铸服务
        /// </summary>
        private void CreateSubInteractables()
        {
            try
            {
                // 获取或创建 otherInterablesInGroup 列表（统一走公用助手）
                var list = NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[GoblinNPC]");
                if (list == null)
                {
                    return;
                }

                // 主选项是"对话"（由 OnTimeOut 处理）
                // 子选项：商店、礼物、重铸、离婚

                // 1. 创建商店交互选项（好感度等级≥2才显示，使用通用组件）
                shopInteractable = NPCInteractionGroupHelper.AddSubInteractable<NPCShopInteractable>(
                    transform,
                    "ShopInteractable",
                    list,
                    component => component.NpcId = GoblinAffinityConfig.NPC_ID);
                if (shopInteractable != null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 创建商店交互选项并注入到交互组（使用通用组件）");
                }

                // 2. 创建礼物赠送交互选项（使用通用组件）
                giftInteractable = NPCInteractionGroupHelper.AddSubInteractable<NPCGiftInteractable>(
                    transform,
                    "GiftInteractable",
                    list,
                    component => component.NpcId = GoblinAffinityConfig.NPC_ID);
                if (giftInteractable != null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 创建礼物赠送交互选项并注入到交互组（使用通用组件）");
                }

                // 3. 创建重铸交互选项
                reforgeInteractable = NPCInteractionGroupHelper.AddSubInteractable<GoblinReforgeInteractable>(
                    transform,
                    "ReforgeInteractable",
                    list);
                if (reforgeInteractable != null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 创建重铸交互选项并注入到交互组");
                }

                // 4. 创建离婚交互选项（仅"婚礼教堂中的当前配偶"才加入交互组）
                if (ShouldAddDivorceOption())
                {
                    divorceInteractable = NPCInteractionGroupHelper.AddSubInteractable<NPCDivorceInteractable>(
                        transform,
                        "DivorceInteractable",
                        list,
                        component => component.NpcId = GoblinAffinityConfig.NPC_ID);
                    if (divorceInteractable != null)
                    {
                        ModBehaviour.DevLog("[GoblinNPC] 创建离婚交互选项并注入到交互组");
                    }
                }

                ModBehaviour.DevLog("[GoblinNPC] 所有子交互选项已注入到 otherInterablesInGroup，共 " + list.Count + " 个（主选项=对话）");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 创建子交互选项失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 只有当前配偶且该NPC实例由婚礼教堂刷出时，才允许添加离婚选项
        /// </summary>
        private bool ShouldAddDivorceOption()
        {
            if (!AffinityManager.IsMarriedToPlayer(GoblinAffinityConfig.NPC_ID))
            {
                return false;
            }

            string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            if (string.IsNullOrEmpty(spouseNpcId) || spouseNpcId != GoblinAffinityConfig.NPC_ID)
            {
                return false;
            }

            if (ModBehaviour.Instance == null)
            {
                return false;
            }

            Transform npcTransform = controller != null ? controller.NpcTransform : transform;
            return ModBehaviour.Instance.IsWeddingNpcInstance(npcTransform);
        }
        
        protected override bool IsInteractable()
        {
            return true;
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            base.OnInteractStart(interactCharacter);
            ModBehaviour.DevLog("[GoblinNPC] 玩家开始与哥布林交互（交互组模式）");
            
            // 让哥布林进入对话状态
            if (controller != null)
            {
                controller.StartDialogue();
            }
        }
        
        /// <summary>
        /// 主交互选项"对话"被选中时调用
        /// </summary>
        protected override void OnTimeOut()
        {
            // 主交互选项"对话"被选中
            try
            {
                ModBehaviour.DevLog("[GoblinNPC] 玩家选择对话（主选项）");
                
                // 播放哥布林交互音效
                BossRushAudioManager.Instance?.PlayGoblinInteractSFX();
                
                // 获取控制器
                if (controller == null)
                {
                    controller = GetComponent<GoblinNPCController>();
                }
                
                // 让哥布林进入对话状态
                if (controller != null)
                {
                    controller.StartDialogue();
                }
                
                // 执行对话逻辑
                DoChat();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 对话交互出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 执行对话逻辑（从 GoblinChatInteractable 移植）
        /// 聊天不限制次数，每次都从话语库随机选一条
        /// 但好感度增加每天只有一次
        /// 好感度等级 >= 8 时每次聊天都会冒爱心
        /// </summary>
        private void DoChat()
        {
            try
            {
                if (NPCAffinityInteractionHelper.TryHandleSpouseCheatingRebuke(
                    GoblinAffinityConfig.NPC_ID,
                    controller?.transform,
                    () =>
                    {
                        if (controller != null)
                        {
                            controller.ShowBrokenHeartBubble();
                        }
                    },
                    "[GoblinNPC]"))
                {
                    if (controller != null)
                    {
                        controller.EndDialogueWithStay(10f);
                    }
                    return;
                }

                // 兜底：若达到剧情等级但因状态窗口错过了自动触发，则在本次"聊天"时优先触发大对话。
                // 叙事顺序优先：先5级再10级
                int currentLevel = AffinityManager.GetLevel(GoblinAffinityConfig.NPC_ID);
                if (currentLevel >= 5 && !AffinityManager.HasTriggeredStory5(GoblinAffinityConfig.NPC_ID))
                {
                    if (controller != null)
                    {
                        controller.TriggerStoryDialogue(5);
                        return;
                    }
                }
                if (currentLevel >= 10 && !AffinityManager.HasTriggeredStory10(GoblinAffinityConfig.NPC_ID))
                {
                    if (controller != null)
                    {
                        controller.TriggerStoryDialogue(10);
                        return;
                    }
                }

                bool dailyChatGranted;
                NPCAffinityInteractionHelper.ProcessChatAffinityAndFeedback(
                    GoblinAffinityConfig.NPC_ID,
                    GoblinAffinityConfig.DAILY_CHAT_AFFINITY,
                    8,
                    () =>
                    {
                        if (controller != null)
                        {
                            controller.ShowLoveHeartBubble();
                        }
                    },
                    "[GoblinNPC]",
                    out dailyChatGranted);

                // 结婚后每日聊天赠送冷凝液（参考护士赠送安神滴剂）
                bool isMarriedToGoblin = AffinityManager.IsMarriedToPlayer(GoblinAffinityConfig.NPC_ID);
                if (isMarriedToGoblin && dailyChatGranted && controller != null)
                {
                    string successBanner = L10n.T("叮当递给了你一瓶冷淬液", "Dingdang gives you a bottle of Cold Quench Fluid");
                    string fullInventoryBanner = L10n.T("背包已满，冷淬液掉落在叮当脚边。", "Inventory full. The Cold Quench Fluid was dropped at Dingdang's feet.");
                    controller.TryGiveRewardItem(ColdQuenchFluidConfig.TYPE_ID, 1, successBanner, fullInventoryBanner);
                }
                
                // 无论是否已聊天，都从问候对话库随机选一条显示
                string dialogue = NPCDialogueSystem.GetDialogue(GoblinAffinityConfig.NPC_ID, DialogueCategory.Greeting);
                NPCDialogueSystem.ShowDialogue(GoblinAffinityConfig.NPC_ID, controller?.transform, dialogue);
                
                // 延迟结束对话（使用10秒停留）
                if (controller != null)
                {
                    controller.EndDialogueWithStay(10f);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 执行对话失败: " + e.Message);
                
                // 结束对话（使用10秒停留）
                if (controller != null)
                {
                    controller.EndDialogueWithStay(10f);
                }
            }
        }
        
        /// <summary>
        /// 获取随机对话内容
        /// </summary>
        private string GetRandomChatDialogue()
        {
            int level = AffinityManager.GetLevel(GoblinAffinityConfig.NPC_ID);
            
            // 根据好感度等级选择不同风格的对话
            if (level >= 8)
            {
                return L10n.T("老朋友！叮当超级开心见到你！", "Old friend! Dingdang is super happy to see you!");
            }
            else if (level >= 5)
            {
                return L10n.T("嘿！今天也来找叮当玩啦？", "Hey! Coming to play with Dingdang today too?");
            }
            else if (level >= 2)
            {
                // 随机选择
                string[] chatDialogues = new string[]
                {
                    "叮当今天心情不错！",
                    "你来找叮当玩啦？",
                    "叮当最喜欢闪闪发光的东西了！",
                    "嘿嘿，叮当有好多宝贝~",
                    "你是叮当的朋友吗？",
                    "叮当的锤子可厉害了！",
                    "今天天气真好呀~"
                };
                int index = UnityEngine.Random.Range(0, chatDialogues.Length);
                return L10n.T(chatDialogues[index], chatDialogues[index]);
            }
            else
            {
                return L10n.T("嗯...你好。", "Hmm... hello.");
            }
        }
        
    }
}
