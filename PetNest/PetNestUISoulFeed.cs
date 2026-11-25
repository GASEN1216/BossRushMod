// ============================================================================
// PetNestUISoulFeed.cs - 遗种巢「献魂」的确认、内嵌按钮与魂火演出
// ============================================================================
// 模块说明（2026-11-24 玩家许愿第 5 项）：
//   - 巢页详情「成长」区下面一行：没满级、今天还没献过就挂一颗「献魂（N 遗魂）」，旁边写这条血脉还剩多少；
//     今天献过了只留一句「今日魂火已燃」。按钮不挂在底栏：有疤、能远征时底栏已经挤满。
//   - 点了先走共享确认框（写清花多少、从几级长到几级、每天一次），确认后由 PetNestService.TryFeedSouls 一笔落档。
//   - 演出（水墨东方）：几缕魂火从立绘下方升起，绕立绘三圈没入；随后立绘外散开一圈金环与一圈余烬环，响一声磬，
//     右下角落一枚朱印「Lv.N」，停一会儿淡去。演出挂在立绘框（不是它里面带裁切的那层）下，面板刷新 / 关闭时
//     随立绘一起销毁。详情在滚动视口里、立绘贴着视口顶边，所以绕圈是压扁的椭圆（像斜看一圈环），不会被视口裁掉。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class PetNestUI
    {
        private string _pendingSoulFeedPetId;
        private int _pendingSoulFeedLevel;

        /// <summary>巢页详情的「献魂」：先确认，再落档；成功后在下一次刷新的详情头部播魂火。</summary>
        private void RequestSoulFeed(string petId)
        {
            PetNestPetRecord pet = PetNestService.TryGetPet(petId);
            if (pet == null) return;
            int cost = PetNestService.SoulFeedCost(pet);
            int souls = PetNestService.GetSouls(pet.lineageKey);
            int nextLevel = Mathf.Min(PetNestTuning.PetMaxLevel, pet.level + 1);
            BossRushConfirmDialog.Options options = new BossRushConfirmDialog.Options
            {
                Title = string.Format(L10n.T("献 {0} 遗魂？", "Offer {0} relic souls?"), cost),
                Target = PetNestService.GetDecoratedPetName(pet),
                Body = string.Format(L10n.T(
                        "同脉遗魂化作魂火，它会从 Lv{0} 长到 Lv{1}。每只崽每天（游戏里的一天）只能献一次。",
                        "Souls of its own bloodline turn to soul fire: it grows from Lv{0} to Lv{1}. Each cub takes one offering per in-game day."),
                    pet.level, nextLevel),
                Warning = souls >= cost
                    ? string.Format(L10n.T("这条血脉的遗魂 {0} → {1}", "Souls of this bloodline: {0} → {1}"), souls, souls - cost)
                    : string.Format(L10n.T("这条血脉只有 {0} 遗魂，不够", "This bloodline has only {0} souls; not enough"), souls),
                ConfirmLabel = L10n.T("献魂", "Offer souls"),
                CancelLabel = L10n.T("再想想", "Not now"),
                Danger = false,
                OnConfirm = delegate
                {
                    int level;
                    if (!PetNestUIPages.TryFeedSoulsAndNote(petId, out level)) return;
                    _pendingSoulFeedPetId = petId;
                    _pendingSoulFeedLevel = level;
                    // 正跟着出战的那只立刻换上新等级的血量、伤害与体型（同击杀升级的接法）
                    try { PetNestCompanionRuntime.RefreshProgression(PetNestService.TryGetPet(petId)); }
                    catch (Exception e) { ModBehaviour.DevLog("[PetNest] 献魂升级表现刷新失败: " + e.Message); }
                },
            };
            if (pet.shiny) options.DecorateTarget = PetNestShinyTextShimmer.Attach;
            ShowConfirm(options, Refresh);
        }

        /// <summary>「成长」区下方一行：一颗内嵌按钮 + 一句说明；两样都没有就不画。</summary>
        private void RenderInlineAction(PetNestDetailData detail)
        {
            if (detail.InlineAction == null && string.IsNullOrEmpty(detail.InlineCaption)) return;
            const float RowHeight = 44f;
            const float ButtonWidth = 220f;
            GameObject row = ZombieModeUIHelper.CreateRect("InlineAction", _nestDetailRoot,
                new Vector2(0.5f, 0.5f), new Vector2(NestDetailTextWidth, RowHeight));
            _spawned.Add(row);
            SetLayoutHeight(row, RowHeight);

            float left = 0f;
            if (detail.InlineAction != null)
            {
                Button button = CreateActionButton(row.transform, "InlineButton", detail.InlineAction, new Vector2(ButtonWidth, RowHeight - 4f));
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                left = ButtonWidth + 14f;
            }
            if (!string.IsNullOrEmpty(detail.InlineCaption))
            {
                TextMeshProUGUI caption = ZombieModeUIHelper.CreateText(
                    "InlineCaption", row.transform, detail.InlineCaption, 16f,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(left, 0f), new Vector2(NestDetailTextWidth - left, RowHeight),
                    TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
                caption.rectTransform.pivot = new Vector2(0f, 0.5f);
                caption.rectTransform.anchoredPosition = new Vector2(left, 0f);
                caption.enableWordWrapping = true;
                BossRushUI.ApplyGameFont(caption);
            }
        }

        /// <summary>刚献过魂、详情又正好是那只崽：在立绘上播一次魂火。只播一次。</summary>
        private void PlayPendingSoulFeedFx(Transform header)
        {
            if (string.IsNullOrEmpty(_pendingSoulFeedPetId) || header == null) return;
            string petId = _pendingSoulFeedPetId;
            _pendingSoulFeedPetId = null;
            if (!string.Equals(ResolveSelectedPetId(), petId, StringComparison.Ordinal)) return;
            // 立绘框本身不裁切（裁切在它的 _Clip 子层），演出挂在框下、居中，详情布局怎么变都跟着立绘走
            RectTransform portrait = header.Find("Portrait") as RectTransform;
            if (portrait == null) return;
            PetNestSoulFeedFx.Play(portrait, NestPortraitSize, _pendingSoulFeedLevel);
        }
    }

    /// <summary>魂火演出：只做表现，挂在调用方给的界面节点下，节点销毁演出就停。</summary>
    internal static class PetNestSoulFeedFx
    {
        private const int MoteCount = 5;
        private const float MoteSeconds = 1.1f;
        private const float MoteStagger = 0.09f;
        /// <summary>绕圈的纵向压扁比：立绘中心离视口顶边只有半个立绘高，圈不能竖着画满。</summary>
        private const float OrbitSquash = 0.42f;
        private static readonly Color SoulFire = new Color(1f, 0.78f, 0.45f, 1f);
        private static readonly Color SoulCore = new Color(1f, 0.96f, 0.84f, 1f);

        /// <summary>在 <paramref name="portrait"/> 中心播一次魂火入体 + 升级落印。</summary>
        internal static void Play(RectTransform portrait, float portraitSize, int level)
        {
            if (portrait == null) return;
            GameObject root = ZombieModeUIHelper.CreateRect("SoulFeedFx", portrait, new Vector2(0.5f, 0.5f), Vector2.zero);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.SetAsLastSibling();
            Vector2 center = Vector2.zero;
            BossRushInkMotion motion = BossRushInkMotion.For(root);
            BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxSoulRise);

            System.Random random = new System.Random();
            float radius = portraitSize * 0.62f;
            for (int i = 0; i < MoteCount; i++)
            {
                float size = 16f + 6f * (float)random.NextDouble();
                Image mote = CreateGlow(rootRect, "Mote" + i, size, i % 2 == 0 ? SoulFire : SoulCore);
                RectTransform moteRect = mote.rectTransform;
                // 从立绘下方升起：起点在正下方一段弧上，先往上浮、同时绕三圈收进立绘中心
                float startAngle = -Mathf.PI * 0.5f + ((float)random.NextDouble() - 0.5f) * 1.6f;
                float startRadius = radius * (1.35f + 0.25f * (float)random.NextDouble());
                float rise = portraitSize * (0.55f + 0.2f * (float)random.NextDouble());
                float delay = i * MoteStagger;
                motion.Add(delay, MoteSeconds, false, t =>
                {
                    if (moteRect == null) return;
                    float angle = startAngle + t * Mathf.PI * 6f;
                    float r = startRadius * Mathf.Pow(1f - t, 0.8f);
                    float lift = (1f - t) * (1f - t) * (1f - t) * rise;
                    moteRect.anchoredPosition = center + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * OrbitSquash - lift);
                    float alpha = Mathf.Min(1f, t * 5f) * (t > 0.85f ? (1f - t) / 0.15f : 1f);
                    Color c = mote.color;
                    c.a = alpha;
                    mote.color = c;
                    moteRect.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, t);
                }, () => { if (mote != null) UnityEngine.Object.Destroy(mote.gameObject); });
            }

            float bloomAt = (MoteCount - 1) * MoteStagger + MoteSeconds;
            Image flash = CreateGlow(rootRect, "Bloom", portraitSize * 1.2f, SoulFire);
            flash.rectTransform.anchoredPosition = center;
            Color flashColor = flash.color;
            flashColor.a = 0f;
            flash.color = flashColor;
            motion.Add(bloomAt, 0.8f, true, t =>
            {
                if (flash == null) return;
                Color c = flash.color;
                c.a = 0.55f * (1f - t);
                flash.color = c;
                flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.15f, t);
            }, () =>
            {
                if (flash != null) UnityEngine.Object.Destroy(flash.gameObject);
            });
            motion.Add(bloomAt, 0.01f, true, t => { }, () =>
            {
                if (rootRect == null) return;
                // 环长到比立绘大一圈就淡尽：再大顶上会被滚动视口裁掉
                BossRushInkSkin.SpawnRing(rootRect, center, portraitSize * 0.95f, BossRushInkColors.Gold, 0.85f, 1.45f, 0.75f);
                BossRushInkSkin.SpawnRing(rootRect, center, portraitSize * 0.7f, BossRushInkColors.Ember, 0.5f, 1.5f, 0.55f);
                BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxChimeStone);
                StampLevelSeal(rootRect, center + new Vector2(portraitSize * 0.40f, -portraitSize * 0.36f), level);
            });
            motion.Add(bloomAt + 3.2f, 0.01f, true, t => { }, () =>
            {
                if (root != null) UnityEngine.Object.Destroy(root);
            });
        }

        /// <summary>朱印「Lv.N」：落印后停 1.6 秒再淡去。</summary>
        private static void StampLevelSeal(RectTransform parent, Vector2 position, int level)
        {
            RectTransform seal = BossRushInkSkin.CreateSeal(parent, string.Empty, 52f);
            seal.anchoredPosition = position;
            seal.localRotation = Quaternion.Euler(0f, 0f, -6f);
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText("Level", seal, "Lv." + level, 17f,
                Vector2.zero, new Vector2(48f, 30f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            label.fontStyle = FontStyles.Bold;
            label.enableWordWrapping = false;
            BossRushUI.ApplyGameFont(label);
            BossRushInkSkin.StampSeal(seal, null, 0f);
            CanvasGroup group = seal.GetComponent<CanvasGroup>();
            BossRushInkMotion.For(seal.gameObject).Add(1.8f, 0.6f, false, t =>
            {
                if (group != null) group.alpha = 1f - t;
            });
        }

        private static Image CreateGlow(RectTransform parent, string name, float size, Color color)
        {
            GameObject glow = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(size, size));
            Image image = glow.AddComponent<Image>();
            image.sprite = BossRushInkSkin.GetGlowSprite();
            image.raycastTarget = false;
            image.color = new Color(color.r, color.g, color.b, 0f);
            return image;
        }
    }
}
