# Birthday Cake delayed-gift regression

This fixture compiles the production DelayedBirthdayCakeGift iterator from Integration/BossRushIntegrationRuntimeModule_BirthdayCake.cs. The only adapter is a small WaitForSeconds stand-in and a counter for the later grant-check call. It verifies the two-second wait, that the grant check waits until after the yielded delay, and that the completed iterator invokes the check once. It does not model Unity scenes, player inventory, or save storage.

Run through the aggregate regression entry point with python tools/run_runtime_regressions.py --filter BirthdayCakeGift.