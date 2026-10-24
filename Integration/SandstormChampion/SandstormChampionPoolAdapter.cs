using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>冠军之影的共享 Boss 池入口；战斗与奖励仍由每只角色自己的控制器持有。</summary>
    internal static class SandstormChampionPoolAdapter
    {
        internal static bool IsPreset(EnemyPresetInfo preset)
        {
            return preset != null && IsPreset(preset.name);
        }

        internal static bool IsPreset(string nameKey)
        {
            return string.Equals(nameKey, SandstormChampionBoss.NameKey, StringComparison.Ordinal);
        }

        internal static void Register(ModBehaviour owner)
        {
            if (owner == null || !owner.HasArenaEnemyPresetCatalog
                || owner.FindArenaEnemyPreset(SandstormChampionBoss.NameKey) != null) return;
            owner.AddArenaEnemyPreset(new EnemyPresetInfo
            {
                name = SandstormChampionBoss.NameKey,
                displayName = L10n.T("冠军之影", "Shadow of the Champion"),
                team = (int)Teams.wolf,
                baseHealth = SandstormChampionConfig.BaseHealth,
                baseDamage = SandstormChampionConfig.DashDamage,
                healthMultiplier = 1f,
                damageMultiplier = 1f,
                expReward = 400
            });
        }

        private static bool SupportsPlayerCombat(ModBehaviour owner)
        {
            // 技能只以主玩家为目标，不能进入阵营战、旁观群战或 G 的托管生成事务。
            return owner != null && !owner.IsModeEActive && !owner.IsModeFActive
                && !owner.IsZombieModeActive && !ModBehaviour.IsModeGRunInProgressSafe()
                && !ModBehaviour.IsModeHRunInProgressSafe();
        }

        internal static async UniTask<CharacterMainControl> SpawnAsync(ModBehaviour owner, Vector3 position,
            Func<bool> isActiveCheck, bool isNonWaveSpawn, bool deferActivationUntilNextFrame = false)
        {
            if (!SupportsPlayerCombat(owner) || SceneLoader.IsSceneLoading) return null;
            int sceneHandle = SceneManager.GetActiveScene().handle;
            Func<bool> current = () => SupportsPlayerCombat(owner) && ModBehaviour.Instance == owner
                && !SceneLoader.IsSceneLoading && SceneManager.GetActiveScene().handle == sceneHandle
                && (isActiveCheck == null || isActiveCheck());
            if (!current()) return null;
            CharacterMainControl character = await SandstormChampionBoss.SpawnAsync(owner, position, current,
                deferActivationUntilNextFrame);
            if (character == null) return null;
            if (!current())
            {
                CleanupCancelled(character);
                return null;
            }
            if (!isNonWaveSpawn)
            {
                owner.RegisterArenaWaveBossFromContent(character);
                character.gameObject.AddComponent<SandstormChampionPoolWaveLease>().Bind(owner, character);
            }
            // 专属星阙在官方创建尸体箱前入箱；不接会清空库存的随机奖励箱漏斗。
            return character;
        }

        internal static void CleanupCancelled(CharacterMainControl character)
        {
            if (character != null) UnityEngine.Object.Destroy(character.gameObject);
        }
    }

    internal sealed class SandstormChampionPoolWaveLease : MonoBehaviour
    {
        private ModBehaviour owner;
        private CharacterMainControl character;

        internal void Bind(ModBehaviour host, CharacterMainControl boss)
        {
            owner = host;
            character = boss;
        }

        private void OnDestroy()
        {
            if (owner != null)
            {
                owner.ClearArenaCurrentBossFromContent(character);
                owner.RemoveArenaWaveBossFromContent(character);
            }
            owner = null;
            character = null;
        }
    }
}
