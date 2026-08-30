// ============================================================================
// RandomEventEffectsBridge.cs — 随机事件「鸭生无常」的 ModBehaviour 桥（查询 / 播报 / 天气 / 特效 / 声音）
// ============================================================================
// 模块职责：
//   随机事件的事件实现类（RandomEventCatalog*.cs）是普通 internal 类，拿不到
//   ModBehaviour 的 private 基建（GetDirectionFromPlayer / infiniteHellMode /
//   _cachedCharacters / SpawnEnemyCoreInternalAsync / GetLootBoxTemplateWithLoader ...）。
//   本文件是 `public partial class ModBehaviour`，把这些能力收口成一组 internal 方法，
//   事件层只经这些方法触碰宿主，绝不自己反射、绝不自己新增 Harmony patch。
//
// 硬约束（AGENTS 4.5 / 4.6 / 4.7 / 4.12 / 4.14）：
//   1. 零新增 Harmony patch、零新增反射绑定策略；只复用既有缓存与既有静态 API。
//   2. 全部方法 no-throw：任何异常只 DevLog，不得拖崩宿主。
//   3. 不触碰任何波次状态机符号（隔离面见 tests/RandomEventsWaveIsolationGuard.py）。
//   4. 每帧热路径不加日志；CollectEventBuffTargets 由调用方按 2 秒节流，禁止逐帧调用。
//   5. 天气强制是「先捕获原值 → 结束还原」的成对操作，调用方必须注册还原动作。
//
// 已核实的官方 API 坑（勿删注释）：
//   - ExplosionManager.CreateExplosion 首两行裸解引用 CharacterMainControl.Main.transform
//     与 LevelManager.Instance.MainCharacter.transform，任一为 null 必 NRE → 调用前必须判空。
//   - DamageInfo 是 struct，elementFactors 只在带参构造里 new，default(DamageInfo) 会 NRE
//     → 一律 new DamageInfo(character)。
//   - AudioManager.Post 返回 FMOD 类型，编译清单没有 FMOD 引用 → 只能用 PlayStringer(string)
//     与 ModBehaviour.PlaySoundEffect(绝对路径)。
//   - AISound.fromTeam 必须与目标敌人不同队，否则 AICharacterController.OnSound 直接 return；
//     用 Teams.player 可覆盖全部敌方阵营，同时不会给玩家播假警报。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // ====================================================================
        // 局状态查询
        // ====================================================================

        /// <summary>
        /// 当前是否处于无间炼狱局。infiniteHellMode 是 private 字段且没有任何公开门面，
        /// 只能在本 partial 桥里读，事件层只经本方法消费（仅用于空投品质上限分档）。
        /// </summary>
        internal bool IsRandomEventInfiniteHellRun()
        {
            try
            {
                return infiniteHellMode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>当前场景刷新点的 no-throw 只读包装。无刷新点时返回 null。</summary>
        internal Vector3[] GetRandomEventSpawnPointsSafe()
        {
            try
            {
                return GetCurrentSceneSpawnPoints();
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 读取场景刷新点失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 取一个距离玩家至少 minDistance 的落地点。
        /// 预设刷新点优先 → 玩家周围环形 NavMesh 兜底 → SnapToGround 硬兜底（总是返回可用值）。
        /// </summary>
        internal Vector3 GetRandomEventSafePointAwayFromPlayer(float minDistance)
        {
            Vector3 basePos = Vector3.zero;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    basePos = player.transform.position;
                }
            }
            catch (Exception) { }

            try
            {
                Vector3[] points = GetRandomEventSpawnPointsSafe();
                if (points != null && points.Length > 0)
                {
                    return SpawnPositionHelper.FindNearestSafeSpawnPoint(points, basePos, minDistance);
                }

                Vector3 resolved;
                if (SpawnPositionHelper.TryFindAroundPlayer(
                        basePos,
                        8,
                        Mathf.Max(1f, minDistance),
                        out resolved,
                        SpawnPositionHelper.DefaultLiftOffset,
                        minDistance))
                {
                    return resolved;
                }

                return SpawnPositionHelper.SnapToGround(basePos + Vector3.forward * Mathf.Max(1f, minDistance));
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 解析事件落点失败: " + e.Message);
                return basePos;
            }
        }

        /// <summary>
        /// 收集可被随机事件临时增益的目标（当前场内、存活、与玩家敌对的角色）。
        /// 复用 ModBehaviour 既有的静态角色缓存，绝不新起一套扫描器。
        ///
        /// ⚠️ 4.12 红线：本方法内含一次 FindObjectsOfType（RefreshCharacterCache），
        /// 调用方必须节流（血月按 2 秒一次），**禁止逐帧调用**。
        /// buffer 会被就地清空后填充，调用方复用同一个 List 以免每次分配。
        /// </summary>
        internal void CollectEventBuffTargets(List<CharacterMainControl> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();

            try
            {
                // 事件期间怪是动态刷出来的，必须真刷新才能补挂到新怪身上。
                RefreshCharacterCache();

                CharacterMainControl main = null;
                try { main = CharacterMainControl.Main; } catch (Exception) { }

                for (int i = 0; i < _cachedCharacters.Count; i++)
                {
                    CharacterMainControl c = _cachedCharacters[i];
                    if (c == null || c == main)
                    {
                        continue;
                    }

                    try
                    {
                        if (c.Health == null || c.Health.IsDead)
                        {
                            continue;
                        }
                        if (!Team.IsEnemy(Teams.player, c.Team))
                        {
                            continue;
                        }
                        // 遗种巢随从可能被临时改判为敌对，明确豁免，别给玩家的崽加 buff。
                        if (PetNestCompanionAgent.IsCompanionCharacter(c))
                        {
                            continue;
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    buffer.Add(c);
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 收集事件增益目标失败: " + e.Message);
            }
        }

        // ====================================================================
        // 播报
        // ====================================================================

        /// <summary>事件横幅播报。走官方通知系统，无自建 UI。</summary>
        internal void ShowRandomEventBanner(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                ShowBigBanner(text);
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 事件横幅播报失败: " + e.Message);
            }
        }

        /// <summary>带方位的事件横幅（如「空投补给 · 正北」）。玩家不存在时退化为纯文本。</summary>
        internal void ShowRandomEventDirectionalBanner(string eventName, Vector3 worldPos)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return;
            }

            string text = eventName;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    string direction = L10n.Direction(GetDirectionFromPlayer(worldPos, player.transform.position));
                    if (!string.IsNullOrEmpty(direction))
                    {
                        text = eventName + " · " + direction;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 解析事件方位失败: " + e.Message);
            }

            ShowRandomEventBanner(text);
        }

        // ====================================================================
        // 天气（E2 血月支线）
        // ====================================================================

        /// <summary>
        /// 强制天气，并回吐原值供还原。
        /// WeatherManager.Instance 为 null（竞技场可能没有天气系统）时静默返回 false，
        /// 调用方按「天气失败不算触发失败」处理。
        /// 注：ForceWeather / ForceWeatherValue 不进存档（SaveData 只含 valid + seed）。
        /// </summary>
        internal bool TryApplyRandomEventForcedWeather(
            Duckov.Weathers.Weather weather,
            out bool prevForce,
            out Duckov.Weathers.Weather prevValue)
        {
            prevForce = false;
            prevValue = Duckov.Weathers.Weather.Sunny;

            try
            {
                Duckov.Weathers.WeatherManager inst = Duckov.Weathers.WeatherManager.Instance;
                if (inst == null)
                {
                    return false;
                }

                prevForce = inst.ForceWeather;
                prevValue = inst.ForceWeatherValue;
                Duckov.Weathers.WeatherManager.SetForceWeather(true, weather);
                return true;
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 强制天气失败，已跳过天气支线: " + e.Message);
                prevForce = false;
                prevValue = Duckov.Weathers.Weather.Sunny;
                return false;
            }
        }

        /// <summary>还原被事件改过的强制天气。幂等，Instance 为 null 时静默跳过。</summary>
        internal void RestoreRandomEventForcedWeather(bool prevForce, Duckov.Weathers.Weather prevValue)
        {
            try
            {
                if (Duckov.Weathers.WeatherManager.Instance == null)
                {
                    return;
                }
                Duckov.Weathers.WeatherManager.SetForceWeather(prevForce, prevValue);
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 还原强制天气失败: " + e.Message);
            }
        }

        // ====================================================================
        // 特效 / 声音
        // ====================================================================

        /// <summary>
        /// 对 AI 广播一次假声源（E1 落地引怪 / E5 声东击西）。
        /// fromTeam 固定 Teams.player：既能被全部敌方阵营听见，又因
        /// Team.IsEnemy(player, player) == false 而不会给玩家播假警报。
        /// fromCharacter / fromObject 留空，AI 只会去 pos 探查而不会直扑玩家。
        /// </summary>
        internal void MakeRandomEventAiSound(Vector3 pos, float radius, SoundTypes soundType)
        {
            try
            {
                AISound sound = default(AISound);
                sound.pos = pos;
                sound.radius = radius;
                sound.fromTeam = Teams.player;
                sound.soundType = soundType;
                sound.fromCharacter = null;
                sound.fromObject = null;
                AIMainBrain.MakeSound(sound);
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 制造 AI 声源失败: " + e.Message);
            }
        }

        /// <summary>世界空间飘字。PopText.instance 为 null 时静默跳过。</summary>
        internal void PopRandomEventText(string text, Vector3 worldPos, Color color, float size)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                if (FX.PopText.instance == null)
                {
                    return;
                }
                FX.PopText.Pop(text, worldPos, color, size);
            }
            catch (Exception)
            {
                // 纯装饰，失败无需刷屏
            }
        }

        /// <summary>
        /// 零伤害爆炸（E1 落地尘土 / E6 烟花）。
        ///
        /// ⚠️ 待实机验证：零伤爆炸是否附带击退 / 是否仍会对半径内的 DamageReceiver 派发
        /// damageValue=0 的 Hurt（进而触发 Health.OnHurt、让 AI noticed=true、污染图鉴计时）。
        /// 当前对策是把半径压到 RandomEventsTuning.FireworksExplosionRadius(0.05f)，
        /// 让 Physics.OverlapSphereNonAlloc 命中 0 个 receiver。
        /// 若实机发现仍有击退或掉血，改为纯粒子实例化（ExplosionManager.normalFxPfb），
        /// 不再调用 CreateExplosion。
        /// </summary>
        internal void CreateRandomEventHarmlessExplosion(Vector3 center, ExplosionFxTypes fx, float shake)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    // CreateExplosion 首两行裸读 Main.transform，null 必 NRE。
                    return;
                }

                LevelManager level = LevelManager.Instance;
                if (level == null || level.ExplosionManager == null || level.MainCharacter == null)
                {
                    return;
                }

                DamageInfo dmg = new DamageInfo(main);
                dmg.damageValue = 0f;
                dmg.isExplosion = true;

                level.ExplosionManager.CreateExplosion(
                    center,
                    RandomEventsTuning.FireworksExplosionRadius,
                    dmg,
                    fx,
                    shake,
                    false);
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 零伤爆炸失败: " + e.Message);
            }
        }

        /// <summary>播放 Mod 自带音效。relativePath 相对 Assets/Sounds/，缺文件静默跳过。</summary>
        internal void PlayRandomEventModSound(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return;
            }

            try
            {
                string modPath = GetModPath();
                if (string.IsNullOrEmpty(modPath))
                {
                    return;
                }

                string full = Path.Combine(
                    Path.Combine(Path.Combine(modPath, "Assets"), "Sounds"),
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    return;
                }

                PlaySoundEffect(full);
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 播放事件音效失败: " + e.Message);
            }
        }

        /// <summary>
        /// 播放官方 stinger。key 不带路径前缀（官方内部拼 "Music/Stinger/{key}"）。
        /// 禁止直接调 AudioManager.Post：它返回 FMOD 类型，编译清单没有 FMOD 引用。
        /// </summary>
        internal void PlayRandomEventStinger(string stingerKey)
        {
            if (string.IsNullOrEmpty(stingerKey))
            {
                return;
            }

            try
            {
                Duckov.AudioManager.PlayStringer(stingerKey);
            }
            catch (Exception)
            {
                // 音频非关键路径，无 FMOD / 无 AudioManager 实例时静默降级
            }
        }
    
        /// <summary>
        /// 局内随机事件的宿主销毁清理。调度器与 HUD 都是纯运行时态，无落盘，
        /// 但残留的强制天气、敌人增益与生成物必须在这里一并归零。
        /// </summary>
        internal void CleanupRandomEventsRuntimeOnDestroy()
        {
            SafeRuntime.Run("RandomEventDirector.ResetStaticCaches", () => RandomEventDirector.ResetStaticCaches());
            SafeRuntime.Run("RandomEventCatalog.ResetStaticCaches", () => RandomEventCatalog.ResetStaticCaches());
            SafeRuntime.Run("RandomEventHud.ResetStaticCaches", () => RandomEventHud.ResetStaticCaches());
        }
}
}
