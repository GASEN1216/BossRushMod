// Mode H 静态内容目录的逐文件解析实现（设计提案 §23.2）。
// 与 ModeHContentCatalog.cs 同属一个 partial 类：那边负责加载、签名核对与只读访问，
// 这里只放各数据文件的解析与审计，拆分只为遵守仓库单文件 1200 行预算。
using System;
using System.Collections.Generic;

namespace BossRush
{
    public static partial class ModeHContentCatalog
    {
        #region 解析：BossProfiles

        private static bool ParseBossProfiles(ModeHJsonValue root)
        {
            List<ModeHJsonValue> items;
            if (!root.TryGetArray("profileTemplates", out items) || items.Count == 0)
            {
                _lastError = "boss_profiles_empty";
                return false;
            }

            List<ModeHProfileTemplate> templates = new List<ModeHProfileTemplate>(items.Count);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> orders = new HashSet<int>();

            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "boss_profile_not_object";
                    return false;
                }
                ModeHProfileTemplate t = new ModeHProfileTemplate();
                if (!item.TryGetString("profileTemplateId", out t.ProfileTemplateId)
                    || !ModeHStateModel.IsValidStableId(t.ProfileTemplateId))
                {
                    _lastError = "boss_profile_id_invalid";
                    return false;
                }
                if (!ids.Add(t.ProfileTemplateId))
                {
                    _lastError = "boss_profile_id_duplicate:" + t.ProfileTemplateId;
                    return false;
                }
                if (!item.TryGetString("stableKey", out t.StableKey) || string.IsNullOrEmpty(t.StableKey))
                {
                    _lastError = "boss_profile_stable_key_missing:" + t.ProfileTemplateId;
                    return false;
                }
                if (!keys.Add(t.StableKey))
                {
                    _lastError = "boss_profile_stable_key_duplicate:" + t.StableKey;
                    return false;
                }
                item.TryGetString("displayNameKey", out t.DisplayNameKey);
                item.TryGetString("rumorKey", out t.RumorKey);
                item.TryGetString("archetypeId", out t.ArchetypeId);
                item.TryGetString("temperamentId", out t.TemperamentId);
                item.TryGetString("quirkId", out t.QuirkId);
                item.TryGetString("anomalyId", out t.AnomalyId);
                item.TryGetString("signatureCommandId", out t.SignatureCommandId);
                item.TryGetString("standInPatternId", out t.StandInPatternId);
                item.TryGetBool("productionCandidate", out t.ProductionCandidate);
                item.TryGetInt("productionOrder", out t.ProductionOrder);
                item.TryGetInt("threatScore", out t.ThreatScore);
                item.TryGetStringList("capabilityTags", out t.CapabilityTags);

                if (!IsKnown(ModeHStableIds.AllArchetypes, t.ArchetypeId))
                {
                    _lastError = "boss_profile_archetype_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (!IsKnown(ModeHStableIds.AllTemperaments, t.TemperamentId))
                {
                    _lastError = "boss_profile_temperament_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                bool hasQuirk = !string.IsNullOrEmpty(t.QuirkId);
                bool hasAnomaly = !string.IsNullOrEmpty(t.AnomalyId);
                if (hasQuirk && hasAnomaly)
                {
                    _lastError = "boss_profile_quirk_anomaly_conflict:" + t.ProfileTemplateId;
                    return false;
                }
                if (hasQuirk && !IsKnown(ModeHStableIds.AllQuirks, t.QuirkId))
                {
                    _lastError = "boss_profile_quirk_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (hasAnomaly && !IsKnown(ModeHStableIds.AllAnomalies, t.AnomalyId))
                {
                    _lastError = "boss_profile_anomaly_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (!IsKnown(ModeHStableIds.AllSignatureCommands, t.SignatureCommandId))
                {
                    _lastError = "boss_profile_signature_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (!IsKnown(ModeHStableIds.AllStandInPatterns, t.StandInPatternId))
                {
                    _lastError = "boss_profile_standin_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (t.ThreatScore <= 0)
                {
                    _lastError = "boss_profile_threat_invalid:" + t.ProfileTemplateId;
                    return false;
                }
                if (t.ProductionCandidate)
                {
                    if (t.ProductionOrder <= 0 || !orders.Add(t.ProductionOrder))
                    {
                        _lastError = "boss_profile_order_invalid:" + t.ProfileTemplateId;
                        return false;
                    }
                }
                templates.Add(t);
            }

            List<string> excluded;
            root.TryGetStringList("excludedStableKeys", out excluded);
            _excludedStableKeys = excluded != null ? excluded : new List<string>();
            _profileTemplates = templates;
            return true;
        }

        #endregion

        #region 解析：Commands 与兼容矩阵

        private static bool ParseCommands(ModeHJsonValue root)
        {
            List<string> whitelist;
            if (!root.TryGetStringList("controlPointWhitelist", out whitelist) || whitelist.Count == 0)
            {
                _lastError = "commands_whitelist_missing";
                return false;
            }
            _controlPointWhitelist = whitelist;

            List<ModeHCommandSpec> commands = new List<ModeHCommandSpec>();
            if (!ParseCommandArray(root, "commonCommands", false, commands)) return false;
            if (!ParseCommandArray(root, "signatureCommands", true, commands)) return false;

            int commonCount = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                if (!commands[i].IsSignature) commonCount++;
            }
            if (commonCount != ModeHStableIds.AllCommonCommands.Length)
            {
                _lastError = "commands_common_count_mismatch";
                return false;
            }
            _commands = commands;
            return true;
        }

        private static bool ParseCommandArray(
            ModeHJsonValue root, string field, bool isSignature, List<ModeHCommandSpec> output)
        {
            List<ModeHJsonValue> items;
            if (!root.TryGetArray(field, out items) || items.Count == 0)
            {
                _lastError = "commands_section_missing:" + field;
                return false;
            }
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "command_not_object:" + field;
                    return false;
                }
                ModeHCommandSpec spec = new ModeHCommandSpec();
                spec.IsSignature = isSignature;
                if (!item.TryGetString("commandId", out spec.CommandId)
                    || !ModeHStateModel.IsValidStableId(spec.CommandId))
                {
                    _lastError = "command_id_invalid:" + field;
                    return false;
                }
                item.TryGetString("nameKey", out spec.NameKey);
                item.TryGetString("descKey", out spec.DescKey);
                item.TryGetString("intent", out spec.Intent);
                item.TryGetString("archetypeId", out spec.ArchetypeId);
                item.TryGetInt("requiresEnemyCountAtLeast", out spec.RequiresEnemyCountAtLeast);
                item.TryGetBool("requiresRelayEntered", out spec.RequiresRelayEntered);

                if (isSignature && !IsKnown(ModeHStableIds.AllArchetypes, spec.ArchetypeId))
                {
                    _lastError = "signature_command_archetype_unknown:" + spec.CommandId;
                    return false;
                }
                if (!isSignature && !IsKnown(ModeHStableIds.AllCommonCommands, spec.CommandId))
                {
                    _lastError = "common_command_unknown:" + spec.CommandId;
                    return false;
                }
                if (isSignature && !IsKnown(ModeHStableIds.AllSignatureCommands, spec.CommandId))
                {
                    _lastError = "signature_command_unknown:" + spec.CommandId;
                    return false;
                }

                if (!ParseEffects(item, "effects", spec.CommandId, out spec.Effects)) return false;
                output.Add(spec);
            }
            return true;
        }

        private static bool ParseEffects(
            ModeHJsonValue owner, string field, string ownerId, out List<ModeHEffectSpec> effects)
        {
            effects = new List<ModeHEffectSpec>();
            List<ModeHJsonValue> items;
            if (!owner.TryGetArray(field, out items) || items.Count == 0)
            {
                _lastError = "effects_missing:" + ownerId;
                return false;
            }
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "effect_not_object:" + ownerId;
                    return false;
                }
                ModeHEffectSpec effect = new ModeHEffectSpec();
                if (!item.TryGetString("effectId", out effect.EffectId) || string.IsNullOrEmpty(effect.EffectId))
                {
                    _lastError = "effect_id_missing:" + ownerId;
                    return false;
                }
                if (!item.TryGetString("controlPointId", out effect.ControlPointId)
                    || string.IsNullOrEmpty(effect.ControlPointId))
                {
                    _lastError = "effect_control_point_missing:" + effect.EffectId;
                    return false;
                }
                if (_controlPointWhitelist != null && !_controlPointWhitelist.Contains(effect.ControlPointId))
                {
                    _lastError = "effect_control_point_not_whitelisted:" + effect.EffectId;
                    return false;
                }
                if (!item.TryGetString("op", out effect.Op) || string.IsNullOrEmpty(effect.Op))
                {
                    _lastError = "effect_op_missing:" + effect.EffectId;
                    return false;
                }
                item.TryGetInt("multiplierMilli", out effect.MultiplierMilli);
                item.TryGetInt("capMilli", out effect.CapMilli);
                item.TryGetInt("valueMilli", out effect.ValueMilli);
                item.TryGetInt("addMilli", out effect.AddMilli);
                item.TryGetBool("boolValue", out effect.BoolValue);
                item.TryGetBool("selfSettled", out effect.SelfSettled);
                item.TryGetInt("windowSeconds", out effect.WindowSeconds);
                item.TryGetString("role", out effect.Role);
                item.TryGetString("appliesWhen", out effect.AppliesWhen);
                item.TryGetString("commandId", out effect.TargetCommandId);
                item.TryGetString("slot", out effect.TargetSlot);
                if (!item.TryGetBool("restore", out effect.Restore))
                {
                    // 默认还原；nextReleaseSkillTimeMarker 必须显式写 restore=false（§17.6.2）
                    effect.Restore = true;
                }
                if (string.Equals(effect.ControlPointId, "nextReleaseSkillTimeMarker", StringComparison.Ordinal)
                    && effect.Restore)
                {
                    _lastError = "effect_marker_must_not_restore:" + effect.EffectId;
                    return false;
                }
                effects.Add(effect);
            }
            return true;
        }

        private static bool ParseCommandCompatibility(ModeHJsonValue root)
        {
            List<string> selfSettled;
            root.TryGetStringList("selfSettledEffects", out selfSettled);
            _selfSettledEffectIds = selfSettled != null ? selfSettled : new List<string>();

            List<ModeHJsonValue> items;
            if (!root.TryGetArray("effectCatalog", out items) || items.Count == 0)
            {
                _lastError = "effect_catalog_empty";
                return false;
            }

            List<ModeHEffectSpec> catalog = new List<ModeHEffectSpec>(items.Count);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "effect_catalog_not_object";
                    return false;
                }
                ModeHEffectSpec entry = new ModeHEffectSpec();
                string commandId;
                if (!item.TryGetString("commandId", out commandId)
                    || !item.TryGetString("effectId", out entry.EffectId)
                    || !item.TryGetString("controlPointId", out entry.ControlPointId))
                {
                    _lastError = "effect_catalog_field_missing";
                    return false;
                }
                if (!seen.Add(entry.EffectId))
                {
                    _lastError = "effect_catalog_duplicate:" + entry.EffectId;
                    return false;
                }
                if (!entry.EffectId.StartsWith(commandId + ".", StringComparison.Ordinal))
                {
                    _lastError = "effect_catalog_id_shape:" + entry.EffectId;
                    return false;
                }
                entry.Op = "catalog";
                entry.SelfSettled = _selfSettledEffectIds.Contains(entry.EffectId);
                catalog.Add(entry);
            }

            // 目录必须覆盖 Commands.json 中的每一条 effect
            if (_commands != null)
            {
                for (int i = 0; i < _commands.Count; i++)
                {
                    List<ModeHEffectSpec> effects = _commands[i].Effects;
                    if (effects == null) continue;
                    for (int j = 0; j < effects.Count; j++)
                    {
                        if (!seen.Contains(effects[j].EffectId))
                        {
                            _lastError = "effect_catalog_missing_effect:" + effects[j].EffectId;
                            return false;
                        }
                    }
                }
            }

            _effectCatalog = catalog;
            return true;
        }

        #endregion

        #region 解析：LoadoutKits

        private static bool ParseLoadoutKits(ModeHJsonValue root)
        {
            List<ModeHJsonValue> items;
            if (!root.TryGetArray("kits", out items) || items.Count == 0)
            {
                _lastError = "kits_empty";
                return false;
            }
            List<ModeHKitSpec> kits = new List<ModeHKitSpec>(items.Count);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> starterOrders = new HashSet<int>();

            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "kit_not_object";
                    return false;
                }
                ModeHKitSpec kit = new ModeHKitSpec();
                if (!item.TryGetString("kitId", out kit.KitId) || !ModeHStateModel.IsValidStableId(kit.KitId))
                {
                    _lastError = "kit_id_invalid";
                    return false;
                }
                if (!ids.Add(kit.KitId))
                {
                    _lastError = "kit_id_duplicate:" + kit.KitId;
                    return false;
                }
                item.TryGetBool("isStarterKit", out kit.IsStarterKit);
                item.TryGetInt("starterOrder", out kit.StarterOrder);
                item.TryGetString("nameKey", out kit.NameKey);
                item.TryGetString("descKey", out kit.DescKey);
                item.TryGetString("replaceSlot", out kit.ReplaceSlot);
                item.TryGetInt("typeId", out kit.TypeId);
                item.TryGetStringList("resolveTags", out kit.ResolveTags);
                item.TryGetInt("resolveMinQuality", out kit.ResolveMinQuality);
                item.TryGetInt("resolveMaxQuality", out kit.ResolveMaxQuality);
                item.TryGetInt("resolveOrdinal", out kit.ResolveOrdinal);
                item.TryGetInt("gameQuality", out kit.GameQuality);
                item.TryGetInt("ammoTypeId", out kit.AmmoTypeId);
                item.TryGetInt("ammoCount", out kit.AmmoCount);
                item.TryGetBool("resolveAmmoByCaliber", out kit.ResolveAmmoByCaliber);
                item.TryGetStringList("compatibleArchetypeIds", out kit.CompatibleArchetypeIds);
                item.TryGetStringList("compatibleProfileIds", out kit.CompatibleProfileIds);
                item.TryGetStringList("publicTags", out kit.PublicTags);

                if (!IsKnown(ModeHStableIds.AllowedKitSlots, kit.ReplaceSlot))
                {
                    _lastError = "kit_slot_not_allowed:" + kit.KitId;
                    return false;
                }
                if (kit.GameQuality < ModeHConfig.MinGameQuality || kit.GameQuality > ModeHConfig.MaxGameQuality)
                {
                    _lastError = "kit_quality_out_of_range:" + kit.KitId;
                    return false;
                }
                bool hasPinnedType = kit.TypeId > 0;
                bool hasResolver = kit.ResolveTags != null && kit.ResolveTags.Count > 0;
                if (!hasPinnedType && !hasResolver)
                {
                    _lastError = "kit_type_unresolvable:" + kit.KitId;
                    return false;
                }
                if (hasResolver)
                {
                    if (kit.ResolveMinQuality < ModeHConfig.MinGameQuality
                        || kit.ResolveMaxQuality > ModeHConfig.MaxGameQuality
                        || kit.ResolveMinQuality > kit.ResolveMaxQuality)
                    {
                        _lastError = "kit_resolve_quality_invalid:" + kit.KitId;
                        return false;
                    }
                    if (kit.ResolveOrdinal < 0)
                    {
                        _lastError = "kit_resolve_ordinal_invalid:" + kit.KitId;
                        return false;
                    }
                }
                bool isWeaponSlot = string.Equals(kit.ReplaceSlot, "PrimaryWeapon", StringComparison.Ordinal)
                    || string.Equals(kit.ReplaceSlot, "SecondaryWeapon", StringComparison.Ordinal);
                if (isWeaponSlot && kit.AmmoCount <= 0 && !kit.ResolveAmmoByCaliber && kit.AmmoTypeId <= 0)
                {
                    _lastError = "kit_ammo_missing:" + kit.KitId;
                    return false;
                }
                if (kit.IsStarterKit)
                {
                    if (kit.StarterOrder <= 0 || !starterOrders.Add(kit.StarterOrder))
                    {
                        _lastError = "kit_starter_order_invalid:" + kit.KitId;
                        return false;
                    }
                }
                if (kit.CompatibleArchetypeIds == null) kit.CompatibleArchetypeIds = new List<string>();
                if (kit.CompatibleProfileIds == null) kit.CompatibleProfileIds = new List<string>();
                if (kit.PublicTags == null) kit.PublicTags = new List<string>();
                kits.Add(kit);
            }

            _kits = kits;
            return true;
        }

        #endregion

        #region 解析：Scars（伤病 + 战痕）

        private static bool ParseScars(ModeHJsonValue root)
        {
            List<ModeHJsonValue> injuryItems;
            if (!root.TryGetArray("injuries", out injuryItems)
                || injuryItems.Count != ModeHStableIds.AllInjuries.Length)
            {
                _lastError = "injuries_count_mismatch";
                return false;
            }
            List<ModeHInjurySpec> injuries = new List<ModeHInjurySpec>(injuryItems.Count);
            for (int i = 0; i < injuryItems.Count; i++)
            {
                ModeHJsonValue item = injuryItems[i];
                ModeHInjurySpec spec = new ModeHInjurySpec();
                if (item == null || !item.TryGetString("injuryId", out spec.InjuryId)
                    || !IsKnown(ModeHStableIds.AllInjuries, spec.InjuryId))
                {
                    _lastError = "injury_id_unknown";
                    return false;
                }
                item.TryGetString("nameKey", out spec.NameKey);
                item.TryGetString("descKey", out spec.DescKey);
                item.TryGetString("scope", out spec.Scope);
                item.TryGetInt("triggerHealthFractionMilli", out spec.TriggerHealthFractionMilli);
                item.TryGetInt("requiresEnemyCountAtLeast", out spec.RequiresEnemyCountAtLeast);
                if (!ParseEffects(item, "components", spec.InjuryId, out spec.Components)) return false;
                injuries.Add(spec);
            }

            List<ModeHJsonValue> scarItems;
            if (!root.TryGetArray("scars", out scarItems) || scarItems.Count != ModeHStableIds.AllScars.Length)
            {
                _lastError = "scars_count_mismatch";
                return false;
            }
            List<ModeHScarSpec> scars = new List<ModeHScarSpec>(scarItems.Count);
            for (int i = 0; i < scarItems.Count; i++)
            {
                ModeHJsonValue item = scarItems[i];
                ModeHScarSpec spec = new ModeHScarSpec();
                if (item == null || !item.TryGetString("scarId", out spec.ScarId)
                    || !IsKnown(ModeHStableIds.AllScars, spec.ScarId))
                {
                    _lastError = "scar_id_unknown";
                    return false;
                }
                item.TryGetString("nameKey", out spec.NameKey);
                item.TryGetString("descKey", out spec.DescKey);
                item.TryGetString("trigger", out spec.Trigger);
                item.TryGetInt("windowSeconds", out spec.WindowSeconds);
                item.TryGetStringList("compatibleArchetypeIds", out spec.CompatibleArchetypeIds);
                item.TryGetString("benefitTag", out spec.BenefitTag);
                item.TryGetString("costTag", out spec.CostTag);
                item.TryGetInt("benefitOdds", out spec.BenefitOdds);
                item.TryGetInt("costOdds", out spec.CostOdds);
                if (!ParseEffects(item, "components", spec.ScarId, out spec.Components)) return false;

                bool hasBenefit = false;
                bool hasCost = false;
                for (int j = 0; j < spec.Components.Count; j++)
                {
                    string role = spec.Components[j].Role;
                    if (string.Equals(role, "benefit", StringComparison.Ordinal)) hasBenefit = true;
                    else if (string.Equals(role, "cost", StringComparison.Ordinal)) hasCost = true;
                }
                if (!hasBenefit || !hasCost)
                {
                    _lastError = "scar_missing_benefit_or_cost:" + spec.ScarId;
                    return false;
                }
                if (spec.CompatibleArchetypeIds == null || spec.CompatibleArchetypeIds.Count == 0)
                {
                    _lastError = "scar_archetype_missing:" + spec.ScarId;
                    return false;
                }
                scars.Add(spec);
            }

            _injuries = injuries;
            _scars = scars;
            return true;
        }

        #endregion

        #region 解析：ThreatPlans

        private static bool ParseThreatPlans(ModeHJsonValue root)
        {
            List<ModeHJsonValue> corridorItems;
            if (!root.TryGetArray("matchCorridor", out corridorItems)
                || corridorItems.Count != ModeHConfig.SeasonMatchCount)
            {
                _lastError = "corridor_count_mismatch";
                return false;
            }
            List<ModeHMatchCorridor> corridors = new List<ModeHMatchCorridor>(corridorItems.Count);
            for (int i = 0; i < corridorItems.Count; i++)
            {
                ModeHJsonValue item = corridorItems[i];
                ModeHMatchCorridor c = new ModeHMatchCorridor();
                if (item == null
                    || !item.TryGetInt("matchIndex", out c.MatchIndex)
                    || !item.TryGetInt("threatBudget", out c.ThreatBudget)
                    || !item.TryGetInt("simultaneousCap", out c.SimultaneousCap)
                    || !item.TryGetInt("minFillPercent", out c.MinFillPercent)
                    || !item.TryGetStringList("skeletonIds", out c.SkeletonIds))
                {
                    _lastError = "corridor_field_missing";
                    return false;
                }
                if (c.MatchIndex != i + 1)
                {
                    _lastError = "corridor_order_mismatch";
                    return false;
                }
                if (c.ThreatBudget != ModeHConfig.GetThreatBudget(c.MatchIndex)
                    || c.SimultaneousCap != ModeHConfig.GetSimultaneousEnemyCap(c.MatchIndex))
                {
                    _lastError = "corridor_conflicts_with_config:" + c.MatchIndex;
                    return false;
                }
                corridors.Add(c);
            }

            List<ModeHJsonValue> skeletonItems;
            if (!root.TryGetArray("skeletons", out skeletonItems) || skeletonItems.Count == 0)
            {
                _lastError = "skeletons_empty";
                return false;
            }
            List<ModeHSkeletonSpec> skeletons = new List<ModeHSkeletonSpec>(skeletonItems.Count);
            HashSet<string> skeletonIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < skeletonItems.Count; i++)
            {
                ModeHJsonValue item = skeletonItems[i];
                ModeHSkeletonSpec s = new ModeHSkeletonSpec();
                if (item == null || !item.TryGetString("skeletonId", out s.SkeletonId)
                    || !skeletonIds.Add(s.SkeletonId))
                {
                    _lastError = "skeleton_id_invalid";
                    return false;
                }
                item.TryGetString("nameKey", out s.NameKey);
                item.TryGetInt("minUnits", out s.MinUnits);
                item.TryGetInt("maxUnits", out s.MaxUnits);
                item.TryGetStringList("publicTags", out s.PublicTags);
                item.TryGetBool("hasHighThreatCore", out s.HasHighThreatCore);
                item.TryGetInt("woundedUnits", out s.WoundedUnits);
                item.TryGetBool("requiresEchoReturn", out s.RequiresEchoReturn);
                if (s.MinUnits <= 0 || s.MaxUnits < s.MinUnits)
                {
                    _lastError = "skeleton_units_invalid:" + s.SkeletonId;
                    return false;
                }
                skeletons.Add(s);
            }
            for (int i = 0; i < corridors.Count; i++)
            {
                List<string> ids = corridors[i].SkeletonIds;
                for (int j = 0; j < ids.Count; j++)
                {
                    if (!skeletonIds.Contains(ids[j]))
                    {
                        _lastError = "corridor_skeleton_unknown:" + ids[j];
                        return false;
                    }
                }
            }

            List<ModeHJsonValue> entryItems;
            if (!root.TryGetArray("entryScripts", out entryItems) || entryItems.Count == 0)
            {
                _lastError = "entry_scripts_empty";
                return false;
            }
            List<ModeHEntryScriptSpec> entryScripts = new List<ModeHEntryScriptSpec>(entryItems.Count);
            for (int i = 0; i < entryItems.Count; i++)
            {
                ModeHJsonValue item = entryItems[i];
                ModeHEntryScriptSpec e = new ModeHEntryScriptSpec();
                if (item == null || !item.TryGetString("entryScriptId", out e.EntryScriptId))
                {
                    _lastError = "entry_script_id_missing";
                    return false;
                }
                item.TryGetString("nameKey", out e.NameKey);
                item.TryGetString("hintKey", out e.HintKey);
                item.TryGetStringList("publicTags", out e.PublicTags);
                item.TryGetBool("coreEntersLast", out e.CoreEntersLast);
                item.TryGetBool("hiddenSeat", out e.HiddenSeat);
                List<ModeHJsonValue> batch;
                if (!item.TryGetArray("batchPattern", out batch) || batch.Count == 0)
                {
                    _lastError = "entry_script_batch_missing:" + e.EntryScriptId;
                    return false;
                }
                e.BatchPattern = new List<int>(batch.Count);
                for (int j = 0; j < batch.Count; j++)
                {
                    if (batch[j] == null || batch[j].Kind != ModeHJsonKind.Integer || batch[j].IntegerValue <= 0)
                    {
                        _lastError = "entry_script_batch_invalid:" + e.EntryScriptId;
                        return false;
                    }
                    e.BatchPattern.Add((int)batch[j].IntegerValue);
                }
                entryScripts.Add(e);
            }

            List<ModeHJsonValue> conditionItems;
            if (!root.TryGetArray("arenaConditions", out conditionItems) || conditionItems.Count == 0)
            {
                _lastError = "arena_conditions_empty";
                return false;
            }
            List<ModeHArenaConditionSpec> conditions = new List<ModeHArenaConditionSpec>(conditionItems.Count);
            for (int i = 0; i < conditionItems.Count; i++)
            {
                ModeHJsonValue item = conditionItems[i];
                ModeHArenaConditionSpec c = new ModeHArenaConditionSpec();
                if (item == null || !item.TryGetString("conditionId", out c.ConditionId))
                {
                    _lastError = "arena_condition_id_missing";
                    return false;
                }
                item.TryGetString("nameKey", out c.NameKey);
                item.TryGetStringList("publicTags", out c.PublicTags);
                item.TryGetStringList("favoredArchetypeIds", out c.FavoredArchetypeIds);
                item.TryGetStringList("disfavoredArchetypeIds", out c.DisfavoredArchetypeIds);
                conditions.Add(c);
            }

            List<ModeHJsonValue> capabilityItems;
            if (!root.TryGetArray("archetypeCapabilityMatrix", out capabilityItems)
                || capabilityItems.Count != ModeHStableIds.AllArchetypes.Length)
            {
                _lastError = "archetype_capability_count_mismatch";
                return false;
            }
            List<ModeHArchetypeCapability> capabilities =
                new List<ModeHArchetypeCapability>(capabilityItems.Count);
            for (int i = 0; i < capabilityItems.Count; i++)
            {
                ModeHJsonValue item = capabilityItems[i];
                ModeHArchetypeCapability c = new ModeHArchetypeCapability();
                if (item == null || !item.TryGetString("archetypeId", out c.ArchetypeId)
                    || !IsKnown(ModeHStableIds.AllArchetypes, c.ArchetypeId))
                {
                    _lastError = "archetype_capability_unknown";
                    return false;
                }
                item.TryGetStringList("primaryAnswers", out c.PrimaryAnswers);
                item.TryGetStringList("hardLockedBy", out c.HardLockedBy);
                capabilities.Add(c);
            }

            List<ModeHJsonValue> synergyItems;
            if (!root.TryGetArray("synergyCategories", out synergyItems) || synergyItems.Count == 0)
            {
                _lastError = "synergy_categories_empty";
                return false;
            }
            List<ModeHSynergyCategory> synergies = new List<ModeHSynergyCategory>(synergyItems.Count);
            HashSet<string> synergyIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < synergyItems.Count; i++)
            {
                ModeHJsonValue item = synergyItems[i];
                ModeHSynergyCategory c = new ModeHSynergyCategory();
                if (item == null || !item.TryGetString("categoryId", out c.CategoryId)
                    || !synergyIds.Add(c.CategoryId)
                    || !item.TryGetString("publicTag", out c.PublicTag)
                    || !item.TryGetInt("budgetShare", out c.BudgetShare)
                    || c.BudgetShare <= 0)
                {
                    _lastError = "synergy_category_invalid";
                    return false;
                }
                synergies.Add(c);
            }

            List<ModeHJsonValue> reconItems;
            if (!root.TryGetArray("reconChoices", out reconItems)
                || reconItems.Count != ModeHStableIds.AllReconChoices.Length)
            {
                _lastError = "recon_choice_count_mismatch";
                return false;
            }
            List<ModeHReconChoiceSpec> recons = new List<ModeHReconChoiceSpec>(reconItems.Count);
            for (int i = 0; i < reconItems.Count; i++)
            {
                ModeHJsonValue item = reconItems[i];
                ModeHReconChoiceSpec c = new ModeHReconChoiceSpec();
                if (item == null || !item.TryGetString("reconChoiceId", out c.ReconChoiceId)
                    || !IsKnown(ModeHStableIds.AllReconChoices, c.ReconChoiceId)
                    || !item.TryGetString("revealField", out c.RevealField))
                {
                    _lastError = "recon_choice_invalid";
                    return false;
                }
                item.TryGetString("nameKey", out c.NameKey);
                recons.Add(c);
            }

            _corridors = corridors;
            _skeletons = skeletons;
            _entryScripts = entryScripts;
            _arenaConditions = conditions;
            _archetypeCapabilities = capabilities;
            _synergyCategories = synergies;
            _reconChoices = recons;
            return true;
        }

        #endregion

        #region 解析：OddsWeights（唯一允许同版本内置 fallback 的纯数值表）

        private static bool ParseOddsWeights(ModeHJsonValue root)
        {
            if (TryParseOddsWeightsCore(root))
            {
                _usedOddsFallback = false;
                return true;
            }
            // 纯数值权重允许同版本内置 fallback（§23.2）
            ApplyBuiltInOddsFallback();
            _usedOddsFallback = true;
            return true;
        }

        private static bool TryParseOddsWeightsCore(ModeHJsonValue root)
        {
            List<ModeHJsonValue> tierItems;
            if (!root.TryGetArray("oddsTiers", out tierItems) || tierItems.Count != 5) return false;
            List<ModeHOddsTier> tiers = new List<ModeHOddsTier>(5);
            for (int i = 0; i < tierItems.Count; i++)
            {
                ModeHJsonValue item = tierItems[i];
                ModeHOddsTier tier = new ModeHOddsTier();
                if (item == null
                    || !item.TryGetInt("odds", out tier.Odds)
                    || !item.TryGetInt("minPublicEdge", out tier.MinPublicEdge)
                    || !item.TryGetInt("maxPublicEdge", out tier.MaxPublicEdge))
                {
                    return false;
                }
                item.TryGetString("toneKey", out tier.ToneKey);
                if (tier.Odds != i + 1) return false;
                tiers.Add(tier);
            }

            ModeHJsonValue player;
            ModeHJsonValue enemy;
            if (!root.TryGetObject("playerWeights", out player)) return false;
            if (!root.TryGetObject("enemyWeights", out enemy)) return false;

            List<ModeHJsonValue> matrixItems;
            if (!root.TryGetArray("archetypeMatrix", out matrixItems) || matrixItems.Count == 0) return false;
            List<string> pairs = new List<string>(matrixItems.Count);
            for (int i = 0; i < matrixItems.Count; i++)
            {
                ModeHJsonValue item = matrixItems[i];
                string attacker;
                string defender;
                if (item == null || !item.TryGetString("attacker", out attacker)
                    || !item.TryGetString("defender", out defender))
                {
                    return false;
                }
                if (!IsKnown(ModeHStableIds.AllArchetypes, attacker)
                    || !IsKnown(ModeHStableIds.AllArchetypes, defender))
                {
                    return false;
                }
                pairs.Add(attacker + ">" + defender);
            }

            List<ModeHJsonValue> mapItems;
            if (!root.TryGetArray("commandTagMap", out mapItems)
                || mapItems.Count != ModeHStableIds.AllCommonCommands.Length)
            {
                return false;
            }
            List<ModeHCommandTagMapping> map = new List<ModeHCommandTagMapping>(mapItems.Count);
            for (int i = 0; i < mapItems.Count; i++)
            {
                ModeHJsonValue item = mapItems[i];
                ModeHCommandTagMapping m = new ModeHCommandTagMapping();
                if (item == null || !item.TryGetString("commandId", out m.CommandId)) return false;
                if (!IsKnown(ModeHStableIds.AllCommonCommands, m.CommandId)) return false;
                item.TryGetStringList("alignedTags", out m.AlignedTags);
                item.TryGetStringList("conflictedTags", out m.ConflictedTags);
                if (m.AlignedTags == null) m.AlignedTags = new List<string>();
                if (m.ConflictedTags == null) m.ConflictedTags = new List<string>();
                map.Add(m);
            }

            List<ModeHJsonValue> vectorItems;
            if (!root.TryGetArray("testVectors", out vectorItems) || vectorItems.Count < 3) return false;
            List<ModeHOddsTestVector> vectors = new List<ModeHOddsTestVector>(vectorItems.Count);
            for (int i = 0; i < vectorItems.Count; i++)
            {
                ModeHJsonValue item = vectorItems[i];
                ModeHOddsTestVector v = new ModeHOddsTestVector();
                if (item == null
                    || !item.TryGetString("vectorId", out v.VectorId)
                    || !item.TryGetInt("playerPublicScore", out v.PlayerPublicScore)
                    || !item.TryGetInt("enemyPublicScore", out v.EnemyPublicScore)
                    || !item.TryGetInt("expectedOdds", out v.ExpectedOdds))
                {
                    return false;
                }
                if (ModeHStateModel.ResolveOddsTier(v.PlayerPublicScore - v.EnemyPublicScore) != v.ExpectedOdds)
                {
                    return false;
                }
                vectors.Add(v);
            }

            _oddsTiers = tiers;
            _playerWeights = player;
            _enemyWeights = enemy;
            _archetypeMatrixPairs = pairs;
            _commandTagMap = map;
            _oddsTestVectors = vectors;
            return true;
        }

        private static void ApplyBuiltInOddsFallback()
        {
            _oddsTiers = new List<ModeHOddsTier>();
            _oddsTiers.Add(MakeTier(1, ModeHConfig.OddsThresholdX1MinEdge, 9999));
            _oddsTiers.Add(MakeTier(2, ModeHConfig.OddsThresholdX2MinEdge, ModeHConfig.OddsThresholdX1MinEdge - 1));
            _oddsTiers.Add(MakeTier(3, ModeHConfig.OddsThresholdX3MinEdge, ModeHConfig.OddsThresholdX2MinEdge - 1));
            _oddsTiers.Add(MakeTier(4, ModeHConfig.OddsThresholdX4MinEdge, ModeHConfig.OddsThresholdX3MinEdge - 1));
            _oddsTiers.Add(MakeTier(5, -9999, ModeHConfig.OddsThresholdX4MinEdge - 1));

            ModeHJsonValue player = ModeHJsonValue.NewObject();
            player.AddProperty("relayAvailable", ModeHJsonValue.NewInteger(5));
            player.AddProperty("relayEmpty", ModeHJsonValue.NewInteger(-12));
            player.AddProperty("starterCounters", ModeHJsonValue.NewInteger(8));
            player.AddProperty("starterCountered", ModeHJsonValue.NewInteger(-8));
            player.AddProperty("relayCounters", ModeHJsonValue.NewInteger(4));
            player.AddProperty("relayCountered", ModeHJsonValue.NewInteger(-4));
            player.AddProperty("kitQualityTotalCap", ModeHJsonValue.NewInteger(12));
            player.AddProperty("equipmentTagCounters", ModeHJsonValue.NewInteger(4));
            player.AddProperty("equipmentTagCountered", ModeHJsonValue.NewInteger(-4));
            player.AddProperty("starterInjured", ModeHJsonValue.NewInteger(-5));
            player.AddProperty("relayInjured", ModeHJsonValue.NewInteger(-3));
            player.AddProperty("anomalyBlood", ModeHJsonValue.NewInteger(-5));
            player.AddProperty("anomalyCrowd", ModeHJsonValue.NewInteger(-7));
            player.AddProperty("anomalyStrong", ModeHJsonValue.NewInteger(-4));
            player.AddProperty("anomalyError", ModeHJsonValue.NewInteger(-2));
            player.AddProperty("scarBenefit", ModeHJsonValue.NewInteger(3));
            player.AddProperty("scarCost", ModeHJsonValue.NewInteger(-3));
            player.AddProperty("scarTotalMin", ModeHJsonValue.NewInteger(-8));
            player.AddProperty("scarTotalMax", ModeHJsonValue.NewInteger(8));
            player.AddProperty("commandAligned", ModeHJsonValue.NewInteger(4));
            player.AddProperty("commandConflicted", ModeHJsonValue.NewInteger(-3));
            player.AddProperty("signatureCommandStarter", ModeHJsonValue.NewInteger(5));
            player.AddProperty("signatureCommandRelay", ModeHJsonValue.NewInteger(2));
            player.AddProperty("arenaFavorable", ModeHJsonValue.NewInteger(4));
            player.AddProperty("arenaUnfavorable", ModeHJsonValue.NewInteger(-4));
            ModeHJsonValue kitQuality = ModeHJsonValue.NewArray();
            int[] qualityScores = new int[] { 0, 1, 2, 3, 4, 5, 5, 5 };
            for (int i = 0; i < qualityScores.Length; i++)
            {
                kitQuality.Items.Add(ModeHJsonValue.NewInteger(qualityScores[i]));
            }
            player.AddProperty("kitQualityByGameQuality", kitQuality);
            _playerWeights = player;

            ModeHJsonValue enemy = ModeHJsonValue.NewObject();
            ModeHJsonValue stage = ModeHJsonValue.NewArray();
            int[] stageScores = new int[] { 0, 2, 5, 8, 12, 16 };
            for (int i = 0; i < stageScores.Length; i++)
            {
                stage.Items.Add(ModeHJsonValue.NewInteger(stageScores[i]));
            }
            enemy.AddProperty("stageByMatchIndex", stage);
            ModeHJsonValue counts = ModeHJsonValue.NewArray();
            int[] countScores = new int[] { 0, 4, 8 };
            for (int i = 0; i < countScores.Length; i++)
            {
                counts.Items.Add(ModeHJsonValue.NewInteger(countScores[i]));
            }
            enemy.AddProperty("countUpperBound", counts);
            enemy.AddProperty("highThreatCore", ModeHJsonValue.NewInteger(10));
            enemy.AddProperty("synergyPerCategory", ModeHJsonValue.NewInteger(5));
            enemy.AddProperty("synergyCap", ModeHJsonValue.NewInteger(10));
            enemy.AddProperty("woundedEnemy", ModeHJsonValue.NewInteger(-5));
            enemy.AddProperty("anomalyBlood", ModeHJsonValue.NewInteger(-5));
            enemy.AddProperty("anomalyCrowd", ModeHJsonValue.NewInteger(-7));
            enemy.AddProperty("anomalyStrong", ModeHJsonValue.NewInteger(-4));
            enemy.AddProperty("anomalyError", ModeHJsonValue.NewInteger(-2));
            _enemyWeights = enemy;

            _archetypeMatrixPairs = new List<string>();
            _archetypeMatrixPairs.Add("assault>ranged");
            _archetypeMatrixPairs.Add("ranged>sustain");
            _archetypeMatrixPairs.Add("sustain>tank");
            _archetypeMatrixPairs.Add("tank>assault");
            _archetypeMatrixPairs.Add("finisher>sustain");
            _archetypeMatrixPairs.Add("ranged>finisher");

            _commandTagMap = new List<ModeHCommandTagMapping>();
            AddFallbackMapping("steady", new string[] { "early_burst", "coward_pressure" },
                new string[] { "healer_core", "late_reinforcement" });
            AddFallbackMapping("press", new string[] { "healer_core", "slow_start" },
                new string[] { "attrition", "danger_edge" });
            AddFallbackMapping("center", new string[] { "danger_edge" }, new string[0]);
            AddFallbackMapping("spread", new string[] { "crowd", "crossfire" }, new string[] { "single_core" });
            AddFallbackMapping("finish", new string[] { "wounded_core", "reinforcement" },
                new string[] { "escort_screen" });
            AddFallbackMapping("hold", new string[] { "late_reinforcement" }, new string[] { "early_burst" });
            AddFallbackMapping("guard", new string[] { "early_burst", "crossfire" }, new string[] { "healer_core" });
            AddFallbackMapping("all_in", new string[] { "healer_core", "slow_start" },
                new string[] { "attrition", "danger_edge" });

            _oddsTestVectors = new List<ModeHOddsTestVector>();
            AddFallbackVector("public_edge_19_to_x2", 23, 4, 2);
            AddFallbackVector("public_edge_minus47_to_x5", -21, 26, 5);
            AddFallbackVector("public_edge_minus1_to_x3", 13, 14, 3);
        }

        private static ModeHOddsTier MakeTier(int odds, int minEdge, int maxEdge)
        {
            ModeHOddsTier tier = new ModeHOddsTier();
            tier.Odds = odds;
            tier.MinPublicEdge = minEdge;
            tier.MaxPublicEdge = maxEdge;
            tier.ToneKey = ModeHConfig.LocalizationKeyPrefix + "OddsTone_x" + odds.ToString();
            return tier;
        }

        private static void AddFallbackMapping(string commandId, string[] aligned, string[] conflicted)
        {
            ModeHCommandTagMapping m = new ModeHCommandTagMapping();
            m.CommandId = commandId;
            m.AlignedTags = new List<string>(aligned);
            m.ConflictedTags = new List<string>(conflicted);
            _commandTagMap.Add(m);
        }

        private static void AddFallbackVector(string id, int playerScore, int enemyScore, int expected)
        {
            ModeHOddsTestVector v = new ModeHOddsTestVector();
            v.VectorId = id;
            v.PlayerPublicScore = playerScore;
            v.EnemyPublicScore = enemyScore;
            v.ExpectedOdds = expected;
            _oddsTestVectors.Add(v);
        }

        #endregion
    }
}
