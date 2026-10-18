// ============================================================================
// PetNestCompanionCare.cs - 随从行动方式、喂食与摸摸（2026-10-12 玩家反馈轮）
// ============================================================================
// 玩家反馈：宠物感应范围比人大，常常是「它拽着我打」；没有正经养成，带出去打一架也看不出变化。
// 本文件只放与随从互动有关的状态与入口：
//   - 行动方式：跟随（默认）/ 主动 / 回避。状态存在崽的记录里（SCHEMA+ behaviour），
//     执行交给 PetNestCompanionAgent；交战触发复用 PetNestDownedHandler 已有的全场受伤订阅（只在带崽时存在）。
//   - 喂食：从随身背包挑最便宜的一份食物，给经验并回血，走现有 AddExp 与等级成长；同一只崽 10 分钟冷却。
//   - 摸摸：按性格回一句话，不给数值。
// 入口挂在崽身上现成的背包交互组里，不新建面板、不加常驻扫描。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using Duckov.ItemUsage;
using BossRush.Utils;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>随从行动方式。数值写进存档，不得改序。</summary>
    internal enum PetNestBehaviour
    {
        /// <summary>跟着主人；主人或崽交战后才出手，脱战 10 秒收手。</summary>
        Follow = 0,
        /// <summary>主动索敌（加入行动方式之前的老行为）。</summary>
        Active = 1,
        /// <summary>不出手、贴身跟随、无敌，并定时清掉流血中毒等负面状态。</summary>
        Avoid = 2,
    }

    internal static class PetNestCareService
    {
        /// <summary>回避档清理的负面状态。只认官方互斥标签，正面、未知的状态一律不动。</summary>
        internal static readonly Buff.BuffExclusiveTags[] AvoidCleansedTags =
        {
            Buff.BuffExclusiveTags.Bleeding,
            Buff.BuffExclusiveTags.Poison,
            Buff.BuffExclusiveTags.Nauseous,
            Buff.BuffExclusiveTags.Pain,
            Buff.BuffExclusiveTags.Burning,
            Buff.BuffExclusiveTags.Electric,
            Buff.BuffExclusiveTags.Freeze,
            Buff.BuffExclusiveTags.Stun,
        };

        internal static PetNestBehaviour Resolve(PetNestPetRecord pet)
        {
            int value = pet != null ? pet.behaviour : 0;
            return value >= 0 && value <= 2 ? (PetNestBehaviour)value : PetNestBehaviour.Follow;
        }

        internal static string Describe(PetNestBehaviour behaviour)
        {
            switch (behaviour)
            {
                case PetNestBehaviour.Active: return L10n.T("主动", "Aggressive");
                case PetNestBehaviour.Avoid: return L10n.T("回避", "Evasive");
                default: return L10n.T("跟随", "Follow");
            }
        }

        internal static string DescribeEffect(PetNestBehaviour behaviour)
        {
            switch (behaviour)
            {
                case PetNestBehaviour.Active: return L10n.T("看到敌人就上", "Engages any enemy it sees");
                case PetNestBehaviour.Avoid: return L10n.T("不出手、不掉血，定时清掉流血中毒", "Won't fight, takes no damage, shakes off bleeding and poison");
                default: return L10n.T("你开打它才帮忙，脱战就回来", "Only fights when you do, then comes back");
            }
        }

        /// <summary>轮换到下一种行动方式并落档；在场的随从立刻按新方式行动。</summary>
        internal static bool TryCycleBehaviour(string petId, out PetNestBehaviour now)
        {
            now = Next(Resolve(PetNestService.TryGetPet(petId)));
            return TrySetBehaviour(petId, now);
        }

        /// <summary>轮换顺序里的下一种（局内选项名写「点按换成 X」用）。</summary>
        internal static PetNestBehaviour Next(PetNestBehaviour current)
        {
            return (PetNestBehaviour)(((int)current + 1) % 3);
        }

        /// <summary>
        /// 直接设成某一种并落档；在场的随从立刻按新方式行动。巢页详情的三段选择与局内轮换共用这一个写入口。
        /// </summary>
        internal static bool TrySetBehaviour(string petId, PetNestBehaviour target)
        {
            string error;
            if (string.IsNullOrEmpty(petId) || !PetNestPersistenceAccess.BeginTransaction(out error)) return false;
            try
            {
                PetNestPetRecord pet = PetNestService.TryGetPet(petId);
                if (pet == null)
                {
                    PetNestPersistenceAccess.AbortTransaction();
                    return false;
                }
                PetNestBehaviour now = target;
                pet.behaviour = (int)now;
                if (!PetNestService.StageCommit()) return false;
                PetNestCompanionRuntime.ApplyBehaviour(petId, now);
                return true;
            }
            catch (Exception e)
            {
                PetNestPersistenceAccess.AbortTransaction();
                ModBehaviour.DevLog("[PetNest] 切换行动方式失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 喂一份食物：挑随身背包里最便宜的一份（只认可堆叠或不按次数计的食物，避免吞掉多次用量的整件），
        /// 给 <see cref="PetNestTuning.FeedExp"/> 经验并回 10% 生命。经验与冷却先进候选事务，扣掉食物才提交；
        /// 提交失败把食物原样退回背包。
        /// </summary>
        internal static void TryFeed(string petId, CharacterMainControl companion)
        {
            Transform bubbleTarget = companion != null ? companion.transform : null;
            PetNestPetRecord live = PetNestService.TryGetPet(petId);
            if (live == null) return;
            int minutes = FeedCooldownMinutesLeft(live);
            if (minutes > 0)
            {
                Say(bubbleTarget, L10n.T("还饱着呢，" + minutes + " 分钟后再喂", "Still full. Try again in " + minutes + " min"));
                return;
            }

            Item food = FindCheapestFood();
            if (food == null)
            {
                Say(bubbleTarget, L10n.T("背包里没有能吃的东西", "Nothing edible in your pack"));
                return;
            }
            string foodName = food.DisplayName;

            string error;
            if (!PetNestPersistenceAccess.BeginTransaction(out error)) return;
            SkyIslandInventoryTransaction meal = null;
            bool accepted = false;
            PetNestPetRecord pet = null;
            int oldLevel = 0;
            try
            {
                pet = PetNestService.TryGetPet(petId);
                if (pet == null)
                {
                    PetNestPersistenceAccess.AbortTransaction();
                    return;
                }
                // 喂食会改变主角物品；任何后续落盘都必须把食物与成长放在同一批。
                // 复用背包的局内资产闸，无需等待基地仓库初始化。
                if (!PetNestSaveCoordinator.RequireBackpackAssetSnapshot(out error)
                    || !SkyIslandInventoryTransaction.TryReserve(CharacterMainControl.Main,
                        new[] { new SkyIslandIngredient(food.TypeID, 1) }, out meal))
                {
                    PetNestPersistenceAccess.AbortTransaction();
                    return;
                }
                oldLevel = pet.level;
                PetNestProgressionService.AddExp(pet, PetNestTuning.FeedExp);
                pet.lastFeedUtcTicks = DateTime.UtcNow.Ticks;
                if (!PetNestService.StageCommit()) return;
                // 候选包一旦接纳就不能退款；Commit/Dispose 只处置实际预留的原食物。
                accepted = true;
                meal.Commit();
            }
            catch (Exception e)
            {
                if (!accepted) PetNestPersistenceAccess.AbortTransaction();
                ModBehaviour.DevLog("[PetNest] 喂食失败: " + e.Message);
            }
            finally
            {
                if (meal != null) meal.Dispose();
            }
            if (!accepted) return;

            // 物理写失败保留资产义务，交给已有协调器重试；不返食物、不撤已接纳的成长。
            if (!PetNestSaveCoordinator.RequestAssetFlush(out error))
                ModBehaviour.DevLog("[PetNest] 喂食保存待重试: " + error);
            HealCompanion(companion);
            string line = L10n.T("吃掉了" + foodName + "，经验 +" + PetNestTuning.FeedExp,
                "Ate the " + foodName + ". +" + PetNestTuning.FeedExp + " exp");
            if (pet.level != oldLevel)
            {
                try
                {
                    PetNestCompanionRuntime.RefreshProgression(pet);
                    PetNestBaseIdleSpawner.RefreshProgression(companion, pet);
                }
                catch (Exception e) { ModBehaviour.DevLog("[PetNest] 喂食升级表现刷新失败: " + e.Message); }
                line += L10n.T("，升到 Lv" + pet.level + " 了！", ". Reached Lv" + pet.level + "!");
            }
            Say(bubbleTarget, line);
        }

        /// <summary>喂食冷却还剩几分钟（向上取整）；0 = 现在就能喂。选项名与喂食本身共用。</summary>
        internal static int FeedCooldownMinutesLeft(PetNestPetRecord pet)
        {
            if (pet == null || pet.lastFeedUtcTicks <= 0) return 0;
            long remaining = pet.lastFeedUtcTicks
                + TimeSpan.FromMinutes(PetNestTuning.FeedCooldownMinutes).Ticks - DateTime.UtcNow.Ticks;
            return remaining > 0 ? Math.Max(1, (int)Math.Ceiling(TimeSpan.FromTicks(remaining).TotalMinutes)) : 0;
        }

        /// <summary>
        /// 喂食选项名（审查 F-2，口径同付费服务：冷却中照挂，名字上写还要等多久）：
        /// 能喂时写会喂掉哪份食物，冷却中写剩几分钟，背包没吃的也写出来，不让玩家点了才知道。
        /// </summary>
        internal static string DescribeFeedOption(PetNestPetRecord pet)
        {
            int minutes = FeedCooldownMinutesLeft(pet);
            if (minutes > 0) return L10n.T("喂食（还要 " + minutes + " 分钟）", "Feed (" + minutes + " min)");
            Item food = FindCheapestFood();
            if (food == null) return L10n.T("喂食（背包里没吃的）", "Feed (nothing edible)");
            string name;
            try { name = food.DisplayName; }
            catch (Exception) { name = null; }
            return string.IsNullOrEmpty(name) ? L10n.T("喂食", "Feed") : L10n.T("喂食：", "Feed: ") + name;
        }

        /// <summary>选项名每秒最多重写一次：官方菜单在玩家走近时读名字，冷却分钟数与背包里的食物都会变。</summary>
        private static float _nextFeedLabelRefresh;

        internal static void ForceFeedLabelRefresh(string petId)
        {
            _nextFeedLabelRefresh = 0f;
            RefreshFeedLabel(petId);
        }

        internal static void RefreshFeedLabel(string petId)
        {
            float now = Time.unscaledTime;
            if (now < _nextFeedLabelRefresh) return;
            _nextFeedLabelRefresh = now + 1f;
            try
            {
                LocalizationHelper.InjectLocalization(PetNestFeedOption.Key, DescribeFeedOption(PetNestService.TryGetPet(petId)));
            }
            catch (Exception) { /* 选项名刷新失败沿用旧名 */ }
        }

        /// <summary>摸摸：按性格回一句话，不给数值、不落档。</summary>
        internal static void Pat(PetNestPetRecord pet, CharacterMainControl companion)
        {
            if (companion == null) return;
            string id = pet != null ? pet.personalityId : null;
            string line;
            if (id == PetNestTuning.PersonalityReckless) line = L10n.T("蹭了蹭你的手，又往前看了看，像在找架打", "Nuzzles your hand, then looks ahead like it's itching for a fight");
            else if (id == PetNestTuning.PersonalityCautious) line = L10n.T("先缩了一下，确认是你才把头靠过来", "Flinches first, then leans in once it sees it's you");
            else if (id == PetNestTuning.PersonalityLazy) line = L10n.T("翻了个身，露出肚皮，不打算起来", "Rolls over, belly up, with no plans to get up");
            else if (id == PetNestTuning.PersonalityLoyal) line = L10n.T("贴着你的腿转了一圈，尾巴摇个不停", "Circles your leg, tail wagging nonstop");
            else line = L10n.T("眯起眼睛，发出满足的咕噜声", "Squints and makes a contented rumble");
            Say(companion.transform, line);
            // 摸了会有反应（审查 A-8）：视觉根轻轻放大再回来，只动模型、不动碰撞体，0.35 秒播完
            try { if (companion.modelRoot != null) PetNestPulse.Play(companion.modelRoot, 1.1f, 0.35f); }
            catch (Exception) { /* 少一个小动作不致命 */ }
        }

        /// <summary>
        /// 全场受伤回调里的交战判定（PetNestDownedHandler 只在带崽时订阅，O(1)，零分配）：
        /// 主人打了敌人、敌人打了主人或崽，都算交战，跟随档的崽据此出手。
        /// </summary>
        internal static void NotifyHurt(Health victim, DamageInfo info)
        {
            PetNestCompanionAgent followAgent = PetNestCompanionRuntime.CompanionAgent;
            if (followAgent == null || followAgent.Behaviour != PetNestBehaviour.Follow) return;
            CharacterMainControl companion = followAgent.Self;
            if (companion == null || victim == null) return;
            CharacterMainControl attacker = info.fromCharacter;
            if (attacker == null || attacker == companion) return;
            CharacterMainControl master = CharacterMainControl.Main;
            CharacterMainControl enemy = null;
            if (attacker == master)
            {
                CharacterMainControl target = victim.TryGetCharacter();
                if (target != null && target != companion && Team.IsEnemy(companion.Team, target.Team)) enemy = target;
            }
            else if ((victim == companion.Health || (master != null && victim == master.Health))
                && Team.IsEnemy(companion.Team, attacker.Team))
            {
                enemy = attacker;
            }
            if (enemy == null) return;
            PetNestCompanionAgent agent = PetNestCompanionRuntime.CompanionAgent;
            if (agent != null && agent.Behaviour == PetNestBehaviour.Follow) agent.NotifyEngaged(enemy);
        }

        /// <summary>在崽身上的背包交互组里挂「行动方式 / 喂食 / 摸摸」三个子选项。幂等。</summary>
        internal static void AttachOptions(InteractableBase host, string petId, CharacterMainControl companion)
        {
            if (host == null || companion == null || string.IsNullOrEmpty(petId)) return;
            try
            {
                List<InteractableBase> group = NPCInteractionGroupHelper.GetOrCreateGroupList(host, "[PetNestCare]");
                if (group == null) return;
                host.interactableGroup = true;
                if (host.GetComponentInChildren<PetNestBehaviourOption>(true) == null)
                    NPCInteractionGroupHelper.AddSubInteractable<PetNestBehaviourOption>(host.transform, "PetNestBehaviourOption", group,
                        option => option.Bind(petId, companion));
                if (host.GetComponentInChildren<PetNestFeedOption>(true) == null)
                    NPCInteractionGroupHelper.AddSubInteractable<PetNestFeedOption>(host.transform, "PetNestFeedOption", group,
                        option => option.Bind(petId, companion));
                if (host.GetComponentInChildren<PetNestPatOption>(true) == null)
                    NPCInteractionGroupHelper.AddSubInteractable<PetNestPatOption>(host.transform, "PetNestPatOption", group,
                        option => option.Bind(petId, companion));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 照料选项装配失败: " + e.Message);
            }
        }

        /// <summary>选项名称的本地化：行动方式这一项按当前方式显示，切换后重新注入。</summary>
        internal static void InjectOptionLocalization(PetNestBehaviour current)
        {
            // 写明点了会换成哪一种（审查 F-1）：三态轮换看不到下一种，玩家要点两次、读两次气泡才到想要的那档。
            // 想直接选哪一种，巢页详情里有三段选择。
            LocalizationHelper.InjectLocalization(PetNestBehaviourOption.Key,
                L10n.T("行动方式：", "Behavior: ") + Describe(current)
                + L10n.T("（点按换成" + Describe(Next(current)) + "）", " (tap for " + Describe(Next(current)) + ")"));
            LocalizationHelper.InjectLocalization(PetNestFeedOption.Key, DescribeFeedOption(PetNestService.DeployedPet));
            LocalizationHelper.InjectLocalization(PetNestPatOption.Key, L10n.T("摸摸", "Pet"));
        }

        /// <summary>借宿主现有语言重注入刷新照料名；没有在场宠物时不读取存档或准备交互。</summary>
        internal static void RefreshOptionLocalization()
        {
            if (PetNestCompanionRuntime.CompanionCharacter == null && PetNestBaseIdleSpawner.ActiveCount == 0) return;
            InjectOptionLocalization(Resolve(PetNestService.DeployedPet));
        }

        private static Item FindCheapestFood()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Inventory inventory = player != null && player.CharacterItem != null ? player.CharacterItem.Inventory : null;
            if (inventory == null || inventory.Content == null) return null;
            Item best = null;
            for (int i = 0; i < inventory.Content.Count; i++)
            {
                Item item = inventory.Content[i];
                if (item == null) continue;
                try
                {
                    if (!item.Stackable && item.MaxDurability > 0f) continue;
                    FoodDrink food = item.GetComponent<FoodDrink>();
                    if (food == null || food.energyValue <= 0f) continue;
                    if (best == null || item.Value < best.Value) best = item;
                }
                catch (Exception) { /* 单件读取失败跳过 */ }
            }
            return best;
        }

        private static void HealCompanion(CharacterMainControl companion)
        {
            if (BossRushCoop.IsClient)
            {
                PetNestCoopRuntimeModule.RequestLocalDeployment(PetNestService.DeployedPet);
                return;
            }
            try
            {
                Health health = companion != null ? companion.Health : null;
                if (health == null || health.IsDead || health.MaxHealth <= 0f) return;
                health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + health.MaxHealth * PetNestTuning.FeedHealRatio));
            }
            catch (Exception) { /* 回血失败不影响经验 */ }
        }

        private static void Say(Transform target, string text)
        {
            if (target == null || string.IsNullOrEmpty(text)) return;
            try { DialogueBubblesManager.Show(text, target, -1f, false, false, -1f, 2.5f).Forget(); }
            catch (Exception) { /* 气泡失败只是少一句话 */ }
        }
    }

    /// <summary>照料子选项基类：纯逻辑交互体，不显示世界标记（形态照 PetNestSubInteractableBase）。</summary>
    public abstract class PetNestCareOptionBase : InteractableBase
    {
        internal string PetId;
        internal CharacterMainControl Companion;
        private int _slot = -1;
        protected abstract string NameKey { get; }

        internal void Bind(string petId, CharacterMainControl companion)
        {
            PetId = petId;
            Companion = companion;
            _slot = SavesSystem.CurrentSlot;
        }

        protected override void Awake()
        {
            try
            {
                overrideInteractName = true;
                _overrideInteractNameKey = NameKey;
                InteractName = NameKey;
            }
            catch (Exception) { /* 名字覆盖失败显示默认名 */ }
            try { NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[PetNestCare]"); }
            catch (Exception) { /* 分组列表初始化失败不致命 */ }
            try { base.Awake(); }
            catch (Exception e) { ModBehaviour.DevLog("[PetNest] 照料选项 Awake 失败: " + e.Message); }
            try { MarkerActive = false; }
            catch (Exception) { /* 多一个标记不致命 */ }
        }

        protected override bool IsInteractable()
        {
            bool ok = !string.IsNullOrEmpty(PetId) && !SceneLoader.IsSceneLoading
                && _slot == SavesSystem.CurrentSlot && Companion != null
                && Companion.Health != null && !Companion.Health.IsDead
                && PetNestPersistenceAccess.CanStoreAll;
            if (ok) OnQueried();
            return ok;
        }

        /// <summary>官方菜单来问「能不能交互」时的钩子（玩家走近时）；喂食用它刷新选项名，默认什么都不做。</summary>
        protected virtual void OnQueried() { }

        protected override void OnTimeOut()
        {
            if (!IsInteractable()) return;
            try { base.OnTimeOut(); } catch (Exception) { /* 同宿主 */ }
            try { Run(); }
            catch (Exception e) { ModBehaviour.DevLog("[PetNest] 照料操作失败: " + e.Message); }
        }

        protected abstract void Run();
    }

    public sealed class PetNestBehaviourOption : PetNestCareOptionBase
    {
        internal const string Key = "BossRush_PetNest_Care_Behaviour";
        protected override string NameKey { get { return Key; } }

        protected override void Run()
        {
            PetNestBehaviour now;
            if (!PetNestCareService.TryCycleBehaviour(PetId, out now)) return;
            PetNestCareService.InjectOptionLocalization(now);
            CharacterMainControl companion = Companion;
            if (companion != null)
            {
                try
                {
                    DialogueBubblesManager.Show(PetNestCareService.Describe(now) + "：" + PetNestCareService.DescribeEffect(now),
                        companion.transform, -1f, false, false, -1f, 2.5f).Forget();
                }
                catch (Exception) { /* 气泡失败只是少一句话 */ }
            }
        }
    }

    public sealed class PetNestFeedOption : PetNestCareOptionBase
    {
        internal const string Key = "BossRush_PetNest_Care_Feed";
        protected override string NameKey { get { return Key; } }

        protected override void OnQueried()
        {
            PetNestCareService.RefreshFeedLabel(PetId);
        }

        protected override void Run()
        {
            PetNestCareService.TryFeed(PetId, Companion);
            PetNestCareService.ForceFeedLabelRefresh(PetId);
        }
    }

    public sealed class PetNestPatOption : PetNestCareOptionBase
    {
        internal const string Key = "BossRush_PetNest_Care_Pat";
        protected override string NameKey { get { return Key; } }

        protected override void Run()
        {
            PetNestCareService.Pat(PetNestService.TryGetPet(PetId), Companion);
        }
    }
}
