// ============================================================================
// SkyIslandSessionLabels.cs - 天空岛会话的地名与对手名（纯查表）
// ============================================================================
// 从 SkyIslandSession.cs 原样拆出来（2026-09-14 B 轮）：会话主文件卡在 1200 行预算上，而这一段本来就是独立的一块——
// 区域 id / 地标名 / 遭遇 id → 玩家看得懂的中英文名，只查表、不读会话状态。拆分不改任何成员的语义与可见性；
// 唯一的新增是 EncounterLabel 认得噬风·回响（SkyIslandStormEchoRules）。
// ============================================================================

namespace BossRush
{
    internal sealed partial class SkyIslandSession
    {
        internal static string LandmarkLabel(string name)
        {
            if (name.Length > 4 && name[4] >= 'A' && name[4] <= 'H') return MainRegionLabel(name[4]);
            if (name == "POI_S1") return RegionLabel("S1");
            if (name == "POI_S2") return RegionLabel("S2");
            if (name == "POI_S3") return RegionLabel("S3");
            if (name == "POI_S4") return RegionLabel("S4");
            return name.Replace("POI_", "").Replace('_', ' ');
        }

        /// <summary>
        /// 区域 id（A–H / S1–S4）→ 玩家看得懂的地名。每 0.5 秒的 HUD 刷新都会走到这里，
        /// 所以不再像旧写法那样每次 new 两个八元素数组，全部是直接返回字面量的分支。
        /// </summary>
        internal static string RegionLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            if (id.Length == 1 && id[0] >= 'A' && id[0] <= 'H') return MainRegionLabel(id[0]);
            switch (id)
            {
                case "S1": return L10n.T("蛙鸣池", "Frogsong Pool");
                case "S2": return L10n.T("倒挂邮亭", "Upturned Post Hut");
                case "S3": return L10n.T("听雨洞", "Rainlisten Grotto");
                case "S4": return L10n.T("残星瞭台", "Starfall Overlook");
                default: return id.Replace('_', ' ');
            }
        }

        private static string MainRegionLabel(char region) { return L10n.T(MainRegionCn(region), MainRegionEn(region)); }

        private static string MainRegionCn(char region)
        {
            switch (region)
            {
                case 'A': return "登云码头";
                case 'B': return "风铃集";
                case 'C': return "青穗梯田";
                case 'D': return "悬根林";
                case 'E': return "鸣风栈道";
                case 'F': return "镜水寺";
                case 'G': return "残星工坊";
                default: return "归航钟庭";
            }
        }

        private static string MainRegionEn(char region)
        {
            switch (region)
            {
                case 'A': return "Cloudrise Dock";
                case 'B': return "Windchime Market";
                case 'C': return "Green Terraces";
                case 'D': return "Hanging Root Wood";
                case 'E': return "Windsong Boardwalk";
                case 'F': return "Mirrorwater Temple";
                case 'G': return "Fallen Star Workshop";
                default: return "Homecoming Bell Court";
            }
        }
        /// <summary>
        /// 遭遇 id → 玩家看得懂的名字。id 是 World.json 里的内部键（`C_02` / `S1` / `Zheling`），
        /// 直接拼进「航路已清理 · C_02」等于把调试键名念给玩家听。
        /// 区域遭遇取所在地标名，具名对手取角色名；解析不出来时落到 LandmarkLabel 的兜底写法。
        /// </summary>
        internal static string EncounterLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            if (id == "Zheling") return SkyIslandWorldStory.ResidentName("sky_zheling");
            if (id == "BellKeeper") return L10n.T("守钟装置", "the bell engine");
            if (id == "Storm") return L10n.T("噬风", "the Windeater");
            if (SkyIslandStormEchoRules.IsEcho(id)) return L10n.T("噬风·回响", "the Windeater's echo");
            int underscore = id.IndexOf('_');
            return LandmarkLabel("POI_" + (underscore < 0 ? id : id.Substring(0, underscore)));
        }
    }
}
