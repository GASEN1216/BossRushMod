using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>花冠的 Boss 池目录与生成适配；每个实例的有效期由对应波次 / 事件 owner 提供。</summary>
    internal static class EclipseBloomPoolAdapter
    {
        internal static bool IsPreset(EnemyPresetInfo preset)
        {
            return preset != null && IsPreset(preset.name);
        }

        internal static bool IsPreset(string nameKey)
        {
            return string.Equals(nameKey, EclipseBloomBoss.NameKey, StringComparison.Ordinal);
        }

        internal static void Register(ModBehaviour owner)
        {
            if (owner == null || !owner.HasArenaEnemyPresetCatalog
                || owner.FindArenaEnemyPreset(EclipseBloomBoss.NameKey) != null) return;
            owner.AddArenaEnemyPreset(new EnemyPresetInfo
            {
                name = EclipseBloomBoss.NameKey,
                displayName = L10n.T("蚀日花冠", "Eclipse Bloom"),
                team = (int)Teams.wolf,
                baseHealth = 5000f,
                baseDamage = 34f,
                healthMultiplier = 1f,
                damageMultiplier = 1f,
                expReward = 400
            });
        }

        internal static async UniTask<CharacterMainControl> SpawnAsync(ModBehaviour owner, Vector3 position,
            Func<bool> isActiveCheck, bool isNonWaveSpawn, bool deferActivationUntilNextFrame = false)
        {
            if (owner == null || owner.IsModeEActive || owner.IsModeFActive
                || EclipseBloomEncounterPlacement.IsLoadingSubScene) return null;
            Scene scene;
            Scene levelScene;
            LevelManager levelOwner = LevelManager.Instance;
            if (!EclipseBloomEncounterPlacement.TryGetSpawnScene(out scene)
                || !EclipseBloomEncounterPlacement.TryGetLevelOwnerScene(out levelScene)) return null;
            Func<bool> current = () => owner != null
                && EclipseBloomEncounterPlacement.IsCurrent(scene, levelScene, levelOwner)
                && (isActiveCheck == null || isActiveCheck());
            if (!current()) return null;
            CharacterMainControl character = await EclipseBloomBoss.SpawnAsync(owner, position, current, scene);
            if (character == null) return null;
            if (deferActivationUntilNextFrame) await UniTask.Yield();
            if (!current())
            {
                UnityEngine.Object.Destroy(character.gameObject);
                return null;
            }
            if (!isNonWaveSpawn)
            {
                owner.RegisterArenaWaveBossFromContent(character);
                character.gameObject.AddComponent<EclipseBloomPoolWaveLease>().Bind(owner, character);
            }
            // 不挂随机清空库存的奖励箱漏斗：专属奖励由 Controller 在官方尸体箱创建前入箱。
            // 乱入实例没有波次身份，原生地图模块也不会持有或取消这里的实例。
            return character;
        }

        internal static void CleanupCancelled(CharacterMainControl character)
        {
            if (character != null) UnityEngine.Object.Destroy(character.gameObject);
        }
    }

    internal sealed class EclipseBloomPoolWaveLease : MonoBehaviour
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
            owner = null; character = null;
        }
    }
}
