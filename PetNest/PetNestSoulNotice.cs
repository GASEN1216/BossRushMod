// 只在实际遗魂到账时挂到当前玩家；合并击杀反馈，官方气泡结束后才显示下一批。
using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;
using UnityEngine;

namespace BossRush
{
    internal sealed class PetNestSoulNotice : MonoBehaviour
    {
        private readonly Dictionary<string, long> _pending = new Dictionary<string, long>(StringComparer.Ordinal);
        private float _wait;
        private bool _showing;
        private Transform _anchor;
        private DialogueBubble _bubble;

        internal static void Queue(string lineageKey, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(lineageKey)) return;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null || player.Health.IsDead) return;
                PetNestSoulNotice notice = player.GetComponent<PetNestSoulNotice>();
                if (notice == null) notice = player.gameObject.AddComponent<PetNestSoulNotice>();
                long old;
                notice._pending.TryGetValue(lineageKey, out old);
                if (notice._pending.Count == 0) notice._wait = 0.2f;
                notice._pending[lineageKey] = old + amount;
            }
            catch (Exception e) { ModBehaviour.DevLog("[PetNest] 遗魂反馈入队失败: " + e.Message); }
        }

        private void Update()
        {
            if (_pending.Count == 0 && !_showing) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.gameObject != gameObject || player.Health == null || player.Health.IsDead)
            { _pending.Clear(); CancelCurrentBubble(); return; }
            if (BossRushUI.IsGamePaused() || BossRushUI.IsOfficialHudHidden()) { CancelCurrentBubble(); return; }
            if (_showing)
            {
                // 官方在淡入后重置 interacted，且每字至少等一帧；持续跳字才能让整批数字尽快出现。
                if (_bubble != null && _bubble.Target == _anchor) _bubble.Interact();
                return;
            }
            _wait -= Time.deltaTime;
            if (_wait > 0f || DialogueBubblesManager.Instance == null) return;
            ShowBatch().Forget(OnShowFailed);
        }

        private async UniTask ShowBatch()
        {
            _showing = true;
            try
            {
                StringBuilder text = new StringBuilder();
                int shown = 0;
                long remaining = 0;
                foreach (KeyValuePair<string, long> pair in _pending)
                {
                    if (shown >= 2 && _pending.Count > 3) { remaining += pair.Value; continue; }
                    PetNestLineageInfo lineage;
                    string name = PetNestLineageCatalog.TryGet(pair.Key, out lineage) && lineage != null
                        ? lineage.DisplayName : pair.Key;
                    if (shown++ > 0) text.Append('\n');
                    text.Append(name).Append(L10n.T(" 遗魂 +", " souls +")).Append(pair.Value);
                }
                if (remaining > 0) text.Append('\n').Append(L10n.T("其他血脉 遗魂 +", "Other lineage souls +")).Append(remaining);
                _pending.Clear();
                if (_anchor == null)
                {
                    _anchor = new GameObject("BossRush_SoulNoticeAnchor").transform;
                    _anchor.SetParent(transform, false);
                }
                _anchor.gameObject.SetActive(true);
                // 独有 target 保证跳字和清理只处理自己的气泡；退出由官方的 target 存活检查收尾。
                // skippable 只跳过打字，不会缩短完整文字的 1 秒停留；期间只聚合后续击杀。
                UniTask request = DialogueBubblesManager.Show(text.ToString(), _anchor, 2.1f, false, true, 100000f, 1f);
                DialogueBubblesManager manager = DialogueBubblesManager.Instance;
                if (manager != null)
                {
                    DialogueBubble[] bubbles = manager.GetComponentsInChildren<DialogueBubble>(true);
                    for (int i = 0; i < bubbles.Length; i++)
                        if (bubbles[i] != null && bubbles[i].Target == _anchor) { _bubble = bubbles[i]; break; }
                }
                await request;
            }
            finally { _showing = false; _bubble = null; _wait = 0.15f; }
        }

        private static void OnShowFailed(Exception e)
        { ModBehaviour.DevLog("[PetNest] 遗魂气泡显示失败: " + e.Message); }

        internal static void CleanupCurrentPlayer()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            PetNestSoulNotice notice = player.GetComponent<PetNestSoulNotice>();
            if (notice != null) { notice._pending.Clear(); notice.CancelCurrentBubble(); Destroy(notice); }
        }

        private void CancelCurrentBubble()
        {
            // 官方异步任务使用 unscaled 时间；关闭专属 target 让打字和停留阶段都能主动退出。
            if (_anchor != null) _anchor.gameObject.SetActive(false);
            if (_bubble == null || _bubble.Target != _anchor) return;
            // 先立即隐藏，再由官方任务完成淡出和池内状态收尾；不销毁共享池对象。
            _bubble.Interact();
            _bubble.gameObject.SetActive(false);
            _bubble = null;
        }

        private void OnDisable() { _pending.Clear(); CancelCurrentBubble(); }
        private void OnDestroy()
        {
            _pending.Clear(); CancelCurrentBubble();
            if (_anchor != null) Destroy(_anchor.gameObject);
        }
    }
}
