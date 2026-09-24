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
        // Gunner = 枪械×1 + 匹配口径×2000 + 医疗×3 + 食物×2 + 饮料×1
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

        /// <summary>
        /// 开局流派卡与奖励卡的图标（审美审查 UC-02 / UC-05）：取对应候选池里第一件有图标的官方物品的图标，
        /// 自定义物品直接取 TypeID 的图标。取不到返回 null，界面去掉图标位（不退回汉字或灰方块）。
        /// 只在建界面时调；候选列表走 GetZombieModeRewardCandidateIds 的缓存。
        /// </summary>
        internal Sprite GetZombieModeStarterIcon(ZombieModeStarterLoadout loadout)
        {
            return GetZombieModeTagIcon(loadout == ZombieModeStarterLoadout.Melee ? ZombieModeRewardTagMeleeWeapon : ZombieModeRewardTagGun);
        }

        internal Sprite GetZombieModeRewardIcon(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.RandomMeleeWeapon: return GetZombieModeTagIcon(ZombieModeRewardTagMeleeWeapon);
                case ZombieModeRewardType.RandomGunWithAmmo: return GetZombieModeTagIcon(ZombieModeRewardTagGun);
                case ZombieModeRewardType.AmmoSupply: return GetZombieModeTagIcon(ZombieModeRewardTagBullet);
                case ZombieModeRewardType.MedicalSupply: return GetZombieModeTagIcon(ZombieModeRewardTagsMedicMedicalHealing);
                case ZombieModeRewardType.ArmorOrHelmet: return GetZombieModeTagIcon(ZombieModeRewardTagBodyArmor);
                case ZombieModeRewardType.PortableSafeZoneDevice: return GetZombieModeTypeIcon(PortableSafeZoneDeviceConfig.TYPE_ID);
                case ZombieModeRewardType.FortificationPack: return GetZombieModeTypeIcon(ReinforcedRoadblockPackConfig.TYPE_ID);
                default: return null;
            }
        }

        /// <summary>补给终端货架格的图标（UI 共识对照审查 B-29）：固定 TypeID 取它自己的，按标签抽的取该标签候选池的第一件。</summary>
        internal Sprite GetZombieModeMerchantIcon(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null || (entry.TypeId <= 0 && string.IsNullOrEmpty(entry.GrantTag))) return null;
            return entry.TypeId > 0 ? GetZombieModeTypeIcon(entry.TypeId) : GetZombieModeTagIcon(new[] { entry.GrantTag });
        }

        private Sprite GetZombieModeTagIcon(string[] tags)
        {
            try
            {
                int[] candidates = GetZombieModeRewardCandidateIds(tags, 1, ZombieModeTuning.StarterMaxQuality);
                for (int i = 0; candidates != null && i < candidates.Length; i++)
                {
                    Sprite icon = GetZombieModeTypeIcon(candidates[i]);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 取界面图标失败: " + e.Message);
            }
            return null;
        }

        private static Sprite GetZombieModeTypeIcon(int typeId)
        {
            try
            {
                ItemMetaData meta = ItemAssetsCollection.GetMetaData(typeId);
                return meta.id == typeId ? meta.icon : null;
            }
            catch (System.Exception)
            {
                return null;
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
        // 2026-09-23 审美审查（UC-05 / UC-07 / UC-11）：旧版是手搓的三层直角方框 + 第二套遮罩色、图标位写着「刀」「枪」两个汉字，
        // 按钮手写 ColorBlock 绕过共享三态（悬停白字对比跌破 4.5:1）。现在与本模式其它模态同一套：
        // CreateModalSurface（圆角底图、描边、投影、遮罩暗角淡入）+ CreateCard + 官方物品图标，按钮走共享次级样式。
        // 两个流派是并列的二选一，不设主按钮；卡片强调条按流派分色（近战暖铜 WarningText，枪械钢蓝 RarityRare）。
        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;
        private bool chosen;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
            Build();
            ClaimInputAndPause();
            // 开局必选、没有「取消」：ESC 只用掉事件，不穿透去开官方暂停菜单（B-28）。
            PetNestCancelKey.Attach(gameObject, delegate { }, null);
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.ZombieModal;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = ZombieModeUIHelper.CreateModalSurface(
                "Panel", transform, new Vector2(700f, 452f), BossRushUIColors.Accent);

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText("Title", panel.transform,
                L10n.T("BossRush_ZombieMode_Starter_Title"), 28,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -34f), new Vector2(-48f, 44f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            title.fontStyle = FontStyles.Bold;
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -64f), 2f,
                new Color(BossRushUIColors.Accent.r, BossRushUIColors.Accent.g, BossRushUIColors.Accent.b, 0.6f));
            ZombieModeUIHelper.CreateText("Subtitle", panel.transform,
                L10n.T("BossRush_ZombieMode_Starter_Subtitle"), 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -90f), new Vector2(-48f, 30f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);

            CreateLoadoutCard(panel.transform, "MeleeCard", new Vector2(-157f, -42f),
                L10n.T("BossRush_ZombieMode_Starter_Melee"),
                L10n.T("BossRush_ZombieMode_Starter_Melee_Desc"),
                BossRushUIColors.WarningText, ZombieModeStarterLoadout.Melee, 0);
            CreateLoadoutCard(panel.transform, "GunnerCard", new Vector2(157f, -42f),
                L10n.T("BossRush_ZombieMode_Starter_Gunner"),
                L10n.T("BossRush_ZombieMode_Starter_Gunner_Desc"),
                BossRushUIColors.RarityRare, ZombieModeStarterLoadout.Gunner, 1);
            BossRushUI.PlayOpenAnimation(panel);
        }

        private void CreateLoadoutCard(
            Transform parent, string name, Vector2 position,
            string title, string description, Color accentColor,
            ZombieModeStarterLoadout loadout, int index)
        {
            Vector2 size = new Vector2(290f, 276f);
            GameObject card = BossRush.BossRushUI.CreateCard(name, parent, position, size, BossRushUIColors.SurfaceRaised, accentColor);

            // 图标：该流派候选池里的官方武器图标；取不到就整块去掉，标题上移（不退回汉字）。
            float y = 18f;
            Sprite icon = owner != null ? owner.GetZombieModeStarterIcon(loadout) : null;
            if (icon != null)
            {
                GameObject iconObject = ZombieModeUIHelper.CreateRect("Icon", card.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(120f, 72f), new Vector2(0.5f, 1f));
                Image iconImage = iconObject.AddComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                y += 72f + 8f;
            }

            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", card.transform, title, 22,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(y + 17f)), new Vector2(-32f, 34f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;
            y += 34f + 6f;
            ZombieModeUIHelper.CreateSeparator("Sep", card.transform,
                new Vector2(0.15f, 1f), new Vector2(0.85f, 1f), new Vector2(0f, -y), 1f, BossRushUIColors.Divider);
            y += 8f;

            float buttonHeight = 42f;
            float descHeight = size.y - y - buttonHeight - 30f;
            ZombieModeUIHelper.CreateText("Desc", card.transform, description, 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(2f, -(y + descHeight * 0.5f)), new Vector2(-40f, descHeight),
                TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);

            ZombieModeStarterLoadout capturedLoadout = loadout;
            UnityEngine.Events.UnityAction choose = delegate
            {
                if (chosen)
                {
                    return;
                }
                chosen = true;
                RestoreInputState();
                if (owner != null)
                {
                    owner.SelectZombieModeStarterLoadout(runId, capturedLoadout);
                }
                BossRushUIKit.PlayCloseAndDestroy(gameObject);
            };
            // 整张卡可点（B-28）；卡里的按钮只是把「能点」说清楚。
            ZombieModeClickableCard.Make(card, choose);
            Button button = ZombieModeUIHelper.CreateButton(
                "SelectBtn", card.transform,
                L10n.T("BossRush_ZombieMode_Starter_Select"),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 16f + buttonHeight * 0.5f),
                new Vector2(200f, buttonHeight),
                BossRushUIColors.SurfaceRaised, 17,
                new Vector2(188f, 36f),
                choose,
                true);
            BossRushUIKit.StyleSecondaryButton(button);
            BossRushUIEntranceAnimation.Play(card, 0.06f + index * 0.06f, 0.24f, 14f);
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
