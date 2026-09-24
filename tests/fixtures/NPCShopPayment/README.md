# NPCShopPayment

`run.py` extracts the current `NPCShopPaymentStrategy` and the production purchase, rollback, and sell callbacks from `Integration/Affinity/Systems/NPCShopSystem.cs`. The in-memory stubs supply the official shop callback, item, stock, UI refresh, and money operations. The fixture checks cash passthrough, successful purification payment, failed payment rollback, missing price, unrelated shop callbacks, and rejected selling. It does not load Unity or exercise the live UI.
