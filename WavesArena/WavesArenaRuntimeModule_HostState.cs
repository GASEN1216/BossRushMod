using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal bool IsActive { get; private set; }
        internal MonoBehaviour PlayerCharacter { get; set; }
        private static bool bossRushArenaPlanned = false;
        private static bool bossRushArenaActive = false;
        internal static bool ArenaPlanned { get { return bossRushArenaPlanned; } set { bossRushArenaPlanned = value; } }
        internal static bool ArenaActive { get { return bossRushArenaActive; } set { bossRushArenaActive = value; } }
        private Func<int?> configuredInfiniteHellBosses;
        private Func<BossRushSignInteractable> arenaSignInteractable;
        private Action clearEnemyRecoveryMonitorState;

        internal void BindHostStateServices(Func<int?> configuredBosses,
            Func<BossRushSignInteractable> signInteractable, Action clearRecovery)
        {
            configuredInfiniteHellBosses = configuredBosses;
            arenaSignInteractable = signInteractable;
            clearEnemyRecoveryMonitorState = clearRecovery;
        }

        internal void ConfigureBossRushMode(int bossesPerWave, bool useInfiniteHell)
        {
            // [DEBUG] 记录传入参数
            ModBehaviour.DevLog("[BossRush] ConfigureBossRushMode 调用: 传入 bossesPerWave=" + bossesPerWave + ", useInfiniteHell=" + useInfiniteHell + ", 当前 this.bossesPerWave=" + BossesPerWave);

            if (bossesPerWave < 1)
            {
                bossesPerWave = 1;
            }

            InfiniteHellMode = useInfiniteHell;

            // 无间炼狱模式下优先使用配置文件中的每波 Boss 数
            if (InfiniteHellMode && configuredInfiniteHellBosses().HasValue && configuredInfiniteHellBosses().Value > 0)
            {
                BossesPerWave = configuredInfiniteHellBosses().Value;
                ModBehaviour.DevLog("[BossRush] ConfigureBossRushMode: 无间炼狱模式，使用配置值 this.bossesPerWave=" + BossesPerWave);
            }
            else
            {
                BossesPerWave = bossesPerWave;
                ModBehaviour.DevLog("[BossRush] ConfigureBossRushMode: 普通模式，设置 this.bossesPerWave=" + BossesPerWave);
            }

            // 重置无间炼狱进度状态
            if (InfiniteHellMode)
            {
                InfiniteHellWaveIndex = 0;
                InfiniteHellCashPool = 0L;
                InfiniteHellMilestoneRewardTier = 0;

                try
                {
                    if (arenaSignInteractable() != null)
                    {
                        arenaSignInteractable().AddAmmoRefillOption();
                    }
                }
                catch {}
            }

            try
            {
                ModBehaviour.DevLog("[BossRush] 已设置每波Boss数量: " + BossesPerWave + (InfiniteHellMode ? " (无间炼狱)" : string.Empty));
            }
            catch {}
        }

        internal void SetBossRushRuntimeActive(bool active)
        {
            IsActive = active;
            clearEnemyRecoveryMonitorState();

            // [Bug修复] BossRush开始时确保订阅龙息Buff处理器
            // 无论玩家手上拿什么武器，龙裔遗族Boss的龙息都应该能触发龙焰灼烧
            if (active)
            {
                DragonBreathBuffHandler.Subscribe();
            }

            // 模式结束时清理变异词条和现金磁铁飞行状态
            if (!active)
            {
                MutatorManager.RemoveAll();
                MutatorUI.HideAll();
                ClearCashMagnetState();
            }
        }

        internal void ReturnToBossRushStart()
        {
            try
            {
                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                if (main == null)
                {
                    try
                    {
                        main = PlayerCharacter as CharacterMainControl;
                    }
                    catch {}
                }

                if (main == null)
                {
                    ModBehaviour.DevLog("[BossRush] ReturnToBossRushStart: 无法找到玩家角色");
                    return;
                }

                Vector3 targetPos = DemoChallengeStartPosition;
                if (targetPos == Vector3.zero)
                {
                    targetPos = main.transform.position;
                }

                try
                {
                    main.SetPosition(targetPos);
                    ModBehaviour.DevLog("[BossRush] ReturnToBossRushStart: 使用 SetPosition 将玩家传送回 BossRush 起始位置 " + targetPos);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] ReturnToBossRushStart: SetPosition 出错: " + e.Message + "，改用 transform.position");
                    main.transform.position = targetPos;
                }

                owner.ShowMessage(L10n.T("已返回出生点", "Returned to spawn point"));
            }
            catch {}
        }
    }
}
