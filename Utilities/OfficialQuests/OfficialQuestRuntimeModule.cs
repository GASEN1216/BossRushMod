namespace BossRush
{
    /// <summary>
    /// 官方任务投影核心的唯一常驻 owner：只有它 new <see cref="OfficialQuestProjection"/>、只有它 Tick、只有它 Dispose。
    /// 必须先于天空岛与鸭王征程模块注册（host 按注册顺序回调 OnAwake，两者在自己的 OnAwake 里向核心登记任务定义）。
    /// OnUpdate 里没有任何早退门：岛上会话在不在、征程开没开都照跑（0.25 秒节流在核心内）。
    /// </summary>
    internal sealed class OfficialQuestRuntimeModule : BossRushRuntimeModuleBase
    {
        private OfficialQuestProjection projection;

        public override string ModuleName { get { return "OfficialQuests"; } }

        /// <summary>核心实例的只读门面；客户端只能经这里 Register / Unregister，不得再次 new。</summary>
        internal OfficialQuestProjection Projection { get { return projection; } }

        public override void OnAwake(ModBehaviour owner)
        {
            projection = new OfficialQuestProjection(owner);
        }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            OfficialQuestGiverLocator.NotifySceneChanged();
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (projection != null) projection.Tick();
        }

        public override void OnDestroy()
        {
            if (projection != null) projection.Dispose();
            projection = null;
            OfficialQuestGiverLocator.ResetStaticCaches();
        }
    }
}
