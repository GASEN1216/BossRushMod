using System;
using System.Collections.Generic;

namespace BossRush
{
    internal static class Program
    {
        private static void Check(bool value, string reason)
        {
            if (!value) throw new Exception(reason);
        }

        private static void Main()
        {
            var calls = new List<string>();
            EquipmentFactory.RegisterConfigurator("first", (item, name) => calls.Add("old"));
            EquipmentFactory.RegisterConfigurator("second", (item, name) => calls.Add("second:" + name));
            EquipmentFactory.RegisterConfigurator("first", (item, name) => calls.Add("first:" + name));
            EquipmentFactory.RegisterGunConfigurator("gun", (item, name) => calls.Add("gun:" + name));
            EquipmentFactory.ApplyGeneral(new Item());
            Check(calls.Count == 2 && calls[0] == "first:general" && calls[1] == "second:general",
                "repeat registration replaces in place without changing order");
            EquipmentFactory.ApplyGun(new Item());
            Check(calls.Count == 3 && calls[2] == "gun:gun", "gun pre-configuration stays a separate phase");
            bool badKeyRejected = false;
            try { EquipmentFactory.RegisterConfigurator("", (item, name) => { }); }
            catch (ArgumentException) { badKeyRejected = true; }
            Check(badKeyRejected, "empty registration key rejected");
            Console.WriteLine("EquipmentConfiguratorRegistry: PASS");
        }
    }
}
