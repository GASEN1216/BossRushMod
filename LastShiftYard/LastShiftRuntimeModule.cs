using System;
using SodaCraft.Localizations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// COMPAT：末班编组站的模块生命周期 owner。场景身份在启动时登记到多图共用的 <see cref="ModRaidSceneBridge"/>；
    /// 剪票处只在官方仓库区出击里、`Level_HiddenWarehouse` 子场景已加载、两个包都在时挂上，切图前收起。
    /// 剧情存档 `BossRush_LastShiftYard_Story_v1`（<see cref="LastShiftStoryPersistence"/>）也归这里持有；余响跟着剪票处生成与回收，
    /// 基地的「末班票」告示在回到基地时贴一次。
    /// </summary>
    internal sealed class LastShiftRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private readonly LastShiftYuXiang yuXiang = new LastShiftYuXiang();
        private GameObject entry;
        private Scene entryScene;
        private LastShiftStoryPersistence persistence;
        private LastShiftJeffIntro jeffIntro;
        private bool subscribed, bundleWarned, applicationQuitting, returnLightingPending;
        private float nextAttempt;

        public override string ModuleName { get { return "LastShiftYard"; } }

        public override void OnAwake(ModBehaviour host)
        {
            owner = host;
            ModRaidSceneBridge.Register(LastShiftRaidScene.Identity);
            if (persistence == null)
            {
                persistence = new LastShiftStoryPersistence(LastShiftNoteBridge.RequestSync);
                LastShiftStory.Bind(persistence);
                LastShiftNoteBridge.EnsureRuntime();
            }
            if (jeffIntro == null && host != null && host.OfficialQuestRuntime != null)
            {
                jeffIntro = new LastShiftJeffIntro(host.OfficialQuestRuntime.Projection);
                LastShiftStory.JeffIntro = jeffIntro;
            }
            if (!subscribed)
            {
                SceneLoader.onStartedLoadingScene += OnStartedLoading;
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                LevelManager.OnAfterLevelInitialized += OnLevelReady;
                LocalizationManager.OnSetLanguage += OnLanguageChanged;
                Application.quitting += OnApplicationQuitting;
                subscribed = true;
            }
        }

        private void OnApplicationQuitting() { applicationQuitting = true; }

        private void OnLanguageChanged(SystemLanguage language)
        {
            if (entry == null) return;
            // 地图 UI 缓存 Setup 时的名字，更新本地化字典后刷新已有 POI。
            LastShiftYuXiang.InjectLocalization();
            LastShiftYuXiang.RefreshMapMarker(entry);
        }

        private void OnStartedLoading(SceneLoadingContext context) { ClearEntry(); }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == LastShiftEntryPoint.SceneName) nextAttempt = 0f;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (entryScene.handle == scene.handle) ClearEntry();
        }

        /// <summary>回到基地后让引擎按基地自己的设置重算一次环境光（口径同潮闸镇）。</summary>
        private void OnLevelReady()
        {
            nextAttempt = 0f;
            ShowNoticeAtBase();
            if (!returnLightingPending || owner == null) return;
            LastShiftSession session = owner.GetComponent<LastShiftSession>();
            if (session != null && !session.IsClosed) return;
            if (!SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name)) return;
            returnLightingPending = false;
            try { DynamicGI.UpdateEnvironment(); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 返航刷新基地环境光失败: " + e.Message); }
        }

        /// <summary>第七轮入口引导：没去过编组站时在基地贴一次告示（大横幅），之后不再提。</summary>
        private void ShowNoticeAtBase()
        {
            if (owner == null || !SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name)) return;
            // 包不在就不贴（告示只贴一次，贴了就指向一个不存在的剪票处）；口径同杰夫引导的 CanOffer。
            if (!LastShiftRaidScene.IsBundleDeployed() || !LastShiftEntry.IsBundleDeployed()) return;
            try { if (LastShiftStory.TryShowNotice()) owner.ShowBigBanner(LastShiftText.BaseNotice); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 基地告示失败: " + e.Message); }
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            LastShiftStory.Tick();
            if (jeffIntro != null) jeffIntro.Tick();
            if (owner == null) return;
            LastShiftSession active = owner.GetComponent<LastShiftSession>();
            if (active != null && !active.IsClosed)
            {
                returnLightingPending = true;
                ClearEntry();
                return;
            }
            if (Time.unscaledTime < nextAttempt) return;
            nextAttempt = Time.unscaledTime + 1f;
            if (SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || !LevelManager.LevelInited) return;
            Scene target = SceneManager.GetSceneByName(LastShiftEntryPoint.SceneName);
            bool inRaid = LevelManager.Instance != null && LevelManager.Instance.IsRaidMap && !RaidUtilities.CurrentRaid.ended;
            if (!target.IsValid() || !target.isLoaded || !inRaid)
            {
                ClearEntry();
                return;
            }
            if (entryScene.handle != target.handle || entry == null || !entry.activeInHierarchy) ClearEntry();
            if (entry != null)
            {
                yuXiang.Spawn(entry);
                yuXiang.Tick(CharacterMainControl.Main);
                return;
            }
            if (!LastShiftRaidScene.IsBundleDeployed() || !LastShiftEntry.IsBundleDeployed())
            {
                if (!bundleWarned)
                {
                    bundleWarned = true;
                    string missing = LastShiftRaidScene.IsBundleDeployed() ? LastShiftEntry.BundleRelativePath : LastShiftRaidScene.BundleRelativePath;
                    ModBehaviour.CriticalLog("lastshift-bundle-missing", LastShiftRaidScene.LogTag + " 缺少 " + missing + "，本次不开放末班车剪票处。");
                }
                return;
            }
            try
            {
                entry = LastShiftEntry.Create(target, owner, yuXiang);
                entryScene = target;
                yuXiang.Spawn(entry);
            }
            catch (Exception e)
            {
                entry = null;
                ModBehaviour.CriticalLog("lastshift-entry-failed", LastShiftRaidScene.LogTag + " 剪票处创建失败: " + e.Message);
                nextAttempt = Time.unscaledTime + 30f;
            }
        }

        private void ClearEntry()
        {
            GameObject closing = entry;
            entry = null;
            entryScene = default(Scene);
            yuXiang.Despawn();
            if (closing == null) return;
            closing.SetActive(false);
            LastShiftEntry.Release(closing);
            UnityEngine.Object.Destroy(closing);
        }

        public override void OnDestroy()
        {
            if (subscribed)
            {
                SceneLoader.onStartedLoadingScene -= OnStartedLoading;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                LevelManager.OnAfterLevelInitialized -= OnLevelReady;
                LocalizationManager.OnSetLanguage -= OnLanguageChanged;
                Application.quitting -= OnApplicationQuitting;
                subscribed = false;
            }
            ClearEntry();
            if (owner != null)
            {
                LastShiftSession session = owner.GetComponent<LastShiftSession>();
                if (session != null) session.Close(!applicationQuitting, applicationQuitting ? "application_quit" : "runtime_shutdown");
            }
            owner = null;
            ModRaidSceneBridge.Unregister(LastShiftRaidScene.Identity);
            LastShiftNoteBridge.ResetStaticCaches();
            LastShiftSounds.ResetStaticCaches();
            LastShiftChiefFx.ResetStaticCaches();
            LastShiftStory.JeffIntro = null;
            if (jeffIntro != null) { jeffIntro.Dispose(); jeffIntro = null; }
            LastShiftStory.Bind(null);
            if (persistence != null)
            {
                try { persistence.Dispose(); }
                catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 剧情存档收尾失败: " + e.Message); }
                persistence = null;
            }
        }
    }
}
