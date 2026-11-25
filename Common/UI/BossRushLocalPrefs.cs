// ============================================================================
// BossRushLocalPrefs.cs - 界面本地偏好（2026-11-24）
// ============================================================================
// 只记「玩家上次在界面上怎么选的」这类小事：出征令上次的阵容 / 战帖 / 征程、许愿台星河开关。
// 不是玩法配置（不进 ModConfig，不进存档），丢了就回默认，界面照常。
//
// 存放：<Mod 目录>/Cache/LocalPrefs.txt，一行一条 key=value（与许愿台弹幕缓存同一个 Cache 目录）。
// 读：第一次用到时整份读进内存；写：改一条就整份重写（条目只有几条）。
// 键只许字母、数字、下划线、点；值里的换行和等号会被替换掉，所以不需要转义。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BossRush
{
    internal static class BossRushLocalPrefs
    {
        private const string FolderName = "Cache";
        private const string FileName = "LocalPrefs.txt";

        private static Dictionary<string, string> values;

        internal static int GetInt(string key, int fallback)
        {
            string raw = GetRaw(key);
            int parsed;
            return raw != null && int.TryParse(raw, out parsed) ? parsed : fallback;
        }

        internal static bool GetBool(string key, bool fallback)
        {
            string raw = GetRaw(key);
            if (raw == "1") return true;
            if (raw == "0") return false;
            return fallback;
        }

        internal static void SetInt(string key, int value)
        {
            SetRaw(key, value.ToString());
        }

        internal static void SetBool(string key, bool value)
        {
            SetRaw(key, value ? "1" : "0");
        }

        private static string GetRaw(string key)
        {
            if (!IsValidKey(key)) return null;
            EnsureLoaded();
            string value;
            return values.TryGetValue(key, out value) ? value : null;
        }

        private static void SetRaw(string key, string value)
        {
            if (!IsValidKey(key)) return;
            EnsureLoaded();
            string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('=', ' ');
            string old;
            if (values.TryGetValue(key, out old) && old == clean) return;
            values[key] = clean;
            Save();
        }

        private static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 64) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '.';
                if (!ok) return false;
            }
            return true;
        }

        private static void EnsureLoaded()
        {
            if (values != null) return;
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string path = GetFilePath(false);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    if (!IsValidKey(key)) continue;
                    values[key] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LocalPrefs] [WARNING] 读取界面本地偏好失败，按默认处理: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                string path = GetFilePath(true);
                if (string.IsNullOrEmpty(path)) return;
                StringBuilder sb = new StringBuilder(64 + values.Count * 32);
                foreach (KeyValuePair<string, string> entry in values)
                {
                    sb.Append(entry.Key).Append('=').Append(entry.Value).Append('\n');
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LocalPrefs] [WARNING] 写入界面本地偏好失败（下次回默认）: " + e.Message);
            }
        }

        private static string GetFilePath(bool createFolder)
        {
            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath)) return null;
            string folder = Path.Combine(modPath, FolderName);
            if (createFolder) Directory.CreateDirectory(folder);
            return Path.Combine(folder, FileName);
        }

        /// <summary>丢掉内存里的一份（下次用到时重读文件）。由 <see cref="BossRushUI.ResetStaticCaches"/> 调用。</summary>
        internal static void ResetStaticCaches()
        {
            values = null;
        }
    }
}
