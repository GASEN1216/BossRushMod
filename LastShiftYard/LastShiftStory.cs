using System;
using System.Collections.Generic;
using BossRush.Utils;
using Duckov.MiniMaps;
using Duckov.NoteIndexs;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 末班编组站的持久事实（本槽）。只在安全撤离时写入：到访次数、已经带出去的线索、剧情收尾、站台记录。
    /// 一趟里刚找到的线索和记录先记在 <see cref="LastShiftRun"/>，阵亡就丢（第七轮 owner 采纳：死亡丢钢印、票根和本趟线索）。
    /// 钢印与票根是真实物品，跟背包走，不进这里。
    /// </summary>
    internal sealed class LastShiftStoryData
    {
        /// <summary>从编组站安全撤离的次数；大于 0 即回访（跳过首班教学）。</summary>
        internal int Runs;
        /// <summary>带出去的线索，按位记（打孔钳 / 旧录音 / 退件簿 = 位 0–2）。</summary>
        internal int Notes;
        /// <summary>票根已交给余响，「没人来领的末班票」收进图鉴；之后每次剪票她都说出下一班的底细。</summary>
        internal bool Journal;
        /// <summary>站台记录，按位记（<see cref="LastShiftStory.RecordIds"/> 的下标）。</summary>
        internal int Records;
        /// <summary>基地的「末班票」告示已经贴过（只贴一次，之后交给杰夫的任务引路）。</summary>
        internal bool Notice;
        /// <summary>接了杰夫的引导（官方任务 590216）。</summary>
        internal bool JeffAccepted;
        /// <summary>和余响说过话（对话或剪票上车都算）：杰夫引导的目标。</summary>
        internal bool Met;
        /// <summary>杰夫的引导已交付。</summary>
        internal bool JeffDone;
        /// <summary>余响已经对哪几条带出来的线索说过话（按位，同 <see cref="Notes"/>）：每条只接一次话。</summary>
        internal int Told;

        internal LastShiftStoryData Copy()
        {
            return new LastShiftStoryData { Runs = Runs, Notes = Notes, Journal = Journal, Records = Records, Notice = Notice,
                JeffAccepted = JeffAccepted, Met = Met, JeffDone = JeffDone, Told = Told };
        }
    }

    /// <summary>只保存编组站剧情事实；存槽、写屏障、采集与物理保存复用共享管线（形态照 EclipseBloomIntroductionPersistence）。</summary>
    internal sealed class LastShiftStoryPersistence : IBossRushSaveBatchSource, IDisposable
    {
        internal const string StorageKey = "BossRush_LastShiftYard_Story_v1";
        private const int Schema = 1;
        private readonly BossRushSlotJsonStore<LastShiftStoryData> store;
        private readonly BossRushSaveCoordinatorEngine coordinator;
        private readonly Action slotChanged;

        internal LastShiftStoryPersistence(Action onSlotChanged)
        {
            slotChanged = onSlotChanged;
            coordinator = new BossRushSaveCoordinatorEngine(this, true);
            store = new BossRushSlotJsonStore<LastShiftStoryData>(new BossRushSlotJsonStoreSpec<LastShiftStoryData>
            {
                StorageKey = StorageKey, SchemaVersion = Schema, LogPrefix = "[LastShiftStory] ", DisplayName = "末班编组站剧情",
                Encode = Encode, Decode = Decode, ReadSchemaVersion = ReadSchemaVersion,
                CreateDefault = () => new LastShiftStoryData(),
                NotifySlotChanged = OnSlotChanged,
                BeforeCollectSaveData = null,
                AfterPendingWriteFlushed = coordinator.NotifyPendingWriteFlushed
            });
            store.EnsureSubscribed();
        }

        // 首次读取和漏收切槽回调都会在 LoadOrInit 中建立屏障，必须先加载再判断。
        internal bool Writable { get { store.LoadOrInit(); return !store.HasWriteBarrier && !store.IsStoreFaulted; } }
        internal LastShiftStoryData Load() { return store.LoadOrInit(); }

        internal bool Store(LastShiftStoryData value)
        {
            if (!store.Store(value)) return false;
            string error;
            coordinator.RequestFlush(out error);
            return true;
        }

        internal void Tick() { store.LoadOrInit(); coordinator.Tick(); }

        private void OnSlotChanged()
        {
            coordinator.NotifySlotChanged();
            if (slotChanged != null) slotChanged();
        }

        public void Dispose()
        {
            coordinator.TryFlushOnHostDestroy();
            store.ShutdownSubscription();
            store.ResetAll();
            coordinator.Reset();
        }

        string IBossRushSaveBatchSource.LogPrefix { get { return "[LastShiftStory] "; } }
        bool IBossRushSaveBatchSource.HasPendingWrite { get { return store != null && store.HasPendingWrite; } }
        bool IBossRushSaveBatchSource.IsStoreFaulted { get { return store != null && store.IsStoreFaulted; } }
        bool IBossRushSaveBatchSource.HasSnapshotObligation { get { return false; } }
        string IBossRushSaveBatchSource.LastError { get { return store == null ? null : store.LastError; } }
        bool IBossRushSaveBatchSource.CollectSnapshot(out string error) { error = null; return true; }
        bool IBossRushSaveBatchSource.FlushPending() { return store.FlushPending(); }
        void IBossRushSaveBatchSource.OnPhysicalSaveSucceeded() { }

        internal static int ReadSchemaVersion(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int version;
            return root != null && root.TryGetInt("schemaVersion", out version) ? version : -1;
        }

        internal static LastShiftStoryData Decode(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int schema, runs, notes, records;
            bool journal, notice;
            if (root == null || !root.TryGetInt("schemaVersion", out schema) || schema != Schema
                || !root.TryGetInt("runs", out runs) || !root.TryGetInt("notes", out notes)
                || !root.TryGetBool("journal", out journal) || !root.TryGetInt("records", out records)
                || !root.TryGetBool("notice", out notice)
                || runs < 0 || notes < 0 || notes > LastShiftStory.AllNotes
                || records < 0 || records >= 1 << LastShiftStory.RecordIds.Length) return null;
            // 杰夫引导的三个字段是 2026-11-11 同一 schema 里追加的（SCHEMA+）：缺键按「没发生」读。
            bool jeffAccepted, met, jeffDone;
            if (!root.TryGetBool("jeffAccepted", out jeffAccepted)) jeffAccepted = false;
            if (!root.TryGetBool("met", out met)) met = false;
            if (!root.TryGetBool("jeffDone", out jeffDone)) jeffDone = false;
            int told;
            if (!root.TryGetInt("told", out told)) told = 0;
            if (told < 0 || told > LastShiftStory.AllNotes) return null;
            return new LastShiftStoryData { Runs = runs, Notes = notes, Journal = journal, Records = records, Notice = notice,
                JeffAccepted = jeffAccepted, Met = met, JeffDone = jeffDone, Told = told };
        }

        internal static string Encode(LastShiftStoryData value)
        {
            if (value == null) return null;
            return new BossRushJsonWriter().BeginObject().Int("schemaVersion", Schema)
                .Int("runs", value.Runs).Int("notes", value.Notes).Bool("journal", value.Journal)
                .Int("records", value.Records).Bool("notice", value.Notice)
                .Bool("jeffAccepted", value.JeffAccepted).Bool("met", value.Met).Bool("jeffDone", value.JeffDone)
                .Int("told", value.Told).EndObject().ToString();
        }
    }

    /// <summary>
    /// 「没人来领的末班票」（owner 2026-11-07 选定、2026-11-11 第七轮流程拍板）：司机室打孔钳 → 维修棚旧录音 → 押运库退件簿，
    /// 三条线索在一趟里合上（之前带出去的也算）就得到末班票根；带出去，下次在仓库区剪票时自动交给余响，剧情收进图鉴。
    /// 我们的存档是唯一权威，官方笔记图鉴（<see cref="LastShiftNoteBridge"/>）只做镜像。不新增官方任务。
    /// 持久化由 <see cref="LastShiftRuntimeModule"/> 持有；一趟里的进度在 <see cref="LastShiftRun"/>。
    /// </summary>
    internal static class LastShiftStory
    {
        internal const int NoteCount = 3;
        internal const int AllNotes = (1 << NoteCount) - 1;
        internal static readonly string[] NoteMarkers = { "note1", "note2", "note3" };
        /// <summary>站台记录（原型 sim.mjs 的 RECORDS）。下标进存档位，只能往后加。</summary>
        internal static readonly string[] RecordIds = { "sign:medical", "sign:repair", "sign:precision", "shadow", "express", "bossTrain", "red", "alarm" };
        private static LastShiftStoryPersistence persistence;

        internal static void Bind(LastShiftStoryPersistence value) { persistence = value; LastShiftNoteBridge.RequestSync(); }

        internal static LastShiftStoryData Current { get { return persistence == null ? null : persistence.Load(); } }

        /// <summary>剧情存档读写正常。读不了时 <see cref="Current"/> 是默认值，不能拿它下结论。</summary>
        internal static bool Writable { get { return persistence != null && persistence.Writable; } }

        internal static int Runs { get { LastShiftStoryData d = Current; return d == null ? 0 : d.Runs; } }
        internal static bool Journal { get { LastShiftStoryData d = Current; return d != null && d.Journal; } }
        internal static int Notes { get { LastShiftStoryData d = Current; return d == null ? 0 : d.Notes; } }
        internal static int Records { get { LastShiftStoryData d = Current; return d == null ? 0 : d.Records; } }

        internal static int Count(int bits)
        {
            int n = 0;
            for (; bits != 0; bits &= bits - 1) n++;
            return n;
        }

        private static bool Mutate(Action<LastShiftStoryData> change)
        {
            if (persistence == null || !persistence.Writable) return false;
            LastShiftStoryData data = persistence.Load();
            if (data == null) return false;
            LastShiftStoryData copy = data.Copy();
            change(copy);
            return persistence.Store(copy);
        }

        /// <summary>安全撤离：到访次数 +1，本趟线索与记录并进存档，图鉴点亮。阵亡、装配失败不调这里。</summary>
        internal static bool CommitRun(int notes, int records)
        {
            bool stored = Mutate(delegate (LastShiftStoryData d) { d.Runs++; d.Notes |= notes & AllNotes; d.Records |= records; });
            if (!stored) Debug.LogWarning(LastShiftRaidScene.LogTag + " 本趟的到访与线索没写进存档（存档只读或换槽中）");
            else Debug.Log(LastShiftRaidScene.LogTag + " STORY_COMMIT runs=" + Runs + " notes=" + Notes + " records=" + Records);
            LastShiftNoteBridge.RequestSync();
            return stored;
        }

        /// <summary>
        /// 上车时交票根：身上带着票根、故事还没收尾就收下它，写剧情收尾。先写存档再拿走票根——存档写不进去时票根留在身上，下次再交。
        /// 返回 true 表示这一次交成了（调用方弹收尾那一页）。
        /// </summary>
        internal static bool TryHandIn(CharacterMainControl player)
        {
            if (Journal || !Writable) return false;
            Item stub = TidegateInventory.FindCarried(player, BossRushItemIds.LastShiftTicketStub);
            if (stub == null) return false;
            if (!Mutate(delegate (LastShiftStoryData d) { d.Journal = true; })) return false;
            try { TidegateInventory.Consume(stub); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 票根已记收，但没能从背包拿走：" + e.Message); }
            LastShiftNoteBridge.RequestSync();
            Debug.Log(LastShiftRaidScene.LogTag + " STORY_JOURNAL");
            return true;
        }

        /// <summary>基地告示：没去过编组站时贴一次（owner 2026-11-11：只第一次提示，后面交给杰夫的任务）。</summary>
        internal static bool TryShowNotice()
        {
            LastShiftStoryData d = Current;
            if (d == null || d.Notice || d.Runs > 0 || !Writable) return false;
            return Mutate(delegate (LastShiftStoryData x) { x.Notice = true; });
        }

        /// <summary>杰夫引导（官方任务 590216）的客户端；模块启动时绑定，用来在「见过余响」时刷新任务页。</summary>
        internal static LastShiftJeffIntro JeffIntro;

        /// <summary>和余响说过话（对话或剪票上车）：杰夫引导的目标。只写一次。</summary>
        internal static void MarkMet()
        {
            LastShiftStoryData d = Current;
            if (d == null || d.Met) return;
            if (!Mutate(delegate (LastShiftStoryData x) { x.Met = true; }))
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 见过余响没写进存档，下次和她说话再记");
                return;
            }
            if (JeffIntro != null) JeffIntro.NotifyChanged();
        }

        /// <summary>余响对这几条线索说过话了（对话时调）。写不进存档也不挡对话，下次再说一遍。</summary>
        internal static void MarkTold(int bits)
        {
            if (bits == 0) return;
            if (!Mutate(delegate (LastShiftStoryData x) { x.Told |= bits & AllNotes; }))
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响的回应没写进存档，下次对话会再说一遍");
        }

        /// <summary>杰夫引导的接取 / 交付：只由 <see cref="LastShiftJeffIntro"/> 调。</summary>
        internal static bool SetJeff(bool accepted, bool done)
        {
            return Mutate(delegate (LastShiftStoryData x) { x.JeffAccepted = accepted; x.JeffDone = done; });
        }

        /// <summary>收尾页（上车时交票、和余响说话时交票共用）：读完按 <paramref name="confirm"/> 继续。</summary>
        internal static void ShowEnding(string confirm, Action after)
        {
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = LastShiftText.EndingTitle,
                Body = LastShiftText.EndingBody,
                SingleAction = true,
                Warning = LastShiftText.EndingWarning,
                ConfirmLabel = confirm,
                Danger = false,
                OnConfirm = delegate { if (after != null) after(); },
                // 单按钮页的 Esc 也走「取消」：票根这时已经交了，Esc 不能把上车 / 后续吞掉，和按按钮一样往下走。
                OnCancel = delegate { if (after != null) after(); },
            });
        }

        /// <summary>站台记录的一行摘要（运单牌上看）。</summary>
        internal static string RecordsLine(int runRecords)
        {
            int all = Records | runRecords;
            var names = new List<string>();
            for (int i = 0; i < RecordIds.Length; i++) if ((all & (1 << i)) != 0) names.Add(LastShiftText.RecordName(RecordIds[i]));
            return L10n.T("站台记录 ", "Station records ") + Count(all) + "/" + RecordIds.Length
                + (names.Count == 0 ? "" : L10n.T("：", ": ") + string.Join(L10n.T("、", ", "), names.ToArray()));
        }

        internal static void Tick()
        {
            if (persistence != null) persistence.Tick();
            LastShiftNoteBridge.Tick();
        }
    }

    /// <summary>
    /// 一趟里的剧情进度：三处线索的交互点、「末班票 n/3」、站台记录与借车影换边的判定。
    /// 只活在 <see cref="LastShiftSession"/> 里；撤离时交给 <see cref="LastShiftStory.CommitRun"/>，阵亡随会话丢弃。
    /// </summary>
    internal sealed class LastShiftRun : IDisposable
    {
        private readonly Action<string, bool> status;
        private readonly Func<bool> valid;
        private readonly List<GameObject> interactables = new List<GameObject>();
        private readonly bool journal;
        private readonly GameObject[] pins = new GameObject[LastShiftStory.NoteCount];
        private int notes, records;
        private bool stubGiven;
        private float shadowStart = -1f;
        // 借车影换边（原型 layout.facilities.shadowTransfer）：进站或出站时从中线西侧穿到东台，8 秒内。three.js (x,z) → Unity (x,-z)。
        private static readonly Vector3 ShadowFrom = new Vector3(0f, 0f, 5f), ShadowTo = new Vector3(5f, 0f, 20f);
        private const float ShadowRadius = 1.6f, ShadowSeconds = 8f;

        internal int Notes { get { return notes; } }
        internal int Records { get { return records; } }

        internal LastShiftRun(LastShiftWorld world, Action<string, bool> status, Func<bool> valid)
        {
            this.status = status; this.valid = valid;
            journal = LastShiftStory.Journal;
            notes = LastShiftStory.Notes;
            for (int i = 0; i < LastShiftStory.NoteCount; i++)
            {
                int index = i;
                Transform anchor;
                if (!world.Interacts.TryGetValue(LastShiftStory.NoteMarkers[i], out anchor))
                { Debug.LogWarning(LastShiftRaidScene.LogTag + " 缺少线索标记 " + LastShiftStory.NoteMarkers[i]); continue; }
                var host = new GameObject("LastShiftNote_" + (i + 1));
                host.transform.SetParent(world.Root.transform, false);
                host.transform.position = anchor.position;
                interactables.Add(host);
                var device = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftDeviceInteractable>(host.transform, "Interact", new Vector3(0f, 1f, 0f), x =>
                {
                    x.Bind(LastShiftStory.NoteMarkers[index], 1.2f, delegate { return LastShiftText.NoteVerb(index); }, delegate { return valid(); }, delegate { Read(index); });
                    SphereCollider sensor = x.GetComponent<SphereCollider>();
                    if (sensor != null) { sensor.radius = 1.2f; sensor.isTrigger = true; }
                });
                if (device == null) Debug.LogWarning(LastShiftRaidScene.LogTag + " 线索交互点创建失败：" + LastShiftStory.NoteMarkers[i]);
                if (!journal && (notes & (1 << i)) == 0) pins[i] = Pin(world, anchor.position, i);
            }
        }

        /// <summary>M 图上标出还没找到的线索（owner 2026-11-11：用地图标出来，不用字幕指路）。读到就收掉。</summary>
        private GameObject Pin(LastShiftWorld world, Vector3 at, int index)
        {
            GameObject pin = null;
            try
            {
                string key = "BossRush_LastShift_ClueMap_" + (index + 1);
                LocalizationHelper.InjectLocalization(key, LastShiftText.ClueMapName(index));
                pin = new GameObject("LastShiftCluePin_" + (index + 1));
                pin.transform.SetParent(world.Root.transform, false);
                pin.transform.position = at;
                interactables.Add(pin);
                SimplePointOfInterest poi = pin.AddComponent<SimplePointOfInterest>();
                poi.Color = new Color(0.93f, 0.84f, 0.62f);
                poi.ScaleFactor = 1f;
                poi.IsArea = false;
                poi.Setup(null, key);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 线索地图标记失败：" + e.Message); }
            return pin;
        }

        internal void Record(string id)
        {
            int index = Array.IndexOf(LastShiftStory.RecordIds, id);
            if (index >= 0) records |= 1 << index;
        }

        /// <summary>HUD 小标里的「末班票 n/3」：故事收尾后、一条都没找到时不显示。</summary>
        internal string Counter()
        {
            if (journal) return null;
            int n = LastShiftStory.Count(notes);
            return n == 0 ? null : L10n.T("末班票 ", "Last ticket ") + n + "/" + LastShiftStory.NoteCount;
        }

        private void Read(int index)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            notes |= 1 << index;
            if (pins[index] != null) { UnityEngine.Object.Destroy(pins[index]); pins[index] = null; }
            string message = LastShiftText.NoteFinding(index);
            int found = LastShiftStory.Count(notes);
            if (!journal && found == LastShiftStory.NoteCount)
            {
                if (stubGiven || TidegateInventory.Carries(player, BossRushItemIds.LastShiftTicketStub)) { }
                else if (GiveStub(player))
                {
                    stubGiven = true;
                    message += L10n.T(" 你把那张票揣走了。", " You pocket the ticket.");
                }
                else message += L10n.T(" 包里塞不下那张票，回头再来翻。", " There's no room for the ticket. Come back for it.");
            }
            status(message, false);
        }

        /// <summary>出击途中发一件：先进背包，放不下官方投递，再不行落在脚下（口径同天空岛拦路头目退赃）。</summary>
        private static bool GiveStub(CharacterMainControl player)
        {
            if (player == null) return false;
            Item item = null;
            try
            {
                if (ItemAssetsCollection.GetPrefab(BossRushItemIds.LastShiftTicketStub) == null) return false;
                item = ItemAssetsCollection.InstantiateSync(BossRushItemIds.LastShiftTicketStub);
                if (item == null || item.TypeID != BossRushItemIds.LastShiftTicketStub) { if (item != null) item.DestroyTree(); return false; }
                ItemUtilities.SendToPlayer(item, true, false);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 票根投递通知：" + e.Message); }
            if (item == null) return false;
            if (item.InInventory != null || item.PluggedIntoSlot != null) return true;
            try { item.Drop(player, true); return true; }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 票根落地失败：" + e.Message); }
            return false;
        }

        /// <summary>借车影换边：进站或出站时，从中线这头穿到东台那头，8 秒内。</summary>
        internal void Tick(Vector3 position, LastShiftPhase phase)
        {
            if (phase != LastShiftPhase.Arrival && phase != LastShiftPhase.Depart) { shadowStart = -1f; return; }
            if (Flat(position, ShadowFrom) < ShadowRadius) shadowStart = Time.time;
            else if (shadowStart >= 0f && Flat(position, ShadowTo) < ShadowRadius && Time.time - shadowStart < ShadowSeconds)
            {
                shadowStart = -1f;
                if ((records & (1 << 3)) == 0 && (LastShiftStory.Records & (1 << 3)) == 0)
                    status(L10n.T("借着车影换到了东台。", "You crossed to the east platform in the train's shadow."), false);
                Record("shadow");
            }
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        public void Dispose()
        {
            foreach (GameObject g in interactables) if (g != null) UnityEngine.Object.Destroy(g);
            interactables.Clear();
        }
    }

    /// <summary>
    /// 三条线索与收尾 → 官方笔记图鉴的镜像（口径照 <see cref="TidegateNoteBridge"/>）：只往官方 `NoteIndex` 写，先 `Notes.Add` 再 `SetNoteDynamic`；
    /// 标题与正文走本地化注入（`Note_{key}_Title/_Content`）。拿不到 `NoteIndex.Instance` 或存档读不了时静默跳过，镜像保留原状态。
    /// </summary>
    internal static class LastShiftNoteBridge
    {
        internal const string NoteKeyPrefix = "BossRushLastShift_";
        internal const string EndingId = "Ticket";
        private static bool syncPending = true, subscribed, warned;

        private static string NoteId(int index) { return "Clue" + (index + 1); }
        private static string Key(string id) { return NoteKeyPrefix + id; }

        private static IEnumerable<string> AllIds()
        {
            for (int i = 0; i < LastShiftStory.NoteCount; i++) yield return NoteId(i);
            yield return EndingId;
        }

        private static int Index(string id)
        {
            int number;
            return id != null && id.StartsWith("Clue", StringComparison.Ordinal) && int.TryParse(id.Substring(4), out number) ? number - 1 : -1;
        }

        private static bool Recorded(LastShiftStoryData data, string id)
        {
            if (data == null) return false;
            if (id == EndingId) return data.Journal;
            int index = Index(id);
            return index >= 0 && (data.Notes & (1 << index)) != 0;
        }

        /// <summary>启动与切语言时注入标题与正文（由 <see cref="LastShiftRaidScene.InjectLocalization"/> 调）。</summary>
        internal static void InjectNoteKeys()
        {
            try
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < LastShiftStory.NoteCount; i++)
                {
                    map["Note_" + Key(NoteId(i)) + "_Title"] = LastShiftText.NoteTitle(i);
                    map["Note_" + Key(NoteId(i)) + "_Content"] = LastShiftText.NoteBody(i);
                }
                map["Note_" + Key(EndingId) + "_Title"] = LastShiftText.EndingTitle;
                map["Note_" + Key(EndingId) + "_Content"] = LastShiftText.EndingBody;
                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e) { ModBehaviour.DevLog("[LastShiftNote] [WARNING] 图鉴文案注入失败: " + e.Message); }
        }

        internal static void EnsureRuntime()
        {
            if (subscribed) return;
            SavesSystem.OnSetFile += RequestSync;
            SavesSystem.OnSaveDeleted += RequestSync;
            subscribed = true;
        }

        internal static void RequestSync() { syncPending = true; }

        /// <summary>模块每帧调；只在需要同步时做事，平时 O(1)。剧情存档读不了时等它恢复（口径同潮闸镇）。</summary>
        internal static void Tick()
        {
            if (!syncPending || !LastShiftStory.Writable) return;
            try
            {
                LastShiftStoryData data = LastShiftStory.Current;
                NoteIndex index = NoteIndex.Instance;
                if (data == null || index == null || index.Notes == null) return;
                List<Note> notes = index.Notes;
                foreach (string id in AllIds())
                {
                    string key = Key(id);
                    Note note = null;
                    for (int n = 0; n < notes.Count; n++)
                        if (notes[n] != null && string.Equals(notes[n].key, key, StringComparison.Ordinal)) { note = notes[n]; break; }
                    if (note == null)
                    {
                        note = new Note { key = key, image = null, hide = false };
                        notes.Add(note);
                    }
                    bool ours = Recorded(data, id), theirs = NoteIndex.GetNoteUnlocked(key);
                    NoteIndex.SetNoteDynamic(note);
                    if (ours && !theirs) NoteIndex.SetNoteUnlocked(key);
                    else if (!ours && theirs) Relock(index, key);
                }
                syncPending = false;
                warned = false;
            }
            catch (Exception e)
            {
                if (!warned) ModBehaviour.DevLog("[LastShiftNote] [WARNING] 图鉴注册失败: " + e.Message);
                warned = true;
            }
        }

        private static void Relock(NoteIndex index, string key)
        {
            HashSet<string> unlocked = index.UnlockedNotes;
            if (unlocked == null || !unlocked.Remove(key)) return;
            Action<string> changed = NoteIndex.onNoteStatusChanged;
            if (changed != null) changed(key);
        }

        internal static void ResetStaticCaches()
        {
            if (subscribed)
            {
                SavesSystem.OnSetFile -= RequestSync;
                SavesSystem.OnSaveDeleted -= RequestSync;
                subscribed = false;
            }
            syncPending = true;
            warned = false;
        }
    }
}
