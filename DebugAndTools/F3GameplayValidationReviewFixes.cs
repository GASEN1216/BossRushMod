// ============================================================================
// F3GameplayValidationReviewFixes.cs - 2026-10-18 审查修复（CR-2026-10-18-001～023）的实机核对
// ============================================================================
// 整份只在 Dev 构建里编译。owner 定：结论输出成文字，AI 读报告不看截图；
// 婚姻相关只在内存里改、当帧还原；「全部孵化」只核对按钮数据，不真孵（真孵会写存档）。
//
// 基地阶段（RunSuite 2/7）十条同步用例，竞技场阶段一条丧尸用例：
//   AFFINITY_RING_RESTOCK_MARRIAGE    叮当的戒指：已婚不补、单身满 3 天补、当天卖完不补（CR-004）
//   AFFINITY_TAB_NURSE_DISCOUNT       好感页羽织写「治疗折扣」，叮当的折扣与开店价同一入口（CR-005）
//   AFFINITY_DECAY_NOTICE_ACCUMULATE  没来聊天期间两轮衰减，提示的天数与扣分累加（CR-011）
//   NPC_SHOP_OFFICIAL_SAVE_HOOKS      临时官方商店：Awake 挂上的存档 / 读档回调被摘掉（CR-012）
//   PETNEST_HATCH_ALL_PLAN            「全部孵化（N 枚）」N 与巢空位、可识别的蛋一致（CR-006 / 019 的入口侧）
//   PETNEST_DETAIL_ACTIVE_EXPEDITION  刚归来未揭晓又派出：巢页显示本趟剩余时间（CR-007）
//   PETNEST_DETAIL_SLOTS_FULL         队满不挂「派去远征」、底栏写原因（CR-015）
//   PETNEST_DETAIL_SCAR_PRICE         有疤就挂「治一道疤（40 遗魂）」，不看遗魂够不够（CR-016）
//   PETNEST_PULSE_REBASE              摸摸脉冲期间体型被改写，播完停在新体型（CR-017）
//   PETNEST_MUSEUM_ACHIEVEMENT_FRAME  驯养成就行只描边、不挂流光（CR-020）
//   MODE_ZOMBIE_REWARD_FIXES          属性满级不出卡 / 全口径弹药回退与保底 / 手持枪优先（CR-001～003、008、009）
// 遗种巢的假崽与假远征记录只在构建详情数据的那一帧挂进内存表，finally 摘掉，不进存档、不提交事务。
// ============================================================================

#if BOSSRUSH_DEV
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Duckov.Economy;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>基地阶段的审查修复核对；全部只读或内存改动当帧还原。</summary>
        private void RunReviewFixBaseCases()
        {
            RunSyncCase("AFFINITY_RING_RESTOCK_MARRIAGE", AffinityManager.ValidationRingRestockMarriage);
            RunSyncCase("AFFINITY_TAB_NURSE_DISCOUNT", ValidateAffinityTabNurseDiscount);
            RunSyncCase("AFFINITY_DECAY_NOTICE_ACCUMULATE", AffinityManager.ValidationDecayNoticeAccumulates);
            RunSyncCase("NPC_SHOP_OFFICIAL_SAVE_HOOKS", ValidateShopOfficialSaveHooks);
            RunSyncCase("PETNEST_HATCH_ALL_PLAN", PetNestUIPages.ValidationHatchAllPlan);
            RunSyncCase("PETNEST_DETAIL_ACTIVE_EXPEDITION", PetNestUIPages.ValidationDetailActiveExpedition);
            RunSyncCase("PETNEST_DETAIL_SLOTS_FULL", PetNestUIPages.ValidationDetailSlotsFull);
            RunSyncCase("PETNEST_DETAIL_SCAR_PRICE", PetNestUIPages.ValidationDetailScarPrice);
            RunSyncCase("PETNEST_PULSE_REBASE", ValidatePetPulseRebase);
            RunSyncCase("PETNEST_MUSEUM_ACHIEVEMENT_FRAME", PetNestUIPages.ValidationMuseumAchievementFrame);
        }

        /// <summary>
        /// 竞技场阶段：单独开一局丧尸模式，等开局奖励发完后核对奖励判据，再按现有用例的方式清场。
        /// 只取数：选卡池重建一次后还原属性加成表；弹药只挑 TypeID 不发物品。
        /// </summary>
        private IEnumerator RunZombieRewardFixes()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string reason;
            string metrics = string.Empty;
            try
            {
                if (!_host.ValidationStartZombie(out reason))
                { Record("MODE_ZOMBIE_REWARD_FIXES", "FAIL", sw.ElapsedMilliseconds, metrics, reason); yield break; }
                yield return WaitSeconds(6f);
                if (ShouldAbort())
                { Record("MODE_ZOMBIE_REWARD_FIXES", "SKIP", sw.ElapsedMilliseconds, metrics, DescribeAbortReason()); yield break; }
                bool ok = _host.ValidationCheckZombieRewardFixes(out metrics, out reason);
                Record("MODE_ZOMBIE_REWARD_FIXES", ok ? "PASS" : "FAIL", sw.ElapsedMilliseconds, metrics, reason);
            }
            finally { _host.ValidationSafeCleanup(); }
        }

        private static bool ValidateAffinityTabNurseDiscount(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            List<AffinityTabEntry> entries = AffinityTabData.Build();
            AffinityTabEntry nurse = null;
            AffinityTabEntry goblin = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null) continue;
                if (entries[i].NpcId == NurseAffinityConfig.NPC_ID) nurse = entries[i];
                else if (entries[i].NpcId == GoblinAffinityConfig.NPC_ID) goblin = entries[i];
            }
            if (nurse == null) { reason = "nurse_entry_missing"; metrics = "entries=" + entries.Count; return false; }

            // 等级行文案：拿羽织这条的等级数据配一个 25% 折扣，跑生产的 BuildLevelLine
            AffinityTabEntry probe = new AffinityTabEntry
            {
                NpcId = nurse.NpcId, Level = nurse.Level, MaxLevel = nurse.MaxLevel,
                Progress = nurse.Progress, Required = nurse.Required,
                Discount = 0.25f, DiscountIsHealing = nurse.DiscountIsHealing,
            };
            string line = AffinityTabData.BuildLevelLine(probe);
            bool healingLabel = line.Contains(L10n.T("治疗折扣", "Healing discount"))
                && !line.Contains(L10n.T("商店折扣", "Shop discount"));

            float goblinExpected = goblin != null ? NPCShopSystem.GetDiscount(goblin.NpcId) : 0f;
            bool goblinShop = goblin == null || (!goblin.DiscountIsHealing && Math.Abs(goblin.Discount - goblinExpected) < 0.001f);
            metrics = "nurse_lv=" + nurse.Level + ",nurse_discount=" + nurse.Discount.ToString("0.00")
                + ",nurse_healing=" + nurse.DiscountIsHealing + ",label_ok=" + healingLabel
                + ",goblin=" + (goblin != null ? goblin.Discount.ToString("0.00") + "/" + goblinExpected.ToString("0.00") : "absent")
                + ",line=" + line;
            if (!nurse.DiscountIsHealing) reason = "nurse_discount_not_marked_healing";
            else if (!healingLabel) reason = "level_line_label_wrong";
            else if (!goblinShop) reason = "goblin_discount_mismatch";
            return reason == null;
        }

        private static bool ValidateShopOfficialSaveHooks(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            GameObject go = null;
            int saveBefore = -1, loadBefore = -1, saveAfter = -1, loadAfter = -1;
            bool detached = false;
            try
            {
                // 与 NPCShopSystem.CreateShop 同样在激活物体上 AddComponent：官方 Awake 立刻挂存档 / 读档回调
                go = new GameObject("BossRush_F3_ShopHookProbe");
                StockShop shop = go.AddComponent<StockShop>();
                saveBefore = CountSaveHandlers("OnCollectSaveData", shop);
                loadBefore = CountSaveHandlers("OnSetFile", shop);
                detached = NPCShopSystem.DetachOfficialShopSaveHooks(shop);
                saveAfter = CountSaveHandlers("OnCollectSaveData", shop);
                loadAfter = CountSaveHandlers("OnSetFile", shop);
            }
            finally
            {
                // 同帧销毁：官方 Start 不会跑，OnDestroy 再退订一次是空操作
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            metrics = "save=" + saveBefore + "->" + saveAfter + ",load=" + loadBefore + "->" + loadAfter + ",detached=" + detached;
            if (saveBefore < 0 || loadBefore < 0) reason = "saves_event_field_missing";
            else if (saveBefore != 1 || loadBefore != 1) reason = "official_awake_hooks_changed";
            else if (!detached || saveAfter != 0 || loadAfter != 0) reason = "hooks_still_attached";
            return reason == null;
        }

        /// <summary>官方静态事件上挂着几个以 target 为对象的回调；字段不在返回 -1。</summary>
        private static int CountSaveHandlers(string eventName, object target)
        {
            FieldInfo field = typeof(Saves.SavesSystem).GetField(eventName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return -1;
            Delegate handlers = field.GetValue(null) as Delegate;
            if (handlers == null) return 0;
            int count = 0;
            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                if (ReferenceEquals(list[i].Target, target)) count++;
            }
            return count;
        }

        private static bool ValidatePetPulseRebase(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            GameObject go = null;
            try
            {
                go = new GameObject("BossRush_F3_PulseProbe");
                Transform target = go.transform;
                target.localScale = Vector3.one;
                PetNestPulse.Play(target, 1.1f, 0.35f);
                PetNestPulse pulse = go.GetComponent<PetNestPulse>();
                // 脉冲期间升级换体型：生产 ApplyModelScale 先写绝对缩放再 Rebase
                Vector3 grown = new Vector3(1.6f, 1.6f, 1.6f);
                target.localScale = grown;
                PetNestPulse.Rebase(target);
                BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Vector3 rebased = (Vector3)typeof(PetNestPulse).GetField("_base", flags).GetValue(pulse);
                // 把计时推到播完，跑一次生产 Update 的收尾分支
                typeof(PetNestPulse).GetField("_elapsed", flags).SetValue(pulse, 10f);
                typeof(PetNestPulse).GetMethod("Update", flags).Invoke(pulse, null);
                Vector3 final = target.localScale;
                metrics = "base=" + rebased.x.ToString("0.00") + ",final=" + final.x.ToString("0.00") + ",enabled=" + pulse.enabled;
                if (BossRushUI.IsGamePaused()) reason = "game_paused_update_skipped";
                else if ((rebased - grown).sqrMagnitude > 1e-6f) reason = "pulse_base_not_rebased";
                else if ((final - grown).sqrMagnitude > 1e-6f) reason = "pulse_restored_old_scale";
                else if (pulse.enabled) reason = "pulse_still_running";
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            return reason == null;
        }
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private static readonly ZombieModeRewardType[] ValidationAttributeRewards =
        {
            ZombieModeRewardType.AttributeMaxHealth, ZombieModeRewardType.AttributeMoveSpeed,
            ZombieModeRewardType.AttributeMeleeDamage, ZombieModeRewardType.AttributeRangedDamage,
            ZombieModeRewardType.AttributeReloadSpeed, ZombieModeRewardType.AttributeDamageReduction,
        };

        internal bool ValidationCheckRewardFixes(out string metrics, out string reason)
        {
            reason = null;
            string capPart = ValidationAttributeCapPool(ref reason);
            string ammoPart = ValidationAmmoFallback(ref reason);
            string heldPart = ValidationHeldGunPreference(ref reason);
            metrics = capPart + ";" + ammoPart + ";" + heldPart;
            return reason == null;
        }

        /// <summary>CR-001：属性压到上限（减伤按生产的负数累加）后两种选卡池都不再出；归零后都回来。</summary>
        private string ValidationAttributeCapPool(ref string reason)
        {
            Dictionary<string, float> snapshot = new Dictionary<string, float>(runState.AttributeBonuses);
            string leaked = string.Empty;
            string missing = string.Empty;
            try
            {
                for (int i = 0; i < ValidationAttributeRewards.Length; i++)
                {
                    ZombieModeRewardType type = ValidationAttributeRewards[i];
                    float cap = GetZombieModeAttributeCap(type);
                    runState.AttributeBonuses[GetZombieModeAttributeKey(type)] =
                        type == ZombieModeRewardType.AttributeDamageReduction ? -cap : cap;
                }
                List<ZombieModeRewardCatalogEntry> capped = BuildZombieModeRewardCatalogEntries(false);
                capped.AddRange(BuildZombieModeRewardCatalogEntries(true));
                for (int i = 0; i < ValidationAttributeRewards.Length; i++)
                    if (ValidationCatalogHas(capped, ValidationAttributeRewards[i])) leaked += ValidationAttributeRewards[i] + "/";

                for (int i = 0; i < ValidationAttributeRewards.Length; i++)
                    runState.AttributeBonuses[GetZombieModeAttributeKey(ValidationAttributeRewards[i])] = 0f;
                List<ZombieModeRewardCatalogEntry> open = BuildZombieModeRewardCatalogEntries(false);
                for (int i = 0; i < ValidationAttributeRewards.Length; i++)
                    if (!ValidationCatalogHas(open, ValidationAttributeRewards[i])) missing += ValidationAttributeRewards[i] + "/";
            }
            finally
            {
                runState.AttributeBonuses.Clear();
                foreach (KeyValuePair<string, float> pair in snapshot) runState.AttributeBonuses[pair.Key] = pair.Value;
            }
            if (reason == null && leaked.Length > 0) reason = "attribute_at_cap_still_offered";
            if (reason == null && missing.Length > 0) reason = "attribute_below_cap_missing";
            return "cap_leaked=" + (leaked.Length > 0 ? leaked : "none") + ",cap_missing_open=" + (missing.Length > 0 ? missing : "none");
        }

        private static bool ValidationCatalogHas(List<ZombieModeRewardCatalogEntry> entries, ZombieModeRewardType type)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].RewardType == type) return true;
            return false;
        }

        /// <summary>
        /// CR-002 / 008：对真实物品表里每一个有弹药的口径，按生产的挑弹逻辑核对——
        /// 开局区间 1–2 一定挑得到；保底 (6,6) 不放开品质（没有 6 品就不发）；区间在可用品质之上取最高档、之下取最低档。只挑 TypeID，不发物品。
        /// </summary>
        private string ValidationAmmoFallback(ref string reason)
        {
            Dictionary<string, List<int>> qualities = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            string[][] tagSets = { ZombieModeRewardTagAmmo, ZombieModeRewardTagBullet };
            for (int s = 0; s < tagSets.Length; s++)
            {
                Tag[] tags = ResolveZombieModeTags(tagSets[s]);
                if (tags == null || tags.Length <= 0) continue;
                ItemFilter filter = new ItemFilter();
                filter.requireTags = tags;
                filter.minQuality = 0;
                filter.maxQuality = ZombieModeAmmoAnyQualityMax;
                int[] ids = ItemAssetsCollection.GetAllTypeIds(filter);
                if (ids == null) continue;
                for (int i = 0; i < ids.Length; i++)
                {
                    ItemMetaData meta = ItemAssetsCollection.GetMetaData(ids[i]);
                    if (meta.id <= 0 || string.IsNullOrEmpty(meta.caliber) || meta.quality < 0) continue;
                    List<int> list;
                    if (!qualities.TryGetValue(meta.caliber, out list)) { list = new List<int>(); qualities[meta.caliber] = list; }
                    if (!list.Contains(meta.quality)) list.Add(meta.quality);
                }
            }

            int checks = 0;
            string fails = string.Empty;
            string highOnly = string.Empty;
            foreach (KeyValuePair<string, List<int>> pair in qualities)
            {
                string caliber = pair.Key;
                List<int> qs = pair.Value;
                qs.Sort();
                int lo = qs[0], hi = qs[qs.Count - 1];
                if (lo > 2) highOnly += caliber + "(" + lo + "-" + hi + ")/";

                checks++;
                int starter = GetZombieModeItemQuality(PickZombieModeAmmoTypeForCaliber(caliber, 1, 2, true));
                int expectStarter = lo > 2 ? lo : -2;
                if (starter < 0 || (expectStarter >= 0 && starter != expectStarter)) fails += caliber + ":starter=" + starter + "/";

                checks++;
                int strictPick = PickZombieModeAmmoTypeForCaliber(caliber, 6, 6, false);
                int strict = strictPick > 0 ? GetZombieModeItemQuality(strictPick) : -1;
                if (qs.Contains(6) ? strict != 6 : strictPick > 0) fails += caliber + ":guaranteed6=" + strict + "/";

                if (hi + 1 < ZombieModeAmmoAnyQualityMax)
                {
                    checks++;
                    int above = GetZombieModeItemQuality(PickZombieModeAmmoTypeForCaliber(caliber, hi + 1, hi + 2, true));
                    if (above != hi) fails += caliber + ":above=" + above + "/";
                }
            }
            if (qualities.Count == 0 && reason == null) reason = "no_ammo_calibers_found";
            if (fails.Length > 0 && reason == null) reason = "ammo_quality_fallback_wrong";
            return "calibers=" + qualities.Count + ",ammo_checks=" + checks + ",high_only=" + (highOnly.Length > 0 ? highOnly : "none")
                + ",ammo_fails=" + (fails.Length > 0 ? fails : "none");
        }

        /// <summary>CR-009：手上拿着能配弹的枪时，补给 / 商人配弹按手上这把找口径。</summary>
        private string ValidationHeldGunPreference(ref string reason)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            ItemAgent_Gun gun = player != null ? player.GetGun() : null;
            Item held = gun != null ? gun.Item : null;
            if (held == null || !IsZombieModeAmmoTargetWeapon(held)) return "held_gun=none";
            Item preferred = TryGetZombieModePreferredWeaponForMerchantAmmo();
            bool ok = ReferenceEquals(preferred, held);
            if (!ok && reason == null) reason = "held_gun_not_preferred";
            return "held_gun=" + held.TypeID + ",preferred=" + (preferred != null ? preferred.TypeID.ToString() : "null")
                + ",caliber=" + TryReadZombieModeItemCaliber(held);
        }
    }

    public static partial class AffinityManager
    {
        private const string ValidationProbeSpouseId = "bossrush_f3_probe_spouse";
        private const string ValidationProbeDecayId = "bossrush_f3_probe_decay";

        /// <summary>
        /// CR-004：叮当商店的戒指条目 + 开店时的已婚判定 + 库存账本，三段都走生产代码。
        /// 已婚 / 单身只在内存里切（没配偶时临时塞一位假配偶，有配偶时临时取消标记），当帧还原，不写存档。
        /// </summary>
        internal static bool ValidationRingRestockMarriage(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            if (!CanWrite) { reason = "affinity_write_barrier"; return false; }
            INPCShopConfig shop = GetNPCConfig(GoblinAffinityConfig.NPC_ID) as INPCShopConfig;
            ShopItemEntry ring = null;
            List<ShopItemEntry> items = shop != null ? shop.GetShopItems() : null;
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null && items[i].TypeID == DiamondRingConfig.TYPE_ID) ring = items[i];
            if (ring == null) { reason = "ring_shop_entry_missing"; return false; }

            string realSpouse = GetCurrentSpouseNpcId();
            AffinityData spouseData = null;
            if (!string.IsNullOrEmpty(realSpouse)) npcDataMap.TryGetValue(realSpouse, out spouseData);
            bool dirtyBefore = isDirty;
            int today = NPCGiftSystem.GetCurrentGameDay();
            NPCShopStockSlot sold = new NPCShopStockSlot { TypeId = ring.TypeID, Remaining = 0, RefillDay = today - 10, Shown = true };
            NPCShopStockSlot soldToday = sold;
            soldToday.RefillDay = today;
            Func<float> roll = () => 0f;
            bool marriedFlag = false, singleFlag = true;
            int marriedRemain = -1, singleRemain = -1, sameDayRemain = -1;
            try
            {
                if (spouseData == null) npcDataMap[ValidationProbeSpouseId] = new AffinityData(ValidationProbeSpouseId) { isMarriedToPlayer = true };
                marriedFlag = NPCShopSystem.IsPlayerMarriedForShop();
                int days = NPCShopStockLedger.EffectiveRestockDays(ring.RestockDays, ring.RestockOnlyWhenSingle, marriedFlag);
                marriedRemain = NPCShopStockLedger.Resolve(true, sold, ring.TypeID, ring.MaxStock, days, ring.Possibility, today, roll).Remaining;

                if (spouseData != null) spouseData.isMarriedToPlayer = false;
                else npcDataMap.Remove(ValidationProbeSpouseId);
                singleFlag = NPCShopSystem.IsPlayerMarriedForShop();
                days = NPCShopStockLedger.EffectiveRestockDays(ring.RestockDays, ring.RestockOnlyWhenSingle, singleFlag);
                singleRemain = NPCShopStockLedger.Resolve(true, sold, ring.TypeID, ring.MaxStock, days, ring.Possibility, today, roll).Remaining;
                sameDayRemain = NPCShopStockLedger.Resolve(true, soldToday, ring.TypeID, ring.MaxStock, days, ring.Possibility, today, roll).Remaining;
            }
            finally
            {
                npcDataMap.Remove(ValidationProbeSpouseId);
                if (spouseData != null) spouseData.isMarriedToPlayer = true;
                isDirty = dirtyBefore;
            }
            bool restored = string.Equals(GetCurrentSpouseNpcId(), realSpouse, StringComparison.Ordinal);
            metrics = "entry=" + ring.RestockDays + "d/single_only=" + ring.RestockOnlyWhenSingle + "/max=" + ring.MaxStock
                + ",real_spouse=" + (realSpouse ?? "none") + ",married=" + marriedFlag + ":" + marriedRemain
                + ",single=" + singleFlag + ":" + singleRemain + ",same_day=" + sameDayRemain + ",restored=" + restored;
            if (!ring.RestockOnlyWhenSingle || ring.RestockDays != 3) reason = "ring_entry_rule_changed";
            else if (!marriedFlag || singleFlag) reason = "marriage_probe_not_applied";
            else if (marriedRemain != 0) reason = "ring_restocked_while_married";
            else if (singleRemain != 1) reason = "ring_not_restocked_when_single";
            else if (sameDayRemain != 0) reason = "ring_restocked_same_day";
            else if (!restored) reason = "spouse_state_not_restored";
            return reason == null;
        }

        /// <summary>
        /// CR-011：一位临时假 NPC 先离开 5 天、又离开 3 天，玩家没来聊天时提示要写 8 天 / 80 点。
        /// 跑生产的 CheckAndApplyDailyDecay / TryConsumeDecayNotice；期间摘掉好感变化事件（免得弹飘字），finally 全部还原。
        /// </summary>
        internal static bool ValidationDecayNoticeAccumulates(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            if (!CanWrite) { reason = "affinity_write_barrier"; return false; }
            if (!AffinityConfig.ENABLE_DAILY_DECAY) { reason = "daily_decay_disabled"; return false; }
            int today = GetCurrentGameDay();
            if (today < 6) throw new SkyIslandSkipCase("game_day_too_early", "day=" + today);
            if (npcDataMap.ContainsKey(ValidationProbeDecayId)) { reason = "probe_id_taken"; return false; }

            bool dirtyBefore = isDirty;
            Action<string, int, int> changed = OnAffinityChanged;
            int first = -1, second = -1;
            DecayNotice notice = default(DecayNotice);
            bool consumed = false;
            try
            {
                OnAffinityChanged = null;
                AffinityData data = new AffinityData(ValidationProbeDecayId) { points = 300, lastChatDay = today - 20, lastDecayCheckDay = today - 5 };
                npcDataMap[ValidationProbeDecayId] = data;
                first = CheckAndApplyDailyDecay(ValidationProbeDecayId);
                data.lastDecayCheckDay = today - 3;
                second = CheckAndApplyDailyDecay(ValidationProbeDecayId);
                consumed = TryConsumeDecayNotice(ValidationProbeDecayId, out notice);
            }
            finally
            {
                npcDataMap.Remove(ValidationProbeDecayId);
                pendingDecayNotices.Remove(ValidationProbeDecayId);
                OnAffinityChanged = changed;
                isDirty = dirtyBefore;
            }
            int step = AffinityConfig.DAILY_DECAY_AMOUNT;
            metrics = "day=" + today + ",first=" + first + ",second=" + second + ",notice=" + (consumed ? notice.Days + "d/" + notice.Points : "none");
            if (first != 5 * step || second != 3 * step) reason = "decay_amount_unexpected";
            else if (!consumed || notice.Days != 8 || notice.Points != 8 * step) reason = "decay_notice_not_accumulated";
            return reason == null;
        }
    }

    internal static partial class PetNestUIPages
    {
        private const string ValidationProbePetId = "bossrush_f3_probe_pet";

        /// <summary>CR-006 / 019 入口侧：「全部孵化（N 枚）」只在两枚以上能孵时挂，N = min(可识别的蛋, 巢空位)。不点（真孵会写存档）。</summary>
        internal static bool ValidationHatchAllPlan(out string metrics, out string reason)
        {
            reason = null;
            List<Item> eggs = PetNestHatchService.CollectAvailableEggs();
            int known = CountKnownEggs(eggs);
            int free = PetNestService.Capacity - PetNestService.PetCount;
            int batch = Math.Min(known, free);
            bool expected = !PetNestService.IsFull && batch >= 2;
            PetNestPageContent page = BuildHatchPage(delegate { }, delegate { }, delegate { });
            string prefix = L10n.T("全部孵化", "Hatch all");
            PetNestActionData button = null;
            for (int i = 0; i < page.Actions.Count; i++)
                if (page.Actions[i] != null && page.Actions[i].Label != null && page.Actions[i].Label.StartsWith(prefix, StringComparison.Ordinal))
                    button = page.Actions[i];
            metrics = "eggs=" + eggs.Count + ",known=" + known + ",free=" + free + ",batch=" + batch
                + ",button=" + (button != null ? button.Label : "none");
            if ((button != null) != expected) reason = expected ? "hatch_all_button_missing" : "hatch_all_button_unexpected";
            else if (button != null && !button.Label.Contains(batch.ToString())) reason = "hatch_all_count_wrong";
            if (reason == null && !expected)
                throw new SkyIslandSkipCase("fewer_than_two_hatchable_eggs", metrics);
            return reason == null;
        }

        internal static bool ValidationDetailActiveExpedition(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            string lineage = ValidationAnyLineage();
            if (lineage == null) { reason = "no_lineage_in_catalog"; return false; }
            List<PetNestExpeditionRecord> records = PetNestExpeditionService.Records;
            int before = records.Count;
            long now = DateTime.UtcNow.Ticks;
            PetNestPetRecord pet = ValidationProbePet(lineage, PetNestPetState.OnExpedition);
            pet.lockedByExpeditionId = "bossrush_f3_probe_new";
            PetNestExpeditionRecord settledOld = ValidationProbeRecord("bossrush_f3_probe_old", now - TimeSpan.TicksPerHour, true);
            PetNestExpeditionRecord current = ValidationProbeRecord("bossrush_f3_probe_new", now + 10 * TimeSpan.TicksPerMinute, false);
            PetNestDetailData detail;
            try
            {
                // 上一趟已结算、未揭晓的记录排在前面（旧写法按 petId 先撞上它）
                records.Insert(0, settledOld);
                records.Add(current);
                detail = BuildPetDetail(pet, null, ValidationContext());
            }
            finally
            {
                records.Remove(settledOld);
                records.Remove(current);
            }
            bool showsOld = detail.FooterText == L10n.T("远征已归来", "Back from the expedition")
                || ValidationHasAction(detail, L10n.T("查看结果", "See results"));
            metrics = "footer=" + detail.FooterText + ",live=" + (detail.LiveFooter != null) + ",records=" + before + "->" + records.Count;
            if (records.Count != before) reason = "probe_records_not_removed";
            else if (showsOld) reason = "detail_shows_settled_previous_trip";
            else if (detail.LiveFooter == null) reason = "detail_missing_countdown";
            return reason == null;
        }

        internal static bool ValidationDetailSlotsFull(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            string lineage = ValidationAnyLineage();
            if (lineage == null) { reason = "no_lineage_in_catalog"; return false; }
            List<PetNestExpeditionRecord> records = PetNestExpeditionService.Records;
            int before = records.Count;
            int used, cap;
            PetNestExpeditionSlots.HasFreeSlot(out used, out cap);
            PetNestPetRecord pet = ValidationProbePet(lineage, PetNestPetState.InNest);
            string send = L10n.T("派去远征", "Send on expedition");
            bool baselineSend = false;
            PetNestDetailData full;
            List<PetNestExpeditionRecord> fillers = new List<PetNestExpeditionRecord>();
            try
            {
                if (used < cap) baselineSend = ValidationHasAction(BuildPetDetail(pet, null, ValidationContext()), send);
                long now = DateTime.UtcNow.Ticks;
                for (int i = used; i < cap; i++)
                {
                    PetNestExpeditionRecord filler = ValidationProbeRecord("bossrush_f3_probe_fill_" + i, now + 10 * TimeSpan.TicksPerMinute, false);
                    filler.petId = "bossrush_f3_probe_fill_pet_" + i;
                    fillers.Add(filler);
                    records.Add(filler);
                }
                full = BuildPetDetail(pet, null, ValidationContext());
            }
            finally
            {
                for (int i = 0; i < fillers.Count; i++) records.Remove(fillers[i]);
            }
            string expectedFooter = PetNestLocalization.DescribeFailure("expedition_slots_full");
            bool fullSend = ValidationHasAction(full, send);
            metrics = "slots=" + used + "/" + cap + ",baseline_send=" + baselineSend + ",full_send=" + fullSend
                + ",footer=" + full.FooterText + ",records=" + before + "->" + records.Count;
            if (records.Count != before) reason = "probe_records_not_removed";
            else if (used < cap && !baselineSend) reason = "send_missing_with_free_slot";
            else if (fullSend) reason = "send_shown_when_slots_full";
            else if (full.FooterText != expectedFooter) reason = "slots_full_reason_missing";
            return reason == null;
        }

        internal static bool ValidationDetailScarPrice(out string metrics, out string reason)
        {
            reason = null;
            string lineage = ValidationAnyLineage();
            if (lineage == null) { metrics = string.Empty; reason = "no_lineage_in_catalog"; return false; }
            PetNestPetRecord pet = ValidationProbePet(lineage, PetNestPetState.InNest);
            pet.scars = new List<PetNestScarRecord>
            {
                new PetNestScarRecord { ticks = DateTime.UtcNow.Ticks, place = PetNestTuning.DestinationStormSea, killer = "probe",
                    statKey = "WalkSpeed", percent = PetNestTuning.ScarModifierFraction },
            };
            PetNestDetailData detail = BuildPetDetail(pet, null, ValidationContext());
            int cost = PetNestTuning.ScarHealSoulCost;
            string label = L10n.T("治一道疤（" + cost + " 遗魂）", "Treat a scar (" + cost + " souls)");
            int souls = PetNestService.GetSouls(lineage);
            bool shown = ValidationHasAction(detail, label);
            metrics = "souls=" + souls + "/" + cost + ",button=" + shown;
            if (!shown) reason = "scar_button_missing_or_unpriced";
            return reason == null;
        }

        internal static bool ValidationMuseumAchievementFrame(out string metrics, out string reason)
        {
            reason = null;
            PetNestPageContent page = BuildMuseumPage();
            int rows = 0, frameOnly = 0, shiny = 0;
            List<PetNestCardData> cards = new List<PetNestCardData>(page.Cards);
            for (int s = 0; s < page.Sections.Count; s++)
                if (page.Sections[s] != null) cards.AddRange(page.Sections[s].Items);
            for (int i = 0; i < cards.Count; i++)
            {
                PetNestCardData card = cards[i];
                if (card == null || Array.IndexOf(TamingAchievementIds, card.Key) < 0) continue;
                rows++;
                if (card.ShinyFrameOnly) frameOnly++;
                if (card.Shiny) shiny++;
            }
            metrics = "rows=" + rows + ",frame_only=" + frameOnly + ",unlocked=" + shiny;
            if (rows == 0) reason = "achievement_rows_missing";
            else if (frameOnly != rows) reason = "achievement_row_title_shimmer";
            return reason == null;
        }

        private static string ValidationAnyLineage()
        {
            IList<PetNestLineageInfo> all = PetNestLineageCatalog.All;
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && !string.IsNullOrEmpty(all[i].LineageKey)) return all[i].LineageKey;
            return null;
        }

        private static PetNestPetRecord ValidationProbePet(string lineage, PetNestPetState state)
        {
            return new PetNestPetRecord { id = ValidationProbePetId, lineageKey = lineage, level = 1, state = (int)state };
        }

        private static PetNestExpeditionRecord ValidationProbeRecord(string id, long returnTicks, bool settled)
        {
            return new PetNestExpeditionRecord
            {
                id = id, petId = ValidationProbePetId, destinationId = PetNestTuning.DestinationStormSea,
                departTicks = returnTicks - TimeSpan.TicksPerHour, returnTicks = returnTicks, settled = settled,
            };
        }

        private static PetNestNestPageContext ValidationContext()
        {
            return new PetNestNestPageContext
            {
                BatchSelection = new HashSet<string>(),
                Refresh = delegate { }, Select = delegate { }, Rename = delegate { }, Release = delegate { },
                SetBatchMode = delegate { }, ToggleBatch = delegate { }, SendOnExpedition = delegate { },
                CycleSort = delegate { }, ShowResults = delegate { },
            };
        }

        private static bool ValidationHasAction(PetNestDetailData detail, string label)
        {
            if (detail == null || detail.Actions == null) return false;
            for (int i = 0; i < detail.Actions.Count; i++)
                if (detail.Actions[i] != null && detail.Actions[i].Label == label) return true;
            return false;
        }
    }
}
#endif
