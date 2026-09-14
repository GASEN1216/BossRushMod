#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationAutotestStory.cs - 全自动实机验收：剧情快照、阶段推进、还原与记账（Dev 构建）
// ============================================================================
// 这一轮会写专用测试档的天空岛剧情与背包，所以四件事必须成对：
//   1. **快照在第一次写入之前取**：出发之前在基地读剧情存档原文、官方图鉴里已点亮的见闻、岛上物品件数、金钱、语言、强制夜里、时间流速，
//      写进存档键 BossRush_Validation_AutotestSnapshot_v1（崩溃后下次回基地据它恢复）并落一份 story_snapshot.json。
//   2. **阶段推进只走合法入口**：清空 / 还原走 SkyIslandStoryService.DevAutotestReplace（共享 store 的写屏障照常生效），
//      其余是 RecordEncounterCleared / TryApply / RecordNote——与玩家在岛上做同一件事时是同一条代码。
//   3. **还原在收尾阶段做、在 CompleteSession（RunSession 的 finally）里再兜一次**：岛上先经活的 store 还原，回基地后读回核对，
//      不一致再经一份临时门面还原并读回；读回一致才清掉快照键。
//   4. **记账**：发给测试用的岛上物品按件数差收回（只收多出来的、不补少了的）；金钱只记变化不改。
// 任何写入之前先过 AutotestWriteAllowed（Dev + 专用测试档 + 自动验收正在跑或正在做崩溃恢复）。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Economy;
using Duckov.NoteIndexs;
using ItemStatsSystem;
using Saves;
using SodaCraft.Localizations;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private const string AutotestSnapshotKey = "BossRush_Validation_AutotestSnapshot_v1";

        /// <summary>记账范围：天空岛全部物品（纪念品、材料、耗材、工具）。验收只会发、会触发发放的就是这些。</summary>
        private static readonly int[] AutotestLedgerTypeIds =
        {
            BossRushItemIds.SkyIslandHomecomingBadge, BossRushItemIds.SkyIslandWindeaterCore, BossRushItemIds.SkyIslandWindVaneCompass,
            BossRushItemIds.SkyIslandHomecomingBento, BossRushItemIds.SkyIslandStarmossSalve, BossRushItemIds.SkyIslandCloudmossFiber,
            BossRushItemIds.SkyIslandGreenearSheaf, BossRushItemIds.SkyIslandDriftwood, BossRushItemIds.SkyIslandBrassScrap,
            BossRushItemIds.SkyIslandWindcrystalShard, BossRushItemIds.SkyIslandStardust, BossRushItemIds.SkyIslandQinglanWindcrystal,
            BossRushItemIds.SkyIslandWindLantern, BossRushItemIds.SkyIslandWindwardIncense, BossRushItemIds.SkyIslandQinglanCharm,
            BossRushItemIds.SkyIslandCloudmossVeil, BossRushItemIds.SkyIslandGnatZapper, BossRushItemIds.SkyIslandSmokeFan,
        };

        private sealed class AutotestSnapshot
        {
            internal string RunId;
            internal int Slot;
            internal bool RawExists;
            internal string Raw;
            internal SkyIslandStoryData Data;
            internal readonly List<string> OfficialUnlocked = new List<string>();
            internal readonly Dictionary<int, int> Items = new Dictionary<int, int>();
            internal long Money;
            internal SystemLanguage Language;
            internal bool ForceNight;
            internal float TimeScale;
        }

        /// <summary>崩溃恢复进行中：写入门在没有运行中验收时也放行（仍要求专用测试档）。</summary>
        private static bool _autotestRecovering;
        private int _autotestRecoveryCheckedSlot = int.MinValue;

        #region 快照

        private bool TakeAutotestSnapshot(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            if (_autotest == null) { reason = "autotest_not_running"; return false; }
            // 写入门要求快照已落盘；取快照这一次（写快照键）是唯一的例外。
            _autotest.Snapshotting = true;
            try { return TakeAutotestSnapshotCore(ref metrics, out reason); }
            finally { _autotest.Snapshotting = false; }
        }

        private bool TakeAutotestSnapshotCore(ref string metrics, out string reason)
        {
            if (!AutotestWriteAllowed(out reason)) return false;
            if (!IsBaseScene()) { reason = "not_in_base"; return false; }
            if (SkyIslandSessionOrNull() != null || SkyIslandStorySaveRecovery.IsPending()) { reason = "island_story_still_open"; return false; }
            if (SavesSystem.IsSaving) { reason = "saves_system_busy"; return false; }
            try
            {
                if (SavesSystem.KeyExisits(AutotestSnapshotKey) && !string.IsNullOrEmpty(SavesSystem.Load<string>(AutotestSnapshotKey)))
                {
                    // 上一轮没还原成的快照还在：先按它还原。绝不能拿当前状态（可能是上一轮推进到一半的阶段）覆盖它。
                    string recovered;
                    if (!TryRecoverAutotestSnapshot(out recovered)) { reason = "previous_snapshot_not_restored:" + recovered; return false; }
                    metrics = "previous_snapshot=" + recovered + ",";
                }
                var snapshot = new AutotestSnapshot { RunId = _runId, Slot = SavesSystem.CurrentSlot };
                snapshot.RawExists = SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey);
                snapshot.Raw = snapshot.RawExists ? SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey) : null;
                snapshot.Data = snapshot.RawExists ? SkyIslandStoryCodec.Decode(snapshot.Raw) : SkyIslandStoryRules.CreateDefault();
                if (snapshot.Data == null)
                {
                    // 读不动的记录经 store 只能进写屏障、写不回原样：这种档不拿来推进阶段。
                    reason = "story_record_unreadable_refuse_to_stage";
                    return false;
                }
                snapshot.OfficialUnlocked.AddRange(CollectOfficialSkyNotes());
                foreach (int typeId in AutotestLedgerTypeIds)
                {
                    string where;
                    snapshot.Items[typeId] = CountOwnedItems(typeId, out where);
                }
                snapshot.Money = EconomyManager.Money;
                snapshot.Language = LocalizationManager.CurrentLanguage;
                snapshot.ForceNight = SkyIslandNight.DevForceNight;
                snapshot.TimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                string json = EncodeAutotestSnapshot(snapshot);
                SavesSystem.Save<string>(AutotestSnapshotKey, json);
                if (!string.Equals(SavesSystem.Load<string>(AutotestSnapshotKey), json, StringComparison.Ordinal))
                {
                    reason = "snapshot_key_readback_mismatch";
                    return false;
                }
                SavesSystem.SaveFile(false);
                _autotest.Snapshot = snapshot;
                _autotest.SnapshotPersisted = true;
                WriteAutotestTextFile("story_snapshot.json", json);
                metrics += "raw_exists=" + snapshot.RawExists + ",flags=" + snapshot.Data.flags
                    + ",notes=" + snapshot.Data.discoveredNotes.Length + ",cleared=" + snapshot.Data.clearedEncounters.Length
                    + ",regions=" + snapshot.Data.visitedRegions + ",official_unlocked=" + snapshot.OfficialUnlocked.Count
                    + ",money=" + snapshot.Money + ",language=" + snapshot.Language + ",force_night=" + snapshot.ForceNight
                    + ",time_scale=" + snapshot.TimeScale.ToString("F2") + ",key=" + AutotestSnapshotKey;
                return true;
            }
            catch (Exception e)
            {
                reason = "snapshot_failed:" + e.GetType().Name + ":" + e.Message;
                return false;
            }
        }

        // 编码本身是纯函数（F3AutotestJudges.EncodeSnapshot / DecodeSnapshot），崩溃恢复靠它往返不变形，由 tests/fixtures/F3AutotestJudges 钉住；
        // 这里只在 Unity 类型（SystemLanguage）与纯记录之间搬字段。
        private static string EncodeAutotestSnapshot(AutotestSnapshot snapshot)
        {
            var record = new F3AutotestSnapshotRecord
            {
                RunId = snapshot.RunId, Slot = snapshot.Slot, RawExists = snapshot.RawExists, Raw = snapshot.Raw, Money = snapshot.Money,
                Language = snapshot.Language.ToString(), ForceNight = snapshot.ForceNight, TimeScale = snapshot.TimeScale
            };
            record.OfficialUnlocked.AddRange(snapshot.OfficialUnlocked);
            foreach (KeyValuePair<int, int> pair in snapshot.Items) record.Items[pair.Key] = pair.Value;
            return F3AutotestJudges.EncodeSnapshot(record);
        }

        private static AutotestSnapshot DecodeAutotestSnapshot(string json)
        {
            F3AutotestSnapshotRecord record = F3AutotestJudges.DecodeSnapshot(json);
            SkyIslandStoryData data = F3AutotestJudges.SnapshotStory(record);
            if (record == null || data == null) return null;
            var snapshot = new AutotestSnapshot
            {
                RunId = record.RunId, Slot = record.Slot, RawExists = record.RawExists, Raw = record.Raw, Data = data,
                Money = record.Money, ForceNight = record.ForceNight, TimeScale = record.TimeScale
            };
            try { snapshot.Language = (SystemLanguage)Enum.Parse(typeof(SystemLanguage), string.IsNullOrEmpty(record.Language) ? "Unknown" : record.Language, false); }
            catch (Exception) { snapshot.Language = LocalizationManager.CurrentLanguage; }
            snapshot.OfficialUnlocked.AddRange(record.OfficialUnlocked);
            foreach (KeyValuePair<int, int> pair in record.Items) snapshot.Items[pair.Key] = pair.Value;
            return snapshot;
        }

        private static List<string> CollectOfficialSkyNotes()
        {
            var keys = new List<string>();
            try
            {
                foreach (string key in NoteIndex.GetAllNotes(true))
                    if (key != null && key.StartsWith(SkyIslandNoteBridge.NoteKeyPrefix, StringComparison.Ordinal)) keys.Add(key);
            }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 读官方图鉴失败: " + e.Message); }
            return keys;
        }

        #endregion

        #region 阶段推进

        /// <summary>
        /// 在活着的岛上会话上推进一个 story 阶段。每条动作走生产入口；「已经是这样了」（旗标已在、清场已记、手记已收）算通过。
        /// 推完立即落盘，再按纯规则算出来的期望核对「包含」关系并做一次编解码往返。
        /// </summary>
        private bool ApplyAutotestStage(F3AutotestStage stage, out string metrics, out string reason)
        {
            metrics = "stage=" + stage.Id;
            if (!AutotestWriteAllowed(out reason)) return false;
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (story == null || !session.IsReady) { reason = "island_session_not_ready"; return false; }
            SkyIslandStoryData expected;
            if (!_autotest.StageExpectations.TryGetValue(stage.Id, out expected)) { reason = "stage_expectation_missing"; return false; }
            var applied = new List<string>();
            foreach (string text in stage.Apply)
            {
                F3AutotestStageOp op;
                string error;
                if (!F3AutotestJudges.TryParseStageOp(text, out op, out error)) { reason = error; return false; }
                bool ok;
                string message = null;
                switch (op.Kind)
                {
                    case F3AutotestOpKind.Reset:
                        ok = story.DevAutotestReplace(SkyIslandStoryRules.CreateDefault(), true, out message);
                        if (ok) _autotest.StoryResetDone = true;
                        break;
                    case F3AutotestOpKind.ClearEncounter:
                        ok = story.Current.EncounterCleared(op.Argument) || story.RecordEncounterCleared(op.Argument);
                        break;
                    case F3AutotestOpKind.StoryAction:
                        ok = story.TryApply(op.Action, out message);
                        if (!ok)
                        {
                            // TryApply 对「已经做过」与「前置不满足」都返回 false：CanApply 的 blocker 为 null 表示已经做过（或走了互斥分支），不是缺前置。
                            string blocker;
                            SkyIslandStoryRules.CanApply(story.Current, op.Action, out blocker);
                            ok = blocker == null;
                            if (ok) message = "already_applied";
                        }
                        break;
                    case F3AutotestOpKind.RecordNote:
                        ok = Array.IndexOf(story.Current.discoveredNotes, op.Argument) >= 0 || story.RecordNote(op.Argument, out message);
                        break;
                    case F3AutotestOpKind.LightLamp:
                        ok = SkyIslandLights.Lit(story.Current, op.Argument) || LightAutotestLamp(op.Argument, out message);
                        break;
                    default:
                        ok = false;
                        message = "op_kind_unknown";
                        break;
                }
                applied.Add(text + (ok ? "=ok" : "=rejected(" + message + ")"));
                if (!ok)
                {
                    metrics += ",ops=" + string.Join(";", applied.ToArray());
                    reason = "op_rejected:" + text + ":" + message;
                    return false;
                }
            }
            string flushError;
            bool flushed = story.DevAutotestFlush(out flushError);
            string stageMetrics, stageReason, codecReason;
            bool reached = F3AutotestJudges.JudgeStageData(story.Current, expected, out stageMetrics, out stageReason);
            bool codec = F3AutotestJudges.CodecRoundTrip(story.Current, out codecReason);
            metrics += ",ops=" + string.Join(";", applied.ToArray()) + "," + stageMetrics + ",flushed=" + flushed
                + (flushed ? string.Empty : "(" + flushError + ")") + ",codec_roundtrip=" + codec;
            reason = !reached ? stageReason : !codec ? codecReason : !flushed ? "flush_failed:" + flushError : null;
            if (reason == null) _autotest.StagesApplied.Add(stage.Id);
            return reason == null;
        }

        /// <summary>点一盏风晶灯：先把这盏灯的材料发进背包（记账，收尾按件数差收回），再走生产的 SkyIslandFieldcraft.LightLamp。</summary>
        private bool LightAutotestLamp(string lampId, out string message)
        {
            SkyIslandLight light = SkyIslandLights.Find(lampId);
            SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
            if (light == null || field == null) { message = light == null ? "lamp_unknown" : "fieldcraft_missing"; return false; }
            foreach (SkyIslandIngredient input in light.Inputs)
            {
                int missing = input.Count - ItemFactory.GetItemCountInInventory(input.TypeId);
                if (missing > 0 && !GiveAutotestItems(input.TypeId, missing, out message)) return false;
            }
            return field.LightLamp(light, out message);
        }

        /// <summary>发测试用物品进背包（背包满或实例化失败时如实返回），发出去的由收尾按件数差收回。</summary>
        private static bool GiveAutotestItems(int typeId, int count, out string reason)
        {
            if (!AutotestWriteAllowed(out reason)) return false;
            if (ItemAssetsCollection.GetPrefab(typeId) == null) { reason = "prefab_missing:" + typeId; return false; }
            int given = 0;
            for (int n = 0; n < count; n++)
            {
                Item item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null) break;
                if (!ItemUtilities.SendToPlayerCharacterInventory(item, false))
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                    break;
                }
                given++;
            }
            reason = given == count ? null : "pack_full_or_instantiate_failed:" + typeId + "=" + given + "/" + count;
            return given == count;
        }

        #endregion

        #region 还原

        /// <summary>岛上：经活着的 store 整份写回快照并立即落盘、读回核对。之后会话若再写（到访、纪念品手记），回基地还会再核一次。</summary>
        private bool RestoreAutotestStoryOnIsland(out string detail)
        {
            AutotestSnapshot snapshot = _autotest == null ? null : _autotest.Snapshot;
            if (snapshot == null) { detail = "no_snapshot"; return false; }
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (story == null) { detail = "no_live_story"; return false; }
            string error;
            if (!story.DevAutotestReplace(snapshot.Data, true, out error)) { detail = "live_replace_failed:" + error; return false; }
            return ReadbackAutotestStory(snapshot.Data, out detail);
        }

        /// <summary>基地：没有岛上会话时经一份临时门面（同一个 store / 协调器）写回快照、落盘并读回核对。</summary>
        private bool RestoreAutotestStoryAtBase(SkyIslandStoryData data, out string detail)
        {
            if (data == null) { detail = "no_snapshot"; return false; }
            if (SkyIslandSessionOrNull() != null) { detail = "island_session_still_attached"; return false; }
            if (SkyIslandStorySaveRecovery.IsPending()) { detail = "island_story_save_pending"; return false; }
            var service = new SkyIslandStoryService();
            try
            {
                service.DevAutotestOpen();
                string error;
                if (!service.DevAutotestReplace(data, true, out error)) { detail = "base_replace_failed:" + error; return false; }
                return ReadbackAutotestStory(data, out detail);
            }
            catch (Exception e)
            {
                detail = "base_restore_threw:" + e.GetType().Name + ":" + e.Message;
                return false;
            }
            finally
            {
                try { service.Close(); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 临时剧情门面关闭失败: " + e.Message); }
            }
        }

        private static bool ReadbackAutotestStory(SkyIslandStoryData expected, out string detail)
        {
            try
            {
                SkyIslandStoryData actual = SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey)
                    ? SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey))
                    : SkyIslandStoryRules.CreateDefault();
                if (actual == null) { detail = "readback_unreadable"; return false; }
                string diff;
                if (!F3AutotestJudges.SameStory(expected, actual, out diff)) { detail = "readback_differs:" + diff; return false; }
                detail = "readback_equal(flags=" + actual.flags + ",notes=" + actual.discoveredNotes.Length
                    + ",cleared=" + actual.clearedEncounters.Length + ",regions=" + actual.visitedRegions + ")";
                return true;
            }
            catch (Exception e)
            {
                detail = "readback_failed:" + e.GetType().Name;
                return false;
            }
        }

        private void ClearAutotestSnapshotKey()
        {
            string reason;
            if (!AutotestWriteAllowed(out reason)) { ModBehaviour.DevLog("[Validation] 快照键未清除: " + reason); return; }
            try
            {
                SavesSystem.Save<string>(AutotestSnapshotKey, string.Empty);
                SavesSystem.SaveFile(false);
            }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 清除快照键失败: " + e.Message); }
        }

        /// <summary>
        /// 语言（SetLanguage 回原值）、强制夜里、临时无敌、临时压低的血量、时间流速。收尾阶段与 CompleteSession 的同步兜底都调它。
        /// 时间流速只在没有模态面板、没开暂停菜单时拨回：那两种情况下 0 是它们自己的暂停，不是验收留下的。
        /// </summary>
        private bool RestoreAutotestEnvironment(out string detail)
        {
            var parts = new List<string>();
            bool ok = true;
            AutotestSnapshot snapshot = _autotest == null ? null : _autotest.Snapshot;
            if (snapshot != null)
            {
                string language;
                ok &= RestoreAutotestLanguage(out language);
                parts.Add(language);
                try { SkyIslandNight.DevForceNight = snapshot.ForceNight; }
                catch (Exception e) { parts.Add("force_night_threw:" + e.GetType().Name); ok = false; }
                parts.Add("force_night=" + SkyIslandNight.DevForceNight + "/" + snapshot.ForceNight);
                ok &= SkyIslandNight.DevForceNight == snapshot.ForceNight;
            }
            int invincible = 0, healed = 0;
            if (_autotest != null)
            {
                foreach (KeyValuePair<Health, bool> pair in _autotest.InvincibleOriginal)
                {
                    try { if (pair.Key != null) { pair.Key.SetInvincible(pair.Value); invincible++; } }
                    catch (Exception e) { parts.Add("invincible_threw:" + e.GetType().Name); ok = false; }
                }
                _autotest.InvincibleOriginal.Clear();
                foreach (KeyValuePair<Health, float> pair in _autotest.HealthOriginal)
                {
                    try
                    {
                        if (pair.Key != null && !pair.Key.IsDead && pair.Key.CurrentHealth < pair.Value)
                        {
                            pair.Key.SetHealth(pair.Value);
                            healed++;
                        }
                    }
                    catch (Exception e) { parts.Add("health_threw:" + e.GetType().Name); ok = false; }
                }
                _autotest.HealthOriginal.Clear();
            }
            parts.Add("invincible_restored=" + invincible + ",health_restored=" + healed);
            try
            {
                float target = snapshot != null && snapshot.TimeScale > 0f ? snapshot.TimeScale : 1f;
                bool modal = ZombieModeUIHelper.ModalInputLeaseCount > 0 || BossRushUI.IsGamePaused();
                if (!modal && Mathf.Abs(Time.timeScale - target) > 0.001f) Time.timeScale = target;
                parts.Add("time_scale=" + Time.timeScale.ToString("F2") + (modal ? "(modal_or_pause_open)" : string.Empty));
                if (!modal) ok &= Mathf.Abs(Time.timeScale - target) <= 0.001f;
            }
            catch (Exception e) { parts.Add("time_scale_threw:" + e.GetType().Name); ok = false; }
            detail = string.Join(",", parts.ToArray());
            if (_autotest != null) _autotest.Info.EnvironmentRestore = ok ? "PASS" : "FAIL:" + detail;
            return ok;
        }

        private bool SwitchAutotestLanguage()
        {
            if (_autotest == null || _autotest.Snapshot == null) return false;
            try
            {
                SystemLanguage alternate = L10n.IsChinese ? SystemLanguage.English : SystemLanguage.ChineseSimplified;
                LocalizationManager.SetLanguage(alternate);
                _autotest.LanguageSwitched = true;
                _autotest.Info.AltLanguage = LocalizationManager.CurrentLanguage.ToString();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Validation] 切换复拍语言失败: " + e.Message);
                return false;
            }
        }

        private bool RestoreAutotestLanguage(out string detail)
        {
            SystemLanguage target = _autotest != null && _autotest.Snapshot != null ? _autotest.Snapshot.Language : LocalizationManager.CurrentLanguage;
            try
            {
                if (!SameAutotestLanguage(LocalizationManager.CurrentLanguage, target)) LocalizationManager.SetLanguage(target);
            }
            catch (Exception e)
            {
                detail = "set_language_threw:" + e.GetType().Name;
                return false;
            }
            bool ok = SameAutotestLanguage(LocalizationManager.CurrentLanguage, target);
            if (ok && _autotest != null) _autotest.LanguageSwitched = false;
            detail = "language=" + LocalizationManager.CurrentLanguage + "/" + target;
            return ok;
        }

        private static bool SameAutotestLanguage(SystemLanguage a, SystemLanguage b)
        {
            if (a == b) return true;
            bool chineseA = a == SystemLanguage.Chinese || a == SystemLanguage.ChineseSimplified || a == SystemLanguage.ChineseTraditional;
            bool chineseB = b == SystemLanguage.Chinese || b == SystemLanguage.ChineseSimplified || b == SystemLanguage.ChineseTraditional;
            return chineseA && chineseB;
        }

        private void SetAutotestInvincible(bool on)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            Health health = main == null ? null : main.Health;
            if (health == null || _autotest == null) return;
            if (!_autotest.InvincibleOriginal.ContainsKey(health)) _autotest.InvincibleOriginal.Add(health, health.Invincible);
            health.SetInvincible(on);
        }

        private void SetAutotestHealthFraction(float fraction)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            Health health = main == null ? null : main.Health;
            if (health == null || health.IsDead || _autotest == null) return;
            if (!_autotest.HealthOriginal.ContainsKey(health)) _autotest.HealthOriginal.Add(health, health.CurrentHealth);
            health.SetHealth(Mathf.Max(1f, health.MaxHealth * Mathf.Clamp01(fraction)));
        }

        #endregion

        #region 物品与金钱

        private static Dictionary<int, int> CountAutotestPack()
        {
            var counts = new Dictionary<int, int>();
            foreach (int typeId in AutotestLedgerTypeIds) counts[typeId] = ItemFactory.GetItemCountInInventory(typeId);
            return counts;
        }

        /// <summary>按快照件数收回多出来的岛上物品（背包含容器、再到仓库）；少了的不补，只记下来。</summary>
        private static string ReclaimAutotestItems(AutotestSnapshot snapshot)
        {
            if (snapshot == null) return "no_snapshot";
            string gate;
            if (!AutotestWriteAllowed(out gate)) return "not_allowed:" + gate;
            var parts = new List<string>();
            foreach (int typeId in AutotestLedgerTypeIds)
            {
                string where;
                int now = CountOwnedItems(typeId, out where);
                int before;
                snapshot.Items.TryGetValue(typeId, out before);
                if (now == before) continue;
                if (now < before)
                {
                    parts.Add(typeId + ":" + before + "->" + now + "(fewer_not_refilled)");
                    continue;
                }
                int removed = RemoveOwnedItems(typeId, now - before);
                parts.Add(typeId + ":" + before + "->" + now + ",reclaimed=" + removed);
            }
            return parts.Count == 0 ? "no_change" : string.Join(";", parts.ToArray());
        }

        private static int RemoveOwnedItems(int typeId, int count)
        {
            int removed = 0;
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null && main.CharacterItem != null) removed += RemoveFromInventory(main.CharacterItem.Inventory, typeId, count, 0);
            }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 收回背包物品失败: " + e.Message); }
            try
            {
                if (removed < count && PlayerStorage.Inventory != null)
                    removed += RemoveFromInventory(PlayerStorage.Inventory, typeId, count - removed, 0);
            }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 收回仓库物品失败: " + e.Message); }
            return removed;
        }

        private static int RemoveFromInventory(Inventory inventory, int typeId, int count, int depth)
        {
            if (inventory == null || inventory.Content == null || count <= 0 || depth > 4) return 0;
            int removed = 0;
            var content = new List<Item>(inventory.Content);
            for (int i = content.Count - 1; i >= 0 && removed < count; i--)
            {
                Item item = content[i];
                if (item == null) continue;
                if (item.TypeID == typeId)
                {
                    int units = item.Stackable ? Math.Max(1, item.StackCount) : 1;
                    if (item.Stackable && units > count - removed)
                    {
                        item.StackCount -= count - removed;
                        removed = count;
                    }
                    else
                    {
                        inventory.RemoveItem(item);
                        UnityEngine.Object.Destroy(item.gameObject);
                        removed += units;
                    }
                }
                else if (item.Inventory != null && !ReferenceEquals(item.Inventory, inventory))
                    removed += RemoveFromInventory(item.Inventory, typeId, count - removed, depth + 1);
            }
            return removed;
        }

        private string AutotestMoneyLedger()
        {
            AutotestSnapshot snapshot = _autotest == null ? null : _autotest.Snapshot;
            long now = EconomyManager.Money;
            return snapshot == null ? "now=" + now : "before=" + snapshot.Money + ",after=" + now + ",delta=" + (now - snapshot.Money) + "(recorded_not_restored)";
        }

        #endregion

        #region 崩溃恢复

        /// <summary>
        /// 回到基地、没有验收在跑时每个槽检查一次：存档里还有没清的快照键，说明上一轮在还原之前中断（崩溃、强退、宿主销毁），
        /// 按它把剧情写回、收回多出来的岛上物品、复位语言与强制夜里。
        /// </summary>
        private void RecoverInterruptedAutotestIfNeeded()
        {
            int slot;
            try { slot = SavesSystem.CurrentSlot; }
            catch (Exception) { return; }
            if (slot == _autotestRecoveryCheckedSlot) return;
            if (!IsBaseScene() || LevelManager.Instance == null || !LevelManager.AfterInit || SavesSystem.IsSaving) return;
            if (SkyIslandSessionOrNull() != null || SkyIslandStorySaveRecovery.IsPending()) return;
            _autotestRecoveryCheckedSlot = slot;
            string detail;
            bool ok = TryRecoverAutotestSnapshot(out detail);
            if (detail == "no_snapshot") return;
            _status = ok ? "检测到上一轮全自动验收中断，已按快照还原测试档天空岛剧情" : "上一轮全自动验收的快照还原失败：" + detail;
            UnityEngine.Debug.Log("[BossRushValidation] AUTOTEST_RECOVERY | " + (ok ? "PASS" : "FAIL") + " | " + detail);
        }

        private bool TryRecoverAutotestSnapshot(out string detail)
        {
            detail = "no_snapshot";
            try
            {
                if (!SavesSystem.KeyExisits(AutotestSnapshotKey)) return true;
                string json = SavesSystem.Load<string>(AutotestSnapshotKey);
                if (string.IsNullOrEmpty(json)) return true;
                if (!IsDedicatedCurrentSlot()) { detail = "snapshot_on_non_dedicated_slot"; return false; }
                AutotestSnapshot snapshot = DecodeAutotestSnapshot(json);
                if (snapshot == null) { detail = "snapshot_unreadable"; return false; }
                _autotestRecovering = true;
                try
                {
                    string restore;
                    if (!RestoreAutotestStoryAtBase(snapshot.Data, out restore)) { detail = "restore_failed:" + restore; return false; }
                    string items = ReclaimAutotestItems(snapshot);
                    if (!SameAutotestLanguage(LocalizationManager.CurrentLanguage, snapshot.Language)) LocalizationManager.SetLanguage(snapshot.Language);
                    SkyIslandNight.DevForceNight = snapshot.ForceNight;
                    SavesSystem.Save<string>(AutotestSnapshotKey, string.Empty);
                    SavesSystem.SaveFile(false);
                    detail = "recovered_run=" + snapshot.RunId + "," + restore + ",items=" + items;
                    return true;
                }
                finally { _autotestRecovering = false; }
            }
            catch (Exception e)
            {
                detail = "recovery_threw:" + e.GetType().Name + ":" + e.Message;
                return false;
            }
        }

        #endregion
    }
}
#endif
