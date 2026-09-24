#!/usr/bin/env python3
"""Link the current shop payment and rollback methods to an in-memory game adapter."""
from pathlib import Path
import subprocess
import xml.sax.saxutils as xml

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build" / "npc-shop-payment"
SHOP = ROOT / "Integration/Affinity/Systems/NPCShopSystem.cs"


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
    source = SHOP.read_text(encoding="utf-8-sig")
    code = "using System; using System.Collections.Generic; using System.Reflection; namespace BossRush {\n"
    code += extract(source, "internal sealed class NPCShopPaymentStrategy") + "\n"
    code += "public static partial class NPCShopSystem {\n"
    code += "private static NPCShopPaymentStrategy currentPaymentStrategy = NPCShopPaymentStrategy.Cash;\n"
    code += "private static bool isServiceActive; private static StockShop currentShop;\n"
    code += "private static readonly Dictionary<int,int> temporaryPurificationPrices = new Dictionary<int,int>();\n"
    code += 'private const string TemporaryPurificationPriceUnavailable = "TemporaryPurificationPriceUnavailable";\n'
    for signature in (
        "private static bool IsPurificationShop()",
        "private static bool TryGetPurificationPriceForType(int typeId, out int price)",
        "private static void OnItemPurchased(StockShop shop, Item purchasedItem)",
        "private static void RollbackTemporaryPurificationShopPurchase(Item purchasedItem, string reason)",
        "private static void OnItemSoldByPlayer(StockShop shop, Item soldItem, int price)",
        "private static void RejectTemporaryPurificationShopSell(Item soldItem, int price)",
    ):
        code += extract(source, signature) + "\n"
    code += "internal static int RefreshCount;\n"
    code += "private static void UpdateTemporaryShopCurrencyUiDeferred() { RefreshCount++; }\n"
    code += "internal static void BeginTest(StockShop shop, NPCShopPaymentStrategy strategy, int price) { currentShop=shop; currentPaymentStrategy=strategy; isServiceActive=true; temporaryPurificationPrices.Clear(); if(price>0) temporaryPurificationPrices[500001]=price; RefreshCount=0; }\n"
    code += "internal static void Purchase(StockShop shop, Item item) { OnItemPurchased(shop,item); }\n"
    code += "internal static void Sell(StockShop shop, Item item, int price) { OnItemSoldByPlayer(shop,item,price); }\n"
    code += "}}\n"
    extracted = OUT / "Extracted.cs"
    extracted.write_text(code, encoding="utf-8")
    paths = [extracted, HERE / "Stubs.cs", HERE / "Program.cs"]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += "".join('<Compile Include="' + xml.escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
    project += "</ItemGroup></Project>"
    project_file = OUT / "NPCShopPayment.csproj"
    project_file.write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project_file), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
