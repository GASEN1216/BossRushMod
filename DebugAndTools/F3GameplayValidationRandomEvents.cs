using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace BossRush
{
    /// <summary>完整验收的标准模式与八种随机事件实机副作用用例。</summary>
    internal sealed partial class F3GameplayValidationRunner
    {
        private IEnumerator RunStandardAndRandomEvents()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                _host.StartFirstWave();
            }
            catch (Exception e)
            {
                Record("MODE_STANDARD_WAVE", "FAIL", sw.ElapsedMilliseconds, string.Empty, e.ToString());
                _host.ValidationSafeCleanup();
                yield break;
            }

            yield return WaitSeconds(8f);
            string hostileDetails;
            int enemies = _host.ValidationCountHostileCharacters(out hostileDetails);
            Record("MODE_STANDARD_WAVE", _host.IsActive && enemies > 0 ? "PASS" : "FAIL",
                sw.ElapsedMilliseconds, "enemies=" + enemies + ",hostiles=" + hostileDetails,
                _host.IsActive ? string.Empty : "mode_not_active");

            RandomEventDirector director = _host.RandomEventsRuntime != null
                ? _host.RandomEventsRuntime.Director : null;
            IList<RandomEventId> ids = director != null ? director.GetAllEventIds() : null;
            if (ids == null || ids.Count == 0)
            {
                Record("RANDOM_EVENTS_ALL", "FAIL", 0L, string.Empty, "catalog_empty");
            }
            else
            {
                int ok = 0;
                List<string> failures = new List<string>();
                for (int i = 0; i < ids.Count && !ShouldAbort(); i++)
                {
                    Stopwatch eventWatch = Stopwatch.StartNew();
                    string fail;
                    if (!director.TryForceTrigger(ids[i], out fail))
                    {
                        failures.Add(ids[i] + ":" + fail);
                        Record("RANDOM_EVENT_" + ids[i].ToString().ToUpperInvariant(), "FAIL",
                            eventWatch.ElapsedMilliseconds, string.Empty, fail);
                        director.ForceEndActive();
                        yield return WaitSeconds(0.25f);
                        continue;
                    }

                    RandomEventValidationOutcome outcome = RandomEventValidationOutcome.Pending;
                    string metrics = string.Empty;
                    float deadline = Time.realtimeSinceStartup + CaseTimeoutSeconds;
                    while (outcome == RandomEventValidationOutcome.Pending
                        && Time.realtimeSinceStartup < deadline && !ShouldAbort())
                    {
                        outcome = director.GetActiveValidationOutcome(out metrics);
                        if (outcome == RandomEventValidationOutcome.Pending) yield return null;
                    }

                    bool passed = outcome == RandomEventValidationOutcome.Passed;
                    if (passed) ok++;
                    else failures.Add(ids[i] + ":" + (outcome == RandomEventValidationOutcome.Pending
                        ? "effect_timeout" : "effect_failed"));
                    Record("RANDOM_EVENT_" + ids[i].ToString().ToUpperInvariant(), passed ? "PASS" : "FAIL",
                        eventWatch.ElapsedMilliseconds, metrics,
                        passed ? string.Empty : (outcome == RandomEventValidationOutcome.Pending
                            ? "异步副作用在 30 秒内未完成" : "事件副作用验证失败"));
                    director.ForceEndActive();
                    yield return WaitSeconds(0.25f);
                }

                Record("RANDOM_EVENTS_ALL", ok == ids.Count ? "PASS" : "FAIL", 0L,
                    "passed=" + ok + "/" + ids.Count, string.Join(",", failures.ToArray()));
            }

            _host.ValidationSafeCleanup();
        }
    }
}
