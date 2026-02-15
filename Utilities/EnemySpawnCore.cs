// ============================================================================
// EnemySpawnCore.cs - 通用敌人生成核心方法
// ============================================================================
// 模块说明：
//   提取 Mode D 和 Mode E 共用的敌人生成逻辑，消除重复代码。
//   包含预设查找、重试机制、龙裔遗族/龙王特殊处理、大兴兴标记、
//   属性标准化、装备配置、AI设置等通用流程。
//
//   各模式通过回调参数注入差异化逻辑（阵营设置、死亡注册、列表管理等）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// 敌人生成后的配置回调参数
    /// </summary>
    public class EnemySpawnContext
    {
        /// <summary>生成的敌人角色</summary>
        public CharacterMainControl character;

        /// <summary>使用的预设信息</summary>
        public EnemyPresetInfo preset;

        /// <summary>是否为Boss</summary>
        public bool isBoss;

        /// <summary>生成位置</summary>
        public Vector3 position;
    }

    /// <summary>
    /// 通用敌人生成核心方法（Mode D / Mode E 共用）
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 通用敌人生成核心方法
        /// <para>处理预设查找、重试、龙裔遗族/龙王特殊生成、大兴兴标记、属性标准化等通用流程。</para>
        /// <para>生成成功后调用 onSpawned 回调，由调用方注入差异化逻辑（阵营设置、死亡注册等）。</para>
        /// <para>所有重试均失败时调用 onFailed 回调（可选），用于 Mode D 波次计数等兜底逻辑。</para>
        /// </summary>
        /// <param name="preset">初始预设信息</param>
        /// <param name="position">生成位置</param>
        /// <param name="isBoss">是否为Boss</param>
        /// <param name="isActiveCheck">检查模式是否仍然激活的委托（用于中途退出）</param>
        /// <param name="onSpawned">生成成功后的回调（设置阵营、注册死亡、加入列表等）</param>
        /// <param name="onFailed">所有重试均失败后的回调（可选，用于波次计数兜底等）</param>
        /// <param name="waveIndex">波次索引（Mode D 用于配装品质计算，Mode E 传 1）</param>
        /// <param name="skipDragonDescendant">Mode E 用：重试时跳过龙裔遗族预设</param>
        /// <param name="skipDragonKing">Mode E 用：重试时跳过龙王预设</param>
        private async void SpawnEnemyCore(
            EnemyPresetInfo preset,
            Vector3 position,
            bool isBoss,
            Func<bool> isActiveCheck,
            Action<EnemySpawnContext> onSpawned,
            Action onFailed = null,
            int waveIndex = 1,
            bool skipDragonDescendant = false,
            bool skipDragonKing = false)
        {
            try
            {
                const int maxAttempts = 5;
                EnemyPresetInfo currentPreset = preset;

                // 记录调用方传入的原始预设，用于区分"首次使用"和"重试随机"
                EnemyPresetInfo originalPreset = preset;

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        // 预设为空时重新随机
                        if (currentPreset == null)
                        {
                            currentPreset = isBoss ? GetRandomBossPreset() : GetRandomMinionPreset();
                        }
                        if (currentPreset == null)
                        {
                            DevLog("[SpawnCore] 预设为空, attempt=" + attempt);
                            continue;
                        }

                        // Mode E 龙限制：仅在"重试随机"时检查（currentPreset != originalPreset）
                        // 首次循环使用的是调用方已确认的预设，不应被跳过
                        // 重试时需要同时检查 skip 参数和实例字段（防止并发竞态）
                        bool isRetry = (currentPreset != originalPreset);
                        if (isRetry && (skipDragonDescendant || modeEDragonDescendantSpawned) && IsDragonDescendantPreset(currentPreset))
                        {
                            DevLog("[SpawnCore] 重试跳过龙裔遗族（已达上限）");
                            currentPreset = null;
                            continue;
                        }
                        if (isRetry && (skipDragonKing || modeEDragonKingSpawned) && IsDragonKingPreset(currentPreset))
                        {
                            DevLog("[SpawnCore] 重试跳过龙王（已达上限）");
                            currentPreset = null;
                            continue;
                        }

                        DevLog("[SpawnCore] 生成敌人: " + currentPreset.displayName + " (isBoss=" + isBoss + ", attempt=" + attempt + ")");

                        int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                        CharacterMainControl character = null;

                        // 龙裔遗族Boss：使用专用生成方法
                        if (IsDragonDescendantPreset(currentPreset))
                        {
                            try
                            {
                                character = await SpawnDragonDescendant(position);
                            }
                            catch (Exception dragonEx)
                            {
                                DevLog("[SpawnCore] 龙裔遗族生成异常: " + dragonEx.Message);
                                currentPreset = null;
                                continue;
                            }
                        }
                        // 龙王Boss：使用专用生成方法
                        else if (IsDragonKingPreset(currentPreset))
                        {
                            try
                            {
                                character = await SpawnDragonKing(position);
                            }
                            catch (Exception kingEx)
                            {
                                DevLog("[SpawnCore] 龙王生成异常: " + kingEx.Message);
                                currentPreset = null;
                                continue;
                            }
                        }
                        // 普通预设：通过 CharacterRandomPreset 生成
                        else
                        {
                            CharacterRandomPreset targetPreset = null;
                            if (cachedCharacterPresets != null)
                            {
                                cachedCharacterPresets.TryGetValue(currentPreset.name, out targetPreset);
                            }

                            if (targetPreset == null)
                            {
                                DevLog("[SpawnCore] 未找到预设: " + currentPreset.name);
                                currentPreset = null;
                                continue;
                            }

                            try
                            {
                                character = await targetPreset.CreateCharacterAsync(position, Vector3.forward, relatedScene, null, false);
                            }
                            catch (Exception createEx)
                            {
                                DevLog("[SpawnCore] 生成敌人异常: " + createEx.Message);
                                currentPreset = null;
                                continue;
                            }
                        }

                        if (character == null)
                        {
                            DevLog("[SpawnCore] 生成敌人失败: " + (currentPreset != null ? currentPreset.displayName : "null"));
                            currentPreset = null;
                            continue;
                        }

                        // 判断是否为龙裔/龙王专用生成路径
                        // 龙裔/龙王在 SpawnDragonDescendant/SpawnDragonKing 内部已完成：
                        //   - 属性设置、装备配置、能力控制器初始化
                        //   - ApplyBossStatMultiplier（全局倍率）
                        //   - SetActive(true)（角色激活）
                        // 因此这里需要跳过重复操作，并在 Yield 之前立即调用 onSpawned
                        // 以确保 Mode E 的 SetTeam 在角色激活后尽快执行
                        bool isDragonSpecialSpawn = IsDragonDescendantPreset(currentPreset) || IsDragonKingPreset(currentPreset);

                        if (isDragonSpecialSpawn)
                        {
                            // 龙裔/龙王：立即调用 onSpawned（设置阵营），不等 Yield
                            // 这样角色激活后的第一帧就已经有正确的阵营
                            var ctx = new EnemySpawnContext
                            {
                                character = character,
                                preset = currentPreset,
                                isBoss = isBoss,
                                position = position
                            };
                            onSpawned(ctx);

                            // 标记大兴兴（防止被误清理）
                            try
                            {
                                if (IsDaXingXingPreset(currentPreset))
                                {
                                    if (bossRushOwnedDaXingXing != null && !bossRushOwnedDaXingXing.Contains(character))
                                    {
                                        bossRushOwnedDaXingXing.Add(character);
                                    }
                                }
                            }
                            catch { }

                            // 统一伤害倍率（龙裔/龙王也需要）
                            NormalizeDamageMultiplier(character);

                            // 跳过 EquipEnemyForModeD（龙裔/龙王已在内部完成配装）
                            // 跳过 ApplyBossStatMultiplier（龙裔/龙王已在内部调用过）
                            // 跳过 SetActive（龙裔/龙王已在内部激活）
                        }
                        else
                        {
                            // 普通敌人：保持原有流程

                            // [性能优化] 角色创建完成后让出一帧，把配装操作分散到下一帧
                            await UniTask.Yield();

                            // 模式已结束，销毁并退出
                            if (!isActiveCheck())
                            {
                                UnityEngine.Object.Destroy(character.gameObject);
                                DevLog("[SpawnCore] 模式已结束，销毁生成的敌人");
                                return;
                            }

                            // 标记大兴兴（防止被误清理）
                            try
                            {
                                if (IsDaXingXingPreset(currentPreset))
                                {
                                    if (bossRushOwnedDaXingXing != null && !bossRushOwnedDaXingXing.Contains(character))
                                    {
                                        bossRushOwnedDaXingXing.Add(character);
                                    }
                                }
                            }
                            catch { }

                            // 统一伤害倍率
                            NormalizeDamageMultiplier(character);

                            // 应用配装（Boss 保留原有头盔和护甲）
                            EquipEnemyForModeD(character, waveIndex, currentPreset.baseHealth, isBoss);

                            // 应用全局 Boss 数值倍率
                            ApplyBossStatMultiplier(character);

                            // 激活敌人
                            character.gameObject.SetActive(true);

                            // 调用方注入差异化逻辑（阵营设置、命名、AI配置、死亡注册、列表管理等）
                            var ctx = new EnemySpawnContext
                            {
                                character = character,
                                preset = currentPreset,
                                isBoss = isBoss,
                                position = position
                            };
                            onSpawned(ctx);
                        }

                        DevLog("[SpawnCore] 敌人生成成功: " + currentPreset.displayName);
                        return;
                    }
                    catch (Exception e)
                    {
                        DevLog("[SpawnCore] [ERROR] 尝试失败: " + e.Message);
                        currentPreset = null;
                    }
                }

                DevLog("[SpawnCore] [ERROR] 多次尝试仍然失败");

                // 所有重试均失败，调用失败回调（用于 Mode D 波次计数兜底等）
                try { onFailed?.Invoke(); } catch { }
            }
            catch (Exception e)
            {
                DevLog("[SpawnCore] [ERROR] SpawnEnemyCore 异常: " + e.Message);

                // 异常情况也调用失败回调，确保调用方计数不会卡住
                try { onFailed?.Invoke(); } catch { }
            }
        }
    }
}
