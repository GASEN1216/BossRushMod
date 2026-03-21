// ============================================================================
// WeddingChapelInteractable.cs - 婚礼教堂建筑交互组件
// ============================================================================

using System;
using System.Globalization;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 婚礼教堂主交互：显示当前婚姻持续天数。
    /// </summary>
    public class WeddingChapelInteractable : InteractableBase
    {
        private const string TogetherKey = "BossRush_WeddingChapel_Together";
        private const string MarriageDateFormat = "yyyy年M月d日";
        private string cachedSpouseNpcId;
        private int cachedTogetherDays = int.MinValue;
        private string cachedDisplayText;
        private WeddingChapelReplayInteractable replayInteractable;

        protected override void Awake()
        {
            try
            {
                interactMarkerOffset = new Vector3(0f, 1.2f, 0f);
                overrideInteractName = true;
                _overrideInteractNameKey = TogetherKey;
                InteractName = TogetherKey;

                NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(this, "[WeddingChapel]");

                Collider existingCollider = GetComponent<Collider>();
                if (existingCollider == null)
                {
                    SphereCollider sphere = gameObject.AddComponent<SphereCollider>();
                    sphere.radius = 0.75f;
                    sphere.center = Vector3.zero;
                    sphere.isTrigger = false;
                    interactCollider = sphere;
                }
                else
                {
                    interactCollider = existingCollider;
                }

                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    gameObject.layer = interactableLayer;
                }

                RefreshTogetherDaysDisplay(force: true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] Awake failed: " + e.Message);
            }

            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] base.Awake failed: " + e.Message);
            }
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] base.Start failed: " + e.Message);
            }

            if (replayInteractable == null)
            {
                var groupList = NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[WeddingChapel]");
                if (groupList != null)
                {
                    replayInteractable = NPCInteractionGroupHelper.AddSubInteractable<WeddingChapelReplayInteractable>(
                        transform,
                        "WeddingChapelReplayOption",
                        groupList);
                }
            }
        }

        private void RefreshTogetherDaysDisplay(bool force = false)
        {
            try
            {
                string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
                int togetherDays = string.IsNullOrEmpty(spouseNpcId)
                    ? -1
                    : CalculateDaysSinceMarriage(spouseNpcId);

                if (!force && spouseNpcId == cachedSpouseNpcId && togetherDays == cachedTogetherDays)
                {
                    return;
                }

                cachedSpouseNpcId = spouseNpcId;
                cachedTogetherDays = togetherDays;

                if (string.IsNullOrEmpty(spouseNpcId))
                {
                    cachedDisplayText = L10n.T("婚礼教堂", "Wedding Chapel");
                }
                else if (togetherDays >= 0)
                {
                    cachedDisplayText = L10n.T(
                        "<color=#FF69B4>已在一起：" + togetherDays + "天</color>",
                        "<color=#FF69B4>Together: " + togetherDays + " days</color>");
                }
                else
                {
                    cachedDisplayText = L10n.T(
                        "<color=#FF69B4>已在一起</color>",
                        "<color=#FF69B4>Together</color>");
                }

                LocalizationHelper.InjectLocalization(TogetherKey, cachedDisplayText);
                _overrideInteractNameKey = TogetherKey;
                InteractName = TogetherKey;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] 刷新显示文本失败: " + e.Message);
            }
        }

        public static int CalculateDaysSinceMarriage(string npcId)
        {
            string dateText = AffinityManager.GetMarriageDateText(npcId);
            if (string.IsNullOrEmpty(dateText))
            {
                return -1;
            }

            DateTime marriageDate;
            if (DateTime.TryParseExact(
                dateText,
                MarriageDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out marriageDate))
            {
                return Mathf.Max(0, (int)(DateTime.Now.Date - marriageDate.Date).TotalDays);
            }

            if (DateTime.TryParse(dateText, out marriageDate))
            {
                return Mathf.Max(0, (int)(DateTime.Now.Date - marriageDate.Date).TotalDays);
            }

            return -1;
        }

        protected override bool IsInteractable()
        {
            string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            if (string.IsNullOrEmpty(spouseNpcId))
            {
                return false;
            }

            RefreshTogetherDaysDisplay();
            return true;
        }

        protected override void OnTimeOut()
        {
        }
    }

    /// <summary>
    /// 婚礼教堂子交互：回放婚礼过场。
    /// </summary>
    public class WeddingChapelReplayInteractable : InteractableBase
    {
        private const string ReplayKey = "BossRush_WeddingChapel_Replay";

        protected override void Awake()
        {
            try
            {
                LocalizationHelper.InjectLocalization(ReplayKey, L10n.T("回忆当天", "Relive the Moment"));
                overrideInteractName = true;
                _overrideInteractNameKey = ReplayKey;
                InteractName = ReplayKey;
                interactMarkerOffset = new Vector3(0f, 0.15f, 0f);
                interactCollider = GetComponent<Collider>();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] Replay interactable Awake failed: " + e.Message);
            }

            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] Replay base.Awake failed: " + e.Message);
            }

            try
            {
                MarkerActive = false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] Replay post-Awake setup failed: " + e.Message);
            }
        }

        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] Replay base.Start failed: " + e.Message);
            }

            overrideInteractName = true;
            _overrideInteractNameKey = ReplayKey;
            InteractName = ReplayKey;
        }

        protected override bool IsInteractable()
        {
            return !string.IsNullOrEmpty(AffinityManager.GetCurrentSpouseNpcId());
        }

        protected override void OnTimeOut()
        {
            try
            {
                string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
                if (string.IsNullOrEmpty(spouseNpcId))
                {
                    ModBehaviour.DevLog("[WeddingChapel] 当前没有配偶，无法回忆当天。");
                    return;
                }

                NPCMarriageSystem.ReplayMarriageScene(spouseNpcId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WeddingChapel] 回忆当天失败: " + e.Message);
            }
        }
    }
}
