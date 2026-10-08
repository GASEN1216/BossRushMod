using System;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>只在第六章报名石召唤；不登记普通波次、不预载女巫或武器资源。</summary>
    internal static class SandstormChampionBoss
    {
        internal const string NameKey = "BossRush_Campaign_FinalBoss_Name";

        internal static async UniTask<CharacterMainControl> SpawnAsync(ModBehaviour owner, Vector3 position, Func<bool> isCurrent)
        {
            CharacterMainControl boss = null;
            CharacterRandomPreset runtimePreset = null;
            Item reward = null;
            int sceneHandle = SceneManager.GetActiveScene().handle;
            try
            {
                CharacterRandomPreset source = FindBasePreset();
                if (source == null) throw new InvalidOperationException("champion_base_preset_unavailable");
                // 在官方生成之前关掉底模的技能与自动掏枪，避免异步生成期间先释放原 Boss 技能。
                runtimePreset = UnityEngine.Object.Instantiate(source);
                runtimePreset.name = SandstormChampionConfig.PresetInstanceName;
                runtimePreset.nameKey = NameKey;
                runtimePreset.isBoss = true;
                runtimePreset.showName = true;
                runtimePreset.showHealthBar = true;
                runtimePreset.hasSkill = false;
                runtimePreset.skillPfb = null;
                runtimePreset.defaultWeaponOut = false;
                boss = await runtimePreset.CreateCharacterAsync(position, Vector3.forward,
                    SceneManager.GetActiveScene().buildIndex, null, false);
                if (boss == null || boss.Health == null || boss.CharacterItem == null)
                    throw new InvalidOperationException("champion_character_unavailable");
                if (!IsCurrent(owner, sceneHandle, isCurrent)) throw new OperationCanceledException();

                // 先暂停底模 AI，再分帧构建程序化外形，防止初始化间隙开枪或移动。
                BossAIController ai = new BossAIController(boss, "SandstormChampion");
                ai.Pause();
                RemoveOriginalCombat(boss);
                boss.gameObject.name = SandstormChampionConfig.GameObjectName;
                boss.characterPreset = runtimePreset;
                boss.isBossCharacter = true;
                if (BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType != null)
                    BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType.SetValue(runtimePreset, CharacterIconTypes.boss);
                if (!Team.IsEnemy(Teams.player, boss.Team)) boss.SetTeam(Teams.wolf);
                // 角色已初始化，不能缩放根碰撞体；沙尘精的尺寸由独立表现对象承担。
                boss.transform.localScale = Vector3.one;
                Stat hp = boss.CharacterItem.GetStat("MaxHealth");
                if (hp == null) throw new InvalidOperationException("champion_health_stat_unavailable");
                hp.BaseValue = SandstormChampionConfig.BaseHealth;
                boss.Health.SetHealth(boss.Health.MaxHealth);
                float baseline = BossSkillDamageRules.ReadMeleeDamageStatBase(boss);
                owner.ApplyCampaignBossStatMultiplierForRuntime(boss);
                await UniTask.Yield();
                if (!IsCurrent(owner, sceneHandle, isCurrent)) throw new OperationCanceledException();

                // 开打前备好专属掉落。物品缺失时让召唤可重试，不能出现打赢却拿不到武器的局。
                BossRushDynamicItemRegistry.EnsureRegistered(BossRushItemIds.AstralStaff);
                reward = ItemAssetsCollection.InstantiateSync(BossRushItemIds.AstralStaff);
                if (reward == null || reward.TypeID != BossRushItemIds.AstralStaff)
                    throw new InvalidOperationException("champion_reward_unavailable");
                if (!AstralStaffWeaponConfig.TryConfigure(reward))
                    throw new InvalidOperationException("champion_reward_configuration_failed");
                reward.Durability = reward.MaxDurability;

                SandstormChampionController controller = boss.gameObject.AddComponent<SandstormChampionController>();
                controller.Initialize(boss, runtimePreset, reward, baseline, ai, isCurrent, sceneHandle, owner);
                reward = null; // 交给实例 owner；取消召唤 / 切图会一并销毁未发出的奖励。
                runtimePreset = null;
                boss.dropBoxOnDead = true;
                boss.gameObject.SetActive(true);
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(boss);
                boss.Health.showHealthBar = true;
                boss.Health.RequestHealthBar();
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "沙暴冠军已生成，星阙掉落已备妥");
                return boss;
            }
            catch (Exception e)
            {
                if (reward != null) UnityEngine.Object.Destroy(reward.gameObject);
                if (runtimePreset != null) UnityEngine.Object.Destroy(runtimePreset);
                if (boss != null) UnityEngine.Object.Destroy(boss.gameObject);
                if (!(e is OperationCanceledException))
                    Debug.LogWarning(SandstormChampionConfig.LogPrefix + "召唤失败: " + e.Message);
                return null;
            }
        }

        private static void RemoveOriginalCombat(CharacterMainControl boss)
        {
            if (boss.CurrentAction != null) boss.CurrentAction.StopAction();
            boss.CancleSkill();
            boss.Trigger(false, false, true);
            boss.ChangeHoldItem(null);
            RemoveWeapon(boss.PrimWeaponSlot());
            RemoveWeapon(boss.SecWeaponSlot());
            RemoveWeapon(boss.MeleeWeaponSlot());
            StopVanillaAI(boss);
            if (boss.CurrentHoldItemAgent != null)
                throw new InvalidOperationException("champion_original_weapon_still_held");
            Debug.Log(SandstormChampionConfig.LogPrefix + "原版三武器槽已清空，角色技能与四棵行为树已停止，官方 AI 子树已停用");
        }

        internal static void StopVanillaAI(CharacterMainControl boss)
        {
            AICharacterController original = boss.GetComponentInChildren<AICharacterController>(true);
            if (original != null)
            {
                original.StopMove();
                original.defaultWeaponOut = false;
                original.enabled = false;
                original.hasSkill = false;
                if (original.patrolTree != null) original.patrolTree.Stop(true);
                if (original.alertTree != null) original.alertTree.Stop(true);
                if (original.combatTree != null) original.combatTree.Stop(true);
                if (original.combat_Attack_Tree != null) original.combat_Attack_Tree.Stop(true);
                foreach (NodeCanvas.BehaviourTrees.BehaviourTreeOwner treeOwner in
                    original.GetComponentsInChildren<NodeCanvas.BehaviourTrees.BehaviourTreeOwner>(true))
                {
                    treeOwner.StopBehaviour(true);
                    treeOwner.enabled = false;
                }
                // 官方行为树是 Graph，不是 Component；Pause 的反射集合为空也不能留树继续执行。
                // 官方 AI.Init 保证它挂在角色子物体，整体停用同时停止其树 owner 与技能子物体。
                if (original.gameObject == boss.gameObject)
                    throw new InvalidOperationException("champion_ai_subtree_unavailable");
                original.gameObject.SetActive(false);
            }
        }

        private static void RemoveWeapon(Slot slot)
        {
            if (slot == null || slot.Content == null) return;
            Item weapon = slot.Unplug();
            if (weapon == null) throw new InvalidOperationException("champion_original_weapon_unplug_failed");
            weapon.DestroyTree();
        }

        private static bool IsCurrent(ModBehaviour owner, int sceneHandle, Func<bool> isCurrent)
        {
            return owner != null && ModBehaviour.Instance == owner && !SceneLoader.IsSceneLoading
                && SceneManager.GetActiveScene().handle == sceneHandle && isCurrent != null && isCurrent();
        }

        private static CharacterRandomPreset FindBasePreset()
        {
            CharacterRandomPreset[] presets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
            foreach (string key in SandstormChampionConfig.BasePresetNames)
                foreach (CharacterRandomPreset preset in presets)
                    if (preset != null && preset.nameKey == key) return preset;
            return null;
        }
    }
}
