namespace BossRush
{
    internal sealed partial class ModeERuntimeModule
    {
        internal void TickModeERuntime(float deltaTime)
        {
            if (modeEActive)
            {
                UpdateModeEPlayerNameTag();

                modeEIntegrityTimer += deltaTime;
                if (modeEIntegrityTimer >= ModBehaviour.WaveIntegrityCheckInterval)
                {
                    modeEIntegrityTimer = 0f;
                    ModeEIntegrityCheck();
                }

                // Mode E 延迟批量缩放更新（每 5 秒统一应用阵营死亡缩放）
                ModeEScalingBatchUpdate();
            }
            else
            {
                modeEIntegrityTimer = 0f;
            }
        }
    }
}
