using System;
using BossRush;

internal static class Program
{
    private static int checks;
    private const string EntryAllowed = "risk,mode-e,inventory";
    private const string Startup = "arena-reset,recovery,config,config,items,enemies,pool,starter,mutators:ModeD,sign,translate-text,message,translate-text,banner";

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException("ASSERT: " + message + "; calls=" + string.Join(",", Probe.Calls));
    }
    private static void Calls(string expected, string message)
    {
        Check(string.Join(",", Probe.Calls) == expected, message);
    }
    private static ModBehaviour Host(bool awake = true)
    {
        Probe.Calls.Clear(); Probe.Logs.Clear(); Probe.Fault = null;
        Probe.RiskAllowed = true; Probe.Naked = true; Probe.Ticket = 868;
        var host = new ModBehaviour();
        if (awake)
        {
            host.modeERuntime = new ModeERuntimeModule();
            host.modeDRuntime = new ModeDRuntimeModule();
            host.modeDRuntime.OnAwake(host);
        }
        ModBehaviour.Instance = host;
        return host;
    }
    private static void Run()
    {
        var host = Host(); Probe.RiskAllowed = false;
        Check(!host.TryStartModeD(), "risk denial returns false");
        Calls("risk,resolve,translate-key,blocked-message", "risk is checked and explained before other mode or inventory queries");
        Check(!host.modeDRuntime.IsActive, "risk denial does not start runtime");

        host = Host(); host.modeERuntime.modeEActive = true;
        Check(!host.TryStartModeD(), "Mode E excludes Mode D");
        Calls("risk,mode-e", "Mode E denial prevents inventory query and startup");

        host = Host(); Probe.Naked = false;
        Check(!host.TryStartModeD(), "inventory denial returns false");
        Calls(EntryAllowed, "inventory denial prevents startup");

        host = Host();
        host.modeDRuntime.modeDWaveIndex = 99;
        host.modeDRuntime.modeDWaveCompletePending = true;
        host.modeDRuntime.modeDCurrentWaveEnemies.Add(new CharacterMainControl());
        Check(host.TryStartModeD(), "nominal entry succeeds through public bridge");
        Calls(EntryAllowed + "," + Startup, "real startup preserves ordered initialization and mutator timing");
        Check(host.modeDRuntime.IsActive && host.modeDRuntime.modeDWaveIndex == 0
            && !host.modeDRuntime.modeDWaveCompletePending && host.modeDRuntime.modeDCurrentWaveEnemies.Count == 0,
            "real startup clears old wave state on the same runtime");
        Check(host.modeDRuntime.modeDEnemiesPerWave == 5, "real startup uses host configuration");

        host = Host(); host.bossRushTicketTypeId = 500001; Probe.Ticket = 500001;
        Check(host.TryStartModeD(), "inventory query preserves actual configured ticket ID");

        foreach (string fault in new[] { "risk", "resolve", "translate-key", "blocked-message" })
        {
            host = Host(); Probe.RiskAllowed = false; Probe.Fault = fault;
            Check(host.TryStartModeD(), "risk-region exception retains legacy fallback: " + fault);
            Check(host.modeDRuntime.IsActive, "risk-region exception reaches actual startup: " + fault);
            string prefix = fault == "risk" ? "risk" : fault == "resolve" ? "risk,resolve"
                : fault == "translate-key" ? "risk,resolve,translate-key" : "risk,resolve,translate-key,blocked-message";
            Calls(prefix + ",mode-e,inventory," + Startup, "risk exception preserves next checks and complete startup: " + fault);
        }

        host = Host(); Probe.Fault = "mode-e";
        Check(!host.TryStartModeD(), "Mode E query failure is rejected by entry catch");
        Calls("risk,mode-e", "Mode E query exception prevents inventory and startup");

        host = Host(); Probe.Fault = "inventory";
        Check(!host.TryStartModeD(), "inventory query failure is rejected by entry catch");
        Calls(EntryAllowed, "inventory query exception prevents startup");

        host = Host(); Probe.Fault = "arena-reset";
        Check(host.TryStartModeD(), "StartModeD internal catch preserves legacy true entry result");
        Calls(EntryAllowed + ",arena-reset", "startup failure stays at the original first effect");
        Check(!host.modeDRuntime.IsActive, "failure before activation does not activate runtime");

        host = Host(); Probe.Fault = "starter";
        Check(host.TryStartModeD(), "starter boundary exception remains caught by actual StartModeD");
        Calls(EntryAllowed + ",arena-reset,recovery,config,config,items,enemies,pool,starter",
            "starter failure does not roll mutators or set sign");
        Check(host.modeDRuntime.IsActive, "legacy activation before starter boundary is preserved");

        host = Host();
        var other = new ModBehaviour { modeERuntime = new ModeERuntimeModule { modeEActive = true } };
        ModBehaviour.Instance = other;
        Check(host.TryStartModeD(), "public bridge uses current receiver rather than singleton");
        Check(host.StartedOnThisHost && !other.StartedOnThisHost, "startup targets the same host that passed admission");

        host = Host(false);
        Check(!host.TryStartModeD(), "pre-Awake entry returns false without escaping its original catch");
        Calls("risk", "pre-Awake call still checks the risk gate before missing runtime state");

        host = Host(false); Probe.RiskAllowed = false;
        Check(!host.TryStartModeD(), "pre-Awake blocked call keeps normal denial");
        Calls("risk,resolve,translate-key,blocked-message", "pre-Awake blocked call still explains the risk denial");
    }
    public static int Main()
    {
        try { Run(); Console.WriteLine("ModeDEntryOwnership: PASS " + checks + " checks"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
