using System.Collections;
using System.Collections.Generic;
using Duckov.Utilities;
using Duckov.UI;
using ItemStatsSystem;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void ShowZombieModeStarterChoice(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            GameObject root = new GameObject("ZombieMode_StarterChoice");
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, root, root, null);
            ZombieModeStarterChoiceView view = root.AddComponent<ZombieModeStarterChoiceView>();
            view.Initialize(runId, this);
            DevLog("[ZombieMode] 初始流派选择 UI 已创建: runId=" + runId);
        }

        public void SelectZombieModeStarterLoadout(int runId, ZombieModeStarterLoadout loadout)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.LifecyclePhase != ZombieModeLifecyclePhase.WaitingStarterChoice)
            {
                return;
            }

            if (!GrantZombieModeStarterLoadout(loadout))
            {
                FailZombieModeBeforeActive(ZombieModeFailureReason.StarterLoadoutFailed);
                return;
            }

            zombieModeRunState.StarterLoadout = loadout;
            FinalizeZombieModeEntryResources();
            zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.Active;
            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.InitialPreparation;
            UnlockZombieModeContainersForActiveRun(runId);
            BeginZombieModePreparation(runId, true, false);
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_Started"));
        }

        // Melee = 近战×1（品质≤5）+ 医疗品×5 + 食物×3 + 饮料×2
        // Gunner = 枪械×1 + 匹配口径×1000 + 医疗×3 + 食物×2 + 饮料×1
        private bool GrantZombieModeStarterLoadout(ZombieModeStarterLoadout loadout)
        {
            try
            {
                if (loadout != ZombieModeStarterLoadout.Melee && loadout != ZombieModeStarterLoadout.Gunner)
                {
                    return false;
                }

                bool grantedAny = false;
                if (loadout == ZombieModeStarterLoadout.Melee)
                {
                    bool coreGranted = TryGiveRandomItemByTags(ZombieModeRewardTagMeleeWeapon, 1, ZombieModeTuning.StarterMaxQuality);
                    if (!coreGranted)
                    {
                        DevLog("[ZombieMode] 近战开局失败：缺少可发放近战武器");
                        return false;
                    }

                    grantedAny = true;
                    int guaranteedHealing = TryGiveZombieModeStarterGuaranteedHealingItems();
                    if (guaranteedHealing < 2)
                    {
                        DevLog("[ZombieMode] 近战开局失败：保底回血道具不足");
                        return false;
                    }

                    int medical = guaranteedHealing + TryGiveRandomItemByTagsTimes(ZombieModeRewardTagsMedicMedicalHealing, 1, 3, 3);
                    grantedAny |= medical > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(ZombieModeRewardTagFood, 1, 3, 3) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(ZombieModeRewardTagDrink, 1, 3, 2) > 0;
                    zombieModeRunState.StarterAmmoCaliber = string.Empty;
                }
                else if (loadout == ZombieModeStarterLoadout.Gunner)
                {
                    int gunTypeId = FindRandomItemTypeByTags(ZombieModeRewardTagGun, 1, ZombieModeTuning.StarterMaxQuality);
                    bool gunGranted = false;
                    if (gunTypeId > 0)
                    {
                        Item gun = ItemAssetsCollection.InstantiateSync(gunTypeId);
                        if (gun != null)
                        {
                            string caliber = TryReadZombieModeItemCaliber(gun);
                            if (!string.IsNullOrEmpty(caliber))
                            {
                                zombieModeRunState.StarterAmmoCaliber = caliber;
                            }
                            ItemUtilities.SendToPlayer(gun, false, false);
                            gunGranted = true;
                            grantedAny = true;
                        }
                    }

                    if (!gunGranted)
                    {
                        DevLog("[ZombieMode] 枪械开局失败：缺少可发放枪械");
                        return false;
                    }

                    int ammoCount = ZombieModeTuning.StarterGunnerExtraAmmoCount;
                    bool ammoGranted = false;
                    if (ammoCount > 0)
                    {
                        ammoGranted = TryGiveZombieModeStarterAmmo(zombieModeRunState.StarterAmmoCaliber, ammoCount);
                        grantedAny |= ammoGranted;
                    }

                    if (!ammoGranted)
                    {
                        DevLog("[ZombieMode] 枪械开局失败：缺少匹配或通用弹药");
                        return false;
                    }

                    int guaranteedHealing = TryGiveZombieModeStarterGuaranteedHealingItems();
                    if (guaranteedHealing < 2)
                    {
                        DevLog("[ZombieMode] 枪械开局失败：保底回血道具不足");
                        return false;
                    }

                    grantedAny |= (guaranteedHealing + TryGiveRandomItemByTagsTimes(ZombieModeRewardTagsMedicMedicalHealing, 1, 3, 1)) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(ZombieModeRewardTagFood, 1, 3, 2) > 0;
                    grantedAny |= TryGiveRandomItemByTagsTimes(ZombieModeRewardTagDrink, 1, 3, 1) > 0;
                }

                if (!GrantZombieModeStarterProtectionSet())
                {
                    DevLog("[ZombieMode] 开局防具发放失败：缺少护甲/头盔/耳机候选物品");
                    return false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 发放初始流派失败: " + e.Message);
                return false;
            }
        }

        private bool GrantZombieModeStarterProtectionSet()
        {
            bool armorGranted = TryGiveRandomItemByTags(ZombieModeRewardTagBodyArmor, 1, ZombieModeTuning.StarterMaxQuality);
            bool helmetGranted = TryGiveRandomItemByTags(ZombieModeRewardTagHelmet, 1, ZombieModeTuning.StarterMaxQuality);
            bool headsetGranted = TryGiveRandomItemByTags(ZombieModeRewardTagHeadset, 1, ZombieModeTuning.StarterMaxQuality);

            if (!armorGranted)
            {
                DevLog("[ZombieMode] 开局护甲发放失败");
            }
            if (!helmetGranted)
            {
                DevLog("[ZombieMode] 开局头盔发放失败");
            }
            if (!headsetGranted)
            {
                DevLog("[ZombieMode] 开局耳机发放失败");
            }

            return armorGranted && helmetGranted && headsetGranted;
        }

        private int TryGiveZombieModeStarterGuaranteedHealingItems()
        {
            int success = 0;
            for (int i = 0; i < 2; i++)
            {
                if (TryGiveZombieModeStarterGuaranteedHealingItem())
                {
                    success++;
                }
            }
            return success;
        }

        private bool TryGiveZombieModeStarterGuaranteedHealingItem()
        {
            return TryGiveRandomItemByTags(ZombieModeRewardTagHealing, 1, ZombieModeTuning.StarterMaxQuality);
        }

        private bool TryGiveZombieModeWaveClearHealingItem()
        {
            return TryGiveRandomItemByTags(ZombieModeRewardTagHealing, 1, ZombieModeTuning.StarterMaxQuality);
        }

        private int TryGiveRandomItemByTagsTimes(string[] requiredTags, int minQuality, int maxQuality, int times)
        {
            int success = 0;
            for (int i = 0; i < times; i++)
            {
                if (TryGiveRandomItemByTags(requiredTags, minQuality, maxQuality))
                {
                    success++;
                }
            }
            return success;
        }

        private string TryReadZombieModeItemCaliber(Item item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            try
            {
                if (item.Constants == null)
                {
                    return string.Empty;
                }
                string caliber = item.Constants.GetString("Caliber", null);
                return caliber ?? string.Empty;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 弹药 caliber 读取失败: " + e.Message);
            }
            return string.Empty;
        }

        private bool TryGiveZombieModeStarterAmmo(string caliber, int totalCount)
        {
            return TryGiveZombieModeAmmo(caliber, totalCount, 1, 2);
        }

        private bool TryGiveZombieModeAmmo(string caliber, int totalCount, int minQuality, int maxQuality)
        {
            try
            {
                ItemFilter filter = new ItemFilter();
                Tag[] ammoTags = ResolveZombieModeTags(ZombieModeRewardTagAmmo);
                if (ammoTags == null || ammoTags.Length <= 0)
                {
                    ammoTags = ResolveZombieModeTags(ZombieModeRewardTagBullet);
                }
                filter.requireTags = ammoTags;
                filter.minQuality = minQuality;
                filter.maxQuality = maxQuality;
                filter.caliber = caliber ?? string.Empty;

                int[] candidates = ItemAssetsCollection.Search(filter);
                if (candidates == null || candidates.Length <= 0)
                {
                    return false;
                }

                int chosenTypeId = PickZombieModeStrictQualityCandidate(candidates, minQuality, maxQuality);
                if (chosenTypeId <= 0)
                {
                    return false;
                }

                Item ammoItem = ItemAssetsCollection.InstantiateSync(chosenTypeId);
                if (ammoItem == null)
                {
                    return false;
                }

                try { ammoItem.StackCount = totalCount; } catch (System.Exception e) { DevLog("[ZombieMode] ammoItem.StackCount 设置失败: " + e.Message); }
                ItemUtilities.SendToPlayer(ammoItem, true, true);
                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 发放起始弹药失败: " + e.Message);
                return false;
            }
        }

        private bool TryGiveRandomItemByTags(string[] requiredTags, int minQuality, int maxQuality)
        {
            int typeId = FindRandomItemTypeByTags(requiredTags, minQuality, maxQuality);
            if (typeId <= 0)
            {
                return false;
            }

            Item item = ItemAssetsCollection.InstantiateSync(typeId);
            if (item == null)
            {
                return false;
            }

            ItemUtilities.SendToPlayer(item, false, false);
            return true;
        }
    }

    public sealed class ZombieModeStarterChoiceView : MonoBehaviour
    {
        // ==================== 配色方案（与 CashInvestmentView 统一） ====================
        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color PanelOuterColor = new Color(0.12f, 0.16f, 0.24f, 0.98f);
        private static readonly Color PanelBorderColor = new Color(0.22f, 0.30f, 0.44f, 0.45f);
        private static readonly Color PanelInnerColor = new Color(0.10f, 0.12f, 0.16f, 0.98f);
        private static readonly Color HeaderColor = new Color(0.14f, 0.20f, 0.32f, 1.00f);
        private static readonly Color AccentLineColor = new Color(0.35f, 0.55f, 0.85f, 0.70f);
        private static readonly Color SubtitleColor = new Color(0.62f, 0.70f, 0.82f, 0.90f);

        // 近战卡片：暗红-铜色调
        private static readonly Color MeleeCardColor = new Color(0.14f, 0.10f, 0.10f, 0.98f);
        private static readonly Color MeleeAccentColor = new Color(0.85f, 0.50f, 0.25f, 0.95f);
        private static readonly Color MeleeBtnColor = new Color(0.52f, 0.30f, 0.14f, 1.00f);
        private static readonly Color MeleeBtnHoverColor = new Color(0.68f, 0.40f, 0.20f, 1.00f);

        // 枪手卡片：暗蓝-钢色调
        private static readonly Color GunnerCardColor = new Color(0.10f, 0.10f, 0.14f, 0.98f);
        private static readonly Color GunnerAccentColor = new Color(0.35f, 0.65f, 0.92f, 0.95f);
        private static readonly Color GunnerBtnColor = new Color(0.16f, 0.36f, 0.56f, 1.00f);
        private static readonly Color GunnerBtnHoverColor = new Color(0.22f, 0.48f, 0.72f, 1.00f);

        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
            Build();
            ClaimInputAndPause();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            // ── 全屏遮罩 ──
            GameObject backdrop = ZombieModeUIHelper.CreateRect(
                "Backdrop", transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, Vector2.zero);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = BackdropColor;
            backdropImage.raycastTarget = true;

            // ── 外框（深靛色） ──
            GameObject outer = ZombieModeUIHelper.CreateRect("PanelOuter", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(700f, 420f), new Vector2(0.5f, 0.5f));
            Image outerImage = outer.AddComponent<Image>();
            outerImage.color = PanelOuterColor;

            // ── 亮边层 ──
            GameObject borderGlow = ZombieModeUIHelper.CreateRect("BorderGlow", outer.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image borderImg = borderGlow.AddComponent<Image>();
            borderImg.color = PanelBorderColor;

            // ── 主面板 ──
            GameObject panel = ZombieModeUIHelper.CreateRect("Panel", borderGlow.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = PanelInnerColor;

            // ── 标题栏 ──
            float yPos = 0f;
            float headerH = 64f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = HeaderColor;

            ZombieModeUIHelper.CreateText("Title", header.transform,
                L10n.T("BossRush_ZombieMode_Starter_Title"), 26,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, Color.white);
            yPos += headerH;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -yPos), 2f, AccentLineColor);
            yPos += 6f;

            // ── 副标题 ──
            float subtitleH = 36f;
            ZombieModeUIHelper.CreateText("Subtitle", panel.transform,
                L10n.T("BossRush_ZombieMode_Starter_Subtitle"), 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + subtitleH * 0.5f)), new Vector2(-40f, subtitleH),
                TextAlignmentOptions.Center, SubtitleColor);
            yPos += subtitleH + 10f;

            // ── 卡片区域 ──
            float cardW = 290f;
            float cardH = 230f;
            float cardGap = 24f;
            float cardsStartX = -(cardW + cardGap * 0.5f) * 0.5f;

            // 近战卡片
            CreateLoadoutCard(panel.transform, "MeleeCard",
                new Vector2(-(cardW * 0.5f + cardGap * 0.5f), -(yPos + cardH * 0.5f)),
                new Vector2(cardW, cardH),
                L10n.T("BossRush_ZombieMode_Starter_Melee"),
                L10n.T("BossRush_ZombieMode_Starter_Melee_Desc"),
                "刀",
                MeleeCardColor, MeleeAccentColor, MeleeBtnColor, MeleeBtnHoverColor,
                ZombieModeStarterLoadout.Melee);

            // 枪手卡片
            CreateLoadoutCard(panel.transform, "GunnerCard",
                new Vector2(cardW * 0.5f + cardGap * 0.5f, -(yPos + cardH * 0.5f)),
                new Vector2(cardW, cardH),
                L10n.T("BossRush_ZombieMode_Starter_Gunner"),
                L10n.T("BossRush_ZombieMode_Starter_Gunner_Desc"),
                "枪",
                GunnerCardColor, GunnerAccentColor, GunnerBtnColor, GunnerBtnHoverColor,
                ZombieModeStarterLoadout.Gunner);
        }

        private void CreateLoadoutCard(
            Transform parent, string name, Vector2 position, Vector2 size,
            string title, string description, string iconText,
            Color cardBg, Color accentColor, Color btnColor, Color btnHoverColor,
            ZombieModeStarterLoadout loadout)
        {
            // ── 卡片底板 ──
            GameObject card = ZombieModeUIHelper.CreateRect(name, parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                position, size, new Vector2(0.5f, 0.5f));
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = cardBg;

            // ── 顶部高亮条 ──
            GameObject topAccent = ZombieModeUIHelper.CreateRect("TopAccent", card.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -2f), new Vector2(0f, 4f), new Vector2(0.5f, 1f));
            Image topAccentImg = topAccent.AddComponent<Image>();
            topAccentImg.color = accentColor;
            topAccentImg.raycastTarget = false;

            // ── 图标区域（使用文字模拟） ──
            float iconAreaH = 52f;
            ZombieModeUIHelper.CreateText("Icon", card.transform, iconText, 32,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(14f + iconAreaH * 0.5f)), new Vector2(0f, iconAreaH),
                TextAlignmentOptions.Center, accentColor);

            // ── 名称 ──
            float titleY = 14f + iconAreaH + 4f;
            float titleH = 34f;
            ZombieModeUIHelper.CreateText("Title", card.transform, title, 22,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(titleY + titleH * 0.5f)), new Vector2(-16f, titleH),
                TextAlignmentOptions.Center, Color.white);

            // ── 分隔线 ──
            float sepY = titleY + titleH + 6f;
            ZombieModeUIHelper.CreateSeparator("Sep", card.transform,
                new Vector2(0.15f, 1f), new Vector2(0.85f, 1f),
                new Vector2(0f, -sepY), 1f, new Color(accentColor.r, accentColor.g, accentColor.b, 0.35f));

            // ── 说明文字 ──
            float descY = sepY + 8f;
            float descH = 56f;
            ZombieModeUIHelper.CreateText("Desc", card.transform, description, 13,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(descY + descH * 0.5f)), new Vector2(-24f, descH),
                TextAlignmentOptions.Center, new Color(0.70f, 0.74f, 0.80f, 0.92f));

            // ── 选择按钮 ──
            float btnW = 200f;
            float btnH = 42f;
            float btnY = size.y - 18f - btnH * 0.5f;
            ZombieModeStarterLoadout capturedLoadout = loadout;

            Button button = ZombieModeUIHelper.CreateButton(
                "SelectBtn", card.transform,
                L10n.T("BossRush_ZombieMode_Starter_Select"),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -btnY),
                new Vector2(btnW, btnH),
                btnColor, 17,
                new Vector2(btnW - 12f, btnH - 6f),
                delegate
                {
                    RestoreInputState();
                    if (owner != null)
                    {
                        owner.SelectZombieModeStarterLoadout(runId, capturedLoadout);
                    }
                    Destroy(gameObject);
                },
                true);

            // 设置按钮悬停色
            Image btnImage = button.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = btnColor;
            colors.highlightedColor = btnHoverColor;
            colors.pressedColor = btnColor * 0.85f;
            colors.selectedColor = btnHoverColor;
            colors.disabledColor = btnColor * 0.6f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = btnImage;
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "StarterChoice");
        }

        private void RestoreInputState()
        {
            if (inputLease != null)
            {
                inputLease.Release();
                inputLease = null;
            }
        }

        private void OnDestroy()
        {
            RestoreInputState();
        }
    }
}
