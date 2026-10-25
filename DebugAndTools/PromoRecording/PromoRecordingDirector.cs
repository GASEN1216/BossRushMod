#if BOSSRUSH_DEV
// ============================================================================
// PromoRecordingDirector.cs - F3「录制视频」：按剧本依次触发要拍的内容，右侧窄条实时提示
// ============================================================================
// 只在 Dev 构建存在（整份 #if BOSSRUSH_DEV）。挂在宿主 GameObject 上，随宿主销毁；不订阅全局事件，不写存档。
// 每一项先倒数 5 秒给玩家站位，再调用该项的准备动作（刷宝箱怪 / 重开快递 / 放试验台 / 刷花冠），
// 然后按建议时长计时，到点自动进入下一项。提示条只占屏幕最右 180 px，宣传片裁切时直接裁掉。
// 热键（F2–F12 已被其它调试工具占用）：PgDn 下一项、PgUp 重来本项、Home 暂停计时、Insert 刷 3 个靶子、End 结束。
// 小键盘同位键也认（3/9/7/0/1，正是小键盘上印着 PgDn/PgUp/Home/Ins/End 的那几颗）：紧凑键盘没有独立导航键，NumLock 开关都能用。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class PromoRecordingStep
    {
        internal string TitleCn, TitleEn;
        internal string[] HintsCn, HintsEn;
        internal float Seconds;
        /// <summary>准备动作；返回 null 表示成功，否则是给玩家看的失败原因。</summary>
        internal Func<PromoRecordingDirector, string> Prepare;

        internal string Title { get { return L10n.T(TitleCn, TitleEn); } }
        internal string[] Hints { get { return L10n.IsChinese ? HintsCn : HintsEn; } }
    }

    internal sealed class PromoRecordingDirector : MonoBehaviour
    {
        private const float LeadIn = 5f;
        private const float PanelWidth = 180f;

        private enum Phase { Waiting, LeadIn, Running, Finished }

        private ModBehaviour host;
        private List<PromoRecordingStep> steps;
        private int index = -1;
        private Phase phase = Phase.Waiting;
        private float timer;
        private bool paused;
        private string lastError = string.Empty;
        private float healCheck;
        private float uiRefresh;

        internal bool AutoAdvance = true;
        internal bool AutoHeal = true;

        private GameObject overlayRoot;
        private TextMeshProUGUI overlayText;

        internal static PromoRecordingDirector Find(ModBehaviour owner)
        {
            return owner != null ? owner.GetComponent<PromoRecordingDirector>() : null;
        }

        internal static PromoRecordingDirector Begin(ModBehaviour owner, List<PromoRecordingStep> script, int startIndex)
        {
            PromoRecordingDirector director = Find(owner);
            if (director == null) director = owner.gameObject.AddComponent<PromoRecordingDirector>();
            director.host = owner;
            director.steps = script;
            director.EnsureOverlay();
            if (startIndex >= 0) director.StartStep(startIndex);
            else { director.index = -1; director.phase = Phase.Waiting; }
            director.Refresh();
            return director;
        }

        internal static void End(ModBehaviour owner)
        {
            PromoRecordingDirector director = Find(owner);
            if (director != null) Destroy(director);
        }

        internal string StatusLine
        {
            get
            {
                if (steps == null) return string.Empty;
                if (phase == Phase.Waiting) return L10n.T("等待开始（PgDn / 小键盘 3）", "Waiting (PgDn / Num 3)");
                if (phase == Phase.Finished) return L10n.T("全部录完", "All done");
                return (index + 1) + "/" + steps.Count + " " + steps[index].Title;
            }
        }

        private void Update()
        {
            if (steps == null) return;
            float dt = Time.unscaledDeltaTime;
            HandleKeys();

            if (AutoHeal && (healCheck -= dt) <= 0f)
            {
                healCheck = 0.5f;
                PromoRecordingKit.TopUpPlayer(0.45f);
            }

            if (!paused && !BossRushUI.IsGamePaused())
            {
                if (phase == Phase.LeadIn)
                {
                    timer -= dt;
                    if (timer <= 0f) RunPrepare();
                }
                else if (phase == Phase.Running)
                {
                    timer -= dt;
                    if (timer <= 0f && AutoAdvance) Next();
                }
            }

            if ((uiRefresh -= dt) <= 0f) { uiRefresh = 0.2f; Refresh(); }
        }

        private void HandleKeys()
        {
            if (Pressed(KeyCode.PageDown, KeyCode.Keypad3)) Next();
            else if (Pressed(KeyCode.PageUp, KeyCode.Keypad9)) { if (index >= 0 && index < steps.Count) StartStep(index); }
            else if (Pressed(KeyCode.Home, KeyCode.Keypad7)) { paused = !paused; Refresh(); }
            else if (Pressed(KeyCode.Insert, KeyCode.Keypad0)) lastError = PromoRecordingKit.SpawnTargets(host, 3) ?? string.Empty;
            else if (Pressed(KeyCode.End, KeyCode.Keypad1)) Destroy(this);
        }

        private static bool Pressed(KeyCode key, KeyCode keypad)
        {
            return Input.GetKeyDown(key) || Input.GetKeyDown(keypad);
        }

        internal void Next()
        {
            if (phase == Phase.Finished) { Destroy(this); return; }
            StartStep(index + 1);
        }

        internal void StartStep(int i)
        {
            if (i >= steps.Count) { index = steps.Count; phase = Phase.Finished; Refresh(); return; }
            index = Mathf.Max(0, i);
            phase = Phase.LeadIn;
            timer = LeadIn;
            paused = false;
            lastError = string.Empty;
            Refresh();
        }

        private void RunPrepare()
        {
            PromoRecordingStep step = steps[index];
            string error = null;
            try { error = step.Prepare != null ? step.Prepare(this) : null; }
            catch (Exception e) { error = e.Message; ModBehaviour.DevLog("[PromoRecording] 准备失败 " + step.TitleCn + ": " + e); }
            lastError = error ?? string.Empty;
            phase = Phase.Running;
            timer = step.Seconds;
            ModBehaviour.DevLog("[PromoRecording] 开始第 " + (index + 1) + " 项 " + step.TitleCn + (error != null ? " 失败: " + error : string.Empty));
            Refresh();
        }

        private void Refresh()
        {
            if (overlayText == null) return;
            StringBuilder sb = new StringBuilder();
            string accent = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.Accent);
            string muted = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary);
            string warn = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText);
            string danger = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText);
            if (phase == Phase.Waiting)
            {
                sb.Append("<color=").Append(accent).Append("><b>").Append(L10n.T("录制模式", "Recording")).Append("</b></color>\n");
                sb.Append(L10n.T("进普通出击地图，站到开阔处，按 PgDn（小键盘 3）开始。", "Enter a normal raid map, stand in the open, press PgDn (Num 3)."));
            }
            else if (phase == Phase.Finished)
            {
                sb.Append("<color=").Append(accent).Append("><b>").Append(L10n.T("全部录完", "All done")).Append("</b></color>\n");
                sb.Append(L10n.T("按 PgDn（小键盘 3）或 End（小键盘 1）关闭提示。", "Press PgDn (Num 3) or End (Num 1) to close."));
            }
            else
            {
                PromoRecordingStep step = steps[index];
                sb.Append("<color=").Append(accent).Append(">").Append(index + 1).Append("/").Append(steps.Count).Append("</color>  ");
                if (phase == Phase.LeadIn)
                    sb.Append("<color=").Append(warn).Append(">").Append(L10n.T("准备 ", "Ready ")).Append(Mathf.CeilToInt(timer)).Append("</color>");
                else
                    sb.Append("<color=").Append(muted).Append(">").Append(FormatTime(timer)).Append(paused ? L10n.T(" 暂停", " paused") : string.Empty).Append("</color>");
                sb.Append("\n<b>").Append(step.Title).Append("</b>");
                string[] hints = step.Hints;
                for (int i = 0; hints != null && i < hints.Length; i++) sb.Append("\n").Append(hints[i]);
                if (!string.IsNullOrEmpty(lastError))
                    sb.Append("\n<color=").Append(danger).Append(">").Append(L10n.T("未触发：", "Not triggered: ")).Append(lastError).Append("</color>");
                if (index + 1 < steps.Count)
                    sb.Append("\n<color=").Append(muted).Append(">").Append(L10n.T("下一项：", "Next: ")).Append(steps[index + 1].Title).Append("</color>");
            }
            sb.Append("\n<size=12><color=").Append(muted).Append(">")
                .Append(L10n.T("小键盘 3 / PgDn 下一项\n9 / PgUp 重来 · 7 / Home 暂停\n0 / Ins 刷靶 · 1 / End 结束", "Num 3 / PgDn next\n9 / PgUp redo · 7 / Home pause\n0 / Ins targets · 1 / End stop"))
                .Append("</color></size>");
            overlayText.text = sb.ToString();
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        private void EnsureOverlay()
        {
            if (overlayRoot != null) return;
            overlayRoot = new GameObject("BossRush_PromoRecordingOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(overlayRoot);
            Canvas canvas = overlayRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.Hud;
            ZombieModeUIHelper.ConfigureCanvasScaler(overlayRoot.GetComponent<CanvasScaler>());

            // 贴屏幕最右缘、从上往下 150 px 起：避开右上「操作说明」，整条落在宣传片裁切区（x > 1730）之外。
            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-4f, -150f);
            rect.sizeDelta = new Vector2(PanelWidth, 0f);
            Image image = panel.GetComponent<Image>();
            image.color = new Color(BossRushUIColors.Surface.r, BossRushUIColors.Surface.g, BossRushUIColors.Surface.b, 0.82f);
            image.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(image, 8, BossRushUISkinPart.Card);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(panel.transform, false);
            overlayText = label.GetComponent<TextMeshProUGUI>();
            BossRush.BossRushUI.ApplyGameFont(overlayText);
            overlayText.fontSize = 15f;
            overlayText.lineSpacing = -4f;
            overlayText.color = BossRushUIColors.TextPrimary;
            overlayText.enableWordWrapping = true;
            overlayText.raycastTarget = false;
            BossRushUIKit.ApplyWorldTextOutline(overlayText);
        }

        private void OnDestroy()
        {
            if (overlayRoot != null) Destroy(overlayRoot);
            overlayRoot = null;
            overlayText = null;
            steps = null;
            host = null;
            PromoRecordingKit.ResetStaticCaches();
        }
    }

    /// <summary>录制用的道具发放、刷靶子、回血与剧本。</summary>
    internal static class PromoRecordingKit
    {
        internal static string RequireRaid(ModBehaviour host, out CharacterMainControl player, out Scene scene)
        {
            if (FieldAdventureSupport.TryGetRaid(host, out player, out scene)) return null;
            return L10n.T("先进普通出击地图（不是基地或 BossRush）", "Enter a normal raid map first (not base or BossRush)");
        }

        internal static void TopUpPlayer(float threshold)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Health health = player != null ? player.Health : null;
            if (health == null || health.IsDead || health.MaxHealth <= 0f) return;
            if (health.CurrentHealth < health.MaxHealth * threshold) health.SetHealth(health.MaxHealth);
        }

        /// <summary>宝箱怪的三种食材 + 残日盾片。食材从官方物品表里挑最便宜、确实能被投喂的那件。</summary>
        internal static string GrantKit()
        {
            int sent = 0;
            StringBuilder missing = new StringBuilder();
            sent += Grant(FindFood(GreedyMimicFood.Ammunition), 60, missing, "ammo");
            sent += Grant(FindFood(GreedyMimicFood.Explosive), 3, missing, "grenade");
            sent += Grant(FindFood(GreedyMimicFood.Medicine), 3, missing, "medicine");
            sent += GrantShield() ? 1 : 0;
            if (sent == 0) return L10n.T("道具发放失败", "Kit grant failed");
            return missing.Length > 0 ? L10n.T("部分道具缺失：", "Some items missing: ") + missing : null;
        }

        private static readonly Dictionary<GreedyMimicFood, int> foodCache = new Dictionary<GreedyMimicFood, int>();

        internal static int FindFood(GreedyMimicFood food)
        {
            int cached;
            if (foodCache.TryGetValue(food, out cached)) return cached;
            int best = -1;
            float bestValue = float.MaxValue;
            try
            {
                for (int quality = 1; quality <= 4; quality++)
                {
                    ItemFilter filter = new ItemFilter();
                    filter.requireTags = null;
                    filter.minQuality = quality;
                    filter.maxQuality = quality;
                    filter.caliber = string.Empty;
                    int[] ids = ItemAssetsCollection.GetAllTypeIds(filter);
                    if (ids == null) continue;
                    for (int i = 0; i < ids.Length; i++)
                    {
                        int id = ids[i];
                        if (id <= 0 || id >= BossRushItemIds.BossRushTicket) continue;
                        Item prefab = ItemAssetsCollection.GetPrefab(id);
                        if (prefab == null || !prefab.CanBeSold || prefab.Tags.Contains("Quest") || prefab.Tags.Contains("Special")) continue;
                        if (!GreedyMimicInventory.MatchesFood(prefab, food)) continue;
                        if (prefab.Value < bestValue) { bestValue = prefab.Value; best = id; }
                    }
                    if (best > 0) break;
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[PromoRecording] 食材查找失败 " + food + ": " + e.Message); }
            foodCache[food] = best;
            return best;
        }

        private static int Grant(int typeId, int count, StringBuilder missing, string label)
        {
            if (typeId <= 0) { missing.Append(label).Append(' '); return 0; }
            int remaining = count;
            int sent = 0;
            for (int guard = 0; guard < 8 && remaining > 0; guard++)
            {
                Item item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null) break;
                int amount = item.Stackable ? Mathf.Clamp(remaining, 1, Mathf.Max(1, item.MaxStackCount)) : 1;
                if (item.Stackable) item.StackCount = amount;
                ItemUtilities.SendToPlayer(item);
                remaining -= amount;
                sent++;
            }
            if (sent == 0) missing.Append(label).Append(' ');
            return sent;
        }

        internal static bool GrantShield()
        {
            try
            {
                BossRushDynamicItemRegistry.EnsureRegistered(BossRushItemIds.ResidualSunShield);
                Item shield = ItemAssetsCollection.InstantiateSync(BossRushItemIds.ResidualSunShield);
                if (shield == null || shield.TypeID != BossRushItemIds.ResidualSunShield) return false;
                if (!ResidualSunShieldWeaponConfig.TryConfigure(shield)) { UnityEngine.Object.Destroy(shield.gameObject); return false; }
                shield.Durability = shield.MaxDurability;
                ItemUtilities.SendToPlayer(shield);
                return true;
            }
            catch (Exception e) { ModBehaviour.DevLog("[PromoRecording] 残日盾片发放失败: " + e.Message); return false; }
        }

        /// <summary>在玩家身边 11–16 米刷普通敌人当靶子（拾荒者底模，敌对阵营，解除距离休眠）。</summary>
        internal static string SpawnTargets(ModBehaviour host, int count)
        {
            CharacterMainControl player;
            Scene scene;
            string error = RequireRaid(host, out player, out scene);
            if (error != null) return error;
            CharacterRandomPreset preset = FindPreset("Cname_Scav");
            if (preset == null) return L10n.T("找不到拾荒者底模", "Scav preset unavailable");
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 position;
                if (!FieldAdventureSupport.TryFindGroundBands(player.transform.position, TargetBands, out position)) continue;
                SpawnTarget(preset, position, scene).Forget();
                placed++;
            }
            // 一个都没放下也要报出来，否则提示条显示成功、场上却没有靶子。
            return placed > 0 ? null : L10n.T("附近没有空地刷靶子，换个开阔处再按 Insert（小键盘 0）", "No room for targets; move and press Insert (Num 0)");
        }

        private static async UniTaskVoid SpawnTarget(CharacterRandomPreset preset, Vector3 position, Scene scene)
        {
            try
            {
                CharacterMainControl enemy = await preset.CreateCharacterAsync(position, Vector3.forward, scene.buildIndex, null, false);
                if (enemy == null) return;
                if (!Team.IsEnemy(Teams.player, enemy.Team)) enemy.SetTeam(Teams.wolf);
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(enemy);
            }
            catch (Exception e) { ModBehaviour.DevLog("[PromoRecording] 刷靶子失败: " + e.Message); }
        }

        private static CharacterRandomPreset FindPreset(string nameKey)
        {
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                if (preset != null && preset.nameKey == nameKey && !preset.name.StartsWith("BossRush_")) return preset;
            return null;
        }

        private static CharacterMainControl bloom;
        // 远档整轮落空再退到近档；录制时站位不一定开阔，单档失败就得来回找地方。
        private static readonly float[] TargetBands = { 11f, 16f, 7f, 20f };
        private static readonly float[] BloomBands = { 14f, 18f, 10f, 22f };

        /// <summary>录制结束（导演组件销毁）时清掉静态引用：本趟花冠与食材表。</summary>
        internal static void ResetStaticCaches()
        {
            bloom = null;
            foodCache.Clear();
        }

        /// <summary>前方 14–18 米刷一株蚀日花冠；场上已有活着的就不再刷。</summary>
        internal static string SpawnBloom(ModBehaviour host)
        {
            CharacterMainControl player;
            Scene scene;
            string error = RequireRaid(host, out player, out scene);
            if (error != null) return error;
            if (bloom != null && bloom.Health != null && !bloom.Health.IsDead) return null;
            Vector3 position;
            if (!FieldAdventureSupport.TryFindGroundBands(player.transform.position, BloomBands, out position))
                return L10n.T("附近没有空地，换个开阔处再按 PgUp（小键盘 9）", "No open ground nearby; move and press PgUp (Num 9)");
            Scene target = scene;
            SpawnBloomAsync(host, position, target).Forget();
            return null;
        }

        private static async UniTaskVoid SpawnBloomAsync(ModBehaviour host, Vector3 position, Scene target)
        {
            bloom = await EclipseBloomBoss.SpawnAsync(host, position,
                () => target.isLoaded && CharacterMainControl.Main != null, target);
        }

        internal static string Mimic(ModBehaviour host)
        {
            GreedyMimicRuntimeModule module = GreedyMimicRuntimeModule.DevCurrent;
            if (module == null) return "module_unavailable";
            string reason;
            if (module.DevForceSpawn(out reason)) return null;
            return reason == "not_in_raid" ? RequireRaid(host, out _, out _) : L10n.T("附近没有空地放宝箱，换个开阔处再按 PgUp（小键盘 9）", "No room for the chest; move and press PgUp (Num 9)");
        }

        internal static string Parcel(ModBehaviour host)
        {
            CharacterMainControl player;
            Scene scene;
            string error = RequireRaid(host, out player, out scene);
            if (error != null) return error;
            OddParcelRuntimeModule module = OddParcelRuntimeModule.DevCurrent;
            if (module == null) return "module_unavailable";
            string reason = module.DevResetOrders();
            if (reason == null) return null;
            return reason == "not_in_raid" ? RequireRaid(host, out _, out _) ?? reason
                : L10n.T("附近没有空地放发件台，换个开阔处再按 PgUp（小键盘 9）", "No room for the dispatch; move and press PgUp (Num 9)");
        }

        internal static string Bench(ModBehaviour host, DangerousPrototypeKind kind)
        {
            DangerousPrototypeRuntimeModule module = DangerousPrototypeRuntimeModule.DevCurrent;
            if (module == null) return "module_unavailable";
            string reason;
            if (!module.DevForceBench(kind, out reason)) return RequireRaid(host, out _, out _) ?? reason;
            return SpawnTargets(host, 3);
        }

        private static PromoRecordingStep Step(string titleCn, string titleEn, float seconds, Func<PromoRecordingDirector, string> prepare,
            string[] hintsCn, string[] hintsEn)
        {
            return new PromoRecordingStep { TitleCn = titleCn, TitleEn = titleEn, Seconds = seconds, Prepare = prepare, HintsCn = hintsCn, HintsEn = hintsEn };
        }

        private static PromoRecordingStep BenchStep(ModBehaviour host, DangerousPrototypeKind kind, string tipCn, string tipEn)
        {
            string name = DangerousPrototypeCatalog.Name(kind);
            return Step("试用品：" + name, "Prototype: " + name, 60f, d => Bench(host, kind),
                new[] { "身边出现试验台，前方刷了 3 个靶子", "① 手里拿枪，交互试验台装上", "② " + tipCn, "③ 不够打就按 Insert（小键盘 0）再刷" },
                new[] { "A bench appeared; 3 targets ahead", "1. Hold a gun, install at the bench", "2. " + tipEn, "3. Press Insert (Num 0) for more targets" });
        }

        /// <summary>v2.3.8「野外趣事」宣传片剧本。时长是建议值，到点自动进入下一项。</summary>
        internal static List<PromoRecordingStep> BuildFieldAdventureScript(ModBehaviour host)
        {
            return new List<PromoRecordingStep>
            {
                Step("贪吃宝箱怪", "Greedy Mimic", 150f, d => Mimic(host),
                    new[] { "身边 10 米刷出宝箱怪", "① 闭嘴时开两枪，看伤害很小", "② 依次喂弹药、手雷、药品，每口停 3 秒", "③ 选开席，被咬一口再打穿它", "④ 打死后拍掉落" },
                    new[] { "A mimic appeared ~10 m away", "1. Shoot it while closed (tiny damage)", "2. Feed ammo, grenade, medicine; pause 3 s each", "3. Start the feast, take a bite, then kill it", "4. Film the loot" }),
                Step("怪件快递：嘎嘎巨蛋", "Odd Parcel: Quacking Egg", 100f, d => Parcel(host),
                    new[] { "身边出现阿稳的发件台", "① 接单选『嘎嘎巨蛋』", "② 抱着跑，让它叫几声、弹几下", "③ 按 M 看签收台，送达拍 2400 金" },
                    new[] { "Awen's dispatch appeared nearby", "1. Accept the Quacking Egg", "2. Run with it; let it quack and bounce", "3. Press M, deliver, film the payout" }),
                Step("怪件快递：暴躁汽水", "Odd Parcel: Angry Soda", 100f, d => Parcel(host),
                    new[] { "发件台已重开，额度已归还", "① 接单选『暴躁汽水』", "② 抱着猛跑，让它喷一次", "③ 放下泄压，再送到签收台" },
                    new[] { "Dispatch reopened, slots reset", "1. Accept the Angry Soda", "2. Sprint until it erupts once", "3. Set it down to vent, then deliver" }),
                BenchStep(host, DangerousPrototypeKind.JetCannon, "蓄满后开火，拍后坐力把自己推走", "Fire when charged; film the recoil push"),
                BenchStep(host, DangerousPrototypeKind.RicochetSun, "对着墙打，拍光束反弹命中", "Bounce the beam off walls into enemies"),
                BenchStep(host, DangerousPrototypeKind.QuackRail, "让靶子排成一条线，一炮贯穿", "Line them up and pierce them all"),
                BenchStep(host, DangerousPrototypeKind.Thunderbill, "打靶子群，拍闪电连跳", "Hit a group; film the chain lightning"),
                BenchStep(host, DangerousPrototypeKind.GrandFinale, "对着靶子群放烟花，拍满屏开花", "Lob it into the group; film the fireworks"),
                Step("蚀日花冠：招式", "Eclipse Bloom: Moves", 90f, d => SpawnBloom(host),
                    new[] { "前方 15 米刷出蚀日花冠", "① 站着看扇形日光转过来锁定", "② 拉远距离，等日珥从天上落", "③ 先别打死它" },
                    new[] { "Eclipse Bloom spawned ~15 m ahead", "1. Watch the sun fan swing and lock on", "2. Back off and let solar flares fall", "3. Do not kill it yet" }),
                Step("残日盾片", "Residual Sun Shield", 60f, d => SpawnBloom(host),
                    new[] { "背包里已有残日盾片，先装上", "① 迎着花冠的攻击翻滚，拍护盾挡下", "② 挡住后立刻反打（追加伤害）" },
                    new[] { "The shield is in your bag; equip it", "1. Roll into an attack; film the block", "2. Counter right after for bonus damage" }),
                Step("击败蚀日花冠", "Defeat the Eclipse Bloom", 120f, d => SpawnBloom(host),
                    new[] { "① 先打碎四片花瓣", "② 再集火日核", "③ 拍击杀和掉落" },
                    new[] { "1. Break the four petals", "2. Then burst the sun core", "3. Film the kill and loot" }),
            };
        }
    }
}
#endif
