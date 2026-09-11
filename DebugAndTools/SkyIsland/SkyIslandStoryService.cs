using System;
using System.Collections.Generic;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>会话持有的独立槽位剧情门面。共享 store / coordinator 分别拥有存档订阅与唯一物理写盘。</summary>
    internal sealed class SkyIslandStoryService
    {
        private BossRushSlotJsonStore<SkyIslandStoryData> store;
        private BossRushSaveCoordinatorEngine coordinator;
        private bool opened, slotChanged;
        private int entrySlot;
        private string lastSaveError;
        private float nextRecoveryAt;
        private bool assetSnapshotRequired;
        private string summaryCache, summaryStatus;
        private int summaryFlags;

        internal SkyIslandStoryService()
        {
            store = CreateStore();
            coordinator = new BossRushSaveCoordinatorEngine(new SaveSource(this, store), false);
        }

        private BossRushSlotJsonStore<SkyIslandStoryData> CreateStore()
        {
            return new BossRushSlotJsonStore<SkyIslandStoryData>(new BossRushSlotJsonStoreSpec<SkyIslandStoryData>
            {
                StorageKey = SkyIslandStoryRules.StorageKey,
                SchemaVersion = SkyIslandStoryRules.SchemaVersion,
                LogPrefix = "[SkyIsland] ", DisplayName = "晴岚群岛",
                CreateDefault = SkyIslandStoryRules.CreateDefault,
                Encode = SkyIslandStoryCodec.Encode, Decode = SkyIslandStoryCodec.Decode,
                ReadSchemaVersion = SkyIslandStoryCodec.ReadSchemaVersion,
                NotifySlotChanged = OnSlotChanged
            });
        }

        internal bool IsCurrentSlot
        {
            get
            {
                if (!opened || slotChanged) return false;
                try { return SavesSystem.CurrentSlot == entrySlot; }
                catch (Exception) { return false; }
            }
        }
        internal SkyIslandStoryData Current { get { return store.Current; } }
        internal bool CanWrite { get { return IsCurrentSlot && !store.HasWriteBarrier && !store.IsStoreFaulted; } }

        internal bool RequireAssetSnapshot(out string error)
        {
            error = null;
            if (!CanWrite || SavesSystem.IsSaving || CharacterMainControl.Main == null ||
                CharacterMainControl.Main.CharacterItem == null || PlayerStorage.Instance == null ||
                !PlayerStorage.Instance.HasInitialized() || PlayerStorage.Loading ||
                PlayerStorage.Inventory == null || PlayerStorageBuffer.Instance == null)
            { error = "asset_save_not_ready"; return false; }
            assetSnapshotRequired = true;
            return true;
        }
        /// <summary>
        /// 当前目标。HUD 每 0.5 秒读一次，而 `SkyIslandStoryRules.Objective` 每次都重新拼接字符串；
        /// 目标文本只取决于剧情位与界面语言，两者都没变就复用上一次的结果（口径同 <see cref="Summary"/>）。
        /// </summary>
        internal string CurrentObjective
        {
            get
            {
                SkyIslandStoryData data = Current;
                bool chinese = L10n.IsChinese;
                if (objectiveCache == null || objectiveFlags != data.flags || objectiveChinese != chinese)
                {
                    objectiveCache = SkyIslandStoryRules.Objective(data);
                    objectiveFlags = data.flags;
                    objectiveChinese = chinese;
                }
                return objectiveCache;
            }
        }
        private string objectiveCache;
        private int objectiveFlags;
        private bool objectiveChinese;
        internal string SaveStatus
        {
            get
            {
                if (!IsCurrentSlot)
                    return L10n.T("存档槽已改变，请重新进入群岛", "Save slot changed — re-enter the archipelago");
                if (store.HasWriteBarrier)
                    return L10n.T("群岛记录无法读取，已保护原存档；任务暂不可提交",
                        "Archipelago records are unreadable; the original save is protected and progress cannot be submitted");
                if (store.IsStoreFaulted || lastSaveError != null)
                    return L10n.T("群岛进度保存待重试：", "Archipelago progress save will retry: ") +
                        (store.LastError ?? lastSaveError);
                return store.HasPendingWrite || coordinator.HasDeferredFlush
                    ? L10n.T("群岛记录待安全时机保存", "Archipelago records will save at a safe moment")
                    : L10n.T("群岛记录已同步", "Archipelago records are in sync");
            }
        }
        /// <summary>
        /// F6 地图每帧读一次摘要，逐帧重建这串文本会持续产生小额分配（实测 264–320 B/次）。
        /// 只在剧情位或保存状态真的改变时重建。
        /// </summary>
        internal string Summary
        {
            get
            {
                SkyIslandStoryData data = Current;
                string status = SaveStatus;
                if (summaryCache != null && summaryFlags == data.flags && string.Equals(summaryStatus, status, StringComparison.Ordinal))
                    return summaryCache;
                summaryFlags = data.flags;
                summaryStatus = status;
                summaryCache = CurrentObjective +
                    L10n.T("\n支线：种植记录", "\nSide paths: planting record") + Mark(data.Has(SkyIslandStoryFlag.PlantingRecord)) +
                    L10n.T(" 旧信", " · old letter") + Mark(data.Has(SkyIslandStoryFlag.OldLetter)) +
                    L10n.T(" 航路图", " · route chart") + Mark(data.Has(SkyIslandStoryFlag.RouteChart)) +
                    L10n.T(" 观星镜", " · telescope") + Mark(data.Has(SkyIslandStoryFlag.Telescope)) + "\n" + status;
                return summaryCache;
            }
        }
        private static string Mark(bool value) { return value ? " ✓" : " ○"; }

        /// <summary>
        /// 岛上物理落盘的去抖秒数。官方 `SavesSystem.SaveFile` 是「备份拷贝 + 整档同步写」（反编译源 `Saves/SavesSystem.cs:503`），
        /// 旧写法每接受一条事实、下一个 45 m 内无敌人的帧就整档写一次：一个新存档从头玩下来，光是首次到访 12 个区域、
        /// 首次清掉 13 组遭遇、收录 20 处见闻就是四十多次同步写盘，而且恰好落在「刚打完一组」「刚踏上新岛」的帧上。
        /// 到访、清场、见闻这类丢了也能重做的事实攒到这个秒数再一起写；剧情动作（航标、捷径、支线物证、和解、敲钟、噬风）
        /// 与已经欠着的重试照旧下一个安全帧就写。离岛、死亡与宿主销毁走 <see cref="TryClose"/> 的绕闸落盘，不受去抖影响。
        /// </summary>
        internal const float FlushDebounceSeconds = 30f;
        /// <summary>最早一条还没落盘的事实被接受的时刻（unscaled，基础设施节流而非玩法计时）；-1 表示没有待写事实。</summary>
        private float pendingSince = -1f;
        /// <summary>待写批次里有剧情动作：下一个安全帧就写，不等去抖。</summary>
        private bool urgentPending;

        /// <summary>分段计时的起点（realtimeSinceStartup，含读条、读剧情与暂停）；-1 表示本服务还没开过。</summary>
        private float timingOrigin = -1f;
        /// <summary>本趟已记过的计时事件，避免同一件事每秒重投时刷屏。</summary>
        private readonly HashSet<string> timingSeen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 分段计时日志：给 owner 实机回填时长模型（`docs/制作教程/天空岛/天空岛_待人工验证清单.md` 的计时步骤）。
        /// 只在事件上各记一行 `[SkyIsland] SKY_TIMING t=秒 ev=事件 id=对象`——进岛、落地、剧情动作被接受、首次到访、清场、
        /// 见闻、委托与服务、挑战开始、离岛——不在任何每帧路径上。时钟用 realtimeSinceStartup：读剧情、开背包与暂停的时间
        /// 都算进「这一段实际玩了多久」。走 Debug.Log 而不是 DevLog，正式构建里照样有。
        /// </summary>
        internal void LogTiming(string ev, string id)
        {
            if (timingOrigin < 0f || string.IsNullOrEmpty(ev)) return;
            double seconds = Math.Round((double)(Time.realtimeSinceStartup - timingOrigin), 1);
            Debug.Log("[SkyIsland] SKY_TIMING t=" + seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                " ev=" + ev + (string.IsNullOrEmpty(id) ? string.Empty : " id=" + id));
        }

        /// <summary>同一件事一趟只记一行（清场会每秒重投、离岛保存可能被推迟重试）。</summary>
        internal void LogTimingOnce(string ev, string id)
        {
            if (timingOrigin < 0f || !timingSeen.Add(ev + ":" + id)) return;
            LogTiming(ev, id);
        }

        /// <summary>登记一条刚被 store 接受的事实。urgent = 剧情动作，下一个安全帧就落盘。</summary>
        private void MarkPending(bool urgent)
        {
            if (pendingSince < 0f) pendingSince = Time.unscaledTime;
            if (urgent) urgentPending = true;
        }

        internal void Open()
        {
            if (opened) return;
            entrySlot = SavesSystem.CurrentSlot;
            store.EnsureSubscribed();
            store.LoadOrInit();
            slotChanged = false;
            opened = true;
            timingOrigin = Time.realtimeSinceStartup;
            timingSeen.Clear();
            LogTiming("raid_open", "slot" + entrySlot);
        }

        internal bool TryApply(SkyIslandStoryAction action, out string message)
        {
            // LoadOrInit 必须先经过槽位烙印再 Store；不允许旧会话把候选状态写到新槽。
            if (!CanWrite) { message = SaveStatus; return false; }
            SkyIslandStoryData candidate;
            if (!SkyIslandStoryRules.TryApply(Current, action, out candidate, out message)) return false;
            if (!store.Store(candidate))
            {
                message = L10n.T("群岛记录提交失败，可稍后再次操作。",
                    "Could not commit the archipelago record. Try again in a moment.");
                return false;
            }
            MarkPending(true);
            LogTiming("story", action.ToString());
            message += L10n.T("\n进度已记录，待安全时机保存。",
                "\nProgress recorded; it will be written at a safe moment.");
            return true;
        }

        internal bool RecordEncounterCleared(string regionId)
        {
            if (!CanWrite || string.IsNullOrEmpty(regionId)) return false;
            if (Current.EncounterCleared(regionId))
            {
                // 自动组按出击刷新，老档上每趟都会再清一次：存档不动，计时照记（每趟每组一行）。
                LogTimingOnce("clear", regionId);
                return false;
            }
            // 战斗 owner 只在整个遭遇确认清场后调用；中断不消费永久结果。
            SkyIslandStoryData candidate = Current.Copy();
            var values = new List<string>(candidate.clearedEncounters);
            values.Add(regionId);
            candidate.clearedEncounters = values.ToArray();
            if (!store.Store(candidate)) return false;
            MarkPending(false);
            LogTimingOnce("clear", regionId);
            return true;
        }

        internal bool RecordSearch(string marker, out string message)
        {
            SkyIslandStoryAction action;
            if (SkyIslandStoryRules.TrySearchAction(marker, out action)) return TryApply(action, out message);
            if (!CanWrite) { message = SaveStatus; return false; }
            if (Array.IndexOf(Current.discoveredNotes, marker) >= 0)
            {
                message = L10n.T("这页见闻已经收进群岛手记。",
                    "That page is already in your archipelago notes.");
                return false;
            }
            SkyIslandStoryData candidate = Current.Copy();
            var values = new List<string>(candidate.discoveredNotes);
            values.Add(marker);
            candidate.discoveredNotes = values.ToArray();
            if (!store.Store(candidate))
            { message = L10n.T("手记提交失败，请稍后重试。", "Could not commit the note. Try again shortly."); return false; }
            MarkPending(false);
            LogTiming("note", marker);
            message = L10n.T("群岛见闻已收入本槽手记。", "The note is saved to this slot's archipelago journal.");
            return true;
        }

        /// <summary>
        /// 信鸽来信、归航船名册、纪念品发放记录与点起来的风晶灯：和见闻一样写进本槽手记（`discoveredNotes`），共用 <see cref="RecordSearch"/> 的
        /// 去重、去抖与计时口径，不加存档字段。只收已登记的 id（<see cref="SkyIslandLetters.Find"/> / <see cref="SkyIslandCrew.IndexOf"/> /
        /// <see cref="SkyIslandItemRules.FindKeepsake"/> / <see cref="SkyIslandLights.Find"/>），不让任意字符串进存档。
        /// </summary>
        internal bool RecordNote(string id, out string message)
        {
            // 内容批次四：放回蛙鸣池的蛙卵（Frog_1..3，SkyIslandMosquitoRules.IsFrogNote）同样只收登记过的 id。
            if (SkyIslandLetters.Find(id) == null && SkyIslandCrew.IndexOf(id) < 0 && SkyIslandItemRules.FindKeepsake(id) == null &&
                SkyIslandLights.Find(id) == null && !SkyIslandMosquitoRules.IsFrogNote(id))
            {
                message = L10n.T("这条手记没有登记。", "That journal entry is not registered.");
                return false;
            }
            return RecordSearch(id, out message);
        }

        /// <summary>
        /// 仅供「已写入纪念品台账、但官方物品交付在真正接管实例前抛异常」的补偿路径使用。
        /// 只删除当前槽中仍存在的精确 id；写屏障、槽位变化或编码失败时拒绝修改，避免把别的槽/别的事实一起回滚。
        /// </summary>
        internal bool RemoveNote(string id)
        {
            if (!CanWrite || string.IsNullOrEmpty(id)) return false;
            SkyIslandStoryData current = Current;
            if (current == null || current.discoveredNotes == null) return false;
            var values = new List<string>(current.discoveredNotes);
            int index = values.IndexOf(id);
            if (index < 0) return false;
            values.RemoveAt(index);
            SkyIslandStoryData candidate = current.Copy();
            candidate.discoveredNotes = values.ToArray();
            if (!store.Store(candidate)) return false;
            MarkPending(true);
            return true;
        }

        internal static int RegionBit(string id)
        {
            switch (id)
            {
                case "A": return 1; case "B": return 2; case "C": return 4; case "D": return 8;
                case "E": return 16; case "F": return 32; case "G": return 64; case "H": return 128;
                case "S1": return 256; case "S2": return 512; case "S3": return 1024; case "S4": return 2048;
                default: return 0;
            }
        }
        /// <summary>
        /// 地面碰撞体名 → 区域 id。生成器把地面按区域切成 `COL_Ground_{区域}`（12 个岛 + 15 座桥，
        /// 见 tools/generate_sky_island.py）；岛（A–H、S1–S4）返回 id，桥（AB、CS1、K1…）与其它名字返回 null。
        /// 纯函数：会话据此建「脚下是哪个区域」的表，隔离回归直接执行它。
        /// </summary>
        internal static string GroundRegionOf(string colliderName)
        {
            const string prefix = "COL_Ground_";
            if (colliderName == null || colliderName.Length <= prefix.Length ||
                !colliderName.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string id = colliderName.Substring(prefix.Length);
            return RegionBit(id) == 0 ? null : id;
        }

        internal bool HasVisitedRegion(string id) { return (Current.visitedRegions & RegionBit(id)) != 0; }
        internal bool RecordRegionVisited(string regionId)
        {
            int bit = RegionBit(regionId);
            if (!CanWrite || bit == 0 || (Current.visitedRegions & bit) != 0) return false;
            SkyIslandStoryData candidate = Current.Copy();
            candidate.visitedRegions |= bit;
            if (!store.Store(candidate)) return false;
            MarkPending(false);
            LogTiming("region_first", regionId);
            return true;
        }

        internal string DescribeNpc(string id)
        {
            SkyIslandStoryData data = Current;
            switch (id)
            {
                // 居民台词随进度与收到的信变化：信鸽送来的信大多是写给他们的（SkyIslandLetters），收下之后当面会提一句。
                case "sky_qinghe":
                    return (data.Has(SkyIslandStoryFlag.PlantingDelivered)
                        ? (data.Has(SkyIslandStoryFlag.Ending)
                            ? L10n.T("晴禾：归航船上的人都来吃过了。最后一畦我还留着——留给下一位旅人。",
                                "Qinghe: Everyone off the homecoming boat has been by to eat. I am still keeping the last bed — for the next traveller.")
                            : L10n.T("晴禾：新风车转起来了。下一船归来时，他们会有热菜吃。",
                                "Qinghe: The new pinwheel is turning. When the next ship comes home there will be a hot meal waiting."))
                        : L10n.T("晴禾：蛙鸣池那边有我丢下的种植记录。没来得及说出口的事，都写在里面了。",
                            "Qinghe: My planting record is still out at Frogsong Pool. Everything I never got to say is written in it.")) +
                        (SkyIslandLetters.Collected(data, "Letter_03")
                            ? L10n.T("\n……那封没署名的信，我认得那笔字。田埂上那一格，我还替他留着。",
                                "\n…That unsigned letter — I know that handwriting. I am still keeping that plot on the ridge for him.")
                            : string.Empty) + QingheGnatLine(data);
                case "sky_weibai":
                    return (data.Has(SkyIslandStoryFlag.Ending)
                        ? L10n.T("苇白：钟响那天，东西两头的风铃一起响了——苇生以前说，那是岛在叫大家回家。委托板我还挂着，路过就来揭一张。\n",
                            "Weibai: The day the bell rang, the chimes at both ends of the market rang together — Weisheng used to say that is the island calling everyone home. The contract board stays up; take a slip whenever you pass.\n")
                        : data.BothBeacons
                            ? L10n.T("苇白：两盏灯都亮了，东西两头的风铃在一起响！去鸣风栈道吧，钟庭在等你。\n",
                                "Weibai: Both lamps are lit and the chimes at either end are ringing together! On to Windsong Boardwalk — the Bell Court is waiting for you.\n")
                            : L10n.T("苇白：西边悬根林有风标，东边残星工坊有星灯，先去哪边都行。装置还在，修好它们就能让双航标门重新工作。\n",
                                "Weibai: The wind beacon is west in the Hanging Root Wood, the star lamp east at the Fallen Star Workshop — either order works. The devices are still standing; repair them and the twin-beacon gate runs again.\n")) +
                        (SkyIslandLetters.Collected(data, "Letter_02")
                            ? L10n.T("苇生的信你替我收下了？……他还是那么爱说大话。\n", "You took in Weisheng's letter for me? …He still loves to talk big.\n")
                            : string.Empty) + WeibaiZapperLine(data) + CurrentObjective;
                case "sky_fuzhou":
                    return (data.Has(SkyIslandStoryFlag.Ending)
                        ? L10n.T("浮舟：钟声听见了。船一直在这里，船头挂着名册，四个归来的人各写了一页，去看看吧。",
                            "Fuzhou: I heard the bell. The boat has always been here — there is a roster at the bow, and each of the four who came home wrote a page. Go and have a look.")
                        : L10n.T("浮舟：沿桥去风铃集找苇白。走累了随时回来，码头的系泊桩会送你回家，已记录的故事下次继续。",
                            "Fuzhou: Follow the bridge to Windchime Market and find Weibai. Come back whenever you tire — the mooring post at the dock takes you home, and whatever you have recorded carries over.")) +
                        (SkyIslandLetters.Collected(data, "Letter_01")
                            ? L10n.T("\n阿潮的缆绳……我这就挂回最高的那根桩上。", "\nAchao's mooring line… I will hang it back on the tallest post right away.")
                            : string.Empty) + FuzhouLampLine(data);
                case "sky_miantai":
                    return MiantaiLine(data) + MiantaiItchLine;
                case "sky_zheling":
                    return ZhelingLine(data);
                case "sky_bellkeeper":
                    if (data.Has(SkyIslandStoryFlag.Ending))
                        return L10n.T("钟守：钟声不是命令，是回答。名册上多了一行字，我没有擦掉。",
                            "The Bell Keeper: A bell is not an order; it is an answer. A new line appeared in the register, and I have not wiped it away.") +
                            (SkyIslandMosquitoRules.FrogsComplete(data)
                                ? L10n.T("\n夜里敲钟的时候，蛙鸣池那边有蛙应声。", "\nWhen the bell rings at night now, the frogs at Frogsong Pool answer.")
                                : string.Empty);
                    return data.BellKeeperResolved
                        ? L10n.T("钟守：去吧，敲响归航钟。让他们知道，岛上还有人在等。",
                            "The Bell Keeper: Go on, ring the Homecoming Bell. Let them know someone on the islands is still waiting.")
                        : L10n.T("无声钟守：钟一响，就会有人再出海。我需要你证明航路安全，否则先停下我的守钟装置。",
                            "The Silent Bell Keeper: Ring it and someone puts to sea again. Prove the lanes are safe — or stop my bell engine first.");
                default: return CurrentObjective;
            }
        }

        private static string MiantaiLine(SkyIslandStoryData data)
        {
            return data.Has(SkyIslandStoryFlag.WindBeacon)
                        ? (data.Has(SkyIslandStoryFlag.OldLetter)
                            ? L10n.T("眠苔：风标转回来了，根环里的风也顺了。倒挂邮亭那封信你拿到了？那就去镜水寺吧，折翎等它等了很久。",
                                "Miantai: The wind beacon has turned back and the air in the root ring runs smooth again. You have the letter from the Upturned Post Hut? Then go to Mirrorwater Temple — Zheling has waited a long time for it.")
                            : L10n.T("眠苔：风标转回来了，根环里的风也顺了。倒挂邮亭还吊着一封信——风没有把它送到，或许你可以。",
                                "Miantai: The wind beacon has turned back and the air in the root ring runs smooth again. A letter still hangs in the Upturned Post Hut — the wind never delivered it. Perhaps you can."))
                        : L10n.T("眠苔：风标困在根环那头，先清掉附近的威胁再校准。倒挂邮亭还吊着一封信——风没有把它送到，或许你可以。",
                            "Miantai: The wind beacon is stuck out past the root ring; clear the threats around it before you calibrate. A letter still hangs in the Upturned Post Hut — the wind never delivered it. Perhaps you can.");
        }

        private static string ZhelingLine(SkyIslandStoryData data)
        {
            return data.ZhelingResolved
                ? (data.Has(SkyIslandStoryFlag.ZhelingReconciled)
                    ? L10n.T("折翎：路已修好。这次我守着灯，等大家回来。",
                        "Zheling: The road is mended. This time I keep the light and wait for them to come home.") +
                      (SkyIslandLetters.Collected(data, "Letter_06")
                        ? L10n.T("\n扫地的老人还替我擦着钟……等灯都亮了，我回寺里喝那杯茶。",
                            "\nThe old sweeper still polishes the bell for me… Once every light is lit, I will go back to the temple for that cup of tea.")
                        : string.Empty) + ZhelingFrogLine(data)
                    : L10n.T("旧腰牌上刻着：『航路交给你。』",
                        "The old badge is engraved: 'The route is yours now.'"))
                : L10n.T("折翎：我不会再让人走进那场风灾。若有旧信和航路图，就留下来谈；若坚持通行，请明确挑战。",
                    "Zheling: I will not let anyone walk into that storm again. If you carry the old letter and the route chart, stay and talk. If you insist on passing, challenge me outright.");
        }

        /// <summary>内容批次四：眠苔说苔药与药膏怎么分工——苔药管伤、顺手止痒；药膏管痒、抹上一阵都不怕叮。</summary>
        private static string MiantaiItchLine
        {
            get
            {
                return L10n.T("\n被云蚋叮痒了别抓：我的苔药管伤，顺手把痒也止了；药膏更凉，抹上一阵再被叮都不痒。",
                    "\nIf the cloud gnats have you itching, do not scratch: my remedy is for wounds and stops the itch on the way; the salve is cooler, and for a while after it new bites will not itch.");
            }
        }

        /// <summary>内容批次四：晴禾说起梯田水车边的云蚋、她的纱笠与蛙鸣池的青蛙（<see cref="SkyIslandMosquitoRules"/>）。</summary>
        private static string QingheGnatLine(SkyIslandStoryData data)
        {
            if (SkyIslandMosquitoRules.FrogsComplete(data))
                return L10n.T("\n蛙鸣池又有蛙叫了。繁育的水边护好了，青蛙也愿意回来，水车边的蚋少了些。夜里下地我还是带着纱笠。",
                    "\nFrogsong Pool is croaking again. With its breeding shallows tended, the frogs are returning and there are fewer gnats by the water wheel. I still bring my veil to work at night.");
            if (SkyIslandMosquitoRules.FrogsReleased(data) > 0)
                return L10n.T("\n听说有人往蛙鸣池放了蛙卵？好——青蛙回来了，云蚋就少了。",
                    "\nI hear someone has been putting frogspawn back in Frogsong Pool? Good — when the frogs come back, the gnats thin out.");
            return L10n.T("\n夜里水车边蚋多，我拿云苔纤维撒一撮星屑织了顶纱笠。夜里要下地，就来灶台找我织一顶。",
                "\nAt night the gnats swarm by the water wheel, so I wove a veil of cloudmoss with a pinch of stardust. If you go out at night, come to the stove and I will weave you one.");
        }

        /// <summary>内容批次四：苇白是修灯的人——点亮的风晶灯够了，她就把灯芯调成引蚋的陷阱（灭蚊灯配方的门槛读同一个数）。</summary>
        private static string WeibaiZapperLine(SkyIslandStoryData data)
        {
            int lamps = SkyIslandLights.LampsLit(data);
            if (lamps <= 0) return string.Empty;
            SkyIslandRecipe zapper = SkyIslandFieldcraftRules.FindRecipe("Zapper");
            if (zapper != null && lamps < zapper.RequiresLamps)
                return L10n.T("苇白：风晶灯的芯会嗡嗡地唱，云蚋就是循着那点声音和光来的。再亮一盏，我就听得清那个调子，能把灯芯调成陷阱。\n",
                    "Weibai: A windcrystal wick hums, and the cloud gnats come for that hum and the light. Light one more and I will hear the tune clearly enough to turn a wick into a trap.\n");
            return L10n.T("苇白：灭蚊灯的调子我交给浮舟了——晴岚风晶作芯、残铜片打罩，去他的渡口工台做。\n",
                "Weibai: I gave Fuzhou the tune for the gnat zapper — a Qinglan Windcrystal for the wick and brass scrap for the cage. Make it at his dock workbench.\n");
        }

        /// <summary>内容批次四：和解之后的折翎说起寺里池子的青蛙——蛙卵从这里捧回蛙鸣池。</summary>
        private static string ZhelingFrogLine(SkyIslandStoryData data)
        {
            if (SkyIslandMosquitoRules.FrogsComplete(data))
                return L10n.T("\n寺里的青蛙还在，蛙鸣池也有了新的繁育处。你送回去的蛙卵，让那片水边重新有人照看。",
                    "\nThe temple frogs are still here, and Frogsong Pool has a breeding place again. The spawn you carried home means someone is tending those shallows once more.");
            return L10n.T("\n风灾那年，蛙鸣池的青蛙都逃进了寺里的池子。夜里去池边捧一团蛙卵，替它们回家吧。",
                "\nThe year of the storm, the frogs of Frogsong Pool all fled into the temple pool. Scoop up some frogspawn from its edge one night and take them home.");
        }

        /// <summary>
        /// 浮舟说起风晶灯（<see cref="SkyIslandLights"/>）：星灯亮起之前不提（碎晶要进工坊的熔晶炉，炉子还烧不起来），
        /// 之后告诉玩家碎晶攒够五片来找他；十盏都亮了换一句。这是玩家头一回听说「岛上的灯」的地方。
        /// </summary>
        private static string FuzhouLampLine(SkyIslandStoryData data)
        {
            if (!data.Has(SkyIslandStoryFlag.StarLamp)) return string.Empty;
            if (SkyIslandLights.AllLit(data))
                return L10n.T("\n十盏灯都亮着。夜里从码头往外看，像一条回家的路。",
                    "\nAll ten lights are burning. At night, looking out from the dock, they read like a road home.");
            return L10n.T("\n星灯亮了，工坊的熔晶炉又烧得起来了。攒够五片风晶碎片，在我的渡口工台交给我熔成整块，拿去把岛上缺的灯点起来——灯旁暖和，夜风吹不透。",
                "\nThe star lamp is lit, so the workshop's crystal furnace can burn again. Bring five windcrystal shards to my dock workbench and I will have them fused — take the crystal and light the lamps the isles are missing. It is warm beside a lamp, and the night wind cannot get through.");
        }

        internal void Tick(bool safeToFlush)
        {
            // 独立出击地图由会话提供实际战斗安全门；无论官方基地身份如何都不在交火帧写盘。
            if (!IsCurrentSlot || !safeToFlush) return;
            if (!TryRecoverFaultedStore()) return;
            // 共享引擎 Tick 固定只在基地重试；独立地图使用受战斗门保护的 RequestFlush 接续欠账。
            if (store.HasPendingWrite || coordinator.HasDeferredFlush)
            {
                // 去抖只挡「丢了也能重做」的事实（见 FlushDebounceSeconds）；剧情动作与已经欠着的重试照旧立刻写。
                if (pendingSince < 0f) pendingSince = Time.unscaledTime;
                if (urgentPending || coordinator.HasDeferredFlush || Time.unscaledTime - pendingSince >= FlushDebounceSeconds)
                    coordinator.RequestFlush(out lastSaveError);
            }
            if (!store.HasPendingWrite && !coordinator.HasDeferredFlush && !store.IsStoreFaulted)
            {
                lastSaveError = null;
                pendingSince = -1f;
                urgentPending = false;
            }
        }

        internal void Close()
        {
            if (!opened) return;
            if (TryClose()) return;
            if (IsCurrentSlot)
                ModBehaviour.DevLog("[SkyIsland] [WARNING] 离岛保存未完成：" + (store.LastError ?? coordinator.LastError));
            store.ShutdownSubscription();
            opened = false;
        }

        /// <summary>正常离岛先尝试持久化；推迟时保持订阅和 pending，由 recovery owner 接续。</summary>
        internal bool TryClose()
        {
            if (!opened) return true;
            // 离岛计时只记第一次尝试：保存被官方推迟时恢复 owner 每秒重试一次，不重复记。
            LogTimingOnce("raid_close", null);
            if (IsCurrentSlot && (!TryRecoverFaultedStore() || !coordinator.TryFlushOnHostDestroy())) return false;
            store.ShutdownSubscription();
            opened = false;
            return true;
        }

        private bool TryRecoverFaultedStore()
        {
            if (!store.IsStoreFaulted) return true;
            if (!IsCurrentSlot || SavesSystem.IsSaving || Time.unscaledTime < nextRecoveryAt) return false;
            nextRecoveryAt = Time.unscaledTime + 1f;
            BossRushSlotJsonStore<SkyIslandStoryData> replacement = null;
            bool adopted = false;
            try
            {
                SkyIslandStoryData accepted = store.Current.Copy();
                if (SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(accepted)) == null)
                { lastSaveError = "recovery_snapshot_invalid"; return false; }
                // 旧 store 的单向故障不清除。先校验当前槽原 key，再把最后已接受快照交给新 owner。
                replacement = CreateStore();
                replacement.EnsureSubscribed();
                replacement.LoadOrInit();
                if (!IsCurrentSlot || replacement.HasWriteBarrier || replacement.IsStoreFaulted)
                { lastSaveError = replacement.LastError ?? "recovery_read_barrier"; return false; }
                if (!replacement.Store(accepted))
                { lastSaveError = replacement.LastError ?? "recovery_store_failed"; return false; }
                var replacementCoordinator = new BossRushSaveCoordinatorEngine(new SaveSource(this, replacement), false);
                store.ShutdownSubscription();
                store = replacement;
                coordinator = replacementCoordinator;
                adopted = true;
                lastSaveError = null;
                // 接回来的快照是恢复出来的欠账，不等去抖。
                MarkPending(true);
                return true;
            }
            catch (Exception e)
            {
                lastSaveError = "recovery_failed:" + e.GetType().Name;
                return false;
            }
            finally
            {
                if (!adopted && replacement != null) replacement.ShutdownSubscription();
            }
        }

        private void OnSlotChanged()
        {
            slotChanged = true;
            // 旧槽的待写批次不会再写（IsCurrentSlot 挡住），去抖状态一并作废。
            pendingSince = -1f;
            urgentPending = false;
            if (coordinator != null) coordinator.NotifySlotChanged();
            assetSnapshotRequired = false;
        }

        private sealed class SaveSource : IBossRushSaveBatchSource
        {
            private readonly SkyIslandStoryService owner;
            private readonly BossRushSlotJsonStore<SkyIslandStoryData> store;
            internal SaveSource(SkyIslandStoryService ownerValue, BossRushSlotJsonStore<SkyIslandStoryData> value)
            { owner = ownerValue; store = value; }
            public string LogPrefix { get { return "[SkyIsland] "; } }
            public bool HasPendingWrite { get { return store.HasPendingWrite; } }
            public bool IsStoreFaulted { get { return store.IsStoreFaulted; } }
            public bool HasSnapshotObligation { get { return owner.assetSnapshotRequired; } }
            public string LastError { get { return store.LastError; } }
            public bool CollectSnapshot(out string error)
            {
                error = null;
                if (!owner.assetSnapshotRequired) return true;
                try
                {
                    // LevelManager.SaveMainCharacter 是官方程序集的 internal 成员，生产编译不可直接绑定；
                    // 与 PetNest 的资产屏障一致，直接保存主角物品快照。
                    CharacterMainControl.Main.CharacterItem.Save("MainCharacterItemData");
                    SavesSystem.Save<float>("MainCharacterHealth", CharacterMainControl.Main.Health.CurrentHealth);
                    PlayerStorage.Inventory.Save("PlayerStorage");
                    PlayerStorageBuffer.SaveBuffer();
                    return true;
                }
                catch (Exception e) { error = "asset_collect_failed:" + e.GetType().Name; return false; }
            }
            public bool FlushPending() { return store.FlushPending(); }
            public void OnPhysicalSaveSucceeded() { owner.assetSnapshotRequired = false; }
        }
    }
}
