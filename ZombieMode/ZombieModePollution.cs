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
        private readonly Dictionary<int, bool> zombieModeMeleeWeaponTypeCache = new Dictionary<int, bool>();
        private readonly MaterialPropertyBlock zombieModeRendererColorBlock = new MaterialPropertyBlock();
        private static readonly int ZombieModeRendererColorProperty = Shader.PropertyToID("_Color");
        private static readonly int ZombieModeRendererTintColorProperty = Shader.PropertyToID("_TintColor");
        private static readonly int ZombieModeRendererBaseColorProperty = Shader.PropertyToID("_BaseColor");

        private void SetZombieModeRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetPropertyBlock(zombieModeRendererColorBlock);
            Material sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial != null && sharedMaterial.HasProperty(ZombieModeRendererColorProperty))
            {
                zombieModeRendererColorBlock.SetColor(ZombieModeRendererColorProperty, color);
            }
            else if (sharedMaterial != null && sharedMaterial.HasProperty(ZombieModeRendererTintColorProperty))
            {
                zombieModeRendererColorBlock.SetColor(ZombieModeRendererTintColorProperty, color);
            }
            else
            {
                zombieModeRendererColorBlock.SetColor(ZombieModeRendererBaseColorProperty, color);
            }

            renderer.SetPropertyBlock(zombieModeRendererColorBlock);
        }

        // ====================================================================
        // 共享 disk visual mesh / material（审查 §3.3）
        // ====================================================================
        // 之前 8 处 telegraph / 区域 visual 都用 GameObject.CreatePrimitive(Cylinder)，
        // 自带 MeshFilter+MeshRenderer+CapsuleCollider，然后又 Destroy(collider)；
        // 高峰每秒 5–15 个新 GameObject 触发 GC + 物理初始化 ≈ 0.5–1 ms/帧。
        //
        // 改造：第一次仍然用 CreatePrimitive 提取一个 mesh 副本作为 shared mesh，
        // material 重新构造一次 standard shader 实例并共享；之后所有调用直接 new
        // GameObject + AddComponent<MeshFilter+MeshRenderer> 用 sharedMesh / sharedMaterial。
        // 省去 collider 构造 + destroy、省去 default-material 实例化。
        // ====================================================================
        private static Mesh s_zoneDiskMesh;
        private static Material s_zoneDiskMaterial;

        private static void EnsureZoneDiskAssets()
        {
            if (s_zoneDiskMesh == null)
            {
                GameObject scratch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                MeshFilter mf = scratch.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    s_zoneDiskMesh = mf.sharedMesh;
                }
                UnityEngine.Object.Destroy(scratch);
            }

            if (s_zoneDiskMaterial == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }
                s_zoneDiskMaterial = shader != null ? new Material(shader) : null;
            }
        }

        /// <summary>
        /// 创建丧尸模式平面区域 visual（telegraph / 区域伤害指示）。
        /// 替换 GameObject.CreatePrimitive(Cylinder) + Destroy(collider) 模式。
        /// 调用方仍负责 RegisterZombieModeRunOnlyObject + AddComponent<TickRuntime>。
        /// </summary>
        private GameObject CreateZombieModeFlatZoneVisual(string name, Vector3 origin, float radius, float height, Color color)
        {
            EnsureZoneDiskAssets();

            GameObject go = new GameObject(string.IsNullOrEmpty(name) ? "ZombieMode_Zone" : name);
            go.transform.position = origin + Vector3.up * 0.03f;
            go.transform.localScale = new Vector3(radius * 2f, Mathf.Max(0.01f, height), radius * 2f);

            if (s_zoneDiskMesh != null)
            {
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = s_zoneDiskMesh;
            }

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (s_zoneDiskMaterial != null)
            {
                mr.sharedMaterial = s_zoneDiskMaterial;
            }
            try
            {
                SetZombieModeRendererColor(mr, color);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] FlatZone visual 调色失败: " + e.Message);
            }
            return go;
        }

        private ZombieModeEnemyKind RollZombieModeEnemyKind()
        {
            int pollution = zombieModeRunState.TotalPollution;
            int eliteChance = GetZombieModeEliteChancePercent(pollution);
            int specialChance = GetZombieModeSpecialChancePercent(pollution);
            int roll = Random.Range(0, 100);
            if (roll < eliteChance)
            {
                return ZombieModeEnemyKind.Elite;
            }

            if (roll < eliteChance + specialChance)
            {
                return ZombieModeEnemyKind.Special;
            }

            return ZombieModeEnemyKind.Normal;
        }

        private int GetZombieModeSpecialChancePercent(int pollution)
        {
            if (pollution >= 25) return 30;
            if (pollution >= 20) return 25;
            if (pollution >= 15) return 20;
            if (pollution >= 10) return 15;
            if (pollution >= 5) return 10;
            return 5;
        }

        private int GetZombieModeEliteChancePercent(int pollution)
        {
            if (pollution >= 25) return 10;
            if (pollution >= 20) return 8;
            if (pollution >= 15) return 6;
            if (pollution >= 10) return 4;
            if (pollution >= 5) return 2;
            return 1;
        }

        // 静态读取以避免每次刷怪 new[] 装箱（审查 §3.7）。
        private static readonly ZombieModeSpecialKind[] s_zombieModeSpecialKindOrder = new ZombieModeSpecialKind[]
        {
            ZombieModeSpecialKind.Sprinter,
            ZombieModeSpecialKind.Exploder,
            ZombieModeSpecialKind.Plague,
            ZombieModeSpecialKind.Summoner,
            ZombieModeSpecialKind.Harasser
        };

        // System.Enum.GetValues 返回 Array 会装箱；缓存为强类型数组，避免每次 Roll 触发 GC。
        private static readonly ZombieModeEliteAffix[] s_zombieModeEliteAffixAll = (ZombieModeEliteAffix[])
            System.Enum.GetValues(typeof(ZombieModeEliteAffix));

        // RollZombieModeEliteAffixes 内的两个临时容器；Clear() + 复用避免每次 new。
        private readonly List<ZombieModeEliteAffix> rollEliteAffixesScratchSelected = new List<ZombieModeEliteAffix>();
        private readonly List<ZombieModeEliteAffix> rollEliteAffixesScratchPool = new List<ZombieModeEliteAffix>();

        private ZombieModeSpecialKind RollZombieModeSpecialKind()
        {
            return s_zombieModeSpecialKindOrder[Random.Range(0, s_zombieModeSpecialKindOrder.Length)];
        }

        private List<ZombieModeEliteAffix> RollZombieModeEliteAffixes()
        {
            int pollution = zombieModeRunState.TotalPollution;
            int desiredCount = 1;
            if (pollution >= 25)
            {
                desiredCount = Random.Range(2, 4);
            }
            else if (pollution >= 15)
            {
                desiredCount = 2;
            }
            else if (pollution >= 5 && Random.value < 0.35f)
            {
                desiredCount = 2;
            }

            // 注：调用方拿到 list 后会持久持有（marker.EliteAffixes），所以 selected 必须 new 出独立副本；
            // 但 pool 是临时枚举池，可以复用 scratch 容器。
            rollEliteAffixesScratchPool.Clear();
            for (int i = 0; i < s_zombieModeEliteAffixAll.Length; i++)
            {
                ZombieModeEliteAffix affix = s_zombieModeEliteAffixAll[i];
                if (GetZombieModeAffixUnlockTier(affix) <= zombieModeRunState.PollutionTier)
                {
                    rollEliteAffixesScratchPool.Add(affix);
                }
            }

            List<ZombieModeEliteAffix> selected = new List<ZombieModeEliteAffix>();
            while (selected.Count < desiredCount && rollEliteAffixesScratchPool.Count > 0)
            {
                int index = Random.Range(0, rollEliteAffixesScratchPool.Count);
                ZombieModeEliteAffix candidate = rollEliteAffixesScratchPool[index];
                rollEliteAffixesScratchPool.RemoveAt(index);
                selected.Add(candidate);
                if (!IsZombieModeAffixCombinationAllowed(selected))
                {
                    selected.Remove(candidate);
                }
            }

            if (selected.Count <= 0)
            {
                selected.Add(ZombieModeEliteAffix.Tough);
            }

            rollEliteAffixesScratchPool.Clear();
            return selected;
        }

        private int GetZombieModeAffixUnlockTier(ZombieModeEliteAffix affix)
        {
            switch (affix)
            {
                case ZombieModeEliteAffix.Swift:
                case ZombieModeEliteAffix.Frenzied:
                case ZombieModeEliteAffix.Tough:
                    return 0;
                case ZombieModeEliteAffix.Stalwart:
                case ZombieModeEliteAffix.Regenerating:
                case ZombieModeEliteAffix.Burst:
                case ZombieModeEliteAffix.Plague:
                    return 1;
                case ZombieModeEliteAffix.Commander:
                case ZombieModeEliteAffix.ToxicAura:
                case ZombieModeEliteAffix.Splitting:
                case ZombieModeEliteAffix.Shielded:
                    return 3;
                default:
                    return 5;
            }
        }

        private bool IsZombieModeAffixCombinationAllowed(List<ZombieModeEliteAffix> affixes)
        {
            if (affixes == null)
            {
                return true;
            }

            bool stalwart = affixes.Contains(ZombieModeEliteAffix.Stalwart);
            bool shielded = affixes.Contains(ZombieModeEliteAffix.Shielded);
            bool regenerating = affixes.Contains(ZombieModeEliteAffix.Regenerating);
            bool swift = affixes.Contains(ZombieModeEliteAffix.Swift);
            bool toxicAura = affixes.Contains(ZombieModeEliteAffix.ToxicAura);
            bool plague = affixes.Contains(ZombieModeEliteAffix.Plague);
            bool splitting = affixes.Contains(ZombieModeEliteAffix.Splitting);
            bool burst = affixes.Contains(ZombieModeEliteAffix.Burst);

            if (stalwart && shielded && regenerating)
            {
                return false;
            }

            if (stalwart && swift && zombieModeRunState.TotalPollution < 15)
            {
                return false;
            }

            if (toxicAura && plague && swift && zombieModeRunState.TotalPollution < 15)
            {
                return false;
            }

            if (splitting && burst && zombieModeRunState.PerformanceTier >= ZombieModePerformanceTier.SoftProtect)
            {
                return false;
            }

            return true;
        }

        private int CalculateZombieModeEnemyPurificationPoints(bool isBoss, ZombieModeEnemyKind enemyKind)
        {
            if (isBoss)
            {
                return Random.Range(300, 801);
            }

            int min = ZombieModeTuning.NormalPurificationMin;
            int max = ZombieModeTuning.NormalPurificationMax;
            if (enemyKind == ZombieModeEnemyKind.Special)
            {
                min = ZombieModeTuning.SpecialPurificationMin;
                max = ZombieModeTuning.SpecialPurificationMax;
            }
            else if (enemyKind == ZombieModeEnemyKind.Elite)
            {
                min = ZombieModeTuning.ElitePurificationMin;
                max = ZombieModeTuning.ElitePurificationMax;
            }

            int baseValue = Random.Range(min, max + 1);
            int pollutionSteps = Mathf.FloorToInt(zombieModeRunState.TotalPollution / 10f);
            float multiplier = Mathf.Min(1f + pollutionSteps * 0.10f, ZombieModeTuning.PurificationPollutionScaleMax);
            return Mathf.Max(1, Mathf.FloorToInt(baseValue * multiplier));
        }

        private void ApplyZombieModeEnemyTuning(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null || marker.IsBoss)
            {
                return;
            }

            float healthMultiplier = 1f;
            float damageMultiplier = 1f;
            float speedMultiplier = 1f;
            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                healthMultiplier = ZombieModeTuning.SpecialHealthMultiplier;
                damageMultiplier = ZombieModeTuning.SpecialDamageMultiplier;
                speedMultiplier = ZombieModeTuning.SpecialMoveSpeedMultiplier;
                ApplyZombieModeSpecialKindTuning(marker.SpecialKind, ref healthMultiplier, ref damageMultiplier, ref speedMultiplier);
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                bool enhanced = zombieModeRunState.TotalPollution >= 15;
                healthMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteHealthMultiplier : ZombieModeTuning.EliteHealthMultiplier;
                damageMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteDamageMultiplier : ZombieModeTuning.EliteDamageMultiplier;
                speedMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteMoveSpeedMultiplier : ZombieModeTuning.EliteMoveSpeedMultiplier;
                ApplyZombieModeEliteAffixTuning(enemy, marker, ref healthMultiplier, ref damageMultiplier, ref speedMultiplier);
            }

            float pollutionHealthScale = 1f + zombieModeRunState.TotalPollution * ZombieModeTuning.PollutionHealthScalePerPoint;
            float pollutionDamageScale = 1f + zombieModeRunState.TotalPollution * ZombieModeTuning.PollutionDamageScalePerPoint;
            marker.HealthMultiplier = healthMultiplier * pollutionHealthScale;
            marker.DamageMultiplier = damageMultiplier * pollutionDamageScale;
            marker.MoveSpeedMultiplier = speedMultiplier;
            ApplyZombieModeHealthMultiplier(enemy, marker.HealthMultiplier, marker);
            ApplyZombieModeEnemyCombatStatMultipliers(enemy, marker.DamageMultiplier, marker.MoveSpeedMultiplier);
            ApplyZombieModeEnemyName(enemy, marker);
            EnsureZombieModeThreatRuntime(enemy, marker);
        }

        private void ApplyZombieModeSpecialKindTuning(
            ZombieModeSpecialKind specialKind,
            ref float healthMultiplier,
            ref float damageMultiplier,
            ref float speedMultiplier)
        {
            switch (specialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    speedMultiplier *= 1.20f;
                    break;
                case ZombieModeSpecialKind.Exploder:
                    healthMultiplier *= 1.30f / ZombieModeTuning.SpecialHealthMultiplier;
                    break;
                case ZombieModeSpecialKind.Plague:
                    healthMultiplier *= 1.50f / ZombieModeTuning.SpecialHealthMultiplier;
                    speedMultiplier *= 0.95f / ZombieModeTuning.SpecialMoveSpeedMultiplier;
                    break;
                case ZombieModeSpecialKind.Summoner:
                    healthMultiplier *= 1.50f / ZombieModeTuning.SpecialHealthMultiplier;
                    speedMultiplier *= 0.95f / ZombieModeTuning.SpecialMoveSpeedMultiplier;
                    break;
                case ZombieModeSpecialKind.Harasser:
                    healthMultiplier *= 1.30f / ZombieModeTuning.SpecialHealthMultiplier;
                    break;
            }
        }

        private void ApplyZombieModeEliteAffixTuning(
            CharacterMainControl enemy,
            ZombieModeEnemyRuntimeMarker marker,
            ref float healthMultiplier,
            ref float damageMultiplier,
            ref float speedMultiplier)
        {
            for (int i = 0; i < marker.EliteAffixes.Count; i++)
            {
                ZombieModeEliteAffix affix = marker.EliteAffixes[i];
                if (affix == ZombieModeEliteAffix.Swift)
                {
                    speedMultiplier *= 1.30f;
                }
                else if (affix == ZombieModeEliteAffix.Tough)
                {
                    healthMultiplier *= 1.40f;
                }
                else if (affix == ZombieModeEliteAffix.Frenzied)
                {
                    damageMultiplier *= 1.15f;
                    speedMultiplier *= 1.10f;
                }
                else if (affix == ZombieModeEliteAffix.Stalwart)
                {
                    healthMultiplier *= 1.15f;
                }
                else if (affix == ZombieModeEliteAffix.Regenerating)
                {
                    ZombieModeRegenerationAffixRuntime regen = enemy.gameObject.GetComponent<ZombieModeRegenerationAffixRuntime>();
                    if (regen == null)
                    {
                        regen = enemy.gameObject.AddComponent<ZombieModeRegenerationAffixRuntime>();
                    }
                    regen.Initialize(marker.RunId);
                }
                else if (affix == ZombieModeEliteAffix.Shielded)
                {
                    healthMultiplier *= 1.25f;
                }
            }
        }

        private void ApplyZombieModeEnemyHurtAffixes(
            int runId,
            Health health,
            DamageInfo damageInfo,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (!IsZombieModeRunValid(runId) ||
                health == null ||
                damageInfo.fromCharacter == null ||
                marker == null ||
                marker.EnemyKind != ZombieModeEnemyKind.Elite)
            {
                return;
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Shielded))
            {
                ZombieModeShieldedAffixRuntime shield = marker.ShieldedAffix;
                if (shield != null)
                {
                    float dmg = damageInfo.damageValue;
                    if (shield.AbsorbDamage(ref dmg))
                    {
                        float absorbed = damageInfo.damageValue - dmg;
                        if (absorbed > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + absorbed));
                        }
                    }
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Stalwart) &&
                damageInfo.fromCharacter.IsMainCharacter &&
                !IsZombieModeDamageFromMeleeWeapon(damageInfo))
            {
                float restore = Mathf.Max(0f, damageInfo.damageValue * (1f - ZombieModeTuning.StalwartRangedDamageMultiplier));
                if (restore > 0f && health.CurrentHealth > 0f)
                {
                    health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + restore));
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Adaptive) &&
                damageInfo.fromCharacter.IsMainCharacter)
            {
                bool isMelee = IsZombieModeDamageFromMeleeWeapon(damageInfo);
                if (Time.unscaledTime > marker.AdaptiveReductionEndTime)
                {
                    marker.AdaptiveRangedActive = false;
                    marker.AdaptiveMeleeActive = false;
                }

                if (isMelee)
                {
                    marker.AdaptiveMeleeHitCount++;
                    marker.AdaptiveRangedHitCount = 0;
                    if (marker.AdaptiveMeleeHitCount >= ZombieModeTuning.AdaptiveAffixHitThreshold && !marker.AdaptiveMeleeActive)
                    {
                        marker.AdaptiveMeleeActive = true;
                        marker.AdaptiveRangedActive = false;
                        marker.AdaptiveReductionEndTime = Time.unscaledTime + ZombieModeTuning.AdaptiveAffixDurationSeconds;
                        marker.AdaptiveMeleeHitCount = 0;
                        CharacterMainControl ch = marker.Owner;
                        if (ch != null) ch.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
                    }
                    if (marker.AdaptiveMeleeActive)
                    {
                        float reduced = damageInfo.damageValue * ZombieModeTuning.AdaptiveAffixReductionPercent;
                        if (reduced > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + reduced));
                        }
                    }
                }
                else
                {
                    marker.AdaptiveRangedHitCount++;
                    marker.AdaptiveMeleeHitCount = 0;
                    if (marker.AdaptiveRangedHitCount >= ZombieModeTuning.AdaptiveAffixHitThreshold && !marker.AdaptiveRangedActive)
                    {
                        marker.AdaptiveRangedActive = true;
                        marker.AdaptiveMeleeActive = false;
                        marker.AdaptiveReductionEndTime = Time.unscaledTime + ZombieModeTuning.AdaptiveAffixDurationSeconds;
                        marker.AdaptiveRangedHitCount = 0;
                        CharacterMainControl ch = marker.Owner;
                        if (ch != null) ch.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
                    }
                    if (marker.AdaptiveRangedActive)
                    {
                        float reduced = damageInfo.damageValue * ZombieModeTuning.AdaptiveAffixReductionPercent;
                        if (reduced > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + reduced));
                        }
                    }
                }
            }
        }

        private bool IsZombieModeDamageFromMeleeWeapon(DamageInfo damageInfo)
        {
            if (damageInfo.fromWeaponItemID <= 0)
            {
                return false;
            }

            bool cached;
            if (zombieModeMeleeWeaponTypeCache.TryGetValue(damageInfo.fromWeaponItemID, out cached))
            {
                return cached;
            }

            return CacheZombieModeMeleeWeaponType(damageInfo.fromWeaponItemID);
        }

        private bool CacheZombieModeMeleeWeaponType(int typeId)
        {
            bool melee = false;
            bool resolved = false;
            try
            {
                ItemStatsSystem.Item item = null;
                try { item = ItemStatsSystem.ItemAssetsCollection.GetPrefab(typeId); } catch (System.Exception e) { DevLog("[ZombieMode] GetPrefab(" + typeId + ") 失败，回退 ItemFactory: " + e.Message); }
                if (item == null)
                {
                    try { item = ItemFactory.GetLoadedItem(typeId); } catch (System.Exception e) { DevLog("[ZombieMode] ItemFactory.GetLoadedItem(" + typeId + ") 失败: " + e.Message); }
                }

                if (item != null)
                {
                    melee = ItemHasZombieModeTag(item, "MeleeWeapon") ||
                            item.GetComponent<ItemAgent_MeleeWeapon>() != null;
                    resolved = true;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 近战武器类型缓存失败: " + e.Message);
            }

            if (resolved)
            {
                zombieModeMeleeWeaponTypeCache[typeId] = melee;
            }
            return melee;
        }

        private bool ItemHasZombieModeTag(ItemStatsSystem.Item item, string tagName)
        {
            if (item == null || string.IsNullOrEmpty(tagName))
            {
                return false;
            }

            try
            {
                Duckov.Utilities.Tag target = FindZombieModeTagByName(tagName);
                if (target == null || item.Tags == null)
                {
                    return false;
                }
                if (item.Tags.Contains(target))
                {
                    return true;
                }
                // 名称回退：避免 Tag 实例不一致（hot reload / 不同程序集）导致 Contains 漏判
                foreach (Duckov.Utilities.Tag tag in item.Tags)
                {
                    if (tag != null && string.Equals(tag.name, target.name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyZombieModeHealthMultiplier(
            CharacterMainControl enemy,
            float healthMultiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || enemy.Health == null)
            {
                return;
            }

            marker.BaseMaxHealth = enemy.Health.MaxHealth;

            ApplyZombieModeHealthOnlyMultiplier(enemy, healthMultiplier, marker);

            enemy.Health.showHealthBar = marker.EnemyKind == ZombieModeEnemyKind.Elite;
            if (enemy.Health.MaxHealth > 0f)
            {
                enemy.Health.CurrentHealth = enemy.Health.MaxHealth;
            }
        }

        private void ApplyZombieModeHealthOnlyMultiplier(
            CharacterMainControl character,
            float healthMultiplier,
            ZombieModeEnemyRuntimeMarker marker = null)
        {
            if (character == null || character.CharacterItem == null || Mathf.Approximately(healthMultiplier, 1f))
            {
                return;
            }

            try
            {
                if (marker != null && character.Health != null)
                {
                    marker.BaseMaxHealth = character.Health.MaxHealth;
                }

                Stat maxHealthStat = character.CharacterItem.GetStat("MaxHealth");
                if (maxHealthStat == null)
                {
                    maxHealthStat = character.CharacterItem.GetStat("MaxHealth".GetHashCode());
                }

                if (maxHealthStat == null)
                {
                    return;
                }

                float baseValue = maxHealthStat.BaseValue > 0f ? maxHealthStat.BaseValue : maxHealthStat.Value;
                float delta = baseValue * (healthMultiplier - 1f);
                if (Mathf.Abs(delta) > 0.01f)
                {
                    Modifier modifier = new Modifier(ModifierType.Add, delta, this);
                    maxHealthStat.AddModifier(modifier);
                }

                if (character.Health != null && character.Health.MaxHealth > 0f)
                {
                    character.Health.SetHealth(character.Health.MaxHealth);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] Health-only MaxHealth 倍率应用失败: " + e.Message);
            }
        }

        private void ApplyZombieModeEnemyCombatStatMultipliers(CharacterMainControl enemy, float damageMultiplier, float speedMultiplier)
        {
            if (enemy == null || enemy.CharacterItem == null)
            {
                return;
            }

            TryApplyZombieModeEnemyStatMultiplier(enemy.CharacterItem, "WalkSpeed", speedMultiplier);
            TryApplyZombieModeEnemyStatMultiplier(enemy.CharacterItem, "RunSpeed", speedMultiplier);
            TryApplyZombieModeEnemyStatMultiplier(enemy.CharacterItem, "MeleeDamageMultiplier", damageMultiplier);
            TryApplyZombieModeEnemyStatMultiplier(enemy.CharacterItem, "GunDamageMultiplier", damageMultiplier);
        }

        private void TryApplyZombieModeEnemyStatMultiplier(ItemStatsSystem.Item characterItem, string statName, float multiplier)
        {
            if (characterItem == null || string.IsNullOrEmpty(statName) || Mathf.Approximately(multiplier, 1f))
            {
                return;
            }

            try
            {
                Stat stat = characterItem.GetStat(statName);
                if (stat == null)
                {
                    return;
                }

                Modifier modifier = new Modifier(ModifierType.Add, stat.BaseValue * (multiplier - 1f), this);
                stat.AddModifier(modifier);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] AI 倍率 Modifier 应用失败: " + e.Message);
            }
        }

        private void ApplyZombieModeAiSpeedMultiplier(CharacterMainControl enemy, float speedMultiplier)
        {
            if (enemy == null || Mathf.Approximately(speedMultiplier, 1f))
            {
                return;
            }

            enemy.transform.localScale = enemy.transform.localScale * Mathf.Clamp(speedMultiplier, 0.85f, 1.35f);
        }

        private void ApplyZombieModeEnemyName(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null)
            {
                return;
            }

            string label = string.Empty;
            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                label = GetZombieModeSpecialDisplayName(marker.SpecialKind);
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                label = GetZombieModeEliteAffixLabel(marker);
            }

            if (!string.IsNullOrEmpty(label))
            {
                enemy.gameObject.name = enemy.gameObject.name + "_" + label;
                enemy.PopText(label);
            }
        }

        private string GetZombieModeSpecialDisplayName(ZombieModeSpecialKind specialKind)
        {
            switch (specialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    return L10n.T("BossRush_ZombieMode_Special_Sprinter");
                case ZombieModeSpecialKind.Exploder:
                    return L10n.T("BossRush_ZombieMode_Special_Exploder");
                case ZombieModeSpecialKind.Plague:
                    return L10n.T("BossRush_ZombieMode_Special_Plague");
                case ZombieModeSpecialKind.Summoner:
                    return L10n.T("BossRush_ZombieMode_Special_Summoner");
                case ZombieModeSpecialKind.Harasser:
                    return L10n.T("BossRush_ZombieMode_Special_Harasser");
                default:
                    return string.Empty;
            }
        }

        private string GetZombieModeEliteAffixLabel(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.EliteAffixes.Count <= 0)
            {
                return L10n.T("BossRush_ZombieMode_Elite");
            }

            string label = string.Empty;
            for (int i = 0; i < marker.EliteAffixes.Count; i++)
            {
                if (i > 0)
                {
                    label += "\u00B7";
                }
                label += GetZombieModeEliteAffixDisplayName(marker.EliteAffixes[i]);
            }
            return "[" + label + "]" + L10n.T("BossRush_ZombieMode_Elite");
        }

        private string GetZombieModeEliteAffixDisplayName(ZombieModeEliteAffix affix)
        {
            switch (affix)
            {
                case ZombieModeEliteAffix.Swift:
                    return L10n.T("BossRush_ZombieMode_Affix_Swift");
                case ZombieModeEliteAffix.Frenzied:
                    return L10n.T("BossRush_ZombieMode_Affix_Frenzied");
                case ZombieModeEliteAffix.Tough:
                    return L10n.T("BossRush_ZombieMode_Affix_Tough");
                case ZombieModeEliteAffix.Stalwart:
                    return L10n.T("BossRush_ZombieMode_Affix_Stalwart");
                case ZombieModeEliteAffix.Regenerating:
                    return L10n.T("BossRush_ZombieMode_Affix_Regenerating");
                case ZombieModeEliteAffix.Burst:
                    return L10n.T("BossRush_ZombieMode_Affix_Burst");
                case ZombieModeEliteAffix.Plague:
                    return L10n.T("BossRush_ZombieMode_Affix_Plague");
                case ZombieModeEliteAffix.Commander:
                    return L10n.T("BossRush_ZombieMode_Affix_Commander");
                case ZombieModeEliteAffix.ToxicAura:
                    return L10n.T("BossRush_ZombieMode_Affix_ToxicAura");
                case ZombieModeEliteAffix.Splitting:
                    return L10n.T("BossRush_ZombieMode_Affix_Splitting");
                case ZombieModeEliteAffix.Shielded:
                    return L10n.T("BossRush_ZombieMode_Affix_Shielded");
                default:
                    return L10n.T("BossRush_ZombieMode_Affix_Adaptive");
            }
        }

        private void HandleZombieModeSpecialDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            if (marker == null || marker.SpecialKind != ZombieModeSpecialKind.Exploder || character == null)
            {
                return;
            }

            DealZombieModeAreaDamageToPlayer(
                runId,
                character,
                character.transform.position,
                ZombieModeTuning.ExploderDeathRadius,
                ZombieModeTuning.ExploderDeathDamage);
        }

        private void HandleZombieModeEliteDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            if (marker == null || character == null)
            {
                return;
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Burst))
            {
                DealZombieModeAreaDamageToPlayer(
                    runId,
                    character,
                    character.transform.position,
                    ZombieModeTuning.BurstAffixDeathRadius,
                    ZombieModeTuning.BurstAffixDeathDamage);
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Splitting) &&
                zombieModeRunState.PerformanceTier < ZombieModePerformanceTier.SoftProtect)
            {
                int count = ZombieModeTuning.SplittingAffixSpawnCount;
                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = Quaternion.Euler(0f, 360f * i / count, 0f) * Vector3.forward * 1.5f;
                    SpawnZombieModeSmallSplitAsync(runId, character.transform.position + offset).Forget();
                }
            }
        }

        private async Cysharp.Threading.Tasks.UniTask SpawnZombieModeSmallSplitAsync(int runId, Vector3 position)
        {
            CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(runId, position, ZombieModeEnemyKind.Normal, true);
            if (zombie != null)
            {
                zombie.transform.localScale = zombie.transform.localScale * 0.6f;
            }
        }

        private void EnsureZombieModeThreatRuntime(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null || marker.IsBoss ||
                (marker.EnemyKind != ZombieModeEnemyKind.Special && marker.EnemyKind != ZombieModeEnemyKind.Elite))
            {
                return;
            }

            ZombieModeThreatRuntime runtime = enemy.gameObject.GetComponent<ZombieModeThreatRuntime>();
            if (runtime == null)
            {
                runtime = enemy.gameObject.AddComponent<ZombieModeThreatRuntime>();
            }

            float cooldown = marker.EnemyKind == ZombieModeEnemyKind.Elite
                ? ZombieModeTuning.EliteSkillCooldownSeconds
                : GetZombieModeSpecialCooldown(marker.SpecialKind);
            runtime.Initialize(marker.RunId, cooldown);

            if (marker.EnemyKind == ZombieModeEnemyKind.Elite &&
                marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander))
            {
                ZombieModeCommanderAuraRuntime commanderAura = enemy.gameObject.GetComponent<ZombieModeCommanderAuraRuntime>();
                if (commanderAura == null)
                {
                    commanderAura = enemy.gameObject.AddComponent<ZombieModeCommanderAuraRuntime>();
                }

                commanderAura.Initialize(
                    marker.RunId,
                    ZombieModeTuning.CommanderAffixAuraRadius,
                    ZombieModeTuning.CommanderAuraTickIntervalSeconds);
            }
        }

        private float GetZombieModeSpecialCooldown(ZombieModeSpecialKind kind)
        {
            switch (kind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    return ZombieModeTuning.SprinterCooldownSeconds;
                case ZombieModeSpecialKind.Exploder:
                    return ZombieModeTuning.ExploderCooldownSeconds;
                case ZombieModeSpecialKind.Plague:
                    return ZombieModeTuning.PoisonCooldownSeconds;
                case ZombieModeSpecialKind.Summoner:
                    return ZombieModeTuning.SummonerCooldownSeconds;
                case ZombieModeSpecialKind.Harasser:
                    return ZombieModeTuning.HarasserCooldownSeconds;
                default:
                    return ZombieModeTuning.ExploderCooldownSeconds;
            }
        }

        internal void TryExecuteZombieModeEnemyRuntimeSkill(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null ||
                !IsZombieModeRunValid(marker.RunId) ||
                zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat ||
                ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase) ||
                marker.RecycledForPerformance ||
                marker.DeathSettled)
            {
                return;
            }

            CharacterMainControl character = marker.GetComponent<CharacterMainControl>();
            CharacterMainControl player = CharacterMainControl.Main;
            if (character == null || player == null)
            {
                return;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                TryExecuteZombieModeSpecialSkill(marker.RunId, character, marker, player);
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                TryExecuteZombieModeEliteSkill(marker.RunId, character, marker, player);
            }
        }

        private void TryExecuteZombieModeSpecialSkill(
            int runId,
            CharacterMainControl character,
            ZombieModeEnemyRuntimeMarker marker,
            CharacterMainControl player)
        {
            switch (marker.SpecialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    character.PopText(L10n.T("BossRush_ZombieMode_Special_Sprinter"));
                    Vector3 dashTarget = Vector3.MoveTowards(
                        character.transform.position,
                        player.transform.position,
                        ZombieModeTuning.SprinterDashDistance);
                    dashTarget.y = character.transform.position.y;
                    character.transform.position = dashTarget;
                    break;
                case ZombieModeSpecialKind.Exploder:
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        character,
                        character.transform.position,
                        ZombieModeTuning.ExploderDeathRadius,
                        ZombieModeTuning.ExploderDeathDamage,
                        ZombieModeTuning.ExploderDetonationDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Exploder"));
                    break;
                case ZombieModeSpecialKind.Plague:
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        character,
                        character.transform.position,
                        ZombieModeTuning.PlagueCloudRadius,
                        ZombieModeTuning.PlagueCloudDamagePerSecond * ZombieModeTuning.PlagueCloudDurationSeconds,
                        ZombieModeTuning.ThreatTelegraphDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Plague"));
                    break;
                case ZombieModeSpecialKind.Summoner:
                    character.PopText(L10n.T("BossRush_ZombieMode_Special_Summoner"));
                    if (zombieModeRunState.PerformanceTier < ZombieModePerformanceTier.SoftProtect)
                    {
                        for (int i = 0; i < ZombieModeTuning.SummonerSpawnCount; i++)
                        {
                            Vector3 offset = Quaternion.Euler(0f, 360f * i / ZombieModeTuning.SummonerSpawnCount, 0f) * Vector3.forward * 1.5f;
                            SpawnZombieModeSmallSplitAsync(runId, character.transform.position + offset).Forget();
                        }
                    }
                    break;
                case ZombieModeSpecialKind.Harasser:
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        character,
                        player.transform.position,
                        3.5f,
                        ZombieModeTuning.HarasserProjectileDamage,
                        ZombieModeTuning.ThreatTelegraphDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Harasser"));
                    break;
            }
        }

        private void TryExecuteZombieModeEliteSkill(
            int runId,
            CharacterMainControl character,
            ZombieModeEnemyRuntimeMarker marker,
            CharacterMainControl player)
        {
            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Commander"));
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.ToxicAura) ||
                marker.EliteAffixes.Contains(ZombieModeEliteAffix.Plague))
            {
                StartZombieModeTelegraphedAreaDamage(
                    runId,
                    character,
                    character.transform.position,
                    5.5f,
                    26f,
                    ZombieModeTuning.ThreatTelegraphDelaySeconds,
                    L10n.T("BossRush_ZombieMode_Affix_ToxicAura"));
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Shielded))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Shielded"));
                if (character.Health != null && character.Health.CurrentHealth > 0f)
                {
                    float shieldAmount = Mathf.Max(1f, character.Health.MaxHealth * ZombieModeTuning.ShieldedAffixShieldPercent);
                    ZombieModeShieldedAffixRuntime shield = marker.ShieldedAffix;
                    if (shield == null)
                    {
                        shield = character.gameObject.AddComponent<ZombieModeShieldedAffixRuntime>();
                    }
                    shield.ActivateShield(marker.RunId, shieldAmount, ZombieModeTuning.ShieldedAffixDurationSeconds);
                    marker.ShieldedAffix = shield;
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Adaptive))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
            }
        }

        private void ApplyZombieModeCommanderPulse(int runId, CharacterMainControl commander)
        {
            RefreshZombieModeCommanderAuraTargets(runId, commander, ZombieModeTuning.CommanderAffixAuraRadius, null);
        }

        // Commander Aura tick scratch HashSet：复用避免每次 0.5 秒 tick 都 new。
        // 仅在 trackedTargets != null 时使用；调用方负责调用前 Clear()。审查 §3.6。
        private readonly HashSet<int> commanderAuraTargetsScratch = new HashSet<int>();

        internal void RefreshZombieModeCommanderAuraTargets(
            int runId,
            CharacterMainControl commander,
            float radius,
            Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets)
        {
            if (!IsZombieModeRunValid(runId) || commander == null || commander.gameObject == null)
            {
                return;
            }

            float radiusSqr = radius * radius;
            int sourceId = commander.gameObject.GetInstanceID();
            HashSet<int> currentTargets = null;
            if (trackedTargets != null)
            {
                commanderAuraTargetsScratch.Clear();
                currentTargets = commanderAuraTargetsScratch;
            }
            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    record.Kind != ZombieModeRunOnlyObjectKind.Enemy ||
                    record.GameObject == null ||
                    record.GameObject == commander.gameObject)
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker target = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                if (target == null ||
                    target.RunId != runId ||
                    target.IsBoss ||
                    target.EnemyKind != ZombieModeEnemyKind.Normal ||
                    target.RecycledForPerformance ||
                    target.DeathSettled)
                {
                    continue;
                }

                Vector3 delta = target.transform.position - commander.transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                CharacterMainControl targetCharacter = target.GetComponent<CharacterMainControl>();
                if (targetCharacter == null ||
                    targetCharacter.Health == null ||
                    targetCharacter.Health.CurrentHealth <= 0f)
                {
                    continue;
                }

                int targetId = targetCharacter.gameObject.GetInstanceID();
                if (currentTargets != null)
                {
                    currentTargets.Add(targetId);
                }

                ZombieModeCommanderAuraTargetRuntime targetRuntime = targetCharacter.gameObject.GetComponent<ZombieModeCommanderAuraTargetRuntime>();
                if (targetRuntime == null)
                {
                    targetRuntime = targetCharacter.gameObject.AddComponent<ZombieModeCommanderAuraTargetRuntime>();
                }

                targetRuntime.ApplySource(runId, sourceId);
                if (trackedTargets != null)
                {
                    trackedTargets[targetId] = targetRuntime;
                }
            }

            if (trackedTargets == null)
            {
                return;
            }

            List<int> staleTargetIds = null;
            foreach (KeyValuePair<int, ZombieModeCommanderAuraTargetRuntime> entry in trackedTargets)
            {
                if (currentTargets.Contains(entry.Key))
                {
                    continue;
                }

                if (entry.Value != null)
                {
                    entry.Value.RemoveSource(sourceId);
                }

                if (staleTargetIds == null)
                {
                    staleTargetIds = new List<int>();
                }

                staleTargetIds.Add(entry.Key);
            }

            if (staleTargetIds == null)
            {
                return;
            }

            for (int i = 0; i < staleTargetIds.Count; i++)
            {
                trackedTargets.Remove(staleTargetIds[i]);
            }
        }

        private void StartZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            float delay,
            string label)
        {
            if (!IsZombieModeRunValid(runId) || ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase))
            {
                return;
            }

            if (source != null && !string.IsNullOrEmpty(label))
            {
                source.PopText(label);
            }

            // 共享 disk mesh 替代 CreatePrimitive(Cylinder)（审查 §3.3）。
            GameObject telegraph = CreateZombieModeFlatZoneVisual(
                "ZombieMode_Telegraph",
                origin + Vector3.up * 0.03f,
                radius,
                0.02f,
                new Color(1f, 0.16f, 0.08f, 0.35f));

            ZombieModeTelegraphedAreaDamageRuntime runtime = telegraph.AddComponent<ZombieModeTelegraphedAreaDamageRuntime>();
            runtime.Initialize(runId, source, origin, radius, damage, delay);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, telegraph, runtime, null);
        }

        public void TryExecuteZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage)
        {
            if (IsZombieModeRunValid(runId) &&
                !ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase))
            {
                // 起手 telegraph 完成时走 ExplosionManager 路径（审查 §4.2）：
                // 玩家与丧尸都按 team 命中，自动尊重墙体阻挡 / VFX / 屏幕震动；
                // source 为空时 helper 内部回退为 player-only 实现保持原行为。
                DealZombieModeExplosionAreaDamage(runId, source, origin, radius, damage);
            }
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            DealZombieModeAreaDamageToPlayer(runId, null, origin, radius, damage);
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.mainDamageReceiver == null)
            {
                return;
            }

            Vector3 delta = player.transform.position - origin;
            if (delta.sqrMagnitude > radius * radius)
            {
                return;
            }

            CharacterMainControl damageSource = source != null ? source : player;
            DamageInfo damageInfo = new DamageInfo(damageSource);
            damageInfo.damageType = DamageTypes.normal;
            damageInfo.damageValue = damage;
            damageInfo.damagePoint = player.transform.position;
            damageInfo.damageNormal = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.up;
            damageInfo.isFromBuffOrEffect = source == null;

            DamageReceiver receiver = player.mainDamageReceiver;
            receiver.Hurt(damageInfo);
        }

        // ====================================================================
        // ExplosionManager.CreateExplosion 接入（审查 §4.2）
        // ====================================================================
        // DealZombieModeAreaDamageToPlayer 是 mod 自实现的"只对主角伤害"路径，
        // 跳过墙体 raycast，无 VFX，无屏幕震动。当 telegraph 起手或 Boss 死亡爆炸
        // 想要原生效果（屏幕震动 / 标准爆炸 VFX / 障碍物挡墙）时，调用此方法走源码。
        //
        // CreateExplosion 按 team 命中所有敌对单位 — Boss source.Team 为 wolf/scav，
        // wolf 不与 scav 互伤、不与自己互伤；玩家是 player，会被命中。
        // 兜底：源码 API 不可用时退回 player-only 实现。
        // ====================================================================
        public void DealZombieModeExplosionAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            bool canHurtSelf = false)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            try
            {
                if (source != null &&
                    LevelManager.Instance != null &&
                    LevelManager.Instance.ExplosionManager != null)
                {
                    DamageInfo dmgInfo = new DamageInfo(source);
                    dmgInfo.damageValue = damage;
                    dmgInfo.isExplosion = true;

                    LevelManager.Instance.ExplosionManager.CreateExplosion(
                        origin,
                        radius,
                        dmgInfo,
                        ExplosionFxTypes.normal,
                        0.5f,
                        canHurtSelf);
                    return;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] ExplosionManager 调用失败，回退 player-only 路径: " + e.Message);
            }

            // 兜底：源码 API 不可用 / source 为空时仍走 player-only 实现，
            // 与原行为一致避免技能完全失效。
            DealZombieModeAreaDamageToPlayer(runId, source, origin, radius, damage);
        }
    }

    public sealed class ZombieModeTelegraphedAreaDamageRuntime : ZombieModeTimedRunScopedRuntime
    {
        private CharacterMainControl source;
        private Vector3 origin;
        private float radius;
        private float damage;
        private float triggerTime;
        private bool triggered;

        public void Initialize(
            int newRunId,
            CharacterMainControl newSource,
            Vector3 newOrigin,
            float newRadius,
            float newDamage,
            float delay)
        {
            source = newSource;
            origin = newOrigin;
            radius = newRadius;
            damage = newDamage;
            triggerTime = Time.unscaledTime + Mathf.Max(0.05f, delay);
            triggered = false;
            InitializeTimedRuntime(newRunId, Mathf.Max(0.05f, delay) + 0.1f);
        }

        protected override void TickRuntime(ModBehaviour inst)
        {
            if (triggered || Time.unscaledTime < triggerTime)
            {
                return;
            }

            triggered = true;
            inst.TryExecuteZombieModeTelegraphedAreaDamage(RuntimeRunId, source, origin, radius, damage);
            Destroy(gameObject);
        }
    }

    public sealed class ZombieModeThreatRuntime : MonoBehaviour
    {
        private int runId;
        private float cooldown;
        private float nextSkillTime;
        private ZombieModeEnemyRuntimeMarker marker;
        private ModBehaviour owner;

        public void Initialize(int newRunId, float newCooldown)
        {
            runId = newRunId;
            cooldown = Mathf.Max(1f, newCooldown);
            nextSkillTime = Time.unscaledTime + UnityEngine.Random.Range(1.5f, 4f);
            marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            owner = ModBehaviour.Instance;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSkillTime)
            {
                return;
            }

            nextSkillTime = Time.unscaledTime + cooldown + UnityEngine.Random.Range(0f, 2f);
            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                return;
            }

            if (marker == null)
            {
                marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            inst.TryExecuteZombieModeEnemyRuntimeSkill(marker);
        }
    }

    public sealed class ZombieModeCommanderAuraRuntime : MonoBehaviour
    {
        private int runId;
        private float radius;
        private float tickInterval;
        private float nextTickTime;
        private CharacterMainControl owner;
        private ZombieModeEnemyRuntimeMarker marker;
        private readonly Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets =
            new Dictionary<int, ZombieModeCommanderAuraTargetRuntime>();

        public void Initialize(int newRunId, float newRadius, float newTickInterval)
        {
            runId = newRunId;
            radius = Mathf.Max(0.5f, newRadius);
            tickInterval = Mathf.Max(0.2f, newTickInterval);
            nextTickTime = 0f;
            owner = GetComponent<CharacterMainControl>();
            marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                ClearTargets();
                Destroy(this);
                return;
            }

            if (owner == null)
            {
                owner = GetComponent<CharacterMainControl>();
            }

            if (marker == null)
            {
                marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            if (owner == null ||
                marker == null ||
                marker.RunId != runId ||
                marker.DeathSettled ||
                marker.RecycledForPerformance ||
                !marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander) ||
                owner.Health == null ||
                owner.Health.CurrentHealth <= 0f)
            {
                ClearTargets();
                Destroy(this);
                return;
            }

            if (Time.unscaledTime < nextTickTime)
            {
                return;
            }

            nextTickTime = Time.unscaledTime + tickInterval;
            inst.RefreshZombieModeCommanderAuraTargets(runId, owner, radius, trackedTargets);
        }

        private void OnDisable()
        {
            ClearTargets();
        }

        private void OnDestroy()
        {
            ClearTargets();
        }

        private void ClearTargets()
        {
            int sourceId = gameObject != null ? gameObject.GetInstanceID() : 0;
            if (trackedTargets.Count <= 0)
            {
                return;
            }

            foreach (KeyValuePair<int, ZombieModeCommanderAuraTargetRuntime> entry in trackedTargets)
            {
                if (entry.Value != null)
                {
                    entry.Value.RemoveSource(sourceId);
                }
            }

            trackedTargets.Clear();
        }
    }

    public sealed class ZombieModeCommanderAuraTargetRuntime : MonoBehaviour
    {
        private int runId;
        private Stat walkSpeedStat;
        private Stat runSpeedStat;
        private Stat meleeDamageStat;
        private Stat gunDamageStat;
        private Modifier walkSpeedModifier;
        private Modifier runSpeedModifier;
        private Modifier meleeDamageModifier;
        private Modifier gunDamageModifier;
        private readonly HashSet<int> sourceIds = new HashSet<int>();

        public void ApplySource(int newRunId, int sourceId)
        {
            if (sourceId == 0)
            {
                return;
            }

            runId = newRunId;
            sourceIds.Add(sourceId);
            EnsureModifiers();
        }

        public void RemoveSource(int sourceId)
        {
            if (sourceId != 0)
            {
                sourceIds.Remove(sourceId);
            }

            if (sourceIds.Count > 0)
            {
                return;
            }

            ReleaseModifiers();
            Destroy(this);
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (inst == null ||
                inst.ZombieModeCurrentRunId != runId ||
                character == null ||
                character.CharacterItem == null ||
                character.Health == null ||
                character.Health.CurrentHealth <= 0f ||
                sourceIds.Count <= 0)
            {
                ReleaseModifiers();
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            ReleaseModifiers();
        }

        private void EnsureModifiers()
        {
            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (character == null || character.CharacterItem == null)
            {
                return;
            }

            EnsureModifier(character.CharacterItem, "WalkSpeed", ref walkSpeedStat, ref walkSpeedModifier, ZombieModeTuning.CommanderAffixMoveSpeedBonus);
            EnsureModifier(character.CharacterItem, "RunSpeed", ref runSpeedStat, ref runSpeedModifier, ZombieModeTuning.CommanderAffixMoveSpeedBonus);
            EnsureModifier(character.CharacterItem, "MeleeDamageMultiplier", ref meleeDamageStat, ref meleeDamageModifier, ZombieModeTuning.CommanderAffixDamageBonus);
            EnsureModifier(character.CharacterItem, "GunDamageMultiplier", ref gunDamageStat, ref gunDamageModifier, ZombieModeTuning.CommanderAffixDamageBonus);
        }

        private void EnsureModifier(ItemStatsSystem.Item characterItem, string statName, ref Stat stat, ref Modifier modifier, float value)
        {
            if (modifier != null)
            {
                return;
            }

            stat = characterItem.GetStat(statName);
            if (stat == null)
            {
                return;
            }

            modifier = new Modifier(ModifierType.PercentageAdd, value, this);
            stat.AddModifier(modifier);
        }

        private void ReleaseModifiers()
        {
            RemoveModifier(ref walkSpeedStat, ref walkSpeedModifier);
            RemoveModifier(ref runSpeedStat, ref runSpeedModifier);
            RemoveModifier(ref meleeDamageStat, ref meleeDamageModifier);
            RemoveModifier(ref gunDamageStat, ref gunDamageModifier);
            sourceIds.Clear();
        }

        private void RemoveModifier(ref Stat stat, ref Modifier modifier)
        {
            if (stat != null && modifier != null)
            {
                stat.RemoveModifier(modifier);
            }

            stat = null;
            modifier = null;
        }
    }

    public sealed class ZombieModeRegenerationAffixRuntime : MonoBehaviour
    {
        private int runId;
        private float nextTick;

        public void Initialize(int newRunId)
        {
            runId = newRunId;
            nextTick = Time.unscaledTime + 1f;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextTick)
            {
                return;
            }

            nextTick = Time.unscaledTime + 1f;
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                return;
            }

            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (character == null || character.Health == null || character.Health.CurrentHealth <= 0)
            {
                return;
            }

            float heal = Mathf.Max(1f, character.Health.MaxHealth * 0.025f);
            character.Health.SetHealth(Mathf.Min(character.Health.MaxHealth, character.Health.CurrentHealth + heal));
        }
    }

    public sealed class ZombieModeShieldedAffixRuntime : MonoBehaviour
    {
        private int runId;
        private float shieldRemaining;
        private float shieldEndTime;
        private bool shieldActive;

        public void ActivateShield(int newRunId, float amount, float duration)
        {
            runId = newRunId;
            shieldRemaining = amount;
            shieldEndTime = Time.unscaledTime + duration;
            shieldActive = true;
        }

        private void Update()
        {
            if (!shieldActive)
            {
                return;
            }

            if (Time.unscaledTime >= shieldEndTime)
            {
                shieldActive = false;
                shieldRemaining = 0f;
                return;
            }

            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                shieldActive = false;
                return;
            }
        }

        public bool AbsorbDamage(ref float damageValue)
        {
            if (!shieldActive || shieldRemaining <= 0f)
            {
                return false;
            }

            if (damageValue <= shieldRemaining)
            {
                shieldRemaining -= damageValue;
                damageValue = 0f;
            }
            else
            {
                damageValue -= shieldRemaining;
                shieldRemaining = 0f;
                shieldActive = false;
            }
            return true;
        }
    }
}
