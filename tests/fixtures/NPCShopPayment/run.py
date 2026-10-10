#!/usr/bin/env python3
"""Link current shop payment and lifecycle methods to an in-memory game adapter."""
from pathlib import Path
import sys
import tempfile
import xml.sax.saxutils as xml

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "npc-shop-payment"
SHOP = ROOT / "Integration/Affinity/Systems/NPCShopSystem.cs"
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def extract(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="snapshot-", dir=OUT)).resolve()
    source = SHOP.read_text(encoding="utf-8-sig")
    code = "using System; using System.Collections.Generic; using System.Reflection; using UnityEngine; namespace BossRush {\n"
    code += extract(source, "internal sealed class NPCShopPaymentStrategy") + "\n"
    code += "public static partial class NPCShopSystem {\n"
    code += "private static NPCShopPaymentStrategy currentPaymentStrategy = NPCShopPaymentStrategy.Cash;\n"
    code += "private static bool isServiceActive; private static StockShop currentShop;\n"
    code += "private static GameObject shopObject; private static string currentNpcId; private static Transform currentNpcTransform; private static ShopController currentController;\n"
    code += "private static FieldInfo textSellField, priceTextField, interactionButtonField, interactionTextField; private static bool reflectionInitialized; private static string originalTextSell;\n"
    code += "private static readonly HashSet<Item> ownedDisplayItems = new HashSet<Item>();\n"
    code += "private static readonly Dictionary<int,float> temporaryPurificationFactors = new Dictionary<int,float>();\n"
    code += "private static readonly Dictionary<int,int> temporaryPurificationPrices = new Dictionary<int,int>();\n"
    code += 'private const string TemporaryPurificationPriceUnavailable = "TemporaryPurificationPriceUnavailable";\n'
    for signature in (
        "private static bool IsPurificationShop()",
        "private static bool TryGetPurificationPriceForType(int typeId, out int price)",
        "private static void OnItemPurchased(StockShop shop, Item purchasedItem)",
        "private static void RollbackTemporaryPurificationShopPurchase(Item purchasedItem, string reason)",
        "private static void OnItemSoldByPlayer(StockShop shop, Item soldItem, int price)",
        "private static void RejectTemporaryPurificationShopSell(Item soldItem, int price)",
        "public static void CloseShop()",
        "public static void CloseShopIfOwnedBy(Transform npcTransform)",
        "public static void ResetStaticCaches()",
        "private static bool IsCurrentShopOwnedBy(Transform npcTransform)",
        "private static void RegisterEvents()",
        "private static void UnregisterEvents()",
        "private static void OnManagedUIElementClose(ManagedUIElement element)",
        "private static void Cleanup()",
    ):
        code += extract(source, signature) + "\n"
    code += "internal static int RefreshCount;\n"
    code += "private static void UpdateTemporaryShopCurrencyUiDeferred() { RefreshCount++; }\n"
    code += "private static void RestoreShopUIText() { } private static void UnregisterShopSelectionEvent() { }\n"
    code += "internal static void BeginTest(StockShop shop, NPCShopPaymentStrategy strategy, int price, Transform owner = null) { UnregisterEvents(); currentShop=shop; shopObject=shop.gameObject; currentNpcId=\"sky_fuzhou\"; currentNpcTransform=owner; currentController=new ShopController(); currentPaymentStrategy=strategy; isServiceActive=true; temporaryPurificationPrices.Clear(); if(price>0) temporaryPurificationPrices[500001]=price; RefreshCount=0; RegisterEvents(); }\n"
    code += "internal static bool ActiveForTest { get { return isServiceActive; } } internal static ShopController ControllerForTest { get { return currentController; } } internal static void CleanupForTest() { Cleanup(); }\n"
    code += "internal static void OwnDisplayForTest(Item item) { ownedDisplayItems.Add(item); }\n"
    code += "internal static void Purchase(StockShop shop, Item item) { OnItemPurchased(shop,item); }\n"
    code += "internal static void Sell(StockShop shop, Item item, int price) { OnItemSoldByPlayer(shop,item,price); }\n"
    code += "}}\n"
    extracted = invocation / "Extracted.cs"
    extracted.write_text(code, encoding="utf-8")
    paths = [extracted, HERE / "Stubs.cs", HERE / "Program.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += "".join('<Compile Include="' + xml.escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    project += "</ItemGroup></Project>"
    project_file = invocation / "NPCShopPayment.csproj"
    project_file.write_text(project, encoding="utf-8")
    code, output = run_project_fixture(project_file, invocation / "execution", ROOT)
    print(output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
