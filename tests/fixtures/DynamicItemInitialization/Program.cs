using System;

sealed class Item
{
    public int TypeID = 500061;
    public bool AlreadyInitialized, ThrowOnInitialize;
    public int InitializeCalls;
    public readonly Agents AgentUtilities = new Agents();
    public void Initialize()
    {
        InitializeCalls++;
        if (ThrowOnInitialize) throw new InvalidOperationException("host initialization failed");
        if (AlreadyInitialized) return;
        AlreadyInitialized = true;
        AgentUtilities.Initialize(this);
    }
}

sealed class Agents
{
    public Item Master;
    public int BindCalls;
    public void Initialize(Item item) { Master = item; BindCalls++; }
    public Item CreateAgent() { return Master; }
}

static class BossRushDynamicItemRegistry
{
    internal static bool IsBossRushDynamicItemType(int id) { return id == 500061 || id == 500059; }
}
static class ModBehaviour
{
    internal static int Errors;
    internal static void CriticalLog(string key, string message) { Errors++; }
}
static class Program
{
    static int checks;
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
    static void Main()
    {
        Production.InitializeInstance(null);
        foreach (int id in new[] { 500061, 500059 })
        {
            var item = new Item { TypeID = id };
            Check(item.AgentUtilities.CreateAgent() == null, "inactive clone reproduces missing master before repair");
            Production.InitializeInstance(item);
            Check(item.AgentUtilities.CreateAgent() == item, "pickup and handheld agent bind to actual clone");
            Production.InitializeInstance(item);
            Check(item.InitializeCalls == 1 && item.AgentUtilities.BindCalls == 1, "repeat restore is idempotent");
        }
        var original = new Item();
        var stale = new Item { AlreadyInitialized = true };
        stale.AgentUtilities.Master = original;
        Production.InitializeInstance(stale);
        Check(stale.AgentUtilities.Master == stale, "stale initialized clone repairs its owner");
        var official = new Item { TypeID = 123 };
        Production.InitializeInstance(official);
        Check(official.InitializeCalls == 0, "official items remain on official lifecycle");
        var failed = new Item { ThrowOnInitialize = true };
        Production.InitializeInstance(failed);
        Check(ModBehaviour.Errors == 1 && failed.AgentUtilities.Master == null, "host failure is diagnosed without pretending initialized");
        Console.WriteLine("DynamicItemInitialization: PASS " + checks);
    }
}
