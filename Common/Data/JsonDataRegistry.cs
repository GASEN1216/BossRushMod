// ============================================================================
// JsonDataRegistry.cs - JSON data file loader
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace BossRush
{
    internal static class JsonDataRegistry
    {
        public static bool TryReadDataFile(string fileName, out string json)
        {
            json = null;
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            try
            {
                // 数据表读不到 = 运行时退回硬编码兜底或直接缺内容，属玩家可见故障，
                // 因此这里用 CriticalLog（按文件名去重）而不是会被正式构建编译删除的 DevLog。
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath))
                {
                    ModBehaviour.CriticalLog(
                        "json-data-modpath",
                        "[JsonDataRegistry] [ERROR] 无法获取 Mod 路径，跳过数据文件: " + fileName);
                    return false;
                }

                string filePath = Path.Combine(modPath, "Assets", "Data", fileName);
                if (!File.Exists(filePath))
                {
                    ModBehaviour.CriticalLog(
                        "json-data-missing-" + fileName,
                        "[JsonDataRegistry] [ERROR] 数据文件不存在: " + filePath);
                    return false;
                }

                json = File.ReadAllText(filePath, Encoding.UTF8);
                return !string.IsNullOrEmpty(json);
            }
            catch (Exception e)
            {
                ModBehaviour.CriticalLog(
                    "json-data-read-" + fileName,
                    "[JsonDataRegistry] [ERROR] 读取数据文件失败: " + fileName + " - " + e.Message);
                json = null;
                return false;
            }
        }
    }
}
