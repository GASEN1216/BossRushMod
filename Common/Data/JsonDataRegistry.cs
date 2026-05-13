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
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath))
                {
                    ModBehaviour.DevLog("[JsonDataRegistry] [WARNING] 无法获取 Mod 路径，跳过数据文件: " + fileName);
                    return false;
                }

                string filePath = Path.Combine(modPath, "Assets", "Data", fileName);
                if (!File.Exists(filePath))
                {
                    ModBehaviour.DevLog("[JsonDataRegistry] [WARNING] 数据文件不存在: " + filePath);
                    return false;
                }

                json = File.ReadAllText(filePath, Encoding.UTF8);
                return !string.IsNullOrEmpty(json);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[JsonDataRegistry] [WARNING] 读取数据文件失败: " + fileName + " - " + e.Message);
                json = null;
                return false;
            }
        }
    }
}
