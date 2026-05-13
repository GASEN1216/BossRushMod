// ============================================================================
// EquipmentFactory.cs - 自定义装备/武器工厂
// ============================================================================
// 模块说明：
//   提供简单的 API 从 AssetBundle 加载自定义装备、武器和Buff
//   支持自动扫描目录加载所有资源包
//
// ============================================================================
// 资源文件要求（放在 Assets/Equipment/ 目录下）：
// ============================================================================
//
//   BossRush/Assets/Equipment/
//   ├── my_equipment          # 可包含多个装备/武器的 AssetBundle（无扩展名）
//   └── ...
//
// ============================================================================
// Prefab 命名规范（重要！）：
// ============================================================================
//
//   格式：{自定义名}_{类型}_{后缀}
//
//   【装备类】
//   | Prefab 类型 | 命名格式                | 示例                    |
//   |-------------|-------------------------|-------------------------|
//   | 头盔物品    | {名称}_Helmet_Item      | MyGear_Helmet_Item      |
//   | 头盔模型    | {名称}_Helmet_Model     | MyGear_Helmet_Model     |
//   | 护甲物品    | {名称}_Armor_Item       | MyGear_Armor_Item       |
//   | 护甲模型    | {名称}_Armor_Model      | MyGear_Armor_Model      |
//   | 背包物品    | {名称}_Backpack_Item    | MyGear_Backpack_Item    |
//   | 背包模型    | {名称}_Backpack_Model   | MyGear_Backpack_Model   |
//   | 面罩物品    | {名称}_FaceMask_Item    | MyGear_FaceMask_Item    |
//   | 面罩模型    | {名称}_FaceMask_Model   | MyGear_FaceMask_Model   |
//   | 耳机物品    | {名称}_Headset_Item     | MyGear_Headset_Item     |
//   | 耳机模型    | {名称}_Headset_Model    | MyGear_Headset_Model    |
//   | 图腾物品    | {名称}_Totem_Item       | FlightTotem_Lv1_Item    |
//   | 图腾模型    | {名称}_Totem_Model      | FlightTotem_Lv1_Model   |
//
//   【武器类】
//   | Prefab 类型 | 命名格式                | 示例                    |
//   |-------------|-------------------------|-------------------------|
//   | 枪械物品    | {名称}_Gun_Item         | Dragon_Gun_Item         |
//   | 枪械模型    | {名称}_Gun_Model        | Dragon_Gun_Model（可选）|
//   | 子弹预制体  | {名称}_Bullet           | Dragon_Bullet           |
//   | Buff预制体  | {名称}_Buff             | Dragon_Buff             |
//
//   自动匹配规则：
//   - Dragon_Gun_Item 自动匹配 Dragon_Gun_Model、Dragon_Bullet、Dragon_Buff
//   - 武器的 Buff 和 Bullet 会自动注入到 ItemSetting_Gun 组件
//
// ============================================================================
// Unity 中 Prefab 配置要求：
// ============================================================================
//
// 【装备 Item Prefab】必须配置：
//   - Item 组件
//   - typeID：唯一ID（建议 600000+）
//   - ⚠️ 不需要配置 tags，代码会自动添加！
//
// 【图腾 Item Prefab】必须配置：
//   - Item 组件
//   - typeID：唯一ID（建议 600100+）
//   - displayName：本地化键名（如 "BossRush_FlightTotem"）
//   - quality：品质等级（1-8）
//   - ⚠️ 不需要配置 tags，代码会自动添加 Totem 标签
//
// 【武器 Item Prefab】必须配置：
//   - Item 组件 + ItemSetting_Gun 组件
//   - typeID：唯一ID
//   - ItemSetting_Gun 的基础属性（triggerMode, reloadMode 等）
//   - ⚠️ bulletPfb 和 buff 可以不配置，代码会自动关联同名资源
//
// 【Buff Prefab】必须配置：
//   - Buff 组件（来自 Duckov.Buffs 命名空间）
//   - id：唯一Buff ID
//   - maxLayers：最大叠加层数
//   - displayName/description：本地化键名
//   - limitedLifeTime/totalLifeTime：持续时间
//   - effects：Effect列表（可选）
//
// 【Bullet Prefab】必须配置：
//   - Projectile 组件
//   - radius：碰撞半径
//   - hitFx：命中特效（可选）
//
// ============================================================================
// 使用方式：
// ============================================================================
//
//   方式一：自动加载（推荐）
//   EquipmentFactory.LoadAllEquipment();  // 自动扫描并加载所有 bundle
//
//   方式二：手动加载单个 bundle
//   EquipmentFactory.LoadBundle("my_equipment");
//
//   方式三：获取已加载的Buff（用于代码中手动应用）
//   Buff dragonBuff = EquipmentFactory.GetLoadedBuff("Dragon");
//   character.AddBuff(dragonBuff, fromWho);
//
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using Duckov.Utilities;
using Duckov.Buffs;

namespace BossRush
{
    public static partial class EquipmentFactory
    {
        // ========== 装备处理 ==========

        /// <summary>
        /// 处理装备类型物品（头盔、护甲、背包等）
        /// </summary>
        private static void ProcessEquipmentItem(Item itemPrefab, string baseName, EquipmentType equipType, ItemAgent modelAgent)
        {
            string tagName = GetTagName(equipType);

            // 添加装备 Tag
            EquipmentHelper.AddTagToItem(itemPrefab, tagName);

            // 图腾类型需要额外添加 DontDropOnDeadInSlot 标签（绑定装备）
            if (equipType == EquipmentType.Totem)
            {
                EquipmentHelper.AddTagToItem(itemPrefab, "DontDropOnDeadInSlot");
                ModBehaviour.DevLog("[EquipmentFactory] 为图腾添加绑定装备标签: " + itemPrefab.name);
            }

            // 注入 EquipmentModel（如果 Unity 中未配置）
            if (modelAgent != null && !HasEquipmentModel(itemPrefab))
            {
                InjectEquipmentModel(itemPrefab, modelAgent);
            }

            // 为所有装备类型注入 ItemGraphic（备用显示路径，修复假人显示问题）
            // 假人的 CharacterEquipmentController 可能需要 ItemGraphic 作为备用显示路径
            if (modelAgent != null)
            {
                InjectItemGraphicForEquipment(itemPrefab, modelAgent);
            }
        }

        // ========== 武器处理 ==========

        /// <summary>
        /// 处理武器类型物品
        /// </summary>
        private static void ProcessGunItem(
            Item itemPrefab,
            string baseName,
            ItemAgent modelAgent,
            Dictionary<string, ItemSetting_Gun> gunSettingsByBaseName,
            Dictionary<string, Buff> buffsByPrefix,
            Dictionary<string, Projectile> bulletsByPrefix)
        {
            // 添加 Gun Tag
            EquipmentHelper.AddTagToItem(itemPrefab, "Gun");

            // 获取 ItemSetting_Gun 组件
            ItemSetting_Gun gunSetting = null;
            if (gunSettingsByBaseName.ContainsKey(baseName))
            {
                gunSetting = gunSettingsByBaseName[baseName];
            }
            else
            {
                gunSetting = itemPrefab.GetComponent<ItemSetting_Gun>();
            }

            if (gunSetting == null)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 武器缺少 ItemSetting_Gun 组件: " + itemPrefab.name);
                return;
            }

            // 提取武器前缀用于匹配 Buff 和 Bullet
            string weaponPrefix = ExtractWeaponPrefix(baseName);

            // 自动关联 Buff（如果未配置）
            if (gunSetting.buff == null)
            {
                Buff matchedBuff;
                if (buffsByPrefix.TryGetValue(weaponPrefix, out matchedBuff))
                {
                    InjectGunBuff(gunSetting, matchedBuff);
                    ModBehaviour.DevLog("[EquipmentFactory] 自动关联 Buff: " + weaponPrefix + "_Buff -> " + itemPrefab.name);
                }
            }

            // 自动关联 Bullet（如果未配置）
            if (gunSetting.bulletPfb == null && itemPrefab.TypeID != DragonKingBossGunConfig.WeaponTypeId)
            {
                Projectile matchedBullet;
                if (bulletsByPrefix.TryGetValue(weaponPrefix, out matchedBullet))
                {
                    InjectGunBullet(gunSetting, matchedBullet);
                    ModBehaviour.DevLog("[EquipmentFactory] 自动关联 Bullet: " + weaponPrefix + "_Bullet -> " + itemPrefab.name);
                }
            }

            // 注入模型
            if (modelAgent != null)
            {
                // 注入 EquipmentModel（装备栏显示）
                if (!HasEquipmentModel(itemPrefab))
                {
                    InjectEquipmentModel(itemPrefab, modelAgent);
                }

                // 为枪械注入 ItemGraphic（手持和掉落显示的核心依赖）
                // 游戏原版通过 item.ItemGraphic 来：
                //   1. CreateHandheldAgent: IsGun + ItemGraphic != null → ItemAgent_Gun.BuildAgent(ItemGraphic.gameObject)
                //   2. CreatePickupAgent: 默认 PickupAgentPrefab → CreateGraphic() → ItemGraphicInfo.CreateAGraphic(item) → 用 ItemGraphic 实例化3D模型
                InjectItemGraphic(itemPrefab, modelAgent);
            }
        }

        /// <summary>
        /// 注入 Buff 到武器的 ItemSetting_Gun
        /// </summary>
        private static void InjectGunBuff(ItemSetting_Gun gunSetting, Buff buff)
        {
            try
            {
                FieldInfo buffField = typeof(ItemSetting_Gun).GetField("buff",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (buffField != null)
                {
                    buffField.SetValue(gunSetting, buff);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入 Buff 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注入 Bullet 到武器的 ItemSetting_Gun
        /// </summary>
        private static void InjectGunBullet(ItemSetting_Gun gunSetting, Projectile bullet)
        {
            try
            {
                FieldInfo bulletField = typeof(ItemSetting_Gun).GetField("bulletPfb",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (bulletField != null)
                {
                    bulletField.SetValue(gunSetting, bullet);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入 Bullet 失败: " + e.Message);
            }
        }

        // ========== 通用辅助方法 ==========

        /// <summary>
        /// 检查 Item 是否已配置 EquipmentModel
        /// </summary>
        private static bool HasEquipmentModel(Item item)
        {
            try
            {
                var agentUtilitiesField = typeof(Item).GetField("agentUtilities",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentUtilitiesField == null) return false;

                var agentUtilities = agentUtilitiesField.GetValue(item);
                if (agentUtilities == null) return false;

                var agentsField = agentUtilities.GetType().GetField("agents",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentsField == null) return false;

                var agentsList = agentsField.GetValue(agentUtilities);
                if (agentsList == null) return false;

                var countProp = agentsList.GetType().GetProperty("Count");
                var itemProp = agentsList.GetType().GetProperty("Item");
                int count = (int)countProp.GetValue(agentsList);

                for (int i = 0; i < count; i++)
                {
                    var entry = itemProp.GetValue(agentsList, new object[] { i });
                    var keyField = entry.GetType().GetField("key", BindingFlags.Public | BindingFlags.Instance);

                    string key = keyField != null ? keyField.GetValue(entry) as string : null;
                    if (key == "EquipmentModel")
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 修复 Model 的 Layer 和 Shader
        /// </summary>
        private static void FixModelLayerAndShader(GameObject modelGo)
        {
            try
            {
                SetLayerRecursively(modelGo, CHARACTER_LAYER);

                if (gameShader == null)
                {
                    gameShader = Shader.Find(GAME_SHADER_NAME);
                }

                if (gameShader != null)
                {
                    var renderers = modelGo.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers)
                    {
                        if (renderer.sharedMaterials != null)
                        {
                            foreach (var mat in renderer.sharedMaterials)
                            {
                                if (mat != null && mat.shader != null)
                                {
                                    string oldShaderName = mat.shader.name;
                                    if (oldShaderName.Contains("Standard") ||
                                        oldShaderName.Contains("Lit") ||
                                        oldShaderName.Contains("Universal"))
                                    {
                                        mat.shader = gameShader;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 修复 Layer/Shader 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 为缺少 DuckovItemAgent 组件的模型创建运行时副本并添加组件
        /// AssetBundle 中的对象是只读的，动态添加的组件无法被正确序列化
        /// 所以需要先实例化创建可修改的运行时副本
        /// </summary>
        private static DuckovItemAgent CreateModelWrapper(GameObject originalModel, string modelName)
        {
            try
            {
                // 实例化 AssetBundle 中的对象，创建一个可修改的运行时副本
                GameObject runtimeCopy = UnityEngine.Object.Instantiate(originalModel);
                runtimeCopy.name = modelName;  // 保持原名
                runtimeCopy.hideFlags = HideFlags.HideAndDontSave;

                // 在运行时副本上添加 DuckovItemAgent 组件
                DuckovItemAgent agent = runtimeCopy.AddComponent<DuckovItemAgent>();

                // 确保 socketsList 被初始化
                var socketsField = typeof(DuckovItemAgent).GetField("socketsList",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (socketsField != null)
                {
                    var socketsList = socketsField.GetValue(agent);
                    if (socketsList == null)
                    {
                        socketsField.SetValue(agent, new List<Transform>());
                    }
                }

                // 设置 Layer
                SetLayerRecursively(runtimeCopy, CHARACTER_LAYER);

                // [Bug修复] 将模板移到极远处，防止装备模型（如龙王之冕）在场景中被渲染
                // Instantiate 出来的副本会被游戏装备系统放到正确位置（挂载到角色骨骼），不受影响
                runtimeCopy.transform.position = new Vector3(0f, -9999f, 0f);

                // 不要销毁，让它作为 prefab 使用
                UnityEngine.Object.DontDestroyOnLoad(runtimeCopy);

                ModBehaviour.DevLog("[EquipmentFactory] 成功创建模型运行时副本: " + modelName);
                return agent;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 创建模型运行时副本失败: " + modelName + " - " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 递归设置 Layer
        /// </summary>
        private static ItemAgent TryCreateEmbeddedMeleeModel(Item itemPrefab, string baseName)
        {
            if (itemPrefab == null)
            {
                return null;
            }

            try
            {
                Renderer[] renderers = itemPrefab.GetComponentsInChildren<Renderer>(true);
                bool has3DRenderer = false;
                foreach (Renderer renderer in renderers)
                {
                    if (renderer != null && !(renderer is SpriteRenderer))
                    {
                        has3DRenderer = true;
                        break;
                    }
                }

                if (!has3DRenderer)
                {
                    ModBehaviour.DevLog("[EquipmentFactory] 近战物品缺少可用渲染节点，无法创建内嵌模型: " + itemPrefab.name);
                    return null;
                }

                GameObject runtimeCopy = UnityEngine.Object.Instantiate(itemPrefab.gameObject);
                runtimeCopy.name = baseName + "_EmbeddedMeleeModel";
                runtimeCopy.hideFlags = HideFlags.HideAndDontSave;

                Item copiedItem = runtimeCopy.GetComponent<Item>();
                if (copiedItem != null)
                {
                    UnityEngine.Object.DestroyImmediate(copiedItem);
                }

                ItemGraphicInfo copiedGraphic = runtimeCopy.GetComponent<ItemGraphicInfo>();
                if (copiedGraphic != null)
                {
                    UnityEngine.Object.DestroyImmediate(copiedGraphic);
                }

                DuckovItemAgent[] existingAgents = runtimeCopy.GetComponents<DuckovItemAgent>();
                foreach (DuckovItemAgent existingAgent in existingAgents)
                {
                    if (existingAgent != null)
                    {
                        UnityEngine.Object.DestroyImmediate(existingAgent);
                    }
                }

                ItemAgent_MeleeWeapon embeddedAgent = runtimeCopy.GetComponent<ItemAgent_MeleeWeapon>();
                if (embeddedAgent == null)
                {
                    embeddedAgent = runtimeCopy.AddComponent<ItemAgent_MeleeWeapon>();
                }

                EnsureSocketsListInitialized(embeddedAgent);
                SetLayerRecursively(runtimeCopy, CHARACTER_LAYER);
                FixModelLayerAndShader(runtimeCopy);
                runtimeCopy.transform.position = new Vector3(0f, -9999f, 0f);
                UnityEngine.Object.DontDestroyOnLoad(runtimeCopy);

                ModBehaviour.DevLog("[EquipmentFactory] 已为近战物品创建内嵌模型 Handheld 源: " + itemPrefab.name);
                return embeddedAgent;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 创建近战内嵌模型失败: " + itemPrefab.name + " - " + e.Message);
                return null;
            }
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        /// <summary>
        /// 注入 EquipmentModel 到物品
        /// </summary>
        private static void InjectEquipmentModel(Item item, ItemAgent modelAgent)
        {
            try
            {
                var agentUtilitiesField = typeof(Item).GetField("agentUtilities",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentUtilitiesField == null) return;

                var agentUtilities = agentUtilitiesField.GetValue(item);
                if (agentUtilities == null) return;

                var agentsField = agentUtilities.GetType().GetField("agents",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentsField == null) return;

                // 获取 AgentKeyPair 类型
                Type agentKeyPairType = null;
                var nestedTypes = agentUtilities.GetType().GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var nt in nestedTypes)
                {
                    if (nt.Name == "AgentKeyPair")
                    {
                        agentKeyPairType = nt;
                        break;
                    }
                }

                if (agentKeyPairType == null)
                {
                    agentKeyPairType = Type.GetType("ItemStatsSystem.ItemAgentUtilities+AgentKeyPair, ItemStatsSystem");
                }

                if (agentKeyPairType == null) return;

                var agentsList = agentsField.GetValue(agentUtilities);
                if (agentsList == null)
                {
                    var listType = typeof(List<>).MakeGenericType(agentKeyPairType);
                    agentsList = Activator.CreateInstance(listType);
                    agentsField.SetValue(agentUtilities, agentsList);
                }

                var keyField = agentKeyPairType.GetField("key",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var agentPrefabField = agentKeyPairType.GetField("agentPrefab",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                object newEntry = Activator.CreateInstance(agentKeyPairType);
                if (keyField != null)
                {
                    keyField.SetValue(newEntry, "EquipmentModel");
                }
                if (agentPrefabField != null)
                {
                    agentPrefabField.SetValue(newEntry, modelAgent);
                }

                var addMethod = agentsList.GetType().GetMethod("Add");
                if (addMethod != null)
                {
                    addMethod.Invoke(agentsList, new object[] { newEntry });
                }

                var cacheField = agentUtilities.GetType().GetField("hashedAgentsCache",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (cacheField != null)
                {
                    cacheField.SetValue(agentUtilities, null);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入 EquipmentModel 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 为装备类型注入 ItemGraphic（通用版本，用于头盔、护甲等）
        /// 修复假人显示问题：假人的 CharacterEquipmentController 可能需要 ItemGraphic 作为备用显示路径
        /// </summary>
        private static void InjectItemGraphicForEquipment(Item item, ItemAgent modelAgent)
        {
            try
            {
                // 检查已有的 ItemGraphic 是否有效
                ItemGraphicInfo existingGraphic = item.ItemGraphic;
                if (existingGraphic != null)
                {
                    bool isValid = false;
                    try
                    {
                        GameObject existingGo = existingGraphic.gameObject;
                        if (existingGo != null)
                        {
                            Renderer[] renderers = existingGo.GetComponentsInChildren<Renderer>(true);
                            isValid = renderers != null && renderers.Length > 0;
                        }
                    }
                    catch { isValid = false; }

                    if (isValid)
                    {
                        return; // 已有有效的 ItemGraphic，跳过
                    }
                }

                GameObject modelGo = modelAgent.gameObject;

                // 为装备创建通用的 ItemGraphicInfo 组件
                ItemGraphicInfo graphicInfo = modelGo.GetComponent<ItemGraphicInfo>();
                if (graphicInfo == null)
                {
                    graphicInfo = modelGo.AddComponent<ItemGraphicInfo>();
                }

                // 设置 groundPoint（掉落在地面时的定位点）
                if (graphicInfo.groundPoint == null)
                {
                    Transform existingGround = modelGo.transform.Find("GroundPoint");
                    if (existingGround != null)
                    {
                        graphicInfo.groundPoint = existingGround;
                    }
                    else
                    {
                        GameObject groundPointGo = new GameObject("GroundPoint");
                        groundPointGo.transform.SetParent(modelGo.transform);
                        groundPointGo.transform.localPosition = Vector3.zero;
                        groundPointGo.transform.localRotation = Quaternion.identity;
                        groundPointGo.transform.localScale = Vector3.one;
                        graphicInfo.groundPoint = groundPointGo.transform;
                    }
                }

                // 通过反射设置 item.itemGraphic 私有字段
                FieldInfo itemGraphicField = typeof(Item).GetField("itemGraphic",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemGraphicField != null)
                {
                    itemGraphicField.SetValue(item, graphicInfo);
                    ModBehaviour.DevLog("[EquipmentFactory] 成功注入 ItemGraphic (装备): " + item.name);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入装备 ItemGraphic 失败: " + item.name + " - " + e.Message);
            }
        }

        /// <summary>
        /// 为枪械注入 ItemGraphic（ItemGraphicInfo_Gun），使游戏原版的手持和掉落显示路径正常工作
        /// 手持路径：CreateHandheldAgent → IsGun + ItemGraphic != null → ItemAgent_Gun.BuildAgent(ItemGraphic.gameObject)
        /// 掉落路径：CreatePickupAgent → 默认 PickupAgentPrefab → CreateGraphic() → ItemGraphicInfo.CreateAGraphic(item)
        /// </summary>
        private static void InjectItemGraphic(Item item, ItemAgent modelAgent)
        {
            try
            {
                // 检查已有的 ItemGraphic 是否有效
                // AssetBundle 中的 ItemGraphic 引用可能在游戏更新后丢失（序列化引用断裂）
                ItemGraphicInfo existingGraphic = item.ItemGraphic;
                if (existingGraphic != null)
                {
                    // 验证已有 ItemGraphic 的 GameObject 是否有效
                    bool isValid = false;
                    try
                    {
                        // 检查 GameObject 是否存在且有实际的渲染内容
                        GameObject existingGo = existingGraphic.gameObject;
                        if (existingGo != null)
                        {
                            // 检查是否有 Renderer 或子对象（空壳 ItemGraphic 无法正常显示）
                            Renderer[] renderers = existingGo.GetComponentsInChildren<Renderer>(true);
                            isValid = renderers != null && renderers.Length > 0;

                            string graphicType = existingGraphic.GetType().Name;
                            int childCount = existingGo.transform.childCount;
                            int rendererCount = renderers != null ? renderers.Length : 0;
                            ModBehaviour.DevLog("[EquipmentFactory] 已有 ItemGraphic 检查: " + item.name +
                                " type=" + graphicType +
                                " children=" + childCount +
                                " renderers=" + rendererCount +
                                " valid=" + isValid);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[EquipmentFactory] 已有 ItemGraphic 检查异常: " + e.Message);
                        isValid = false;
                    }

                    if (isValid)
                    {
                        ModBehaviour.DevLog("[EquipmentFactory] 物品已有有效 ItemGraphic，跳过注入: " + item.name);
                        return;
                    }
                    else
                    {
                        ModBehaviour.DevLog("[EquipmentFactory] 物品已有 ItemGraphic 但无效（无渲染器），将强制替换: " + item.name);
                    }
                }

                GameObject modelGo = modelAgent.gameObject;

                // 在模型 GameObject 上创建 ItemGraphicInfo_Gun 组件
                // ItemGraphicInfo_Gun 继承自 ItemGraphicInfo，是枪械专用的图形信息组件
                ItemGraphicInfo_Gun graphicInfo = modelGo.GetComponent<ItemGraphicInfo_Gun>();
                if (graphicInfo == null)
                {
                    graphicInfo = modelGo.AddComponent<ItemGraphicInfo_Gun>();
                }

                // 设置枪械动画类型为 gun（默认是 normal，会导致 BuildAgent 读取后覆盖为错误值）
                graphicInfo.handAnimationType = HandheldAnimationType.gun;

                // 设置 groundPoint（掉落在地面时的定位点）
                if (graphicInfo.groundPoint == null)
                {
                    Transform existingGround = modelGo.transform.Find("GroundPoint");
                    if (existingGround != null)
                    {
                        graphicInfo.groundPoint = existingGround;
                    }
                    else
                    {
                        GameObject groundPointGo = new GameObject("GroundPoint");
                        groundPointGo.transform.SetParent(modelGo.transform);
                        groundPointGo.transform.localPosition = Vector3.zero;
                        groundPointGo.transform.localRotation = Quaternion.identity;
                        groundPointGo.transform.localScale = Vector3.one;
                        graphicInfo.groundPoint = groundPointGo.transform;
                    }
                }

                // 确保 Sockets 父节点存在，并将 Muzzle/Tec 等关键节点移入其中
                // BuildAgent 会在 Sockets 下查找 Muzzle 和 Tec，如果找不到会创建默认位置 (0,0,0) 的节点
                // 导致枪口火焰出现在手上而不是枪口位置
                Transform socketsParent = modelGo.transform.Find("Sockets");
                if (socketsParent == null)
                {
                    // 创建 Sockets 父节点
                    GameObject socketsGo = new GameObject("Sockets");
                    socketsGo.transform.SetParent(modelGo.transform);
                    socketsGo.transform.localPosition = Vector3.zero;
                    socketsGo.transform.localRotation = Quaternion.identity;
                    socketsGo.transform.localScale = Vector3.one;
                    socketsParent = socketsGo.transform;
                }

                // 将根节点下的 Muzzle、Muzzle2、Tec 移入 Sockets 下
                // 这些节点可能直接在模型根节点下（AssetBundle 导出时的结构）
                string[] socketNodeNames = { "Muzzle", "Muzzle2", "Tec" };
                foreach (string nodeName in socketNodeNames)
                {
                    // 先检查是否已在 Sockets 下
                    Transform existingInSockets = socketsParent.Find(nodeName);
                    if (existingInSockets != null) continue;

                    // 查找根节点下的同名节点并移入 Sockets
                    Transform nodeInRoot = modelGo.transform.Find(nodeName);
                    if (nodeInRoot != null)
                    {
                        nodeInRoot.SetParent(socketsParent);
                        ModBehaviour.DevLog("[EquipmentFactory] 将 " + nodeName + " 移入 Sockets: " + item.name);
                    }
                }

                // [龙息武器] 手动调整 Tec（配件）和 Muzzle（枪口）的位置，整体往左偏移
                if (item.TypeID == DragonBreathConfig.WEAPON_TYPE_ID)
                {
                    float xOffset = -0.05f; // 往左偏移0.05单位
                    Transform tecNode = socketsParent.Find("Tec");
                    if (tecNode != null)
                    {
                        Vector3 pos = tecNode.localPosition;
                        tecNode.localPosition = new Vector3(pos.x + xOffset, pos.y, pos.z);
                        ModBehaviour.DevLog("[EquipmentFactory] 龙息武器 Tec 位置调整: x " + (pos.x + xOffset));
                    }
                    Transform muzzleNode = socketsParent.Find("Muzzle");
                    if (muzzleNode != null)
                    {
                        Vector3 pos = muzzleNode.localPosition;
                        muzzleNode.localPosition = new Vector3(pos.x + xOffset, pos.y, pos.z);
                        ModBehaviour.DevLog("[EquipmentFactory] 龙息武器 Muzzle 位置调整: x " + (pos.x + xOffset));
                    }
                }

                // 设置 sockets（配件插槽，用于显示枪口、瞄准镜等附件模型）
                if (socketsParent.childCount > 0)
                {
                    var socketTransforms = new List<Transform>();
                    foreach (Transform child in socketsParent)
                    {
                        socketTransforms.Add(child);
                    }
                    graphicInfo.SetSockets(socketTransforms);
                }

                // 通过反射设置 item.itemGraphic 私有字段
                FieldInfo itemGraphicField = typeof(Item).GetField("itemGraphic",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (itemGraphicField != null)
                {
                    itemGraphicField.SetValue(item, graphicInfo);
                    ModBehaviour.DevLog("[EquipmentFactory] 成功注入 ItemGraphic (ItemGraphicInfo_Gun): " + item.name);
                }
                else
                {
                    ModBehaviour.DevLog("[EquipmentFactory] 未找到 itemGraphic 字段: " + item.name);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 注入 ItemGraphic 失败: " + item.name + " - " + e.Message);
            }
        }

        private static void FinalizeCustomMeleeWeapon(Item itemPrefab, ItemAgent modelAgent, string baseName)
        {
            try
            {
                EquipmentHelper.AddTagToItem(itemPrefab, "Weapon");
                EquipmentHelper.AddTagToItem(itemPrefab, "MeleeWeapon");

                try
                {
                    itemPrefab.SetBool("IsMeleeWeapon", true, true);
                }
                catch
                {
                }

                if (itemPrefab.GetComponent<ItemSetting_MeleeWeapon>() == null)
                {
                    itemPrefab.gameObject.AddComponent<ItemSetting_MeleeWeapon>();
                    ModBehaviour.DevLog("[EquipmentFactory] 自动补齐 ItemSetting_MeleeWeapon: " + itemPrefab.name);
                }

                if (modelAgent == null)
                {
                    ModBehaviour.DevLog("[EquipmentFactory] 近战武器缺少模型，跳过 Handheld 注入: " + itemPrefab.name);
                    return;
                }

                ItemAgent_MeleeWeapon meleeTemplate = itemPrefab.GetComponent<ItemAgent_MeleeWeapon>();
                if (meleeTemplate == null)
                {
                    meleeTemplate = itemPrefab.gameObject.AddComponent<ItemAgent_MeleeWeapon>();
                }

                ApplyMeleeAgentDefaults(meleeTemplate, null);

                ItemAgent_MeleeWeapon handheldPrefab =
                    CreateMeleeHandheldPrefab(itemPrefab, modelAgent, meleeTemplate, baseName);
                if (handheldPrefab == null)
                {
                    ModBehaviour.DevLog("[EquipmentFactory] 近战 Handheld prefab 创建失败: " + itemPrefab.name);
                    return;
                }

                SetAgentUtilityPrefab(itemPrefab, HANDHELD_AGENT_KEY, handheldPrefab);
                ModBehaviour.DevLog("[EquipmentFactory] 已从工厂源头注册近战 Handheld: " + itemPrefab.name);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] FinalizeCustomMeleeWeapon 失败: " + itemPrefab.name + " - " + e.Message);
            }
        }

        private static ItemAgent_MeleeWeapon CreateMeleeHandheldPrefab(
            Item itemPrefab,
            ItemAgent modelAgent,
            ItemAgent_MeleeWeapon meleeTemplate,
            string baseName)
        {
            try
            {
                GameObject runtimeCopy = UnityEngine.Object.Instantiate(modelAgent.gameObject);
                runtimeCopy.name = baseName + "_Handheld_Model";
                runtimeCopy.hideFlags = HideFlags.HideAndDontSave;
                runtimeCopy.transform.position = new Vector3(0f, -9999f, 0f);
                SetLayerRecursively(runtimeCopy, CHARACTER_LAYER);
                UnityEngine.Object.DontDestroyOnLoad(runtimeCopy);

                DuckovItemAgent[] existingAgents = runtimeCopy.GetComponents<DuckovItemAgent>();
                foreach (DuckovItemAgent existingAgent in existingAgents)
                {
                    if (existingAgent != null && existingAgent.GetType() == typeof(DuckovItemAgent))
                    {
                        UnityEngine.Object.DestroyImmediate(existingAgent);
                    }
                }

                ItemAgent_MeleeWeapon handheldPrefab = runtimeCopy.GetComponent<ItemAgent_MeleeWeapon>();
                if (handheldPrefab == null)
                {
                    handheldPrefab = runtimeCopy.AddComponent<ItemAgent_MeleeWeapon>();
                }

                ApplyMeleeAgentDefaults(handheldPrefab, meleeTemplate);
                return handheldPrefab;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] 创建近战 Handheld prefab 失败: " + itemPrefab.name + " - " + e.Message);
                return null;
            }
        }

        private static void ApplyMeleeAgentDefaults(ItemAgent_MeleeWeapon target, ItemAgent_MeleeWeapon template)
        {
            if (target == null)
            {
                return;
            }

            // 原版近战武器的 handheldSocket 是 normalHandheld（挂在 RightHandSocket，受攻击动画骨骼驱动）
            // 而不是 meleeWeapon（挂在 MeleeWeaponSocketFixed，固定位置不受动画驱动）
            target.handheldSocket = template != null ? template.handheldSocket : HandheldSocketTypes.normalHandheld;
            target.handAnimationType = template != null ? template.handAnimationType : HandheldAnimationType.meleeWeapon;

            if (template != null)
            {
                if (template.hitFx != null)
                {
                    target.hitFx = template.hitFx;
                }

                if (template.slashFx != null)
                {
                    target.slashFx = template.slashFx;
                }
            }

            EnsureSocketsListInitialized(target);

            try
            {
                FieldInfo soundKeyField = typeof(ItemAgent_MeleeWeapon).GetField("soundKey",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (soundKeyField != null)
                {
                    string soundKey = "Default";
                    if (template != null)
                    {
                        object rawValue = soundKeyField.GetValue(template);
                        if (rawValue is string templateKey && !string.IsNullOrWhiteSpace(templateKey))
                        {
                            soundKey = templateKey;
                        }
                    }

                    soundKeyField.SetValue(target, soundKey);
                }
            }
            catch
            {
            }

            try
            {
                FieldInfo slashDelayField = typeof(ItemAgent_MeleeWeapon).GetField("slashFxDelayTime",
                    BindingFlags.Public | BindingFlags.Instance);
                if (slashDelayField != null)
                {
                    float delay = 0.05f;
                    if (template != null)
                    {
                        object rawValue = slashDelayField.GetValue(template);
                        if (rawValue is float templateDelay)
                        {
                            delay = templateDelay;
                        }
                    }

                    slashDelayField.SetValue(target, delay);
                }
            }
            catch
            {
            }
        }

        private static void EnsureSocketsListInitialized(DuckovItemAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            try
            {
                FieldInfo socketsField = typeof(DuckovItemAgent).GetField("socketsList",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (socketsField == null)
                {
                    return;
                }

                object socketsList = socketsField.GetValue(agent);
                if (socketsList == null)
                {
                    socketsField.SetValue(agent, new List<Transform>());
                }
            }
            catch
            {
            }
        }

        private static void SetAgentUtilityPrefab(Item item, string key, ItemAgent prefab)
        {
            if (item == null || prefab == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            try
            {
                FieldInfo agentUtilitiesField = typeof(Item).GetField("agentUtilities",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentUtilitiesField == null)
                {
                    return;
                }

                object agentUtilities = agentUtilitiesField.GetValue(item);
                if (agentUtilities == null)
                {
                    return;
                }

                FieldInfo agentsField = agentUtilities.GetType().GetField("agents",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentsField == null)
                {
                    return;
                }

                Type agentKeyPairType = null;
                foreach (Type nestedType in agentUtilities.GetType().GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (nestedType.Name == "AgentKeyPair")
                    {
                        agentKeyPairType = nestedType;
                        break;
                    }
                }

                if (agentKeyPairType == null)
                {
                    agentKeyPairType = Type.GetType("ItemStatsSystem.ItemAgentUtilities+AgentKeyPair, ItemStatsSystem");
                }

                if (agentKeyPairType == null)
                {
                    return;
                }

                object agentsList = agentsField.GetValue(agentUtilities);
                if (agentsList == null)
                {
                    Type listType = typeof(List<>).MakeGenericType(agentKeyPairType);
                    agentsList = Activator.CreateInstance(listType);
                    agentsField.SetValue(agentUtilities, agentsList);
                }

                FieldInfo keyField = agentKeyPairType.GetField("key",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo agentPrefabField = agentKeyPairType.GetField("agentPrefab",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (keyField == null || agentPrefabField == null)
                {
                    return;
                }

                PropertyInfo countProperty = agentsList.GetType().GetProperty("Count");
                PropertyInfo itemProperty = agentsList.GetType().GetProperty("Item");
                object existingEntry = null;
                int count = countProperty != null ? (int)countProperty.GetValue(agentsList) : 0;
                for (int i = 0; i < count; i++)
                {
                    object entry = itemProperty.GetValue(agentsList, new object[] { i });
                    if ((keyField.GetValue(entry) as string) == key)
                    {
                        existingEntry = entry;
                        break;
                    }
                }

                if (existingEntry == null)
                {
                    existingEntry = Activator.CreateInstance(agentKeyPairType);
                    keyField.SetValue(existingEntry, key);
                    agentsList.GetType().GetMethod("Add").Invoke(agentsList, new object[] { existingEntry });
                }

                agentPrefabField.SetValue(existingEntry, prefab);

                FieldInfo cacheField = agentUtilities.GetType().GetField("hashedAgentsCache",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (cacheField != null)
                {
                    cacheField.SetValue(agentUtilities, null);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentFactory] SetAgentUtilityPrefab 失败: " + item.name + " / " + key + " - " + e.Message);
            }
        }
    }
}
