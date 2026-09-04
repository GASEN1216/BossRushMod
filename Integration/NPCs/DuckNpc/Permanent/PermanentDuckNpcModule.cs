// ============================================================================
// PermanentDuckNpcModule.cs - 永久捏脸 NPC 接入 NPC 模块注册中心
// ============================================================================
// 模块说明：
//   与 DuckNpcModule（一次性随机 NPC）并列的第二个模块，区别只有两点：
//     1. CreateAffinityConfig() 返回**真配置**而不是 null —— 这是"永久"的根基；
//     2. 生成后额外挂交互（PermanentDuckNpcInteractable）与名字标签，
//        并登记进 PermanentDuckNpcRegistry 供婚姻系统反查。
//
//   注册方式：**不需要手动注册**，NPCModuleRegistry.AutoDiscoverModules() 会扫到。
//   要求 public 无参构造 —— 本类 internal sealed 且不写显式构造，隐式构造是 public。
//   **不要加 private 构造函数**，那会让它被静默跳过。
//
//   与羽织/叮当的一处关键差异：它们一个模块管一只 NPC；
//   本模块管**所有** isPermanent 的蓝图，因此 NpcId 返回的是模块自己的标识，
//   而不是某只 NPC 的 id。好感度配置按蓝图逐条注册（见 RegisterAllAffinityConfigs）。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 永久捏脸 NPC 模块。
    /// </summary>
    internal sealed class PermanentDuckNpcModule : INPCModule
    {
        private const string LogPrefix = "[PermanentDuckNpc]";

        /// <summary>名字标签高度。</summary>
        private const float NameTagHeight = 2.2f;

        public string NpcId
        {
            get { return "duck_npc_permanent"; }
        }

        /// <summary>
        /// 现有：快递员 10、哥布林 20、护士 30、一次性捏脸 40。这里取 50。
        /// 该值只决定遍历顺序，但必须与现有值互不相同（List.Sort 不稳定）。
        /// </summary>
        public int SpawnOrder
        {
            get { return 50; }
        }

        /// <summary>
        /// 注册中心只允许一个模块返回一份配置，而本模块管 N 只 NPC。
        /// 因此这里返回第一条永久蓝图的配置，其余在 Spawn 时补注册。
        /// </summary>
        /// <remarks>
        /// AffinityManager.RegisterNPC 是幂等的（按 npcId 覆盖写字典），
        /// 重复注册无副作用，所以"先注册一条、其余延后"是安全的。
        /// </remarks>
        public INPCAffinityConfig CreateAffinityConfig()
        {
            RegisterAllAffinityConfigs();

            List<DuckNpcBlueprint> permanents = PermanentDuckNpcRegistry.GetAllPermanent();
            if (permanents.Count == 0)
            {
                return null;
            }
            return GetOrCreateConfig(permanents[0]);
        }

        // ====================================================================
        // 好感度配置
        // ====================================================================

        private static readonly Dictionary<string, PermanentDuckNpcAffinityConfig> _configs =
            new Dictionary<string, PermanentDuckNpcAffinityConfig>(StringComparer.Ordinal);

        internal static PermanentDuckNpcAffinityConfig GetOrCreateConfig(DuckNpcBlueprint blueprint)
        {
            if (blueprint == null || string.IsNullOrEmpty(blueprint.id))
            {
                return null;
            }

            PermanentDuckNpcAffinityConfig config;
            if (_configs.TryGetValue(blueprint.id, out config))
            {
                return config;
            }

            config = new PermanentDuckNpcAffinityConfig(blueprint);
            _configs[blueprint.id] = config;
            return config;
        }

        /// <summary>把所有永久蓝图逐条注册进 AffinityManager。</summary>
        internal static void RegisterAllAffinityConfigs()
        {
            List<DuckNpcBlueprint> permanents = PermanentDuckNpcRegistry.GetAllPermanent();
            for (int i = 0; i < permanents.Count; i++)
            {
                PermanentDuckNpcAffinityConfig config = GetOrCreateConfig(permanents[i]);
                if (config == null)
                {
                    continue;
                }

                try
                {
                    AffinityManager.RegisterNPC(config);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 注册好感度配置失败 "
                        + permanents[i].id + ": " + e.Message);
                }
            }
        }

        // ====================================================================
        // 场景门控
        // ====================================================================

        public bool ShouldSpawnInScene(ModBehaviour mod, string sceneName)
        {
            if (mod == null || string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            // 竞技场让位：注册中心在竞技场有「随机支援 NPC 三选一」抽签，
            // 参与就会挤掉羽织/叮当的名额。永久 NPC 常驻主城/普通图即可。
            if (IsArenaLikeScene(mod, sceneName))
            {
                return false;
            }

            List<DuckNpcBlueprint> permanents = PermanentDuckNpcRegistry.GetAllPermanent();
            for (int i = 0; i < permanents.Count; i++)
            {
                DuckNpcBlueprint blueprint = permanents[i];
                if (blueprint == null || !blueprint.AllowsScene(sceneName))
                {
                    continue;
                }

                // 已婚 NPC 不在普通地图刷新（只在婚礼教堂由婚姻系统强制生成），
                // 与羽织/叮当同一条规则。
                if (AffinityManager.IsMarriedToPlayer(blueprint.id))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool IsArenaLikeScene(ModBehaviour mod, string sceneName)
        {
            try
            {
                if (mod.ShouldUseRandomSupportNpcSelection(sceneName))
                {
                    return true;
                }
                if (mod.UsesArenaSupportNpcPlacement())
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 竞技场判定失败，按不生成处理: " + e.Message);
                return true;
            }
            return false;
        }

        // ====================================================================
        // 生成 / 销毁
        // ====================================================================

        private bool _spawnInFlight;

        public void Spawn(ModBehaviour mod)
        {
            if (mod == null || _spawnInFlight)
            {
                return;
            }

            string sceneName;
            try
            {
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 取当前场景名失败: " + e.Message);
                return;
            }

            _spawnInFlight = true;
            SpawnForSceneAsync(sceneName).Forget();
        }

        private async UniTaskVoid SpawnForSceneAsync(string sceneName)
        {
            try
            {
                RegisterAllAffinityConfigs();

                List<DuckNpcBlueprint> permanents = PermanentDuckNpcRegistry.GetAllPermanent();
                for (int i = 0; i < permanents.Count; i++)
                {
                    DuckNpcBlueprint blueprint = permanents[i];
                    if (blueprint == null || !blueprint.AllowsScene(sceneName))
                    {
                        continue;
                    }

                    if (AffinityManager.IsMarriedToPlayer(blueprint.id))
                    {
                        continue;
                    }

                    if (PermanentDuckNpcRegistry.GetInstance(blueprint.id) != null)
                    {
                        continue;
                    }

                    // 与羽织/叮当一致：生成时统一结算每日好感度衰减
                    NPCAffinityInteractionHelper.ApplyDailyDecayOnSpawn(blueprint.id, LogPrefix);

                    await SpawnOneAsync(blueprint, sceneName);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 生成异常: " + e.Message);
            }
            finally
            {
                _spawnInFlight = false;
            }
        }

        private async UniTask SpawnOneAsync(DuckNpcBlueprint blueprint, string sceneName)
        {
            Vector3 position;
            if (!TryResolveSpawnPosition(sceneName, out position))
            {
                ModBehaviour.DevLog(LogPrefix + " 取不到刷新点，跳过: " + blueprint.id);
                return;
            }

            CharacterMainControl npc = await DuckNpcSpawner.SpawnAsync(blueprint, position, Vector3.back);
            if (npc == null)
            {
                return;
            }

            // await 之后场景可能已切走：这只 NPC 属于上一张图，立刻回收，
            // 否则会在新场景里留下一只无人管理的孤儿。
            if (!IsStillSameScene(sceneName))
            {
                ModBehaviour.DevLog(LogPrefix + " 生成完成时场景已切换，回收: " + blueprint.id);
                DuckNpcSpawner.Despawn(npc);
                return;
            }

            AttachPermanentParts(npc, blueprint, position);
            PermanentDuckNpcRegistry.RegisterInstance(blueprint.id, npc);
            ModBehaviour.DevLog(LogPrefix + " 已生成永久 NPC " + blueprint.id + " @ " + position);
        }

        private static void AttachPermanentParts(
            CharacterMainControl npc, DuckNpcBlueprint blueprint, Vector3 home)
        {
            // 交互：**必须**挂到专用子物体，不能挂角色根节点。
            // 官方 InteractableBase.Awake 会征用同 GO 上的 Collider 并把该 GO 的层
            // 改成 Interactable —— 挂根节点会把 ECM2 移动胶囊和 Character 层一起毁掉。
            try
            {
                PermanentDuckNpcInteractable.Attach(npc, blueprint.id);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 挂交互失败 " + blueprint.id + ": " + e.Message);
            }

            // 名字标签：走与现有三位 NPC 相同的 helper，取配置里的 DisplayName，
            // 不硬编码字符串。
            try
            {
                PermanentDuckNpcAffinityConfig config = GetOrCreateConfig(blueprint);
                if (config != null)
                {
                    NPCNameTagHelper.RegisterOriginalHealthBarName(
                        npc.transform, config.DisplayName, NameTagHeight, LogPrefix);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 注册名字标签失败 " + blueprint.id + ": " + e.Message);
            }

            if (!blueprint.canWander)
            {
                return;
            }

            try
            {
                DuckNpcMovement movement = npc.gameObject.AddComponent<DuckNpcMovement>();
                // Bind 失败（场景无 A* 图等）时组件自我禁用，NPC 退回站桩，不是致命错误。
                movement.Bind(npc, home, blueprint.wanderRadius);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 挂移动组件失败 " + blueprint.id + ": " + e.Message);
            }
        }

        private static bool IsStillSameScene(string expectedSceneName)
        {
            try
            {
                return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == expectedSceneName;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveSpawnPosition(string sceneName, out Vector3 position)
        {
            position = Vector3.zero;

            try
            {
                Vector3[] points = ModBehaviour.GetSharedCommonNPCSpawnPointsForScene(sceneName);
                if (!NPCSpawnConfig.TryGetSharedSpawnPosition(points, out position, null))
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 取刷新点异常: " + e.Message);
                return false;
            }

            try
            {
                RaycastHit hit;
                if (Physics.Raycast(position + Vector3.up, Vector3.down, out hit, 5f))
                {
                    position = hit.point + new Vector3(0f, 0.1f, 0f);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 落点贴地修正失败: " + e.Message);
            }

            return position != Vector3.zero;
        }

        public void Destroy(ModBehaviour mod)
        {
            List<DuckNpcBlueprint> permanents = PermanentDuckNpcRegistry.GetAllPermanent();
            for (int i = 0; i < permanents.Count; i++)
            {
                DuckNpcBlueprint blueprint = permanents[i];
                if (blueprint == null)
                {
                    continue;
                }

                CharacterMainControl npc = PermanentDuckNpcRegistry.GetInstance(blueprint.id);
                if (npc != null)
                {
                    DuckNpcSpawner.Despawn(npc);
                }
                PermanentDuckNpcRegistry.UnregisterInstance(blueprint.id);
            }

            // 注意不要在这里把 _spawnInFlight 置 false：那条 async 还在飞，
            // 它自己的 finally 会置位。提前放开会让下一次 Spawn 与它并发。
        }

        /// <summary>
        /// 由婚姻系统的泛化分支调用：在指定位置强制生成一只永久 NPC（婚礼教堂用）。
        /// </summary>
        internal static async UniTask<CharacterMainControl> ForceSpawnAtAsync(
            string npcId, Vector3 position, bool stayStill)
        {
            DuckNpcBlueprint blueprint;
            if (!PermanentDuckNpcRegistry.TryGetBlueprint(npcId, out blueprint))
            {
                return null;
            }

            CharacterMainControl existing = PermanentDuckNpcRegistry.GetInstance(npcId);
            if (existing != null)
            {
                return existing;
            }

            CharacterMainControl npc = await DuckNpcSpawner.SpawnAsync(blueprint, position, Vector3.back);
            if (npc == null)
            {
                return null;
            }

            AttachPermanentParts(npc, blueprint, position);

            if (stayStill)
            {
                try
                {
                    DuckNpcMovement movement = npc.GetComponent<DuckNpcMovement>();
                    if (movement != null)
                    {
                        movement.Hold();
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 站桩设置失败 " + npcId + ": " + e.Message);
                }
            }

            PermanentDuckNpcRegistry.RegisterInstance(npcId, npc);
            return npc;
        }

        /// <summary>清空配置缓存。Mod 卸载时调用。</summary>
        internal static void ResetStaticCaches()
        {
            _configs.Clear();
        }
    }
}
