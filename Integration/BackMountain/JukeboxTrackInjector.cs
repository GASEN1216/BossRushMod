// ============================================================================
// JukeboxTrackInjector.cs - 往基地点唱机追加 mod 战歌
// ============================================================================
// 官方 BaseBGMSelector.entries 是 public List<Entry>，可以直接运行时追加，零 Harmony。
// Entry.filePath 非空时官方走 AudioManager.PlayCustomBGM(filePath) 播放外部文件，
// 这正是我们要的路径——mp3 放 Assets/Sounds/BGM/ 即可，不需要进 FMOD 音频库。
//
// 【每次进基地都要重新注入】
//   BaseBGMSelector 是场景里的组件，每次进基地都会重建并在 Awake 里重填 entries，
//   所以本注入器不是「一次性」的，而是「每次进基地幂等追加一遍」。
//   判重看 musicName：官方自己也会扫 StreamingAssets/Music 追加，重名会显得很乱。
//
// 【存档 index 会移位是官方机制本身如此】
//   官方只存当前曲目的 index（savekey="BaseBGMSelector"），曲目增减必然移位。
//   我们能做的是把 mod 条目**固定追加在官方条目之后**并保持稳定顺序，
//   让至少官方曲目的 index 不受影响。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>基地点唱机的 mod 曲目注入器。</summary>
    internal static class JukeboxTrackInjector
    {
        /// <summary>
        /// 幂等注入。点唱机未解锁时跳过（dormant）。
        /// 每次进基地调一次——组件是场景级的，会随场景重建。
        /// </summary>
        internal static void EnsureInjected()
        {
            try
            {
                if (!BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Jukebox)) return;

                IList<BossBgmJukeboxEntry> tracks = BossBgmCoordinator.GetJukeboxTracks();
                if (tracks == null || tracks.Count == 0) return;

                BaseBGMSelector selector = UnityEngine.Object.FindObjectOfType<BaseBGMSelector>();
                if (selector == null) return;
                if (selector.entries == null) return;

                int added = 0;
                for (int i = 0; i < tracks.Count; i++)
                {
                    BossBgmJukeboxEntry track = tracks[i];
                    if (track == null || string.IsNullOrEmpty(track.musicName)) continue;
                    if (ContainsTrack(selector.entries, track.musicName)) continue;

                    string path = BossBgmCoordinator.ResolveSoundPath(track.file);
                    if (string.IsNullOrEmpty(path)) continue;

                    BaseBGMSelector.Entry entry = new BaseBGMSelector.Entry();
                    entry.musicName = track.musicName;
                    entry.author = string.IsNullOrEmpty(track.author) ? "BossRushMod" : track.author;
                    // switchName 留空：filePath 非空时官方走外部文件路径，不查 FMOD 事件
                    entry.switchName = string.Empty;
                    entry.filePath = path;

                    selector.entries.Add(entry);
                    added++;
                }

                if (added > 0)
                {
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "点唱机已追加战歌 " + added + " 首");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 点唱机注入失败: " + e.Message);
            }
        }

        private static bool ContainsTrack(List<BaseBGMSelector.Entry> entries, string musicName)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].musicName, musicName, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
