using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void SetZombieModeRendererColor(Renderer renderer, Color color)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.SetZombieModeRendererColor(renderer, color);
        }

        private GameObject CreateZombieModeFlatZoneVisual(string name, Vector3 origin, float radius, float height, Color color)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(GameObject);
            }
            return module.CreateZombieModeFlatZoneVisual(name, origin, radius, height, color);
        }

        private ZombieModeEnemyKind RollZombieModeEnemyKind()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(ZombieModeEnemyKind);
            }
            return module.RollZombieModeEnemyKind();
        }

        private ZombieModeSpecialKind RollZombieModeSpecialKind()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(ZombieModeSpecialKind);
            }
            return module.RollZombieModeSpecialKind();
        }

        private List<ZombieModeEliteAffix> RollZombieModeEliteAffixes()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(List<ZombieModeEliteAffix>);
            }
            return module.RollZombieModeEliteAffixes();
        }

        private int CalculateZombieModeEnemyPurificationPoints(bool isBoss, ZombieModeEnemyKind enemyKind)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(int);
            }
            return module.CalculateZombieModeEnemyPurificationPoints(isBoss, enemyKind);
        }

        private void ApplyZombieModeEnemyTuning(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ApplyZombieModeEnemyTuning(enemy, marker);
        }

        private void ApplyZombieModeEnemyHurtAffixes(
            int runId,
            Health health,
            DamageInfo damageInfo,
            ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ApplyZombieModeEnemyHurtAffixes(runId, health, damageInfo, marker);
        }

        private bool IsZombieModeDamageFromMeleeWeapon(DamageInfo damageInfo)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(bool);
            }
            return module.IsZombieModeDamageFromMeleeWeapon(damageInfo);
        }

        private bool ItemHasZombieModeTag(ItemStatsSystem.Item item, string tagName)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(bool);
            }
            return module.ItemHasZombieModeTag(item, tagName);
        }

        private void ApplyZombieModeHealthOnlyMultiplier(
            CharacterMainControl character,
            float healthMultiplier,
            ZombieModeEnemyRuntimeMarker marker = null)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ApplyZombieModeHealthOnlyMultiplier(character, healthMultiplier, marker);
        }

        private void ApplyZombieModeEnemyCombatStatMultipliers(
            CharacterMainControl enemy,
            float damageMultiplier,
            float speedMultiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ApplyZombieModeEnemyCombatStatMultipliers(enemy, damageMultiplier, speedMultiplier, marker);
        }

        // RuntimeModule 通过窄桥读取仍由波次与入口 partial 持有的策略计算。
        internal int GetZombieModePacingWaveForRuntimeModule()
        {
            return GetZombieModePacingWave();
        }

        internal float GetZombieModeWaveSpeedMultiplierForRuntimeModule(int wave)
        {
            return GetZombieModeWaveSpeedMultiplier(wave);
        }

        internal Duckov.Utilities.Tag FindZombieModeTagByNameForRuntimeModule(string tagName)
        {
            return FindZombieModeTagByName(tagName);
        }
    }
}
