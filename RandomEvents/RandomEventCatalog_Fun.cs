// ============================================================================
// RandomEventCatalog_Fun.cs — 随机事件「鸭生无常」E5~E8 实现（趣味向）
// ============================================================================
// 模块职责：
//   E5 声东击西 / E6 鸭王的烟花 / E7 金鸭雨 / E8 鸭群巡游。
//   注册表与 E1~E4 见 RandomEventCatalog.cs，本文件是按职责拆出的追加文件
//   （同一 namespace，不共用类型，仅为控制单文件行数预算）。
//
// 本组事件的共同特征：
//   - 生成物极少（E5/E6 零实体，E7 只有掉在地上的现金，E8 只有官方下蛋鸭），
//     因此清理路径短，但**仍然必须**覆盖到时 / 局末 / 切图 / 关开关 / 宿主销毁五条路径。
//   - 协程一律登记进 ctx.Scope，由 RuntimeScope.Clear 统一 StopCoroutine。
//   - 所有对官方 API 的调用都在桥里做过判空，事件层不重复裸调。
//
// ⚠️ E6 待实机验证（见 RandomEventEffectsBridge.cs 的同名说明）：
//    零伤害爆炸是否带击退 / 是否仍会对半径内 DamageReceiver 派发 damageValue=0 的 Hurt。
//    当前用极小半径（RandomEventsTuning.FireworksExplosionRadius）规避；
//    若实机确认仍有击退或吸怪，改为纯粒子实例化，不再调用 CreateExplosion。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    // ========================================================================
    // E5 声东击西
    // ========================================================================

    /// <summary>
    /// 声东击西：在玩家周围环形依次制造假声源，把 AI 引向空处。
    /// 零生成物，清理只需停协程。
    /// AISound.fromTeam 固定 Teams.player（桥内写死），既能被全部敌方听见，
    /// 又不会给玩家自己播假警报。
    /// </summary>
    internal sealed class RandomEventFeint : RandomEventBase
    {
        internal override RandomEventId Id { get { return RandomEventId.Feint; } }

        internal override string DisplayName
        {
            get { return L10n.T("声东击西", "Feint"); }
        }

        internal override float DurationSeconds { get { return RandomEventsTuning.FeintDurationSeconds; } }

        internal override float Weight { get { return RandomEventsTuning.WeightFeint; } }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                CharacterMainControl player = RandomEventEffectHelpers.ResolveAlivePlayer();
                if (owner == null || player == null || ctx == null || ctx.Scope == null)
                {
                    return false;
                }

                owner.ShowRandomEventBanner(L10n.T("远处传来奇怪的动静……", "Strange noises from afar..."));

                Coroutine co = owner.StartCoroutine(FeintRoutine(owner));
                ctx.Scope.RegisterCoroutine(co);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 声东击西触发失败: " + e.Message);
                return false;
            }
        }

        private IEnumerator FeintRoutine(ModBehaviour owner)
        {
            int count = Mathf.Max(1, RandomEventsTuning.FeintSoundCount);
            for (int i = 0; i < count; i++)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || owner == null)
                {
                    yield break;
                }

                // 落点解析放在独立方法里：迭代器块内不允许 try/catch 包住 yield
                Vector3 p;
                if (!TryResolveFeintPoint(player, i, count, out p))
                {
                    yield break;
                }

                owner.MakeRandomEventAiSound(p, RandomEventsTuning.FeintSoundRadius, SoundTypes.unknowNoise);
                owner.PopRandomEventText(
                    L10n.T("咕嘎?", "Quack?"),
                    p + Vector3.up * 1.2f,
                    BossRushUIColors.Accent,
                    1f);

                yield return new WaitForSeconds(RandomEventsTuning.FeintSoundIntervalSeconds);
            }
        }

        private static bool TryResolveFeintPoint(CharacterMainControl player, int index, int count, out Vector3 point)
        {
            point = Vector3.zero;
            try
            {
                float angle = 360f * index / count + UnityEngine.Random.Range(-20f, 20f);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                point = SpawnPositionHelper.SnapToGround(
                    player.transform.position + dir * RandomEventsTuning.FeintSoundRingRadius);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            RandomEventEffectHelpers.ClearScope(ctx, reason);
        }
    }

    // ========================================================================
    // E6 鸭王的烟花
    // ========================================================================

    /// <summary>
    /// 鸭王的烟花：玩家周围连放零伤害烟花，纯演出。
    /// 每发之前都重新判空主角：官方 CreateExplosion 会裸解引用 CharacterMainControl.Main，
    /// 玩家在演出途中死亡 / 切图会直接 NRE（桥内也有判空，这里是第二道闸）。
    /// </summary>
    internal sealed class RandomEventFireworks : RandomEventBase
    {
        internal override RandomEventId Id { get { return RandomEventId.Fireworks; } }

        internal override string DisplayName
        {
            get { return L10n.T("鸭王的烟花", "The Duck King's Fireworks"); }
        }

        internal override float DurationSeconds { get { return RandomEventsTuning.FireworksDurationSeconds; } }

        internal override float Weight { get { return RandomEventsTuning.WeightFireworks; } }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                CharacterMainControl player = RandomEventEffectHelpers.ResolveAlivePlayer();
                if (owner == null || player == null || ctx == null || ctx.Scope == null)
                {
                    return false;
                }

                owner.ShowRandomEventBanner(L10n.T("鸭王点燃了烟花！", "The Duck King lit the fireworks!"));

                Coroutine co = owner.StartCoroutine(FireworksRoutine(owner));
                ctx.Scope.RegisterCoroutine(co);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 鸭王的烟花触发失败: " + e.Message);
                return false;
            }
        }

        private IEnumerator FireworksRoutine(ModBehaviour owner)
        {
            Color[] palette = new Color[]
            {
                BossRushUIColors.RarityLegendary,
                BossRushUIColors.RarityRare,
                BossRushUIColors.RarityEpic,
                BossRushUIColors.RarityUncommon,
                BossRushUIColors.Accent
            };

            int bursts = Mathf.Max(1, RandomEventsTuning.FireworksBurstCount);
            for (int i = 0; i < bursts; i++)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || owner == null)
                {
                    yield break;
                }

                // 落点解析放在独立方法里：迭代器块内不允许 try/catch 包住 yield
                Vector3 p;
                if (!TryResolveBurstPoint(player, out p))
                {
                    yield break;
                }

                owner.CreateRandomEventHarmlessExplosion(
                    p,
                    ExplosionFxTypes.normal,
                    RandomEventsTuning.FireworksShakeStrength);
                owner.PopRandomEventText("★", p, palette[i % palette.Length], 1.6f);

                yield return new WaitForSeconds(RandomEventsTuning.FireworksIntervalSeconds);
            }
        }

        private static bool TryResolveBurstPoint(CharacterMainControl player, out Vector3 point)
        {
            point = Vector3.zero;
            try
            {
                float angle = UnityEngine.Random.Range(0f, 360f);
                point = player.transform.position
                    + Quaternion.Euler(0f, angle, 0f) * Vector3.forward
                      * UnityEngine.Random.Range(4f, RandomEventsTuning.FireworksRingRadius)
                    + Vector3.up * UnityEngine.Random.Range(2f, RandomEventsTuning.FireworksHeight);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            RandomEventEffectHelpers.ClearScope(ctx, reason);
        }
    }

    // ========================================================================
    // E7 金鸭雨
    // ========================================================================

    /// <summary>
    /// 金鸭雨：玩家脚下天降一批现金堆（TypeID 451，StackCount 就是金额）。
    /// 分帧生成在桥里做，事件层只负责触发与播报。
    ///
    /// 设计约定：掉在地上的现金**不回收**——它已经是玩家收益。
    /// 因此本事件的清理只需清空作用域（无 Scope 托管实体），语义上仍然幂等。
    /// </summary>
    internal sealed class RandomEventGoldenDuckRain : RandomEventBase
    {
        internal override RandomEventId Id { get { return RandomEventId.GoldenDuckRain; } }

        internal override string DisplayName
        {
            get { return L10n.T("金鸭雨", "Golden Duck Rain"); }
        }

        internal override float DurationSeconds { get { return RandomEventsTuning.GoldenDuckRainDurationSeconds; } }

        internal override float Weight { get { return RandomEventsTuning.WeightGoldenDuckRain; } }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                CharacterMainControl player = RandomEventEffectHelpers.ResolveAlivePlayer();
                if (owner == null || player == null || ctx == null)
                {
                    return false;
                }

                owner.ShowRandomEventBanner(L10n.T("天降横财：金鸭雨！", "Golden Duck Rain: money from the sky!"));

                int piles = UnityEngine.Random.Range(
                    RandomEventsTuning.GoldenDuckRainPileMin,
                    RandomEventsTuning.GoldenDuckRainPileMax + 1);

                ctx.AnchorPosition = player.transform.position;
                owner.SpawnRandomEventCashPiles(
                    player.transform.position,
                    RandomEventsTuning.GoldenDuckRainTotalCash,
                    piles,
                    RandomEventsTuning.GoldenDuckRainScatterRadius);

                owner.PlayRandomEventModSound("lottery/special.mp3");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 金鸭雨触发失败: " + e.Message);
                return false;
            }
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            RandomEventEffectHelpers.ClearScope(ctx, reason);
        }
    }

    // ========================================================================
    // E8 鸭群巡游
    // ========================================================================

    /// <summary>
    /// 鸭群巡游：一队官方下蛋鸭大摇大摆地横穿战场，纯演出，不参与战斗判定。
    /// 清理必须自己销毁：它们复用 eggSpawnPreset 拿到了清怪豁免，
    /// 不自清就会残留到局末（这正是豁免的副作用）。
    /// </summary>
    internal sealed class RandomEventDuckParade : RandomEventBase
    {
        private readonly List<CharacterMainControl> _ducks = new List<CharacterMainControl>(8);
        private bool _cleanedUp = true;
        private int _sceneBuildIndex;

        internal override RandomEventId Id { get { return RandomEventId.DuckParade; } }

        internal override string DisplayName
        {
            get { return L10n.T("鸭群巡游", "Duck Parade"); }
        }

        internal override float DurationSeconds { get { return RandomEventsTuning.DuckParadeDurationSeconds; } }

        internal override float Weight { get { return RandomEventsTuning.WeightDuckParade; } }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                CharacterMainControl player = RandomEventEffectHelpers.ResolveAlivePlayer();
                if (owner == null || player == null || ctx == null)
                {
                    return false;
                }

                Vector3 dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                Vector3 start = SpawnPositionHelper.SnapToGround(
                    player.transform.position - dir * RandomEventsTuning.DuckParadeStartDistance);

                int count = UnityEngine.Random.Range(
                    RandomEventsTuning.DuckParadeCountMin,
                    RandomEventsTuning.DuckParadeCountMax + 1);

                _ducks.Clear();
                _cleanedUp = false;
                _sceneBuildIndex = SceneManager.GetActiveScene().buildIndex;

                owner.SpawnRandomEventParadeDucks(start, dir, count, IsSpawnStillValid, HandleDuckSpawned);

                ctx.AnchorPosition = start;
                owner.ShowRandomEventBanner(L10n.T(
                    "一队鸭鸭大摇大摆地横穿了战场",
                    "A duck parade waddles across the battlefield"));
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 鸭群巡游触发失败: " + e.Message);
                _cleanedUp = true;
                return false;
            }
        }

        /// <summary>异步生成续作的有效性闸。命名方法，供桥以方法组形式引用。</summary>
        private bool IsSpawnStillValid()
        {
            try
            {
                return !_cleanedUp && SceneManager.GetActiveScene().buildIndex == _sceneBuildIndex;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void HandleDuckSpawned(CharacterMainControl duck)
        {
            if (duck == null)
            {
                return;
            }

            // 生成完成时事件可能已经收尾，直接回收，绝不留活口
            if (_cleanedUp)
            {
                try
                {
                    if (duck.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(duck.gameObject);
                    }
                }
                catch (Exception) { }
                return;
            }

            _ducks.Add(duck);
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            _cleanedUp = true;

            RunScopedRegistry.ForEachReverse(
                _ducks,
                delegate (CharacterMainControl duck)
                {
                    if (duck.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(duck.gameObject);
                    }
                },
                delegate (Exception e, CharacterMainControl duck)
                {
                    ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 销毁巡游鸭失败: " + e.Message);
                });
            _ducks.Clear();

            RandomEventEffectHelpers.ClearScope(ctx, reason);
        }
    }
}
