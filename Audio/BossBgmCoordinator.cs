// ============================================================================
// BossBgmCoordinator.cs - Boss 战循环 BGM / 胜利 stinger / 点唱机曲目的统一注册表
// ============================================================================
// 设计目标：**代码一次到位，正式曲目后补零改动**。
//   曲目映射走 Assets/Data/Audio/BgmTracks.json（AGENTS.md 4.8 第 3 层），
//   音频文件放 <modRoot>/Assets/Sounds/BGM/。表缺失或文件缺失一律静默跳过，
//   行为与「没有这套系统」完全一致——这是 M1 的验收口径。
//
// 【为什么用反射调官方 AudioManager】
//   compile_official.bat 没有引用 FMOD DLL，而 PlayCustomBGM 的返回类型是
//   FMOD.Studio.EventInstance?，签名里带不进来。因此只能 MethodInfo.Invoke 并忽略返回值
//   （PostCustomSFX 早有同款先例，见 Audio/BossRushAudioHooks.cs）。
//   BGM 起停是低频事件，不是热路径，反射开销无关紧要；返回值也确实用不到。
//
// 【为什么不用管「恢复原来的 BGM」】
//   竞技场局内**没有官方 BGM 循环**：PlayBGM 的调用方只有主界面、Credits 和基地点唱机；
//   局内环境声走独立的 ambientSource，与 bgmSource 分离。所以 Boss 战结束 StopBGM
//   即完全还原。切场景与玩家死亡另有官方自动 StopBGM 兜底。
//
// 【与龙王既有 dragonking.mp3 的关系】
//   龙王原本走 PostCustomSFX 播一次性音效（不循环、不占 bgmSource）。
//   规则：表里有 DragonKing 条目 → 由本协调器接管为循环 BGM；
//   没有条目 → 维持旧行为。零素材时行为完全不变。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    // 以下四个类是 JsonUtility 反序列化目标：字段由 JsonUtility.FromJson 反射赋值，
    // 代码里只读不写，所以编译器会对每个字段报 CS0649「从未赋值」。那是误报——
    // 真按提示改成属性或加初始值，JsonUtility 就绑不上了（它只认公有字段）。
    // 因此在这里定点关掉 CS0649，范围只覆盖这四个 DTO，不影响文件其余部分。
#pragma warning disable 0649

    /// <summary>Boss 战 BGM 条目（JsonUtility 反序列化目标，字段名即 JSON 键名）。</summary>
    [Serializable]
    internal class BossBgmTrackEntry
    {
        /// <summary>Boss 标识。与 BossBgmKeys 的常量对应。</summary>
        public string bossKey;

        /// <summary>相对 Assets/Sounds/BGM/ 的文件名。</summary>
        public string file;

        /// <summary>是否循环。Boss 战 BGM 一律 true。</summary>
        public bool loop = true;

        /// <summary>
        /// 阶段号。V1 恒为 0，为二期 FMOD SetState 阶段切歌预留。
        /// 解析时非 0 的条目会被忽略并记一条日志，避免二期数据表被 V1 代码静默错播。
        /// </summary>
        public int phase;
    }

    /// <summary>一次性 stinger 条目。</summary>
    [Serializable]
    internal class BossBgmStingerEntry
    {
        /// <summary>事件标识。与 BossBgmEvents 的常量对应。</summary>
        public string eventKey;

        /// <summary>相对 Assets/Sounds/BGM/ 的文件名。</summary>
        public string file;
    }

    /// <summary>点唱机追加曲目条目（后山设施消费，见 JukeboxTrackInjector）。</summary>
    [Serializable]
    internal class BossBgmJukeboxEntry
    {
        /// <summary>点唱机上显示的曲名。</summary>
        public string musicName;

        /// <summary>作者署名。</summary>
        public string author;

        /// <summary>相对 Assets/Sounds/BGM/ 的文件名。</summary>
        public string file;
    }

    /// <summary>数据表根对象。</summary>
    [Serializable]
    internal class BossBgmTrackTable
    {
        public int version;
        public BossBgmTrackEntry[] bossTracks;
        public BossBgmStingerEntry[] stingers;
        public BossBgmJukeboxEntry[] jukebox;
    }

#pragma warning restore 0649

    /// <summary>Boss 标识常量。改名会让数据表静默失配，等同破坏契约。</summary>
    internal static class BossBgmKeys
    {
        internal const string DragonKing = "DragonKing";
        internal const string DragonDescendant = "DragonDescendant";
        internal const string PhantomWitch = "PhantomWitch";
    }

    /// <summary>stinger 事件常量。</summary>
    internal static class BossBgmEvents
    {
        /// <summary>单个自定义 Boss 被击败。</summary>
        internal const string BossVictory = "BossVictory";

        /// <summary>整局通关（胜利奖励箱流程启动时）。</summary>
        internal const string RunVictory = "RunVictory";
    }

    /// <summary>
    /// Boss BGM 协调器。全静态：一次会话内只有一份播放状态。
    /// 所有入口 no-throw，任何一步失败都退化为「没有音乐」，绝不影响玩法。
    /// </summary>
    internal static class BossBgmCoordinator
    {
        #region 常量

        private const string DataSubDirectory = "Audio";
        private const string DataFileName = "BgmTracks.json";
        private const string SoundRelativeDir = "Assets/Sounds/BGM";
        private const string LogPrefix = "[BossBGM] ";

        #endregion

        #region 状态

        private static bool _tableLoadAttempted;
        private static Dictionary<string, BossBgmTrackEntry> _bossTracks;
        private static Dictionary<string, string> _stingerFiles;
        private static List<BossBgmJukeboxEntry> _jukebox;

        /// <summary>当前由本协调器起播的 Boss 标识；null 表示没有在播。</summary>
        private static string _playingBossKey;

        /// <summary>
        /// 起播时所在场景的 handle。用于让播放记账**跨局自愈**：
        /// 玩家中途弃局（Boss 没死、死亡回调没走）时 _playingBossKey 会残留，
        /// 下一局同一个 Boss 会被误判成「已经在放」而永远不起播——表现为战斗静音。
        /// 比对场景 handle 就能识别出这种陈旧状态，且不需要新增任何全局场景订阅。
        /// </summary>
        private static int _playingSceneHandle;

        private sealed class BossBgmOwnerLease
        {
            internal int OwnerId;
            internal UnityEngine.Object Owner;
            internal string BossKey;
            internal long ActivationOrder;
        }

        private static readonly List<BossBgmOwnerLease> _ownerLeases =
            new List<BossBgmOwnerLease>();
        private static long _nextActivationOrder;

        /// <summary>文件存在性缓存（照 SoundFileExistsCached 先例，避免反复打盘）。</summary>
        private static readonly Dictionary<string, bool> _fileExists =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static MethodInfo _playCustomBgmMethod;
        private static MethodInfo _stopBgmMethod;
        private static bool _audioMethodsResolved;

        #endregion

        #region 数据表

        /// <summary>
        /// 幂等装载数据表。表不存在时留空集合并记一次日志，之后所有查询都返回「无条目」。
        /// </summary>
        private static void EnsureTableLoaded()
        {
            if (_tableLoadAttempted) return;
            _tableLoadAttempted = true;

            _bossTracks = new Dictionary<string, BossBgmTrackEntry>(StringComparer.Ordinal);
            _stingerFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            _jukebox = new List<BossBgmJukeboxEntry>();

            try
            {
                string json;
                if (!JsonDataRegistry.TryReadDataFile(DataSubDirectory, DataFileName, out json))
                {
                    // 没有曲目表是完全合法的状态（正式曲目还没做），不喊错
                    ModBehaviour.DevLog(LogPrefix + "未找到曲目表，BGM 功能保持静默");
                    return;
                }

                BossBgmTrackTable table = JsonUtility.FromJson<BossBgmTrackTable>(json);
                if (table == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 曲目表解析为空，BGM 功能保持静默");
                    return;
                }

                if (table.bossTracks != null)
                {
                    for (int i = 0; i < table.bossTracks.Length; i++)
                    {
                        BossBgmTrackEntry entry = table.bossTracks[i];
                        if (entry == null) continue;
                        if (string.IsNullOrEmpty(entry.bossKey) || string.IsNullOrEmpty(entry.file)) continue;
                        if (entry.phase != 0)
                        {
                            // 二期阶段切歌的数据不应被 V1 代码当成主曲目播
                            ModBehaviour.DevLog(LogPrefix + "忽略 phase!=0 的条目（阶段切歌是二期功能）: "
                                + entry.bossKey);
                            continue;
                        }
                        _bossTracks[entry.bossKey] = entry;
                    }
                }

                if (table.stingers != null)
                {
                    for (int i = 0; i < table.stingers.Length; i++)
                    {
                        BossBgmStingerEntry entry = table.stingers[i];
                        if (entry == null) continue;
                        if (string.IsNullOrEmpty(entry.eventKey) || string.IsNullOrEmpty(entry.file)) continue;
                        _stingerFiles[entry.eventKey] = entry.file;
                    }
                }

                if (table.jukebox != null)
                {
                    for (int i = 0; i < table.jukebox.Length; i++)
                    {
                        BossBgmJukeboxEntry entry = table.jukebox[i];
                        if (entry == null) continue;
                        if (string.IsNullOrEmpty(entry.musicName) || string.IsNullOrEmpty(entry.file)) continue;
                        _jukebox.Add(entry);
                    }
                }

                ModBehaviour.DevLog(LogPrefix + "曲目表已装载: boss=" + _bossTracks.Count
                    + ", stinger=" + _stingerFiles.Count + ", jukebox=" + _jukebox.Count);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 曲目表装载异常: " + e.Message);
            }
        }

        #endregion

        #region 查询

        /// <summary>
        /// 该 Boss 是否配了循环 BGM **且文件确实存在**。
        /// 调用方据此决定是否接管既有播放逻辑（龙王旧路径就靠它判断）。
        /// </summary>
        internal static bool HasBossTrack(string bossKey)
        {
            try
            {
                EnsureTableLoaded();
                if (string.IsNullOrEmpty(bossKey)) return false;

                BossBgmTrackEntry entry;
                if (!_bossTracks.TryGetValue(bossKey, out entry)) return false;
                return FileExists(ResolveSoundPath(entry.file));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>点唱机曲目快照（后山 JukeboxTrackInjector 消费）。永不返回 null。</summary>
        internal static IList<BossBgmJukeboxEntry> GetJukeboxTracks()
        {
            try
            {
                EnsureTableLoaded();
                List<BossBgmJukeboxEntry> result = new List<BossBgmJukeboxEntry>();
                for (int i = 0; i < _jukebox.Count; i++)
                {
                    BossBgmJukeboxEntry entry = _jukebox[i];
                    if (entry == null) continue;
                    if (!FileExists(ResolveSoundPath(entry.file))) continue;
                    result.Add(entry);
                }
                return result;
            }
            catch (Exception)
            {
                return new List<BossBgmJukeboxEntry>();
            }
        }

        /// <summary>音频文件的绝对路径。曲目名为空或拿不到 mod 路径时返回 null。</summary>
        internal static string ResolveSoundPath(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return null;
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;
                return Path.Combine(
                    modPath,
                    SoundRelativeDir.Replace('/', Path.DirectorySeparatorChar),
                    fileName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region 播放

        /// <summary>
        /// 起播某 Boss 的循环 BGM。幂等：同一个 Boss 重复调用不会重启曲子。
        /// 无条目/无文件/官方 API 不可用时返回 false，调用方据此走旧逻辑或什么都不做。
        /// </summary>
        internal static bool PlayBossBgm(string bossKey)
        {
            try
            {
                EnsureTableLoaded();
                if (string.IsNullOrEmpty(bossKey)) return false;

                BossBgmTrackEntry entry;
                if (!_bossTracks.TryGetValue(bossKey, out entry)) return false;

                string path = ResolveSoundPath(entry.file);
                if (!FileExists(path)) return false;

                // 已经在放同一首：不重启，否则多阶段 Boss 每次阶段回调都会把曲子掐回开头。
                // 但只有在同一个场景里才算数——跨局的残留记账要当作没在放（见 _playingSceneHandle）。
                if (string.Equals(_playingBossKey, bossKey, StringComparison.Ordinal)
                    && IsPlaybackFromCurrentScene())
                {
                    return true;
                }

                if (!InvokePlayCustomBgm(path, entry.loop)) return false;

                _playingBossKey = bossKey;
                _playingSceneHandle = GetActiveSceneHandle();
                ModBehaviour.DevLog(LogPrefix + "起播 Boss BGM: " + bossKey);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 起播失败 " + bossKey + ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 为一个具体 Boss 获取 BGM 租约。同一 owner 重复调用幂等；同 key 多 owner
        /// 会共享曲目，最后一个 owner 释放之前不会停。
        /// </summary>
        internal static bool AcquireBossBgm(string bossKey, UnityEngine.Object owner)
        {
            if (owner == null || !HasBossTrack(bossKey)) return false;
            try
            {
                EnsureOwnerLeasesForCurrentScene();
                int ownerId = owner.GetInstanceID();
                for (int i = 0; i < _ownerLeases.Count; i++)
                {
                    BossBgmOwnerLease existing = _ownerLeases[i];
                    if (existing.OwnerId == ownerId)
                    {
                        return string.Equals(existing.BossKey, bossKey, StringComparison.Ordinal);
                    }
                }

                BossBgmOwnerLease lease = new BossBgmOwnerLease();
                lease.OwnerId = ownerId;
                lease.Owner = owner;
                lease.BossKey = bossKey;
                lease.ActivationOrder = ++_nextActivationOrder;
                _ownerLeases.Add(lease);

                if (string.Equals(_playingBossKey, bossKey, StringComparison.Ordinal)
                    && IsPlaybackFromCurrentScene()) return true;
                if (PlayBossBgm(bossKey)) return true;

                _ownerLeases.Remove(lease);
                return false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] owner BGM 获取失败: " + e.Message);
                return false;
            }
        }

        internal static void ReleaseBossBgm(string bossKey, UnityEngine.Object owner)
        {
            if (owner == null) return;
            try
            {
                EnsureOwnerLeasesForCurrentScene();
                int ownerId = owner.GetInstanceID();
                for (int i = _ownerLeases.Count - 1; i >= 0; i--)
                {
                    BossBgmOwnerLease lease = _ownerLeases[i];
                    if (lease.OwnerId == ownerId
                        && string.Equals(lease.BossKey, bossKey, StringComparison.Ordinal))
                    {
                        _ownerLeases.RemoveAt(i);
                    }
                }

                if (!string.Equals(_playingBossKey, bossKey, StringComparison.Ordinal)) return;
                if (HasLiveLeaseForKey(bossKey)) return;

                BossBgmOwnerLease resume = GetMostRecentLiveLease();
                if (resume != null)
                {
                    PlayBossBgm(resume.BossKey);
                }
                else
                {
                    StopBossBgm(bossKey);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] owner BGM 释放失败: " + e.Message);
            }
        }

        private static void EnsureOwnerLeasesForCurrentScene()
        {
            int current = GetActiveSceneHandle();
            if (_playingSceneHandle != 0 && current != 0 && _playingSceneHandle != current)
            {
                _ownerLeases.Clear();
                _playingBossKey = null;
                _playingSceneHandle = 0;
            }
            for (int i = _ownerLeases.Count - 1; i >= 0; i--)
            {
                if (_ownerLeases[i].Owner == null) _ownerLeases.RemoveAt(i);
            }
        }

        private static bool HasLiveLeaseForKey(string bossKey)
        {
            for (int i = 0; i < _ownerLeases.Count; i++)
                if (string.Equals(_ownerLeases[i].BossKey, bossKey, StringComparison.Ordinal)) return true;
            return false;
        }

        private static BossBgmOwnerLease GetMostRecentLiveLease()
        {
            BossBgmOwnerLease best = null;
            for (int i = 0; i < _ownerLeases.Count; i++)
            {
                BossBgmOwnerLease lease = _ownerLeases[i];
                if (best == null || lease.ActivationOrder > best.ActivationOrder) best = lease;
            }
            return best;
        }

        internal static int ActiveOwnerLeaseCount { get { EnsureOwnerLeasesForCurrentScene(); return _ownerLeases.Count; } }
        internal static string PlayingBossKey { get { return _playingBossKey; } }

        /// <summary>
        /// 停止本协调器起播的 BGM。
        /// bossKey 非空时只在「当前在放的正是它」时才停——避免 A Boss 的死亡回调
        /// 掐掉 B Boss 刚起播的曲子（多 Boss 波次里这是常态）。
        /// </summary>
        internal static void StopBossBgm(string bossKey = null)
        {
            try
            {
                if (_playingBossKey == null) return;
                if (!string.IsNullOrEmpty(bossKey)
                    && !string.Equals(_playingBossKey, bossKey, StringComparison.Ordinal))
                {
                    return;
                }

                // 记账来自上一局（玩家弃局后场景已换）：官方切场景时已经 StopBGM 过了，
                // 这里只清记账，不要再去停当前场景里可能正在放的别的东西。
                if (!IsPlaybackFromCurrentScene())
                {
                    _playingBossKey = null;
                    return;
                }

                InvokeStopBgm();
                ModBehaviour.DevLog(LogPrefix + "停止 Boss BGM: " + _playingBossKey);
                _playingBossKey = null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 停止失败: " + e.Message);
                _playingBossKey = null;
            }
        }

        /// <summary>
        /// 播一次性 stinger。走 ModBehaviour.PlaySoundEffect（内部是官方 PostCustomSFX），
        /// 不占 bgmSource，因此不会打断正在放的循环 BGM。
        /// </summary>
        internal static void PlayStinger(string eventKey)
        {
            try
            {
                EnsureTableLoaded();
                if (string.IsNullOrEmpty(eventKey)) return;

                string file;
                if (!_stingerFiles.TryGetValue(eventKey, out file)) return;

                string path = ResolveSoundPath(file);
                if (!FileExists(path)) return;

                ModBehaviour owner = ModBehaviour.Instance;
                if (owner == null) return;
                owner.PlaySoundEffect(path);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] stinger 播放失败 " + eventKey + ": " + e.Message);
            }
        }

        /// <summary>
        /// 切场景后的状态复位。**不调 StopBGM**：官方在场景加载时已经停过，
        /// 这里只清「在放什么」的记账。
        ///
        /// 注意：调用它不是必须的——播放记账靠 _playingSceneHandle 自愈，
        /// 没有任何调用点时行为也正确。提供它只是为了让确有场景回调的模块能主动清干净。
        /// </summary>
        internal static void NotifySceneChanged()
        {
            _ownerLeases.Clear();
            _playingBossKey = null;
            _playingSceneHandle = 0;
            _nextActivationOrder = 0L;
        }

        /// <summary>当前活动场景的 handle；取不到时返回 0。</summary>
        private static int GetActiveSceneHandle()
        {
            try
            {
                return UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>播放记账是否产生自当前场景（否则是上一局的残留）。</summary>
        private static bool IsPlaybackFromCurrentScene()
        {
            int current = GetActiveSceneHandle();
            if (current == 0 || _playingSceneHandle == 0)
            {
                // 拿不到场景信息时保守认为「还在同一局」：宁可少停一次，
                // 也不要把玩家正在听的曲子误判成陈旧记账而反复重启。
                return true;
            }
            return current == _playingSceneHandle;
        }

        #endregion

        #region 官方 AudioManager 反射

        private static void EnsureAudioMethodsResolved()
        {
            if (_audioMethodsResolved) return;
            _audioMethodsResolved = true;

            try
            {
                Type audioManagerType = Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                if (audioManagerType == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 找不到官方 AudioManager，BGM 功能不可用");
                    return;
                }

                // PlayCustomBGM(string filePath, bool loop = true)。带默认参数，
                // 反射 Invoke 必须显式补齐两个实参，不能只传一个。
                _playCustomBgmMethod = audioManagerType.GetMethod(
                    "PlayCustomBGM",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(string), typeof(bool) },
                    null);

                _stopBgmMethod = audioManagerType.GetMethod(
                    "StopBGM",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

                if (_playCustomBgmMethod == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 官方 PlayCustomBGM 签名不匹配（游戏更新？）");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 解析官方 AudioManager 失败: " + e.Message);
            }
        }

        private static bool InvokePlayCustomBgm(string path, bool loop)
        {
            EnsureAudioMethodsResolved();
            if (_playCustomBgmMethod == null) return false;
            try
            {
                // 返回值是 FMOD.Studio.EventInstance?，本程序集没引用 FMOD，
                // 因此只能忽略返回值——我们也确实不需要它。
                _playCustomBgmMethod.Invoke(null, new object[] { path, loop });
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] PlayCustomBGM 调用失败: " + e.Message);
                return false;
            }
        }

        private static void InvokeStopBgm()
        {
            EnsureAudioMethodsResolved();
            if (_stopBgmMethod == null) return;
            try
            {
                _stopBgmMethod.Invoke(null, null);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] StopBGM 调用失败: " + e.Message);
            }
        }

        #endregion

        #region 工具与生命周期

        private static bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            bool exists;
            if (_fileExists.TryGetValue(path, out exists)) return exists;

            try
            {
                exists = File.Exists(path);
            }
            catch (Exception)
            {
                exists = false;
            }
            _fileExists[path] = exists;
            return exists;
        }

        /// <summary>宿主销毁时的静态缓存复位。</summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                StopBossBgm();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 复位 BGM 协调器失败: " + e.Message);
            }

            _tableLoadAttempted = false;
            _bossTracks = null;
            _stingerFiles = null;
            _jukebox = null;
            _playingBossKey = null;
            _playingSceneHandle = 0;
            _ownerLeases.Clear();
            _nextActivationOrder = 0L;
            _fileExists.Clear();
            _playCustomBgmMethod = null;
            _stopBgmMethod = null;
            _audioMethodsResolved = false;
        }

        #endregion
    }
}
