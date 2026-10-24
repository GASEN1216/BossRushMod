using System;
using System.Collections.Generic;
using BossRush;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(label);
    }
    private static void Near(float actual, float expected, string label)
    {
        Check(!float.IsNaN(actual) && !float.IsInfinity(actual) && Math.Abs(actual - expected) <= 0.001f,
            label + ": expected " + expected + ", actual " + actual);
    }
    private static void Main()
    {
        MimicMenusAndGrowth();
        MimicAllMealOrdersAndAttacks();
        MimicHealingAndDamageGeometry();
        ParcelHandlingAndPressure();
        ParcelIntegrityAndSettlement();
        ParcelPaymentFailureAndReentry();
        PrototypeBorrowAndCooldown();
        PrototypeExpiryAndReflection();
        PrototypeRewardsAndCancellation();
        InventoryEligibilityAndSelection();
        InventoryConsumptionCommitAndRollback();
        InventoryRewardOwnership();
        Console.WriteLine("PASS: " + checks + " production-linked FieldAdventureRules assertions");
    }

    private static void MimicMenusAndGrowth()
    {
        var empty = new GreedyMimicRules();
        Check(empty.Meals == 0 && empty.MaximumHealth == 600f && empty.Scale == 1f, "unfed mimic has base health and scale");
        foreach (int invalid in new[] { -1, 3, 32, int.MinValue, int.MaxValue })
        {
            Check(!empty.CanFeed((GreedyMimicFood)invalid) && !empty.Feed((GreedyMimicFood)invalid), "invalid meals never consume growth capacity");
            Check(empty.Meals == 0 && empty.Level((GreedyMimicFood)invalid) == 0, "invalid meal attempts preserve empty state");
        }
        Check(GreedyMimicRules.Cost(GreedyMimicFood.Ammunition) == 12, "ammunition feeding consumes twelve rounds");
        Check(GreedyMimicRules.Cost(GreedyMimicFood.Explosive) == 1 && GreedyMimicRules.Cost(GreedyMimicFood.Medicine) == 1,
            "grenades and medicine consume one item per meal");
        for (int turn = -1; turn < 20; turn++)
            Check(empty.AttackAt(turn) == GreedyMimicAttack.Bite, "unfed mimic cannot invent weapon or healing attacks");

        foreach (GreedyMimicFood food in new[] { GreedyMimicFood.Ammunition, GreedyMimicFood.Explosive, GreedyMimicFood.Medicine })
        {
            var rules = new GreedyMimicRules();
            float previousHealth = rules.MaximumHealth, previousScale = rules.Scale;
            for (int meal = 0; meal < 3; meal++)
            {
                Check(rules.CanFeed(food) && rules.Feed(food), "each of the first three meals is accepted");
                Check(rules.MaximumHealth > previousHealth && rules.Scale > previousScale, "feeding visibly enlarges and toughens the mimic");
                previousHealth = rules.MaximumHealth; previousScale = rules.Scale;
            }
            Check(rules.Meals == 3 && rules.Level(food) == 3, "three same-kind meals retain all three upgrades");
            Near(rules.MaximumHealth, 1320f, "fully fed mimic health budget");
            Near(rules.Scale, 1.54f, "fully fed mimic size budget");
            foreach (GreedyMimicFood fourth in new[] { GreedyMimicFood.Ammunition, GreedyMimicFood.Explosive, GreedyMimicFood.Medicine })
                Check(!rules.CanFeed(fourth) && !rules.Feed(fourth), "fourth meal is rejected for every food type");
            Check(rules.Meals == 3 && rules.Level(food) == 3, "rejected fourth meals cannot change growth");
            Near(rules.MaximumHealth, previousHealth, "rejected fourth meals preserve health budget");
        }
    }

    private static void MimicAllMealOrdersAndAttacks()
    {
        GreedyMimicAttack[] learned = { GreedyMimicAttack.Volley, GreedyMimicAttack.Bombs, GreedyMimicAttack.Recovery };
        // 穷举所有三次投喂顺序；期望只来自玩家喂过的类别，不复制生产选招算法。
        for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) for (int c = 0; c < 3; c++)
        {
            var rules = new GreedyMimicRules();
            var expected = new HashSet<GreedyMimicAttack>();
            foreach (int food in new[] { a, b, c })
            {
                Check(rules.Feed((GreedyMimicFood)food), "all legal meal orders remain available");
                expected.Add(learned[food]);
            }
            var seen = new HashSet<GreedyMimicAttack>();
            for (int turn = 0; turn < 24; turn++)
            {
                GreedyMimicAttack attack = rules.AttackAt(turn);
                if (turn % 2 == 0) Check(attack == GreedyMimicAttack.Bite, "every learned attack is separated by a bite turn");
                else
                {
                    Check(expected.Contains(attack), "mimic only attacks with the foods the player actually fed");
                    seen.Add(attack);
                }
            }
            Check(seen.SetEquals(expected), "all learned food behaviors appear in the bounded attack cycle");
            Check(rules.AttackAt(-1) == GreedyMimicAttack.Bite, "negative turn falls back to a safe bite selection");
            Check(!rules.Feed(GreedyMimicFood.Ammunition), "mixed feeding also stops at three meals");
        }
        var mixed = new GreedyMimicRules();
        mixed.Feed(GreedyMimicFood.Medicine); mixed.Feed(GreedyMimicFood.Explosive); mixed.Feed(GreedyMimicFood.Ammunition);
        Check(mixed.AttackAt(1) == GreedyMimicAttack.Volley && mixed.AttackAt(3) == GreedyMimicAttack.Bombs
            && mixed.AttackAt(5) == GreedyMimicAttack.Recovery && mixed.AttackAt(7) == GreedyMimicAttack.Volley,
            "one of each food yields the documented volley bomb recovery cycle independent of feeding order");
        Near(mixed.Damage(GreedyMimicAttack.Volley), 21f, "one ammunition meal has the intended volley damage");
        Near(mixed.Damage(GreedyMimicAttack.Bombs), 30f, "one explosive meal has the intended bomb damage");
    }

    private static void MimicHealingAndDamageGeometry()
    {
        var medic = new GreedyMimicRules();
        medic.Feed(GreedyMimicFood.Medicine);
        Near(medic.Recovery(medic.MaximumHealth), 0f, "full health mimic cannot heal further");
        Near(medic.Recovery(medic.MaximumHealth + 10f), 0f, "overfilled health never becomes negative healing");
        Near(medic.Recovery(medic.MaximumHealth - 1f), 1f, "healing is clamped to missing health");
        Near(medic.Recovery(1f), 110f, "one medicine meal heals the intended amount when badly hurt");
        medic.Feed(GreedyMimicFood.Medicine); medic.Feed(GreedyMimicFood.Medicine);
        Near(medic.Recovery(1f), 180f, "three medicine meals have a bounded stronger recovery");
        Check(!GreedyMimicRules.Interrupted(44.99f) && GreedyMimicRules.Interrupted(45f), "healing interrupt damage has an exact inclusive threshold");
        Check(GreedyMimicRules.WarningSeconds(GreedyMimicAttack.Recovery) > GreedyMimicRules.WarningSeconds(GreedyMimicAttack.Bombs)
            && GreedyMimicRules.WarningSeconds(GreedyMimicAttack.Bombs) > GreedyMimicRules.WarningSeconds(GreedyMimicAttack.Volley),
            "healing and bombs give longer visible reaction windows than volleys");
        Check(GreedyMimicRules.InCircle(3f, 4f, 5f) && !GreedyMimicRules.InCircle(3f, 4.01f, 5f), "blast circle distinguishes exact edge from outside");
        Near(GreedyMimicRules.DistanceToSegmentSquared(3f, 2f, 0f, 0f, 6f, 0f), 4f, "beam geometry measures perpendicular distance");
        Near(GreedyMimicRules.DistanceToSegmentSquared(7f, 0f, 0f, 0f, 6f, 0f), 1f, "beam geometry stops at its endpoint");
        Near(GreedyMimicRules.DistanceToSegmentSquared(3f, 4f, 0f, 0f, 0f, 0f), 25f, "collapsed beam has finite point distance");
    }

    private static void ParcelHandlingAndPressure()
    {
        foreach (OddParcelKind kind in new[] { OddParcelKind.QuackingEgg, OddParcelKind.AngrySoda })
        {
            var parcel = new OddParcelCondition(kind);
            Check(parcel.Integrity == 100f && parcel.Pressure == 0f && !parcel.Paid && !parcel.Ruined,
                "new parcels begin intact and unpaid");
            parcel.Step(0.5f, 6f, true);
            float pressure = parcel.Pressure;
            Check(pressure > 0f && parcel.Integrity == 100f, "running builds pressure before causing damage");
            parcel.Step(0.25f, 6f, false);
            Check(parcel.Pressure < pressure && parcel.Integrity == 100f, "putting a parcel down relieves pressure without damaging it");
            for (int i = 0; i < 20; i++) parcel.Step(0.5f, 0f, false);
            Near(parcel.Pressure, 0f, "resting on the ground bottoms pressure at zero");

            var capped = new OddParcelCondition(kind);
            var ordinary = new OddParcelCondition(kind);
            bool longFrameBurst = capped.Step(1000f, 6f, true);
            bool normalBurst = ordinary.Step(0.5f, 6f, true);
            Near(capped.Pressure, ordinary.Pressure, "a long frame cannot skip an entire parcel warning cycle");
            Check(longFrameBurst == normalBurst && capped.Integrity == ordinary.Integrity, "long frame damage is bounded to one ordinary step");

            foreach (float invalid in new[] { -1f, 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                float before = ordinary.Pressure;
                Check(!ordinary.Step(invalid, 6f, true), "invalid or paused elapsed time cannot erupt a parcel");
                Near(ordinary.Pressure, before, "invalid or paused elapsed time preserves parcel pressure");
            }
            foreach (float invalidSpeed in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                float before = ordinary.Pressure;
                Check(!ordinary.Step(0.5f, invalidSpeed, true), "nonfinite movement cannot erupt a parcel");
                Near(ordinary.Pressure, before, "nonfinite movement preserves parcel pressure");
            }
        }
        var egg = new OddParcelCondition(OddParcelKind.QuackingEgg);
        egg.Step(0.5f, 6f, true);
        float eggPressure = egg.Pressure;
        egg.Step(0.25f, 0f, true);
        Check(egg.Pressure < eggPressure, "standing still calms the quacking egg even while carried");
        var soda = new OddParcelCondition(OddParcelKind.AngrySoda);
        soda.Step(0.5f, 6f, true);
        float sodaPressure = soda.Pressure;
        soda.Step(0.25f, 0f, true);
        Check(soda.Pressure > sodaPressure, "soda continues building pressure until it is actually put down");
    }

    private static void Erupt(OddParcelCondition parcel)
    {
        bool erupted = false;
        for (int step = 0; step < 100 && !erupted; step++) erupted = parcel.Step(0.1f, 6f, true);
        Check(erupted, "continued sprinting eventually causes a visible parcel eruption");
    }
    private static void ParcelIntegrityAndSettlement()
    {
        foreach (OddParcelKind kind in new[] { OddParcelKind.QuackingEgg, OddParcelKind.AngrySoda })
        {
            var parcel = new OddParcelCondition(kind);
            Check(parcel.Reward == 2400, "perfect delivery has the documented payout");
            Erupt(parcel);
            Check(parcel.Integrity < 100f && parcel.Integrity > 0f && parcel.Pressure < 100f,
                "eruption reduces integrity and releases pressure");
            int damagedReward = parcel.Reward;
            Check(damagedReward < 2400 && damagedReward >= 1200, "damaged parcels reduce payment within the delivery budget");
            for (int i = 0; i < 20; i++) parcel.Step(0.5f, 0f, false);
            Check(parcel.Reward == damagedReward, "putting a parcel down cannot repair earlier damage or increase its reward");
            long paidAmount = 0;
            Check(parcel.TryPay(amount => { paidAmount = amount; return true; }), "damaged but intact parcel can settle");
            Check(paidAmount == damagedReward && parcel.Paid, "settlement passes the actual condition-adjusted reward");
            float integrity = parcel.Integrity, pressure = parcel.Pressure;
            for (int i = 0; i < 100; i++) Check(!parcel.Step(0.5f, 6f, true), "settled parcel cannot erupt again");
            Near(parcel.Integrity, integrity, "settled parcel integrity stops changing");
            Near(parcel.Pressure, pressure, "settled parcel pressure stops changing");

            var ruined = new OddParcelCondition(kind);
            for (int burst = 0; burst < 20 && !ruined.Ruined; burst++) Erupt(ruined);
            Check(ruined.Ruined && ruined.Integrity == 0f, "repeated mishandling ends at zero integrity");
            int callbacks = 0;
            Check(!ruined.TryPay(amount => { callbacks++; return true; }) && callbacks == 0,
                "ruined parcel never invokes the payment service");
        }
    }

    private static void ParcelPaymentFailureAndReentry()
    {
        var parcel = new OddParcelCondition(OddParcelKind.QuackingEgg);
        Check(!parcel.TryPay(null) && !parcel.Paid, "missing payment service leaves claim retryable");
        int callbacks = 0;
        Check(!parcel.TryPay(amount => { callbacks++; return false; }) && !parcel.Paid,
            "payment failure does not mark a parcel paid");
        bool threw = false;
        try { parcel.TryPay(amount => { callbacks++; throw new ApplicationException("simulated service failure"); }); }
        catch (ApplicationException) { threw = true; }
        Check(threw && !parcel.Paid, "payment exception leaves the same parcel unpaid");
        bool reentrantAccepted = true;
        Check(parcel.TryPay(amount =>
        {
            callbacks++;
            reentrantAccepted = parcel.TryPay(nested => { callbacks++; return true; });
            return true;
        }), "payment can retry successfully after false and exception results");
        Check(!reentrantAccepted && callbacks == 3, "reentrant settlement cannot call the payment service twice");
        Check(!parcel.TryPay(amount => { callbacks++; return true; }) && callbacks == 3,
            "paid parcel rejects every later payout attempt");
        var other = new OddParcelCondition(OddParcelKind.AngrySoda);
        Check(other.TryPay(amount => true), "a different parcel keeps an independent settlement owner");
    }

    private static void PrototypeBorrowAndCooldown()
    {
        var invalidRules = new DangerousPrototypeRules();
        foreach (int invalid in new[] { -1, 2, 31, 32, int.MinValue, int.MaxValue })
        {
            Check(!invalidRules.CanBorrow((DangerousPrototypeKind)invalid)
                && !invalidRules.Borrow((DangerousPrototypeKind)invalid), "invalid prototype kinds cannot alias valid claim bits");
            Check(!invalidRules.Active && !invalidRules.Completed, "invalid prototype selection leaves the lease untouched");
        }
        foreach (DangerousPrototypeKind kind in new[] { DangerousPrototypeKind.JetCannon, DangerousPrototypeKind.RicochetSun })
        {
            var rules = new DangerousPrototypeRules();
            Check(!rules.TryFire() && !rules.Claim(), "unborrowed prototype cannot shoot or claim a reward");
            Check(rules.Borrow(kind) && rules.Active && !rules.Completed, "valid prototype can be borrowed");
            Near(rules.Remaining, 150f, "new test starts with its full time budget");
            Check(!rules.Borrow(kind), "an active lease cannot be overwritten");
            Check(rules.TryFire() && !rules.TryFire(), "initial charge permits one blast and blocks rapid duplicate shots");
            rules.Tick(rules.RechargeSeconds * 0.5f, true);
            Check(!rules.TryFire(), "half recharge cannot fire the prototype");
            rules.Tick(rules.RechargeSeconds * 0.5f, true);
            Check(rules.TryFire(), "full recharge makes the next normal shot eligible");
            float remaining = rules.Remaining;
            rules.Tick(10f, false);
            Near(rules.Charge, 0f, "holstered prototype does not recharge");
            Near(rules.Remaining, remaining - 10f, "switching guns still consumes the trial time budget");
            foreach (float invalid in new[] { -1f, 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                remaining = rules.Remaining;
                Check(!rules.Tick(invalid, true), "invalid elapsed time cannot expire the trial");
                Near(rules.Remaining, remaining, "invalid elapsed time preserves remaining trial time");
                Near(rules.Charge, 0f, "invalid elapsed time cannot charge the prototype");
            }
            rules.Tick(30f, true);
            Near(rules.Charge, 1f, "long recharge is capped at one ready blast");
            Check(rules.TryFire() && !rules.TryFire(), "stored charge cannot accumulate multiple free blasts");
        }
    }

    private static void Complete(DangerousPrototypeRules rules, bool reflected)
    {
        int target = rules.Target;
        for (int hit = 1; hit <= target; hit++)
            Check(rules.ObserveHit(reflected) == (hit == target), "only the final qualifying hit completes the trial");
        Check(rules.Completed && !rules.Active && rules.Progress == target, "completed trial freezes at its target");
    }
    private static void PrototypeExpiryAndReflection()
    {
        var expiring = new DangerousPrototypeRules();
        expiring.Borrow(DangerousPrototypeKind.JetCannon);
        expiring.ObserveHit(false);
        expiring.Tick(149f, false);
        Check(expiring.Active && expiring.Progress == 1, "trial remains active until the exact deadline");
        Check(expiring.Tick(1f, false), "trial expires at the exact deadline even when holstered");
        Check(!expiring.Active && !expiring.Completed && expiring.Progress == 0 && !expiring.Claim(),
            "timed out progress cannot be claimed as success");
        Check(!expiring.Tick(100f, true), "expired trial cannot expire or notify twice");
        Check(expiring.Borrow(DangerousPrototypeKind.JetCannon), "failed trial remains available for a fresh attempt");

        var ricochet = new DangerousPrototypeRules();
        ricochet.Borrow(DangerousPrototypeKind.RicochetSun);
        for (int hit = 0; hit < 20; hit++) Check(!ricochet.ObserveHit(false), "direct beam damage never advances the ricochet challenge");
        Check(ricochet.Progress == 0 && ricochet.Target == 3, "ricochet challenge requires three reflected hits");
        Complete(ricochet, true);
        Check(!ricochet.ObserveHit(true) && !ricochet.TryFire(), "completed prototype stops damage eligibility and extra progress");
        Check(!ricochet.Tick(1000f, true) && ricochet.Completed, "completed reward survives the old trial deadline");
        Check(!ricochet.Borrow(DangerousPrototypeKind.JetCannon), "unclaimed completion blocks borrowing another rig");
        Check(DangerousPrototypeRules.Damage(DangerousPrototypeKind.RicochetSun, 1)
            > DangerousPrototypeRules.Damage(DangerousPrototypeKind.RicochetSun, 0), "wall bounces increase combat damage");
        Near(DangerousPrototypeRules.Damage(DangerousPrototypeKind.JetCannon, 0), 85f, "jet cannon fixed damage budget");
        Near(DangerousPrototypeRules.Damage(DangerousPrototypeKind.RicochetSun, 0), 22f, "direct beam fixed damage budget");
        Near(DangerousPrototypeRules.Damage(DangerousPrototypeKind.RicochetSun, 3), 110f, "reflected beam fixed damage budget");
    }

    private static void PrototypeRewardsAndCancellation()
    {
        var rules = new DangerousPrototypeRules();
        rules.Borrow(DangerousPrototypeKind.JetCannon);
        rules.ObserveHit(false); rules.Cancel();
        Check(rules.Borrow(DangerousPrototypeKind.JetCannon) && rules.Progress == 0,
            "returning an unfinished rig permits a fresh attempt without inherited hits");
        Complete(rules, false);
        Check(rules.Claim() && !rules.Claim(), "completed prototype rewards can be claimed exactly once");
        Check(!rules.CanBorrow(DangerousPrototypeKind.JetCannon), "a claimed prototype cannot be farmed again in the same raid");
        Check(rules.Borrow(DangerousPrototypeKind.RicochetSun), "claiming one prototype leaves the other available");
        rules.Cancel();
        Check(!rules.CanBorrow(DangerousPrototypeKind.JetCannon), "cancelling another lease preserves already claimed quota");
        Check(rules.Borrow(DangerousPrototypeKind.RicochetSun), "cancelled unclaimed prototype can be borrowed again");
        Complete(rules, true);
        rules.Cancel();
        Check(!rules.Claim() && !rules.CanBorrow(DangerousPrototypeKind.JetCannon), "cancelling unclaimed completion cannot resurrect an earlier reward");
        Check(rules.Borrow(DangerousPrototypeKind.RicochetSun), "unclaimed cancelled completion leaves its own quota available");
        Complete(rules, true);
        Check(rules.Claim() && !rules.CanBorrow(DangerousPrototypeKind.JetCannon)
            && !rules.CanBorrow(DangerousPrototypeKind.RicochetSun), "both prototype quotas can be exhausted independently");
        rules.Cancel();
        Check(!rules.CanBorrow(DangerousPrototypeKind.JetCannon) && !rules.CanBorrow(DangerousPrototypeKind.RicochetSun),
            "ordinary cleanup cannot clear the raid reward ledger");
        rules.ResetRaid();
        Check(rules.CanBorrow(DangerousPrototypeKind.JetCannon) && rules.CanBorrow(DangerousPrototypeKind.RicochetSun),
            "only a new raid resets both prototype reward quotas");
        Check(!rules.Active && !rules.Completed && rules.Progress == 0, "new raid also clears unfinished trial state");
    }
}
