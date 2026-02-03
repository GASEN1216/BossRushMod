// ============================================================================
// NPCExceptionHandler.cs - NPC系统异常处理工具
// ============================================================================
// 模块说明：
//   提供统一的异常处理机制，替换现有的空 catch {} 块
//   支持记录异常、安全执行操作等功能
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

using System;

namespace BossRush.Utils
{
    /// <summary>
    /// NPC系统异常处理工具
    /// 提供统一的异常记录和安全执行机制
    /// </summary>
    public static class NPCExceptionHandler
    {
        // ============================================================================
        // 日志前缀常量
        // ============================================================================
        
        /// <summary>
        /// 警告日志前缀
        /// </summary>
        private const string WARNING_PREFIX = "[NPC] [WARNING]";
        
        /// <summary>
        /// 错误日志前缀
        /// </summary>
        private const string ERROR_PREFIX = "[NPC] [ERROR]";
        
        // ============================================================================
        // 异常记录方法
        // ============================================================================
        
        /// <summary>
        /// 记录异常但继续执行（用于非关键路径）
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="context">发生位置描述，便于定位问题</param>
        public static void LogAndIgnore(Exception ex, string context)
        {
            if (ex == null) return;
            
            // 输出警告级别日志，包含上下文和异常消息
            ModBehaviour.DevLog($"{WARNING_PREFIX} {context}: {ex.Message}");
        }
        
        /// <summary>
        /// 记录异常并重新抛出（用于关键路径，需要上层处理）
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="context">发生位置描述，便于定位问题</param>
        /// <exception cref="Exception">重新抛出原始异常</exception>
        public static void LogAndRethrow(Exception ex, string context)
        {
            if (ex == null) return;
            
            // 输出错误级别日志，包含完整堆栈跟踪
            ModBehaviour.DevLog($"{ERROR_PREFIX} {context}: {ex.Message}\n{ex.StackTrace}");
            
            // 重新抛出异常，保留原始堆栈信息
            throw ex;
        }
        
        // ============================================================================
        // 安全执行方法
        // ============================================================================
        
        /// <summary>
        /// 安全执行操作（无返回值版本）
        /// 捕获异常并记录，不会中断程序执行
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="context">操作描述，用于异常日志</param>
        /// <returns>是否执行成功</returns>
        public static bool TryExecute(Action action, string context)
        {
            if (action == null) return false;
            
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                LogAndIgnore(ex, context);
                return false;
            }
        }
        
        /// <summary>
        /// 安全执行并返回结果（泛型版本）
        /// 捕获异常并记录，失败时返回默认值
        /// </summary>
        /// <typeparam name="T">返回类型</typeparam>
        /// <param name="func">要执行的函数</param>
        /// <param name="context">操作描述，用于异常日志</param>
        /// <param name="defaultValue">失败时的默认值</param>
        /// <returns>执行结果或默认值</returns>
        public static T TryExecute<T>(Func<T> func, string context, T defaultValue = default)
        {
            if (func == null) return defaultValue;
            
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                LogAndIgnore(ex, context);
                return defaultValue;
            }
        }
    }
}
