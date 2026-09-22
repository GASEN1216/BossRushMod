using System;
using System.Collections.Generic;
namespace BossRush
{
    public static class ModBehaviour
    {
        public static string AuditRoot;
        public static string GetModPath() { return AuditRoot; }
        public static void DevLog(string message) { }
        public static void CriticalLog(string key, string message) { }
    }
    public static class B48Probe
    {
        public static void Main(string[] args)
        { Run(args[0]); }
        public static void Run(string root)
        {
            ModBehaviour.AuditRoot = root;
            if (!ModeHCommandCompatibilityRegistry.EnsureValidated())
                throw new Exception(ModeHCommandCompatibilityRegistry.LastError);
            const string key = "Cname_Boss_Shot";
            ModeHCommandSpec finish = ModeHContentCatalog.Commands.Find(c => c.CommandId == "finish");
            ModeHCommandSpec guard = ModeHContentCatalog.Commands.Find(c => c.CommandId == "guard");
            if (finish == null || guard == null) throw new Exception("Production command missing");
            foreach (ModeHEffectSpec effect in finish.Effects)
            {
                if (!ModeHCommandCompatibilityRegistry.IsActionControlPoint(effect.ControlPointId))
                    throw new Exception("finish contains unexpected field effect");
                ModeHCommandCompatibilityRegistry.RecordEffectStatus(key, effect.EffectId,
                    ModeHCommandCompatibilityStatus.ActionApplied);
            }
            foreach (ModeHEffectSpec effect in guard.Effects)
                ModeHCommandCompatibilityRegistry.RecordEffectStatus(key, effect.EffectId,
                    ModeHCommandCompatibilityStatus.VerifiedBehavior);
            Console.WriteLine("before.finish=" + ModeHCommandCompatibilityRegistry.GetCommandStatus(key,"finish")
                + ";selectable=" + ModeHCommandCompatibilityRegistry.IsCommandSelectable(key,"finish"));
            var statuses = new List<ModeHCommandCertificationStatusDto>();
            new ProductionStatusWriter().Capture(statuses,key,"finish");
            new ProductionStatusWriter().Capture(statuses,key,"guard");
            Console.WriteLine("saved.finish.effect_status=" + statuses[0].effectStatuses[0].status);
            ModeHCommandCompatibilityRegistry.RestoreCertificationEffects(new List<ModeHPresetCertificationRecordDto>
            {
                new ModeHPresetCertificationRecordDto
                { stableKey=key, status=(int)ModeHCertificationStatus.Passed, commandStatuses=statuses }
            });
            Console.WriteLine("after.finish=" + ModeHCommandCompatibilityRegistry.GetCommandStatus(key,"finish")
                + ";selectable=" + ModeHCommandCompatibilityRegistry.IsCommandSelectable(key,"finish"));
            if (!ModeHCommandCompatibilityRegistry.IsCommandSelectable(key, "finish")
                || ModeHCommandCompatibilityRegistry.GetCommandStatus(key, "finish") != ModeHCommandCompatibilityStatus.ActionApplied)
                throw new Exception("Action evidence lost after report roundtrip");
            if (!ModeHCommandCompatibilityRegistry.IsCommandSelectable(key, "guard"))
                throw new Exception("Field evidence lost after report roundtrip");
            foreach (int invalid in new [] { -1, 4, 6, 999 })
            {
                statuses[0].effectStatuses.ForEach(e => e.status = invalid);
                ModeHCommandCompatibilityRegistry.RestoreCertificationEffects(new List<ModeHPresetCertificationRecordDto> {
                    new ModeHPresetCertificationRecordDto { stableKey=key, status=(int)ModeHCertificationStatus.Passed, commandStatuses=statuses }
                });
                if (ModeHCommandCompatibilityRegistry.IsCommandSelectable(key, "finish"))
                    throw new Exception("Invalid effect evidence accepted: " + invalid);
            }
            Console.WriteLine("PASS action/field roundtrip and illegal per-effect states rejected");
            Console.WriteLine("after.guard=" + ModeHCommandCompatibilityRegistry.GetCommandStatus(key,"guard")
                + ";selectable=" + ModeHCommandCompatibilityRegistry.IsCommandSelectable(key,"guard"));
        }
    }
}
