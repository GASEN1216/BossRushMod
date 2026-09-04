using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private sealed class KitProbeRequest
        {
            internal bool Done;
            internal bool Abandoned;
            internal ModeHSpawnHandle Handle;
            internal string Error;
        }

        // 在 H 缓存验收的 Drafting 租约内执行：复用真实认证池、隔离生成桥和装配事务。
        // 只检查本用例创建的临时角色；不写选秀、战斗、结算或玩家装备状态。
        private IEnumerator RunModeHStarterKits(ModeHSupportedMap map)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<string> kitIds = ModeHLoadoutKitRegistry.GetStarterKitIds();
            List<string> details = new List<string>();
            int passed = 0;
            foreach (string kitId in kitIds)
            {
                if (ShouldAbort()) break;
                ModeHResolvedKit kit = ModeHLoadoutKitRegistry.GetKit(kitId);
                ModeHProfileTemplate template = FindKitProbeProfile(kit);
                if (template == null)
                { details.Add(kitId + "=compatible_certified_profile_missing"); continue; }
                KitProbeRequest request = new KitProbeRequest();
                ModeHKitApplication application = null;
                string reason = null;
                string metrics = string.Empty;
                bool valid = false;
                try
                {
                    CreateKitProbe(request, template, map).Forget();
                    float deadline = Time.realtimeSinceStartup + CaseTimeoutSeconds;
                    while (!request.Done && !ShouldAbort() && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    if (!request.Done) reason = "create_timeout_or_cancel";
                    else if (request.Handle == null) reason = request.Error ?? "create_null";
                    else
                    {
                        try
                        {
                            valid = ModeHLoadoutKitApplicator.TryApply(request.Handle, new[] { kitId },
                                out application, out reason);
                            if (valid) valid = VerifyKitProbe(request.Handle, kit, application, out metrics);
                            if (!valid && reason == null) reason = "slot_or_ammo_readback_failed";
                        }
                        catch (Exception e) { reason = "kit_probe_exception:" + e; valid = false; }
                    }
                }
                finally
                {
                    request.Abandoned = true;
                    ModeHLoadoutKitApplicator.Recycle(application);
                    ModeHSpawnBridge.Recycle(request.Handle);
                    request.Handle = null;
                }
                if (valid) passed++;
                details.Add(kitId + "=" + (valid ? metrics : reason));
                yield return null;
            }
            // 中止（玩家取消 / 套件超时 / 连续脏状态）一律记 SKIP，与全仓其余用例同口径
            // （RunIsolatedCase、RunSyncCaseGated、ITEM_FACTORY、MODE_ZOMBIE_EXTRACTION）。
            // 旧写法把中止折进 allPassed，取消时唯一可能的结果就是 FAIL，理由还写成
            // 「整备物化失败」这种指向产品缺陷的措辞——制造假红，掩盖真红。
            if (ShouldAbort())
            {
                Record("MODE_H_STARTER_KITS", "SKIP", sw.ElapsedMilliseconds,
                    "kits=" + passed + "/" + kitIds.Count + ",aborted=" + DescribeAbortReason(),
                    DescribeAbortReason());
                yield break;
            }

            bool allPassed = kitIds.Count > 0 && passed == kitIds.Count;
            Record("MODE_H_STARTER_KITS", allPassed ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "kits=" + passed + "/" + kitIds.Count + ",details=" + string.Join(";", details.ToArray()),
                allPassed ? null : "starter_kit_materialization_failed");
        }

        private static ModeHProfileTemplate FindKitProbeProfile(ModeHResolvedKit kit)
        {
            if (kit == null || !kit.Available || kit.Spec == null) return null;
            foreach (string key in ModeHPresetRegistry.ProductionKeys)
            {
                ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(key);
                if (template != null
                    && ModeHLoadoutKitRegistry.IsArchetypeCompatible(kit.Spec, template.ArchetypeId)
                    && ModeHLoadoutKitRegistry.IsProfileCompatible(kit.Spec, template.ProfileTemplateId))
                    return template;
            }
            return null;
        }

        private static async UniTaskVoid CreateKitProbe(KitProbeRequest request,
            ModeHProfileTemplate template, ModeHSupportedMap map)
        {
            ModeHSpawnHandle handle = null;
            try
            {
                handle = await ModeHSpawnBridge.CreateIsolatedAsync(
                    ModeHPresetRegistry.GetAuditedPreset(template.StableKey), template.StableKey,
                    Teams.scav, map.StagingPos, null);
                if (handle != null) handle.ProfileId = template.ProfileTemplateId;
                if (!request.Abandoned) request.Handle = handle;
            }
            catch (Exception e) { request.Error = "create_exception:" + e; }
            finally
            {
                // 超时/取消后抵达的异步产物仍由本请求回收，不交给下一用例或全场清理兜底。
                if (request.Abandoned) ModeHSpawnBridge.Recycle(handle);
                request.Done = true;
            }
        }

        private static bool VerifyKitProbe(ModeHSpawnHandle handle, ModeHResolvedKit kit,
            ModeHKitApplication application, out string metrics)
        {
            Item character = handle.Character.CharacterItem;
            Item equipped = character.Slots.GetSlot(kit.Spec.ReplaceSlot).Content;
            metrics = "slot=" + (equipped == null ? 0 : equipped.TypeID);
            if (equipped == null || equipped.TypeID != kit.ResolvedTypeId) return false;
            if (kit.Spec.AmmoCount == 0) return true;
            ItemSetting_Gun gun = equipped.GetComponent<ItemSetting_Gun>();
            if (gun == null) return false;
            int total = 0;
            foreach (Item item in application.CreatedItems)
            {
                if (item == null || item == equipped) continue;
                if (item.TypeID != gun.TargetBulletID || item.StackCount <= 0
                    || item.StackCount > item.MaxStackCount
                    || (item.InInventory != equipped.Inventory && item.InInventory != character.Inventory))
                    return false;
                total += item.StackCount;
            }
            int loaded = gun.GetBulletCount();
            metrics += ",loaded=" + loaded + ",usable=" + gun.BulletCount + ",total=" + total;
            return loaded == Math.Min(gun.Capacity, kit.Spec.AmmoCount)
                && gun.BulletCount == loaded && total == kit.Spec.AmmoCount;
        }
    }
}
