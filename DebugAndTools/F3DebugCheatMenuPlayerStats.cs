// ============================================================================
// F3DebugCheatMenu partial - extracted from F3DebugCheatMenu.cs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private void UpdateF3DebugCheatNavButtonColors()
        {
            foreach (KeyValuePair<F3DebugCheatPage, Image> entry in f3DebugCheatNavButtonImages)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                entry.Value.color = entry.Key == f3DebugCheatCurrentPage
                    ? new Color(0.30f, 0.45f, 0.68f, 1f)
                    : new Color(0.18f, 0.20f, 0.26f, 1f);
            }
        }

        private void RefreshF3DebugCheatSummary()
        {
            if (f3DebugCheatSummaryText == null)
            {
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            string subSceneId = string.Empty;
            try
            {
                subSceneId = MultiSceneCore.ActiveSubSceneID ?? string.Empty;
            }
            catch { }

            string playerPosText = "-";
            CharacterMainControl player;
            if (TryGetMainCharacter(out player))
            {
                Vector3 p = player.transform.position;
                playerPosText = string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", p.x, p.y, p.z);
            }

            string summary = L10n.T("DevMode: ", "DevMode: ") + DevModeEnabled
                + "    " + L10n.T("场景: ", "Scene: ") + sceneName
                + "    " + L10n.T("SubScene: ", "SubScene: ") + (string.IsNullOrEmpty(subSceneId) ? "-" : subSceneId)
                + "    " + L10n.T("坐标: ", "Pos: ") + playerPosText
                + "\n" + L10n.T("BossRush激活: ", "BossRush Active: ") + IsActive
                + "    ModeE: " + modeEActive
                + "    ModeF: " + modeFActive
                + "\n" + L10n.T("作弊状态: ", "Cheat State: ") + BuildPlayerCheatStateSummary()
                + "\n" + BuildCurrentPlayerStatsReadout();

            f3DebugCheatSummaryText.text = summary;
        }

        private string BuildPlayerCheatStateSummary()
        {
            List<string> parts = new List<string>();
            parts.Add("HP x" + f3DebugCheatPlayerState.maxHealthMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            parts.Add("Gun x" + f3DebugCheatPlayerState.gunDamageMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            parts.Add("Melee x" + f3DebugCheatPlayerState.meleeDamageMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            parts.Add("Head=" + (f3DebugCheatPlayerState.headArmorOverride.HasValue ? f3DebugCheatPlayerState.headArmorOverride.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-"));
            parts.Add("Body=" + (f3DebugCheatPlayerState.bodyArmorOverride.HasValue ? f3DebugCheatPlayerState.bodyArmorOverride.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-"));
            return string.Join(", ", parts);
        }

        private string BuildCurrentPlayerStatsReadout()
        {
            CharacterMainControl player;
            if (!TryGetMainCharacter(out player))
            {
                return L10n.T("当前玩家属性: 玩家未就绪", "Current player stats: player not ready");
            }

            float hp = ReadCharacterStatValue(player, "MaxHealth");
            float gun = ReadCharacterStatValue(player, "GunDamageMultiplier");
            float melee = ReadCharacterStatValue(player, "MeleeDamageMultiplier");
            float head = ReadArmorStatValue(player, "HeadArmor", true);
            float body = ReadArmorStatValue(player, "BodyArmor", false);

            return string.Format(CultureInfo.InvariantCulture,
                "{0}{1:0.##}    Gun={2:0.##}    Melee={3:0.##}    HeadArmor={4:0.##}    BodyArmor={5:0.##}",
                L10n.T("当前玩家属性: HP=", "Current Player Stats: HP="),
                hp,
                gun,
                melee,
                head,
                body);
        }

        private void SetF3DebugCheatStatus(string message, bool isError)
        {
            if (f3DebugCheatStatusText != null)
            {
                f3DebugCheatStatusText.text = message;
                f3DebugCheatStatusText.color = isError
                    ? new Color(1f, 0.65f, 0.65f, 1f)
                    : new Color(0.90f, 0.95f, 0.98f, 1f);
            }

            if (!string.IsNullOrEmpty(message))
            {
                try
                {
                    ShowMessage(message);
                }
                catch { }
            }
        }

        private bool TryGetMainCharacter(out CharacterMainControl main)
        {
            main = null;
            CharacterMainControl candidate = null;

            try
            {
                candidate = CharacterMainControl.Main;
            }
            catch { }

            if (IsMainCharacterForF3Debug(candidate))
            {
                main = candidate;
                return true;
            }

            try
            {
                candidate = playerCharacter as CharacterMainControl;
            }
            catch { }

            if (IsMainCharacterForF3Debug(candidate))
            {
                main = candidate;
                return true;
            }

            return false;
        }

        private static bool IsMainCharacterForF3Debug(CharacterMainControl candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            try
            {
                if (candidate == CharacterMainControl.Main)
                {
                    return true;
                }
            }
            catch { }

            try
            {
                return CharacterMainControlExtensions.IsMainCharacter(candidate);
            }
            catch { }

            return false;
        }

        private float ReadCharacterStatValue(CharacterMainControl player, string statName)
        {
            if (player == null || player.CharacterItem == null)
            {
                return -1f;
            }

            try
            {
                Stat stat = player.CharacterItem.GetStat(statName);
                if (stat != null)
                {
                    return stat.Value;
                }
            }
            catch { }

            return -1f;
        }

        private float ReadArmorStatValue(CharacterMainControl player, string statName, bool isHelmet)
        {
            if (player == null)
            {
                return -1f;
            }

            try
            {
                if (player.CharacterItem != null)
                {
                    Stat rootStat = player.CharacterItem.GetStat(statName);
                    if (rootStat != null)
                    {
                        return rootStat.Value;
                    }
                }
            }
            catch { }

            try
            {
                Item equipped = isHelmet ? player.GetHelmatItem() : player.GetArmorItem();
                if (equipped != null)
                {
                    Stat stat = equipped.GetStat(statName);
                    if (stat != null)
                    {
                        return stat.Value;
                    }
                }
            }
            catch { }

            return -1f;
        }

        private void ApplyAllPlayerCheatInputs()
        {
            float maxHealthMultiplier;
            if (!TryReadFloatInput(f3MaxHealthMultiplierInputField, 1f, out maxHealthMultiplier))
            {
                SetF3DebugCheatStatus(L10n.T("血量倍率输入无效", "Invalid health multiplier"), true);
                return;
            }

            float gunDamageMultiplier;
            if (!TryReadFloatInput(f3GunDamageMultiplierInputField, 1f, out gunDamageMultiplier))
            {
                SetF3DebugCheatStatus(L10n.T("枪械伤害倍率输入无效", "Invalid gun multiplier"), true);
                return;
            }

            float meleeDamageMultiplier;
            if (!TryReadFloatInput(f3MeleeDamageMultiplierInputField, 1f, out meleeDamageMultiplier))
            {
                SetF3DebugCheatStatus(L10n.T("近战伤害倍率输入无效", "Invalid melee multiplier"), true);
                return;
            }

            float? headArmorOverride;
            if (!TryReadOptionalFloatInput(f3HeadArmorInputField, out headArmorOverride))
            {
                SetF3DebugCheatStatus(L10n.T("头部护甲输入无效", "Invalid head armor override"), true);
                return;
            }

            float? bodyArmorOverride;
            if (!TryReadOptionalFloatInput(f3BodyArmorInputField, out bodyArmorOverride))
            {
                SetF3DebugCheatStatus(L10n.T("身体护甲输入无效", "Invalid body armor override"), true);
                return;
            }

            f3DebugCheatPlayerState.maxHealthMultiplier = F3DebugCheatMath.SanitizeMultiplier(maxHealthMultiplier);
            f3DebugCheatPlayerState.gunDamageMultiplier = F3DebugCheatMath.SanitizeMultiplier(gunDamageMultiplier);
            f3DebugCheatPlayerState.meleeDamageMultiplier = F3DebugCheatMath.SanitizeMultiplier(meleeDamageMultiplier);
            f3DebugCheatPlayerState.headArmorOverride = headArmorOverride;
            f3DebugCheatPlayerState.bodyArmorOverride = bodyArmorOverride;

            ApplyPlayerCheatParameters(false);
            RefreshF3DebugCheatSummary();
        }

        private void ApplyPlayerCheatParameters(bool silent)
        {
            RemovePlayerCheatRuntimeModifiers();

            CharacterMainControl player;
            if (!TryGetMainCharacter(out player))
            {
                QueuePlayerCheatApply("player_missing");
                if (!silent)
                {
                    SetF3DebugCheatStatus(L10n.T("玩家未就绪，参数已保存，稍后自动应用", "Player not ready. Values saved and will auto-apply later"), true);
                }
                return;
            }

            Item characterItem = player.CharacterItem;
            if (characterItem == null)
            {
                QueuePlayerCheatApply("character_item_missing");
                if (!silent)
                {
                    SetF3DebugCheatStatus(L10n.T("玩家 CharacterItem 未就绪，稍后自动应用", "Player CharacterItem not ready. Will retry later"), true);
                }
                return;
            }

            try
            {
                ApplyCharacterMultiplierModifier(characterItem, "MaxHealth", f3DebugCheatPlayerState.maxHealthMultiplier, ref f3DebugCheatRuntimeBindings.maxHealthStat, ref f3DebugCheatRuntimeBindings.maxHealthModifier);
                ApplyCharacterMultiplierModifier(characterItem, "GunDamageMultiplier", f3DebugCheatPlayerState.gunDamageMultiplier, ref f3DebugCheatRuntimeBindings.gunDamageStat, ref f3DebugCheatRuntimeBindings.gunDamageModifier);
                ApplyCharacterMultiplierModifier(characterItem, "MeleeDamageMultiplier", f3DebugCheatPlayerState.meleeDamageMultiplier, ref f3DebugCheatRuntimeBindings.meleeDamageStat, ref f3DebugCheatRuntimeBindings.meleeDamageModifier);

                ApplyArmorOverrideModifier(player, "HeadArmor", true, f3DebugCheatPlayerState.headArmorOverride, ref f3DebugCheatRuntimeBindings.headArmorStat, ref f3DebugCheatRuntimeBindings.headArmorModifier);
                ApplyArmorOverrideModifier(player, "BodyArmor", false, f3DebugCheatPlayerState.bodyArmorOverride, ref f3DebugCheatRuntimeBindings.bodyArmorStat, ref f3DebugCheatRuntimeBindings.bodyArmorModifier);

                if (player.Health != null)
                {
                    player.Health.SetHealth(player.Health.MaxHealth);
                }

                f3DebugCheatPlayerApplyPending = false;
                f3DebugCheatPlayerApplyReason = string.Empty;
                f3DebugCheatPlayerNextApplyTime = Time.unscaledTime + 1f;

                if (!silent)
                {
                    SetF3DebugCheatStatus(L10n.T("玩家参数已应用", "Player cheat parameters applied"), false);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 应用玩家作弊参数失败: " + e.Message);
                QueuePlayerCheatApply("apply_failed");
                if (!silent)
                {
                    SetF3DebugCheatStatus(L10n.T("应用玩家参数失败", "Failed to apply player parameters"), true);
                }
            }
        }

        private void ApplyCharacterMultiplierModifier(Item characterItem, string statName, float multiplier, ref Stat trackedStat, ref Modifier trackedModifier)
        {
            if (characterItem == null)
            {
                return;
            }

            Stat stat = null;
            try
            {
                stat = characterItem.GetStat(statName);
            }
            catch { }

            if (stat == null)
            {
                trackedStat = null;
                trackedModifier = null;
                return;
            }

            trackedStat = stat;
            if (Mathf.Abs(multiplier - 1f) <= 0.0001f)
            {
                trackedModifier = null;
                return;
            }

            float delta = F3DebugCheatMath.ComputeMultiplierAdditiveDelta(stat.BaseValue, multiplier);
            Modifier modifier = new Modifier(ModifierType.Add, delta, this);
            stat.AddModifier(modifier);
            trackedModifier = modifier;
        }

        private void ApplyArmorOverrideModifier(CharacterMainControl player, string statName, bool isHelmet, float? overrideValue, ref Stat trackedStat, ref Modifier trackedModifier)
        {
            trackedStat = null;
            trackedModifier = null;

            if (!overrideValue.HasValue)
            {
                return;
            }

            Stat stat;
            if (!TryResolveArmorTargetStat(player, statName, isHelmet, out stat))
            {
                QueuePlayerCheatApply(isHelmet ? "head_armor_missing" : "body_armor_missing");
                return;
            }

            trackedStat = stat;
            float delta = F3DebugCheatMath.ComputeAbsoluteAdditiveDelta(stat.Value, overrideValue.Value);
            Modifier modifier = new Modifier(ModifierType.Add, delta, this);
            stat.AddModifier(modifier);
            trackedModifier = modifier;
        }

        private bool TryResolveArmorTargetStat(CharacterMainControl player, string statName, bool isHelmet, out Stat stat)
        {
            stat = null;
            if (player == null)
            {
                return false;
            }

            try
            {
                if (player.CharacterItem != null)
                {
                    stat = player.CharacterItem.GetStat(statName);
                    if (stat != null)
                    {
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                Item equipped = isHelmet ? player.GetHelmatItem() : player.GetArmorItem();
                if (equipped != null)
                {
                    stat = equipped.GetStat(statName);
                    if (stat != null)
                    {
                        return true;
                    }

                    stat = EnsureRuntimeStatExists(equipped, statName, 0f);
                    if (stat != null)
                    {
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                if (player.CharacterItem != null)
                {
                    stat = EnsureRuntimeStatExists(player.CharacterItem, statName, 0f);
                    if (stat != null)
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private Stat EnsureRuntimeStatExists(Item item, string statName, float defaultValue)
        {
            if (item == null)
            {
                return null;
            }

            StatCollection stats = item.Stats;
            if (stats == null)
            {
                try
                {
                    item.CreateStatsComponent();
                    stats = item.Stats;
                }
                catch { }
            }

            if (stats == null)
            {
                return null;
            }

            Stat stat = null;
            try
            {
                stat = stats.GetStat(statName);
            }
            catch { }

            if (stat != null)
            {
                return stat;
            }

            try
            {
                stat = new Stat(statName, defaultValue, false);
                stats.Add(stat);
                return stat;
            }
            catch
            {
                return null;
            }
        }

        private void RemovePlayerCheatRuntimeModifiers()
        {
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.maxHealthStat, f3DebugCheatRuntimeBindings.maxHealthModifier);
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.gunDamageStat, f3DebugCheatRuntimeBindings.gunDamageModifier);
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.meleeDamageStat, f3DebugCheatRuntimeBindings.meleeDamageModifier);
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.headArmorStat, f3DebugCheatRuntimeBindings.headArmorModifier);
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.bodyArmorStat, f3DebugCheatRuntimeBindings.bodyArmorModifier);
            f3DebugCheatRuntimeBindings.Clear();
        }

        private void TryRemoveTrackedModifier(Stat stat, Modifier modifier)
        {
            if (stat == null || modifier == null)
            {
                return;
            }

            try
            {
                stat.RemoveModifier(modifier);
            }
            catch { }
        }

        private bool HasActiveF3PlayerCheatConfig()
        {
            return Mathf.Abs(f3DebugCheatPlayerState.maxHealthMultiplier - 1f) > 0.0001f
                || Mathf.Abs(f3DebugCheatPlayerState.gunDamageMultiplier - 1f) > 0.0001f
                || Mathf.Abs(f3DebugCheatPlayerState.meleeDamageMultiplier - 1f) > 0.0001f
                || f3DebugCheatPlayerState.headArmorOverride.HasValue
                || f3DebugCheatPlayerState.bodyArmorOverride.HasValue;
        }

        private void QueuePlayerCheatApply(string reason)
        {
            if (!HasActiveF3PlayerCheatConfig())
            {
                f3DebugCheatPlayerApplyPending = false;
                f3DebugCheatPlayerApplyReason = string.Empty;
                f3DebugCheatPlayerNextApplyTime = Time.unscaledTime + 1f;
                return;
            }

            f3DebugCheatPlayerApplyPending = true;
            f3DebugCheatPlayerApplyReason = reason ?? string.Empty;
            f3DebugCheatPlayerNextApplyTime = Time.unscaledTime + 1f;
        }

        private void ResetPlayerCheatConfigToDefaults()
        {
            RemovePlayerCheatRuntimeModifiers();
            f3DebugCheatPlayerState.Reset();
            f3DebugCheatPlayerApplyPending = false;
            f3DebugCheatPlayerApplyReason = string.Empty;
            f3DebugCheatPlayerNextApplyTime = Time.unscaledTime + 1f;

            if (f3MaxHealthMultiplierInputField != null) f3MaxHealthMultiplierInputField.text = "1";
            if (f3GunDamageMultiplierInputField != null) f3GunDamageMultiplierInputField.text = "1";
            if (f3MeleeDamageMultiplierInputField != null) f3MeleeDamageMultiplierInputField.text = "1";
            if (f3HeadArmorInputField != null) f3HeadArmorInputField.text = string.Empty;
            if (f3BodyArmorInputField != null) f3BodyArmorInputField.text = string.Empty;

            RefreshF3DebugCheatSummary();
            SetF3DebugCheatStatus(L10n.T("玩家作弊参数已恢复默认", "Player cheat parameters reset to default"), false);
        }

        private bool TryReadFloatInput(InputField field, float defaultValue, out float result)
        {
            result = defaultValue;
            if (field == null)
            {
                return true;
            }

            string text = field.text != null ? field.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                result = defaultValue;
                return true;
            }

            return TryParseFloat(text, out result);
        }

        private bool TryReadOptionalFloatInput(InputField field, out float? result)
        {
            result = null;
            if (field == null)
            {
                return true;
            }

            string text = field.text != null ? field.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            float value;
            if (!TryParseFloat(text, out value))
            {
                return false;
            }

            result = value;
            return true;
        }

        private bool TryParseFloat(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return true;
            }

            return false;
        }

        private void SetInputFieldText(InputField field, string text)
        {
            if (field != null)
            {
                field.text = text;
            }
        }

    }
}
