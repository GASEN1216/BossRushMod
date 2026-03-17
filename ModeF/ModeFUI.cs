using System;
using UnityEngine;
using HarmonyLib;
using TMPro;
using Duckov.UI;
using Duckov.UI.DialogueBubbles;
using BossRush.Utils;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F UI 与提示

        /// <summary>
        /// 广播当前阶段状态（每 15s 调用）
        /// </summary>
        private void BroadcastModeFPhaseStatus()
        {
            try
            {
                if (!modeFActive) return;

                string phaseName = GetModeFPhaseName(modeFState.CurrentPhase);
                float remaining = modeFState.PhaseDuration - modeFState.PhaseElapsed;

                if (modeFState.CurrentPhase == ModeFPhase.Extraction)
                {
                    ShowBigBanner(L10n.T(
                        "<color=green>" + phaseName + "</color> | 掉血率: 3%/s | 速速撤离！",
                        "<color=green>" + phaseName + "</color> | Bleed: 3%/s | Evacuate now!"
                    ));
                }
                else
                {
                    int remainSec = Mathf.CeilToInt(remaining);
                    ShowBigBanner(L10n.T(
                        phaseName + " | 剩余 <color=yellow>" + remainSec + "</color> 秒",
                        phaseName + " | <color=yellow>" + remainSec + "</color>s remaining"
                    ));
                }
            }
            catch { }
        }

        /// <summary>
        /// 广播榜首变化
        /// </summary>
        private void BroadcastModeFLeaderChange(CharacterMainControl newLeader, int marks)
        {
            try
            {
                string leaderName;
                if (newLeader == null)
                {
                    // 玩家是榜首
                    leaderName = GetModeFPlayerName();
                }
                else
                {
                    leaderName = newLeader.gameObject.name;
                    try
                    {
                        if (!string.IsNullOrEmpty(newLeader.CharacterItem.DisplayName))
                        {
                            leaderName = newLeader.CharacterItem.DisplayName;
                        }
                    }
                    catch { }
                }

                ShowBigBanner(L10n.T(
                    "<color=orange>" + leaderName + "</color> 成为悬赏榜首！印记: <color=yellow>" + marks + "</color>",
                    "<color=orange>" + leaderName + "</color> is now the Bounty Leader! Marks: <color=yellow>" + marks + "</color>"
                ));

                DevLog("[ModeF] 榜首切换: " + leaderName + " (marks=" + marks + ")");
            }
            catch { }
        }

        /// <summary>
        /// 显示奖励气泡
        /// </summary>
        private void ShowModeFRewardBubble(string text)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null) return;

                DialogueBubblesManager.Show(text, player.transform, 2.5f, false, false, -1f, 3f);
            }
            catch { }
        }

        /// <summary>
        /// 获取玩家显示名
        /// </summary>
        private string GetModeFPlayerName()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    try
                    {
                        if (player.CharacterItem != null && !string.IsNullOrEmpty(player.CharacterItem.DisplayName))
                        {
                            return player.CharacterItem.DisplayName;
                        }
                    }
                    catch { }

                    try
                    {
                        if (player.gameObject != null && !string.IsNullOrEmpty(player.gameObject.name))
                        {
                            return player.gameObject.name;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return L10n.T("我", "Me");
        }

        /// <summary>
        /// 获取 Boss 的印记后缀显示
        /// </summary>
        public string GetModeFBountyMarkSuffix(CharacterMainControl character)
        {
            try
            {
                if (!modeFActive || character == null) return null;

                int charId = character.GetInstanceID();
                int marks = 0;
                if (modeFState.BountyMarksByCharacterId.TryGetValue(charId, out marks) && marks > 0)
                {
                    return " [" + L10n.T("印记", "Mark") + ": " + marks + "]";
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 获取玩家的印记后缀显示
        /// </summary>
        public string GetModeFPlayerMarkSuffix()
        {
            if (!modeFActive || modeFState.PlayerBountyMarks <= 0) return null;
            return " [" + L10n.T("印记", "Mark") + ": " + modeFState.PlayerBountyMarks + "]";
        }

        #endregion
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private GameObject modeFPlayerNameTagObject;
        private TextMeshPro modeFPlayerNameTagText;
        /// <summary>M4: 缓存上次 NameTag 文本，避免每帧重建字符串</summary>
        private string modeFPlayerNameTagCachedText;
        private bool modeFPlayerNameTagTextDirty = true;
        private float modeFPlayerNameTagTextRefreshTimer = 0f;
        private const float MODEF_PLAYER_NAMETAG_TEXT_REFRESH_INTERVAL = 0.5f;

        private void MarkModeFPlayerNameTagDirty()
        {
            modeFPlayerNameTagTextDirty = true;
        }

        private void EnsureModeFPlayerNameTag()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.transform == null || modeFPlayerNameTagObject != null)
            {
                return;
            }

            NPCNameTagHelper.CreateNameTag(
                player.transform,
                "ModeFPlayerNameTag",
                BuildModeFPlayerNameTagText(),
                2.1f,
                out modeFPlayerNameTagObject,
                out modeFPlayerNameTagText,
                "[ModeF]");
            modeFPlayerNameTagTextDirty = true;
            modeFPlayerNameTagTextRefreshTimer = MODEF_PLAYER_NAMETAG_TEXT_REFRESH_INTERVAL;
        }

        private void UpdateModeFPlayerNameTag()
        {
            if (!modeFActive)
            {
                CleanupModeFPlayerNameTag();
                return;
            }

            EnsureModeFPlayerNameTag();
            if (modeFPlayerNameTagText != null)
            {
                modeFPlayerNameTagTextRefreshTimer += Time.unscaledDeltaTime;
                if (modeFPlayerNameTagTextDirty || modeFPlayerNameTagTextRefreshTimer >= MODEF_PLAYER_NAMETAG_TEXT_REFRESH_INTERVAL)
                {
                    modeFPlayerNameTagTextRefreshTimer = 0f;
                    modeFPlayerNameTagTextDirty = false;

                    string newText = BuildModeFPlayerNameTagText();
                    if (newText != modeFPlayerNameTagCachedText)
                    {
                        modeFPlayerNameTagCachedText = newText;
                        modeFPlayerNameTagText.text = newText;
                    }
                }
            }

            NPCNameTagHelper.UpdateNameTagRotation(modeFPlayerNameTagObject);
        }

        private string BuildModeFPlayerNameTagText()
        {
            string text = GetModeFPlayerName();
            string suffix = GetModeFPlayerMarkSuffix();
            if (!string.IsNullOrEmpty(suffix))
            {
                text += suffix;
            }
            return text;
        }

        private void CleanupModeFPlayerNameTag()
        {
            if (modeFPlayerNameTagObject != null)
            {
                UnityEngine.Object.Destroy(modeFPlayerNameTagObject);
            }

            modeFPlayerNameTagObject = null;
            modeFPlayerNameTagText = null;
            modeFPlayerNameTagCachedText = null;
            modeFPlayerNameTagTextDirty = true;
            modeFPlayerNameTagTextRefreshTimer = 0f;
        }
    }

    [HarmonyPatch(typeof(HealthBar), "RefreshCharacterIcon")]
    public static class ModeFHealthBarNamePatch
    {
        private static ModBehaviour cachedInstance;
        private static int lastRefreshFrame = -1;
        private const string ModeFMarkSuffixZhPrefix = " [印记: ";
        private const string ModeFMarkSuffixEnPrefix = " [Mark: ";

        [HarmonyPostfix]
        public static void Postfix(HealthBar __instance, TextMeshProUGUI ___nameText)
        {
            int currentFrame = Time.frameCount;
            if (cachedInstance == null || currentFrame - lastRefreshFrame >= 60)
            {
                lastRefreshFrame = currentFrame;
                cachedInstance = ModBehaviour.Instance;
            }

            if (cachedInstance == null || !cachedInstance.IsModeFActive || ___nameText == null || !___nameText.gameObject.activeSelf)
            {
                return;
            }

            Health target = __instance.target;
            if (target == null)
            {
                return;
            }

            CharacterMainControl character = target.TryGetCharacter();
            if (character == null)
            {
                return;
            }

            string suffix = character.IsMainCharacter
                ? cachedInstance.GetModeFPlayerMarkSuffix()
                : cachedInstance.GetModeFBountyMarkSuffix(character);

            string baseText = StripModeFMarkSuffix(___nameText.text);
            if (string.IsNullOrEmpty(suffix))
            {
                if (!string.Equals(___nameText.text, baseText, StringComparison.Ordinal))
                {
                    ___nameText.text = baseText;
                }

                return;
            }

            string desiredText = baseText + suffix;
            if (!string.Equals(___nameText.text, desiredText, StringComparison.Ordinal))
            {
                ___nameText.text = desiredText;
            }
        }

        private static string StripModeFMarkSuffix(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string sanitized = text;
            while (TryTrimTrailingModeFMarkSuffix(ref sanitized))
            {
            }

            return sanitized;
        }

        private static bool TryTrimTrailingModeFMarkSuffix(ref string text)
        {
            if (string.IsNullOrEmpty(text) || text[text.Length - 1] != ']')
            {
                return false;
            }

            return TryTrimTrailingModeFMarkSuffix(ref text, ModeFMarkSuffixZhPrefix) ||
                   TryTrimTrailingModeFMarkSuffix(ref text, ModeFMarkSuffixEnPrefix);
        }

        private static bool TryTrimTrailingModeFMarkSuffix(ref string text, string prefix)
        {
            int startIndex = text.LastIndexOf(prefix, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return false;
            }

            int digitsStart = startIndex + prefix.Length;
            if (digitsStart >= text.Length - 1)
            {
                return false;
            }

            for (int i = digitsStart; i < text.Length - 1; i++)
            {
                if (!char.IsDigit(text[i]))
                {
                    return false;
                }
            }

            text = text.Substring(0, startIndex);
            return true;
        }
    }
}
