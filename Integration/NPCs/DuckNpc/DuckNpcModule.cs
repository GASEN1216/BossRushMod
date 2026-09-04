// ============================================================================
// DuckNpcModule.cs - 捏脸 NPC 接入现有 NPC 模块注册中心
// ============================================================================
// 模块说明：
//   实现 INPCModule，让蓝图 NPC 复用现成的场景刷新/销毁流程，
//   不需要在 ModBehaviour 或 BossRushIntegration 里加任何一行。
//
//   注册方式：**不需要手动注册**。NPCModuleRegistry.AutoDiscoverModules() 会扫描
//   本 Mod 程序集里所有非抽象、有 public 无参构造函数的 INPCModule 实现。
//   本类是 internal sealed 且不写显式构造函数 → 隐式无参构造是 public → 会被发现。
//   **不要给本类加 private 构造函数**，那会让它被静默跳过。
//
//   两个必须知道的注册中心行为（决定了本类的实现方式）：
//
//   1. `INPCModule.Spawn` 是同步 void，而生成链路是 async UniTask。
//      因此这里走 fire-and-forget（`.Forget()`），与 DuckNpcDebugProbe 同一范式，
//      并用 _spawnInFlight 防止同一帧重复触发。
//
//   2. 竞技场里有「随机支援 NPC 三选一」抽签：注册中心会把所有
//      NpcId != courier_awen 的模块当作支援 NPC 候选，最后只随机 Spawn 一个。
//      本模块第一版**在竞技场一律返回 false**，避免和哥布林/护士抢名额 ——
//      捏脸 NPC 是"新增内容"，不该挤掉玩家已经熟悉的现有 NPC。
//
//   `Destroy` 必须幂等且可对「从没生成过」的状态调用：注册中心的
//   已婚保命分支实际上是死代码，每个模块的 Destroy 都会被无条件调用。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 捏脸 NPC 模块。按蓝图的 scenes 白名单在对应场景生成。
    /// </summary>
    internal sealed class DuckNpcModule : INPCModule
    {
        private const string LogPrefix = "[DuckNpcModule]";

        /// <summary>
        /// 模块 id。注册中心要求非空且不与现有 NPC 冲突。
        /// </summary>
        public string NpcId
        {
            get { return "duck_npc_blueprint"; }
        }

        /// <summary>
        /// 生成顺序。现有：快递员 10、哥布林 20、护士 30，这里取 40 排在最后。
        /// 该值只决定遍历顺序，不参与任何门控，但必须与现有值互不相同
        /// （List.Sort 不稳定）。
        /// </summary>
        public int SpawnOrder
        {
            get { return 40; }
        }

        /// <summary>
        /// 第一版不做好感度。返回 null 是安全的，快递员模块就是这么做的：
        /// 注册中心两条路径都显式跳过 null config。
        /// </summary>
        public INPCAffinityConfig CreateAffinityConfig()
        {
            return null;
        }

        // ====================================================================
        // 实例
        // ====================================================================

        private readonly List<CharacterMainControl> _spawned = new List<CharacterMainControl>();
        private bool _spawnInFlight;

        // ====================================================================
        // 场景门控
        // ====================================================================

        public bool ShouldSpawnInScene(ModBehaviour mod, string sceneName)
        {
            if (mod == null || string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            // 竞技场让位：见文件头第 2 条。
            if (IsArenaLikeScene(mod, sceneName))
            {
                return false;
            }

            IList<DuckNpcBlueprint> blueprints = DuckNpcRegistry.All;
            for (int i = 0; i < blueprints.Count; i++)
            {
                DuckNpcBlueprint blueprint = blueprints[i];
                if (blueprint != null && !blueprint.isPermanent && blueprint.AllowsScene(sceneName))
                {
                    return true;
                }
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
                // 判不出来就当成竞技场：宁可不生成，也不要挤掉现有支援 NPC 的名额。
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 竞技场判定失败，按不生成处理: " + e.Message);
                return true;
            }
            return false;
        }

        // ====================================================================
        // 生成 / 销毁
        // ====================================================================

        public void Spawn(ModBehaviour mod)
        {
            if (mod == null || _spawnInFlight)
            {
                return;
            }

            if (_spawned.Count > 0)
            {
                ModBehaviour.DevLog(LogPrefix + " 已有捏脸 NPC 存在，跳过生成");
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
                IList<DuckNpcBlueprint> blueprints = DuckNpcRegistry.All;
                for (int i = 0; i < blueprints.Count; i++)
                {
                    DuckNpcBlueprint blueprint = blueprints[i];
                    if (blueprint == null || blueprint.isPermanent || !blueprint.AllowsScene(sceneName))
                    {
                        continue;
                    }

                    Vector3 position;
                    if (!TryResolveSpawnPosition(sceneName, out position))
                    {
                        ModBehaviour.DevLog(LogPrefix + " 取不到刷新点，跳过: " + blueprint.id);
                        continue;
                    }

                    CharacterMainControl npc = await DuckNpcSpawner.SpawnAsync(
                        blueprint, position, Vector3.back);
                    if (npc == null)
                    {
                        continue;
                    }

                    // await 之后场景可能已经切走：此时这只 NPC 属于上一张图，立刻回收，
                    // 否则会在新场景里留下一只不属于任何模块管理的孤儿。
                    if (!IsStillSameScene(sceneName))
                    {
                        ModBehaviour.DevLog(LogPrefix + " 生成完成时场景已切换，回收: " + blueprint.id);
                        DuckNpcSpawner.Despawn(npc);
                        continue;
                    }

                    AttachMovement(npc, blueprint, position);
                    _spawned.Add(npc);
                    ModBehaviour.DevLog(LogPrefix + " 已生成 " + blueprint.id + " @ " + position);
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

        private static void AttachMovement(CharacterMainControl npc, DuckNpcBlueprint blueprint, Vector3 home)
        {
            if (!blueprint.canWander)
            {
                return;
            }

            try
            {
                DuckNpcMovement movement = npc.gameObject.AddComponent<DuckNpcMovement>();
                // Bind 失败（场景无 A* 图等）时组件会自我禁用，NPC 退回站桩，
                // 不是致命错误，不需要回滚整只 NPC。
                movement.Bind(npc, home, blueprint.wanderRadius);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 挂移动组件失败 " + blueprint.id + ": " + e.Message);
            }
        }

        /// <summary>
        /// 取刷新点。复用共享的通用 NPC 点池，不新增一套 NPCSpawnConfig 配置。
        /// </summary>
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

            // 贴地修正，与现有三个 NPC 同款做法。
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
            for (int i = 0; i < _spawned.Count; i++)
            {
                DuckNpcSpawner.Despawn(_spawned[i]);
            }
            _spawned.Clear();

            // 注意不要在这里把 _spawnInFlight 置 false：那条 async 还在飞，
            // 它自己的 finally 会置位。提前放开会让下一次 Spawn 与它并发。
        }
    }
}
