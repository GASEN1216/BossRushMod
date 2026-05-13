// ============================================================================
// BossRushInteractables.cs - 交互组件
// ============================================================================
// 模块说明：
//   定义 BossRush 模组的所有交互组件，包括：
//   - BossRushInteractable: 难度选择交互（弹指可灭/有点意思/无间炼狱）
//   - BossRushSignInteractable: 路牌主交互（包含所有子选项）
//   - BossRushNextWaveInteractable: 下一波交互
//   - BossRushAmmoRefillInteractable: 弹药补给交互
//   - BossRushTeleportBubble: 传送气泡交互
//   - BossRushClearLootboxesInteractable: 清空箱子交互
//
// 交互流程：
//   1. 玩家与路牌交互，选择难度
//   2. 开始第一波后，路牌切换到"加油"模式
//   3. 波次间可通过路牌开始下一波或购买弹药
//   4. 通关后路牌显示"凯旋"状态
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

namespace BossRush
{
    /// <summary>
    /// BossRush 难度选择交互组件
    /// <para>用于选择难度（弹指可灭/有点意思/无间炼狱）或开始第一波</para>
    /// </summary>
    public class BossRushInteractable : InteractableBase
    {
        // 难度与显示配置
        public int bossesPerWave = 1;
        public bool useCustomName = false;
        public string customName = null;
        // 是否为无间炼狱模式入口
        public bool isInfiniteHell = false;

        protected override void Awake()
        {
            // 1. 提前设置名称，防止 base.Awake 中的 patch 读取不到或覆盖
            // 使用统一的场景判断方法
            SetInteractNameByScene();
            this.overrideInteractName = true;

            // 2. 尝试调用基类 Awake，并捕获可能的异常 (如 Patch 导致的 NRE)
            try
            {
                base.Awake();
            }
            catch (Exception)
            {
                // 仅记录警告，不报错，确保 Mod 能继续运行
                // NRE 是预期的，因为其他 Mod 的 Patch 可能无法处理我们这种动态创建的空对象
            }

            // 3. 再次确保名称正确 (以防 base.Awake 重置了它)
            SetInteractNameByScene();

            // 4. 确保对象是"隐形"的，作为一个纯逻辑交互点
            // 不需要 Collider (如果它是 Group 的一部分，主对象有 Collider)
            // 如果它是 MultiInteraction 的一部分，它也不需要 Collider
            // 但是为了安全，我们给它一个禁用的 Collider，防止 GetComp<Collider> 报错
            if (GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.enabled = false;
            }

            // 禁用 MeshRenderer (如果有)
            var renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;

            // 隐藏自身的世界交互标记，只通过菜单显示这个选项
            this.MarkerActive = false;
        }

        protected override void Start()
        {
            base.Start();
            // 强制设置名称，防止Awake时被覆盖或未生效
            SetInteractNameByScene();
            this.overrideInteractName = true;
        }

        /// <summary>
        /// 根据当前场景设置交互名称
        /// </summary>
        private void SetInteractNameByScene()
        {
            // 检查是否在有效的 BossRush 竞技场场景内
            bool isInArena = BossRush.ModBehaviour.Instance != null &&
                             BossRush.ModBehaviour.Instance.IsCurrentSceneValidBossRushArena();

            if (useCustomName && !string.IsNullOrEmpty(customName))
            {
                // 本地化难度选项名称
                string localizedName = GetLocalizedCustomName(customName);
                this.InteractName = localizedName;
                this._overrideInteractNameKey = localizedName;
            }
            else if (isInArena)
            {
                string name = L10n.T("开始第一波", "Start First Wave");
                this.InteractName = name;
                this._overrideInteractNameKey = name;
            }
            else
            {
                this.InteractName = "Boss Rush";
                this._overrideInteractNameKey = "BossRush";
            }
        }

        /// <summary>
        /// 获取本地化的自定义名称（难度选项）- 返回本地化键
        /// </summary>
        private string GetLocalizedCustomName(string cnName)
        {
            // 返回本地化键，实际文本由 LocalizationManager 解析
            switch (cnName)
            {
                case "弹指可灭":
                    return "BossRush_Easy";
                case "有点意思":
                    return "BossRush_Hard";
                case "无间炼狱":
                    return "BossRush_InfiniteHell";
                default:
                    return cnName;
            }
        }

        protected override bool IsInteractable()
        {
            // 只有在Boss Rush未激活时才可以交互
            return BossRush.ModBehaviour.Instance == null || !BossRush.ModBehaviour.Instance.IsActive;
        }

        protected override void OnTimeOut()
        {
            base.OnTimeOut();

            // 交互完成，根据当前场景决定行为
            if (BossRush.ModBehaviour.Instance != null)
            {
                // 如果已经在有效的 BossRush 竞技场场景内（DEMO竞技场或零号区等），直接开始第一波
                if (BossRush.ModBehaviour.Instance.IsCurrentSceneValidBossRushArena())
                {
                    // 通过交互点配置当前模式（默认1，只在使用自定义难度时生效）
                    BossRush.ModBehaviour.Instance.ConfigureBossRushMode(bossesPerWave, isInfiniteHell);
                    BossRush.ModBehaviour.Instance.StartFirstWave();

                    // 交互后隐藏同一菜单下的所有难度选项（包括自己和另一个），防止玩家重复选择
                    try
                    {
                        Transform parent = transform.parent;
                        if (parent != null)
                        {
                            var siblings = parent.GetComponentsInChildren<BossRushInteractable>(true);
                            if (siblings != null)
                            {
                                foreach (var sibling in siblings)
                                {
                                    if (sibling != null && sibling.gameObject != null)
                                    {
                                        sibling.gameObject.SetActive(false);
                                    }
                                }
                                if (parent.gameObject != null)
                                {
                                    // 地面难度入口容器仍然在选完难度后整体销毁
                                    if (parent.gameObject.name == "BossRush_DifficultyEntry")
                                    {
                                        UnityEngine.Object.Destroy(parent.gameObject);
                                    }
                                    // 路牌本体不再销毁，只移除其中的难度/生小鸡选项，给“下一波”选项腾位置
                                    else if (parent.gameObject.name == "BossRush_Roadsign")
                                    {
                                        var sign = parent.GetComponent<BossRushSignInteractable>();
                                        if (sign != null)
                                        {
                                            sign.RemoveDifficultyOptions();

                                            // 普通难度：切换到加油模式，并添加加油站选项
                                            if (!isInfiniteHell)
                                            {
                                                sign.SetCheerMode();
                                                sign.AddAmmoRefillOption(); // 弹指可灭/有点意思也添加加油站
                                            }
                                            else
                                            {
                                                // 无间炼狱：主交互改为显示现金池累计
                                                sign.SetCheerMode(); // 保持状态逻辑为 Cheer
                                                sign.UpdateInfiniteHellCashDisplay(0L);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // 没有父节点时至少隐藏自身
                            gameObject.SetActive(false);
                        }
                    }
                    catch
                    {
                        // 兜底：如果出现异常，至少隐藏自身
                        gameObject.SetActive(false);
                    }
                }
                else
                {
                    // 否则打开地图选择 UI，让玩家确认后再传送
                    // 使用官方 MapSelectionView 流程，确认后才扣费
                    BossRushMapSelectionHelper.ShowBossRushMapSelection();
                }
            }
        }
    }

    public class BossRushAmmoRefillInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                // 使用本地化键，避免两边出现 *
                this.InteractName = "BossRush_AmmoShop";
                this._overrideInteractNameKey = "BossRush_AmmoShop";
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
            try
            {
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch {}
            try
            {
                this.MarkerActive = false;
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            try
            {
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod == null)
                {
                    return false;
                }

                bool bossRushActive = false;
                bool modeDActive = false;
                bool arenaActive = false;  // 竞技场激活状态（通关后仍为true，直到离开场景）

                try
                {
                    bossRushActive = mod.IsActive;
                }
                catch {}

                try
                {
                    modeDActive = mod.IsModeDActive;
                }
                catch {}

                try
                {
                    arenaActive = mod.IsBossRushArenaActive;
                }
                catch {}

                // 只要 BossRush 激活、ModeD 激活、或竞技场激活（通关后），都允许交互
                if (!bossRushActive && !modeDActive && !arenaActive)
                {
                    return false;
                }
            }
            catch {}

            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                if (BossRush.ModBehaviour.Instance != null)
                {
                    BossRush.ModBehaviour.Instance.ShowAmmoShop();
                }
            }
            catch {}
        }
    }

    public class BossRushRepairInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                // 使用本地化键，避免两边出现 *
                this.InteractName = "BossRush_Repair";
                this._overrideInteractNameKey = "BossRush_Repair";
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
            try
            {
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch {}
            try
            {
                this.MarkerActive = false;
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                ItemRepairView.Show();
            }
            catch {}
        }
    }

    public class BossRushTeleportBubble : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                this.InteractName = L10n.T("传送", "Teleport");
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
            try
            {
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = true;
                }
            }
            catch {}
            try
            {
                this.MarkerActive = false;
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}
            try
            {
                this.InteractName = L10n.T("传送", "Teleport");
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                // 从配置系统获取当前地图的默认位置
                Vector3 target = ModBehaviour.GetCurrentSceneDefaultPosition();

                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        GameCamera camera = GameCamera.Instance;
                        Vector3 offset = Vector3.zero;

                        try
                        {
                            if (camera != null)
                            {
                                offset = camera.transform.position - main.transform.position;
                            }
                        }
                        catch {}

                        try
                        {
                            main.SetPosition(target);
                        }
                        catch {}

                        try
                        {
                            if (camera != null)
                            {
                                camera.transform.position = main.transform.position + offset;
                            }
                        }
                        catch {}
                    }
                }
                catch {}
            }
            catch {}

            try
            {
                UnityEngine.Object.Destroy(base.gameObject);
            }
            catch {}
        }
    }

    public class BossRushEntryInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                // 入口显示为“哎哟~你干嘛~”，鼓励玩家多按几次下蛋
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Sign_Entry";
                this.InteractName = "BossRush_Sign_Entry";
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                // 将交互标记放在路牌中部（模型已向上偏移1米，所以这里用0）
                this.interactMarkerOffset = new Vector3(0f, 0f, 0f);
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
            try
            {
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = true;
                }
            }
            catch {}
            try
            {
                this.MarkerActive = false;
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Sign_Entry";
                this.InteractName = "BossRush_Sign_Entry";
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                if (BossRush.ModBehaviour.Instance != null)
                {
                    BossRush.ModBehaviour.Instance.TrySpawnEggForPlayer();
                }
            }
            catch {}
        }
    }

    /// <summary>
    /// 路牌上的主交互组件，包含生小鸡、难度选项、下一波等所有交互选项
    /// </summary>
    public class BossRushSignInteractable : InteractableBase
    {
        private bool optionsInjected = false;
        private List<InteractableBase> groupOptions = new List<InteractableBase>();

        private enum SignState
        {
            EntryAndDifficulty,
            Cheer,
            NextWave,
            Victory
        }

        private SignState _state = SignState.EntryAndDifficulty;

        private void UpdateMainInteractName()
        {
            try
            {
                switch (_state)
                {
                    case SignState.EntryAndDifficulty:
                        // 使用本地化键显示"哎哟~你干嘛~"，避免显示"*"
                        this.overrideInteractName = true;
                        this._overrideInteractNameKey = "BossRush_Sign_Entry";
                        this.InteractName = "BossRush_Sign_Entry";
                        break;
                    case SignState.Cheer:
                        // 使用本地化键显示“加油!!!”（无间炼狱会在此基础上改写为现金池显示）
                        this.InteractName = "BossRush_Sign_Cheer";
                        this.overrideInteractName = true;
                        this._overrideInteractNameKey = "BossRush_Sign_Cheer";
                        break;
                    case SignState.NextWave:
                        // 使用本地化键显示"冲！（下一波）"
                        this.overrideInteractName = true;
                        this._overrideInteractNameKey = "BossRush_Sign_NextWave";
                        this.InteractName = "BossRush_Sign_NextWave";
                        break;
                    case SignState.Victory:
                        // 使用本地化键显示“君王凯旋归来，拿取属于王的荣耀！”
                        this.InteractName = "BossRush_Sign_Victory";
                        this.overrideInteractName = true;
                        this._overrideInteractNameKey = "BossRush_Sign_Victory";
                        break;
                }
            }
            catch {}
        }

        protected override void Awake()
        {
            try
            {
                this.interactableGroup = true;
                _state = SignState.EntryAndDifficulty;
                UpdateMainInteractName();
                // 将路牌交互标记放在路牌中部（模型已向上偏移1米，所以这里用0）
                this.interactMarkerOffset = new Vector3(0f, 0f, 0f);
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}

            // 注入所有选项
            if (!optionsInjected)
            {
                InjectAllOptions();
            }
        }

        private void InjectAllOptions()
        {
            try
            {
                optionsInjected = true;

                // 获取或创建 otherInterablesInGroup 列表
                var field = typeof(InteractableBase).GetField("otherInterablesInGroup",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null) return;

                var list = field.GetValue(this) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(this, list);
                }

                // 主选项 "哎哟~你干嘛~" 由 OnTimeOut 处理（生小鸡），这里只添加难度子选项

                // 1. 弹指可灭（每波 1 个Boss）
                GameObject easyObj = new GameObject("BossRushOption_Easy");
                easyObj.transform.SetParent(transform);
                easyObj.transform.localPosition = Vector3.zero;
                var easyInteract = easyObj.AddComponent<BossRushInteractable>();
                easyInteract.useCustomName = true;
                easyInteract.customName = "弹指可灭";
                easyInteract.bossesPerWave = 1;
                list.Add(easyInteract);
                groupOptions.Add(easyInteract);

                // 2. 有点意思（每波 3 个Boss）
                GameObject hardObj = new GameObject("BossRushOption_Hard");
                hardObj.transform.SetParent(transform);
                hardObj.transform.localPosition = Vector3.zero;
                var hardInteract = hardObj.AddComponent<BossRushInteractable>();
                hardInteract.useCustomName = true;
                hardInteract.customName = "有点意思";
                hardInteract.bossesPerWave = 3;
                list.Add(hardInteract);
                groupOptions.Add(hardInteract);

                // 3. 无间炼狱（每波使用配置中的Boss数量）
                GameObject hellObj = new GameObject("BossRushOption_InfiniteHell");
                hellObj.transform.SetParent(transform);
                hellObj.transform.localPosition = Vector3.zero;
                var hellInteract = hellObj.AddComponent<BossRushInteractable>();
                hellInteract.useCustomName = true;
                hellInteract.customName = "无间炼狱";
                hellInteract.isInfiniteHell = true;
                int hellBosses = 3;
                if (BossRush.ModBehaviour.Instance != null)
                {
                    hellBosses = BossRush.ModBehaviour.Instance.GetInfiniteHellBossesPerWaveFromConfig();
                }
                if (hellBosses < 1) hellBosses = 1;
                hellInteract.bossesPerWave = hellBosses;
                list.Add(hellInteract);
                groupOptions.Add(hellInteract);

                ModBehaviour.DevLog("[BossRush] BossRushSignInteractable: 已注入 3 个难度子选项（含无间炼狱）");
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] BossRushSignInteractable.InjectAllOptions 失败: " + e.Message);
            }
        }

        public void AddNextWaveOnly()
        {
            try
            {
                var field = typeof(InteractableBase).GetField("otherInterablesInGroup",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null) return;

                var list = field.GetValue(this) as List<InteractableBase>;
                if (list == null) return;

                bool hasNext = false;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var item = list[i];
                    if (item == null)
                    {
                        list.RemoveAt(i);
                        continue;
                    }

                    if (item is BossRushNextWaveInteractable)
                    {
                        hasNext = true;
                    }
                }

                if (!hasNext)
                {
                    GameObject nextObj = new GameObject("BossRushOption_NextWave");
                    nextObj.transform.SetParent(transform);
                    nextObj.transform.localPosition = Vector3.zero;
                    var nextInteract = nextObj.AddComponent<BossRushNextWaveInteractable>();
                    list.Add(nextInteract);
                    groupOptions.Add(nextInteract);
                }
            }
            catch
            {
            }
        }

        public void AddAmmoRefillOption()
        {
            try
            {
                var field = typeof(InteractableBase).GetField("otherInterablesInGroup",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null) return;

                var list = field.GetValue(this) as List<InteractableBase>;
                if (list == null) return;

                bool hasAmmo = false;
                bool hasRepair = false;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var item = list[i];
                    if (item == null)
                    {
                        list.RemoveAt(i);
                        continue;
                    }

                    if (item is BossRushAmmoRefillInteractable)
                    {
                        hasAmmo = true;
                    }
                    else if (item is BossRushRepairInteractable)
                    {
                        hasRepair = true;
                    }
                }

                if (!hasAmmo)
                {
                    GameObject ammoObj = new GameObject("BossRushOption_AmmoRefill");
                    ammoObj.transform.SetParent(transform);
                    ammoObj.transform.localPosition = Vector3.zero;
                    var ammoInteract = ammoObj.AddComponent<BossRushAmmoRefillInteractable>();
                    list.Add(ammoInteract);
                    groupOptions.Add(ammoInteract);
                }

                if (!hasRepair)
                {
                    GameObject repairObj = new GameObject("BossRushOption_Repair");
                    repairObj.transform.SetParent(transform);
                    repairObj.transform.localPosition = Vector3.zero;
                    var repairInteract = repairObj.AddComponent<BossRushRepairInteractable>();
                    list.Add(repairInteract);
                    groupOptions.Add(repairInteract);
                }
            }
            catch
            {
            }
        }

        public void RemoveDifficultyOptions()
        {
            try
            {
                // 移除难度选项和生小鸡选项，只保留下一波
                foreach (var option in groupOptions.ToArray())
                {
                    if (option is BossRushInteractable || option is BossRushEntryInteractable)
                    {
                        if (option != null && option.gameObject != null)
                        {
                            option.gameObject.SetActive(false);
                        }
                    }
                }
            }
            catch {}
        }

        public void SetCheerMode()
        {
            _state = SignState.Cheer;
            UpdateMainInteractName();
        }

        public void SetNextWaveMode()
        {
            _state = SignState.NextWave;
            UpdateMainInteractName();
        }

        public void SetVictoryMode()
        {
            _state = SignState.Victory;
            UpdateMainInteractName();
        }

        /// <summary>
        /// 设置路牌为入口/生小鸡模式（白手起家波次结束后使用）
        /// </summary>
        public void SetEntryMode()
        {
            _state = SignState.EntryAndDifficulty;
            UpdateMainInteractName();
        }

        /// <summary>
        /// 在路牌主交互上更新无间炼狱现金池显示
        /// 使用固定本地化键 BossRush_InfiniteHell_Cash，值为“现金池已累计：<color=red>xxx</color>”
        /// </summary>
        public void UpdateInfiniteHellCashDisplay(long cash)
        {
            try
            {
                string key = "BossRush_InfiniteHell_Cash";
                string value = L10n.T(
                    "现金池已累计：<color=red>" + cash.ToString() + "</color>",
                    "Cash Pool: <color=red>" + cash.ToString() + "</color>"
                );

                // 尝试写入本地化字典中的对应键值
                try
                {
                    string[] types = new string[]
                    {
                        "SodaCraft.Localizations.LocalizationManager, SodaLocalization",
                        "SodaCraft.Localizations.LocalizationManager, TeamSoda.Duckov.Core",
                        "SodaCraft.Localizations.LocalizationManager, Assembly-CSharp",
                        "LocalizationManager, Assembly-CSharp"
                    };

                    Type locType = null;
                    for (int i = 0; i < types.Length; i++)
                    {
                        locType = Type.GetType(types[i]);
                        if (locType != null) break;
                    }

                    if (locType != null)
                    {
                        var fields = locType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                        foreach (var field in fields)
                        {
                            object val = null;
                            try { val = field.GetValue(null); } catch {}
                            if (val == null) continue;

                            var dict = val as Dictionary<string, string>;
                            if (dict != null)
                            {
                                if (dict.ContainsKey(key))
                                {
                                    dict[key] = value;
                                }
                                else
                                {
                                    dict.Add(key, value);
                                }
                                break;
                            }

                            var dictObj = val as System.Collections.IDictionary;
                            if (dictObj != null)
                            {
                                if (dictObj.Contains(key))
                                {
                                    dictObj[key] = value;
                                }
                                else
                                {
                                    dictObj.Add(key, value);
                                }
                                break;
                            }
                        }
                    }
                }
                catch {}

                // 使用固定键作为交互名称，实际显示内容由本地化字典提供
                this.InteractName = key;
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                if (BossRush.ModBehaviour.Instance == null)
                {
                    return;
                }

                switch (_state)
                {
                    case SignState.EntryAndDifficulty:
                        // 初始状态：主选项生小鸡
                        BossRush.ModBehaviour.Instance.TrySpawnEggForPlayer();
                        break;
                    case SignState.Cheer:
                        // 加油状态：纯展示，没有实际效果
                        break;
                    case SignState.NextWave:
                        // 开启下一波，然后回到加油状态
                        BossRush.ModBehaviour.Instance.StartNextWaveCountdown();
                        _state = SignState.Cheer;
                        UpdateMainInteractName();
                        break;
                    case SignState.Victory:
                        // 凯旋状态：纯展示，没有实际效果
                        break;
                }
            }
            catch {}
        }
    }

    public class BossRushNextWaveInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                // 使用本地化键，避免两边出现 *，并保持与其他难度选项一致的颜色风格
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Sign_NextWave";
                this.InteractName = "BossRush_Sign_NextWave";
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
            try
            {
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch {}
            try
            {
                this.MarkerActive = false;
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}
            try
            {
                // 使用本地化键，避免两边出现 *，并保持与其他难度选项一致的颜色风格
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Sign_NextWave";
                this.InteractName = "BossRush_Sign_NextWave";
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                if (BossRush.ModBehaviour.Instance != null)
                {
                    BossRush.ModBehaviour.Instance.StartNextWaveCountdown();
                }
            }
            catch {}
            try
            {
                UnityEngine.Object.Destroy(base.gameObject);
            }
            catch {}
        }
    }

    public class BossRushClearEmptyLootboxesInteractable : InteractableBase
    {
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                // 使用本地化键，避免两边出现 *
                this.InteractName = "BossRush_ClearEmptyLootboxes";
                this._overrideInteractNameKey = "BossRush_ClearEmptyLootboxes";
            }
            catch {}
            try
            {
                this.interactCollider = GetComponent<Collider>();
            }
            catch {}
            try
            {
                base.Awake();
            }
            catch {}
            try
            {
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch {}
            try
            {
                this.MarkerActive = false;
            }
            catch {}
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch {}
        }

        protected override bool IsInteractable()
        {
            return true;
        }

        protected override void OnTimeOut()
        {
            try
            {
                int removed = BossRushLootboxUtility.DestroyMarkedLootboxes(int.MinValue, true);

                try
                {
                    var main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        string msg = L10n.T("已清空 " + removed + " 个空箱子", "Cleared " + removed + " empty lootboxes");
                        main.PopText(msg, -1f);
                    }
                }
                catch {}
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 清空空箱子失败: " + e.Message);
            }
        }
    }

}
