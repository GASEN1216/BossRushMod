using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        // 官方角色已经加入场景时，CreateCharacterAsync 仍可能停在 NextFrame / 装备装配。
        // 只延后本模块已决定执行的清场；完成后重新走原筛选，不能把半成品永久豁免。
        private bool DeferNativeFactoryCleanup(CharacterMainControl character, NativeArenaCleanupKind kind)
        {
            NativeArenaFactoryLease lease = character.GetComponent<NativeArenaFactoryLease>();
            if (lease == null || !lease.IsCreating) return false;
            ModBehaviour expectedOwner = owner;
            CharacterMainControl player = CharacterMainControl.Main;
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            int characterScene = character.gameObject.scene.handle;
            bool connected = BossRushCoop.IsConnected;
            string room = BossRushCoop.RunId, asset = BossRushCoop.LocalAssetId;
            lease.Defer(kind, delegate
            {
                if (owner == null || owner != expectedOwner || current != this || character == null
                    || CharacterMainControl.Main != player || BossRushCoop.LocalAssetId != asset
                    || UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != scene
                    || character.gameObject.scene.handle != characterScene || !BossRushCoop.HasWorldAuthority
                    || (connected ? !BossRushCoop.IsConnected || !BossRushCoop.IsHost || BossRushCoop.RunId != room
                        : BossRushCoop.IsConnected)) return;
                if (kind == NativeArenaCleanupKind.ForceKill) ForceKillAllEnemies();
                else if (kind == NativeArenaCleanupKind.ClearEnemies) ClearEnemiesForBossRush();
                else TryCleanNonBossRushDaXingXing();
            });
            return true;
        }

        /// <summary>
        /// 禁用场景中的所有spawner
        /// [性能优化] 只扫描 CharacterSpawnerRoot，因为它是所有 spawner 的根控制器
        /// [性能优化] 使用缓存的反射字段，避免重复获取
        /// [修复] 先通过反射设置 created=true 阻止生成，再销毁 spawner
        /// [Bug修复] 移除范围检查，禁用所有 spawner，因为 spawner root 位置可能与实际刷怪点位置不同
        /// [Bug修复] 销毁前保留灯光组件，避免场景变暗
        /// [性能优化] 把「阻止生成」与「批量销毁」拆开：标记 created=true 这一步同步完成、
        ///   立即阻止刷怪（过图帧必须保证），但灯光重挂 + Destroy 大量 GameObject 这一帧尖峰
        ///   挪到跨帧协程，避免场景加载帧卡顿。
        /// </summary>
        internal void DisableAllSpawners()
        {
            if (!BossRushCoop.HasWorldAuthority) return;
            if (SpawnersDisabled)
            {
                return;
            }

            try
            {
                int disabledCount = 0;

                if (!_createdFieldCached)
                {
                    _cachedCreatedField = typeof(CharacterSpawnerRoot).GetField("created",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    _createdFieldCached = true;
                }

                CharacterSpawnerRoot[] spawnerRoots = ObjectCache.GetCharacterSpawnerRoots();

                // 收集待销毁的 root，销毁动作延后到协程分帧执行。
                List<CharacterSpawnerRoot> rootsToDestroy = new List<CharacterSpawnerRoot>();

                if (spawnerRoots != null)
                {
                    foreach (var root in spawnerRoots)
                    {
                        if (root == null || root.gameObject == null) continue;

                        // [关键] 立即标记 created=true，同步阻止刷怪——这一步极轻量，必须在本帧完成。
                        if (_cachedCreatedField != null)
                        {
                            try
                            {
                                _cachedCreatedField.SetValue(root, true);
                                disabledCount++;
                            }
                            catch (Exception e)
                            {
                                ModBehaviour.DevLog("[BossRush] [WARNING] DisableAllSpawners 标记 spawner created 失败: " + e.Message);
                            }
                        }

                        rootsToDestroy.Add(root);
                    }
                }

                // 立即置位：刷怪已被阻止，重复调用会直接返回；销毁是收尾清理，可异步。
                SpawnersDisabled = true;
                ModBehaviour.DevLog("[BossRush] 已标记禁用 " + disabledCount + " 个 CharacterSpawnerRoot，销毁将分帧进行");

                if (rootsToDestroy.Count > 0)
                {
                    owner.StartCoroutine(DestroySpawnerRootsAcrossFrames(rootsToDestroy));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 销毁spawner时出错: " + e.Message);
            }
        }

        /// <summary>
        /// 分帧销毁已禁用的 spawner root：保留其灯光后销毁，每帧处理少量，
        /// 避免在过图帧一次性 Destroy 大量 GameObject 造成尖峰。
        /// </summary>
        private IEnumerator DestroySpawnerRootsAcrossFrames(List<CharacterSpawnerRoot> rootsToDestroy)
        {
            const int batchSize = 4;       // 每帧处理的 root 数量
            int destroyedCount = 0;
            int preservedLightsCount = 0;
            int processedInBatch = 0;

            for (int i = 0; i < rootsToDestroy.Count; i++)
            {
                CharacterSpawnerRoot root = rootsToDestroy[i];
                if (root == null || root.gameObject == null)
                {
                    continue;
                }

                try
                {
                    Light[] lights = root.gameObject.GetComponentsInChildren<Light>(true);
                    if (lights != null && lights.Length > 0)
                    {
                        foreach (var light in lights)
                        {
                            if (light == null || light.gameObject == null) continue;
                            light.transform.SetParent(null);
                            preservedLightsCount++;
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] DisableAllSpawners 保留灯光失败: " + e.Message);
                }

                try
                {
                    UnityEngine.Object.Destroy(root.gameObject);
                    destroyedCount++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] DisableAllSpawners 销毁 spawner 失败: " + e.Message);
                }

                processedInBatch++;
                if (processedInBatch >= batchSize)
                {
                    processedInBatch = 0;
                    yield return null;  // 下一帧继续
                }
            }

            ModBehaviour.DevLog("[BossRush] 分帧销毁完成：销毁 " + destroyedCount + " 个 CharacterSpawnerRoot，保留了 " + preservedLightsCount + " 个灯光");
        }

        /// <summary>
        /// 定期自检：如果当前波计数大于0但场上已没有任何由BossRush生成的存活Boss，则强制修正并推进波次
        /// </summary>
        internal void TryFixStuckWaveIfNoBossAlive()
        {
            if (!BossRushCoop.HasWorldAuthority) return;
            try
            {
                if (!owner.IsActive)
                {
                    return;
                }

                int aliveBossCount = 0;
                bool hasWaveToCheck = false;

                if (BossesPerWave > 1)
                {
                    if (BossesInCurrentWaveRemaining <= 0)
                    {
                        return;
                    }

                    if (CurrentWaveBosses != null && CurrentWaveBosses.Count > 0)
                    {
                        hasWaveToCheck = true;

                        for (int i = 0; i < CurrentWaveBosses.Count; i++)
                        {
                            MonoBehaviour boss = CurrentWaveBosses[i];
                            if (boss == null)
                            {
                                continue;
                            }

                            try
                            {
                                Health h = boss.GetComponent<Health>();
                                if (h != null && !h.IsDead)
                                {
                                    aliveBossCount++;
                                }
                            }
                            catch (Exception e)
                            {
                                ModBehaviour.DevLog("[BossRush] [WARNING] TryFixStuckWaveIfNoBossAlive 读取多Boss Health失败: " + e.Message);
                            }
                        }
                    }
                }
                else
                {
                    MonoBehaviour bossMb = null;
                    try
                    {
                        bossMb = owner.CurrentBossForWavesArena as MonoBehaviour;
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[BossRush] [WARNING] TryFixStuckWaveIfNoBossAlive 读取当前Boss失败: " + e.Message);
                    }

                    if (bossMb == null)
                    {
                        return;
                    }

                    hasWaveToCheck = true;

                    try
                    {
                        Health h = bossMb.GetComponent<Health>();
                        if (h != null && !h.IsDead)
                        {
                            aliveBossCount = 1;
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[BossRush] [WARNING] TryFixStuckWaveIfNoBossAlive 读取当前Boss Health失败: " + e.Message);
                    }
                }

                if (!hasWaveToCheck)
                {
                    return;
                }

                if (aliveBossCount <= 0)
                {
                    try
                    {
                        ModBehaviour.DevLog("[BossRush] 自检：当前波没有任何存活 Boss，自动修正并推进下一波");
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogWarning("[BossRush] TryFixStuckWaveIfNoBossAlive 日志记录失败: " + e.Message);
                    }

                    if (BossesPerWave > 1)
                    {
                        BossesInCurrentWaveRemaining = 0;
                    }

                    owner.ProceedAfterWaveFinished();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [ERROR] TryFixStuckWaveIfNoBossAlive 错误: " + e.Message);
            }
        }
    }

    internal enum NativeArenaCleanupKind { ForceKill, ClearEnemies, ForeignDaXingXing }

    /// <summary>随真实角色销毁的生成租约；没有轮询、全局角色表或任务包装。</summary>
    internal sealed class NativeArenaFactoryLease : MonoBehaviour
    {
        private Dictionary<NativeArenaCleanupKind, Action> deferred;
        internal bool IsCreating { get; private set; } = true;

        internal void Defer(NativeArenaCleanupKind kind, Action retry)
        {
            if (!IsCreating) return;
            if (deferred == null) deferred = new Dictionary<NativeArenaCleanupKind, Action>();
            if (deferred.ContainsKey(kind)) return;
            deferred.Add(kind, retry);
        }

        internal void Complete()
        {
            if (!IsCreating) return;
            IsCreating = false;
            if (deferred == null) return;
            // ForceKill 先于直接销毁，保留原死亡事件；回调可能再次清场，先释放全部收据。
            var work = new Action[3];
            foreach (var entry in deferred) work[(int)entry.Key] = entry.Value;
            deferred.Clear(); deferred = null;
            foreach (Action retry in work)
            {
                if (retry == null) continue;
                try { retry(); }
                catch (Exception e) { ModBehaviour.DevLog("[BossRush] 原生生成结束后清场失败: " + e.Message); }
            }
        }

        private void OnDestroy() { if (deferred != null) deferred.Clear(); deferred = null; IsCreating = false; }
    }

    /// <summary>只按值读取真实 MoveNext 前后状态；不改状态机、awaiter、返回任务或异常。</summary>
    [HarmonyPatch]
    internal static class NativeArenaFactoryLifecyclePatch
    {
        private static readonly Type machine = FindMachine();
        private static readonly FieldInfo characterField = machine != null ? FindCharacterField(machine) : null;
        private static readonly FieldInfo stateField = machine != null ? machine.GetField("<>1__state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;

        private static Type FindMachine()
        {
            foreach (Type type in typeof(CharacterRandomPreset).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                if (type.Name.StartsWith("<CreateCharacterAsync>d__", StringComparison.Ordinal)) return type;
            return null;
        }

        private static FieldInfo FindCharacterField(Type type)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (field.FieldType == typeof(CharacterMainControl) && field.Name.StartsWith("<character>", StringComparison.Ordinal)) return field;
            return null;
        }

        private static MethodBase TargetMethod()
        {
            if (machine == null || characterField == null || stateField == null || stateField.FieldType != typeof(int))
                throw new MissingMemberException("CharacterRandomPreset.CreateCharacterAsync 生成生命周期字段不匹配");
            return AccessTools.Method(machine, "MoveNext");
        }

        private static void Prefix(object __instance, out CharacterMainControl __state)
        {
            __state = null;
            try { __state = characterField.GetValue(__instance) as CharacterMainControl; }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush] 原生生成生命周期读取失败: " + e.Message); }
        }

        private static void Postfix(object __instance, CharacterMainControl __state)
        {
            try
            {
                bool terminal = (int)stateField.GetValue(__instance) == -2;
                CharacterMainControl character = characterField.GetValue(__instance) as CharacterMainControl;
                if (character == null) character = __state;
                if (character == null) return;
                NativeArenaFactoryLease lease = character.GetComponent<NativeArenaFactoryLease>();
                if (terminal) { if (lease != null) lease.Complete(); }
                else if (lease == null) character.gameObject.AddComponent<NativeArenaFactoryLease>();
            }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush] 原生生成生命周期收尾失败: " + e.Message); }
        }
    }
}
