// EXP05 - drives BrightnessWriter through its transitions and asserts the exposed state.
//
// A fake controller records what was written and a mutable clock stands in for real time, so the
// machine is advanced by its own transitions - never by sleeping. This is the AGENTS rule for
// machine logic, and it is the whole reason the debounce lives in Brightness.cs (no hardware).
//
// Built as a console exe by writer-test.ps1 (a winexe cannot print).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monitron.App
{
    internal sealed class FakeController : IBrightnessController
    {
        public readonly List<string> Writes = new List<string>();
        public bool FailNext;
        private readonly List<MonitorInfo> _monitors = new List<MonitorInfo>();

        public FakeController(params string[] ids)
        {
            int i = 0;
            foreach (string id in ids) _monitors.Add(new MonitorInfo(id, id, MonitorKind.External, 0, 50, 100, ++i));
        }

        public IList<MonitorInfo> Detect() { return _monitors; }

        public int? Read(string id) { return 50; }

        public bool Write(string id, int value)
        {
            Writes.Add(id + "=" + value.ToString(CultureInfo.InvariantCulture));
            if (FailNext) { FailNext = false; return false; }
            return true;
        }
    }

    /// <summary>A clock that only moves when the test says so.</summary>
    internal sealed class FakeClock
    {
        private DateTimeOffset _now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset Now { get { return _now; } }
        public void Advance(TimeSpan by) { _now = _now.Add(by); }
    }

    internal static class BrightnessWriterTests
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ok   " + what); }
            else { _failed++; Console.WriteLine("  FAIL " + what); }
        }

        private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

        private static int Main()
        {
            Console.WriteLine("BrightnessWriter - debounce driven, state asserted");

            CoalesceAndSettle();
            NoWriteWhenUnchanged();
            FailureIsSurfacedNotRetried();
            IndependentPerMonitor();
            FlushLandsPending();
            ApplyIsNotDebounced();

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0} passed, {1} failed", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static void CoalesceAndSettle()
        {
            Console.WriteLine("a drag settles into exactly one write, the last value");
            var fake = new FakeController("A");
            var clock = new FakeClock();
            var w = new BrightnessWriter(fake, Settle, delegate { return clock.Now; });
            w.Seed("A", 50);

            w.Request("A", 40);
            w.Advance();
            Check(fake.Writes.Count == 0, "nothing is written before the settle window passes");

            clock.Advance(TimeSpan.FromMilliseconds(200));
            w.Request("A", 60);              // still dragging: this restarts the window
            clock.Advance(TimeSpan.FromMilliseconds(200));
            w.Advance();
            Check(fake.Writes.Count == 0, "a move inside the window restarts it; still nothing written");

            clock.Advance(TimeSpan.FromMilliseconds(100));   // now past the second deadline
            w.Advance();
            Check(fake.Writes.Count == 1, "one write after it settles");
            Check(fake.Writes[0] == "A=60", "and it is the LAST value asked for, not the first (got " + fake.Writes[0] + ")");
            Check(w.Applied("A") == 60 && w.Target("A") == 60, "applied and target agree afterwards");
            Check(w.IsIdle, "the writer is idle once settled");
        }

        private static void NoWriteWhenUnchanged()
        {
            Console.WriteLine("writing the value that is already applied is a no-op");
            var fake = new FakeController("A");
            var clock = new FakeClock();
            var w = new BrightnessWriter(fake, Settle, delegate { return clock.Now; });
            w.Seed("A", 50);

            w.Request("A", 50);              // the "move" equals what is already there
            clock.Advance(TimeSpan.FromSeconds(1));
            w.Advance();
            Check(fake.Writes.Count == 0, "no write for an unchanged value");
        }

        private static void FailureIsSurfacedNotRetried()
        {
            Console.WriteLine("a failed write is surfaced and not retried until the next move");
            var fake = new FakeController("A");
            var clock = new FakeClock();
            var w = new BrightnessWriter(fake, Settle, delegate { return clock.Now; });
            w.Seed("A", 50);

            string seen = null;
            w.Failed += delegate(string id, string error) { seen = id + ":" + error; };

            fake.FailNext = true;
            w.Request("A", 30);
            clock.Advance(TimeSpan.FromSeconds(1));
            w.Advance();
            Check(fake.Writes.Count == 1, "the write was attempted once");
            Check(w.Error("A") != null, "the failure is recorded");
            Check(seen == "A:write failed", "the failure is surfaced to the UI (got " + seen + ")");
            Check(w.Applied("A") == 50, "a failed write leaves the applied value where it was");

            clock.Advance(TimeSpan.FromSeconds(5));
            w.Advance();
            Check(fake.Writes.Count == 1, "and it does not retry on its own");

            w.Request("A", 55);              // the user moves again: that is the retry
            clock.Advance(TimeSpan.FromSeconds(1));
            w.Advance();
            Check(fake.Writes.Count == 2 && fake.Writes[1] == "A=55", "the next move retries and lands");
            Check(w.Error("A") == null && w.Applied("A") == 55, "and the error is cleared on success");
        }

        private static void IndependentPerMonitor()
        {
            Console.WriteLine("monitors are debounced independently");
            var fake = new FakeController("A", "B");
            var clock = new FakeClock();
            var w = new BrightnessWriter(fake, Settle, delegate { return clock.Now; });
            w.Seed("A", 50);
            w.Seed("B", 50);

            w.Request("B", 70);
            clock.Advance(TimeSpan.FromSeconds(1));
            w.Advance();
            Check(fake.Writes.Count == 1 && fake.Writes[0] == "B=70", "only the moved monitor is written");
            Check(w.Applied("A") == 50, "the untouched monitor keeps its value");
        }

        private static void FlushLandsPending()
        {
            Console.WriteLine("closing the window lands a value still inside its settle window");
            var fake = new FakeController("A");
            var clock = new FakeClock();
            var w = new BrightnessWriter(fake, Settle, delegate { return clock.Now; });
            w.Seed("A", 50);

            w.Request("A", 25);
            w.Advance();
            Check(fake.Writes.Count == 0, "still settling, nothing written yet");
            w.Flush();
            Check(fake.Writes.Count == 1 && fake.Writes[0] == "A=25", "flush writes it now");
            Check(w.IsIdle, "nothing pending afterwards");
        }

        private static void ApplyIsNotDebounced()
        {
            Console.WriteLine("EXP17: a schedule apply lands at once - the settle window is for slider drags");
            var fake = new FakeController("A");
            var clock = new FakeClock();
            var w = new BrightnessWriter(fake, Settle, delegate { return clock.Now; });
            w.Seed("A", 50);

            w.Apply("A", 52);
            w.Advance();
            Check(fake.Writes.Count == 1 && fake.Writes[0] == "A=52",
                "it lands on the very next Advance, with no settle wait (got " + Dump(fake) + ")");

            // The cadence a 10 points/s ramp with a 2-point grain actually uses: 200 ms apart, inside
            // the 250 ms settle. A debounced write here would be swallowed and the move would jump.
            clock.Advance(TimeSpan.FromMilliseconds(200));
            w.Apply("A", 54);
            w.Advance();
            clock.Advance(TimeSpan.FromMilliseconds(200));
            w.Apply("A", 56);
            w.Advance();
            Check(fake.Writes.Count == 3 && w.Applied("A") == 56,
                "consecutive 200 ms steps are not coalesced away (got " + Dump(fake) + ")");

            w.Request("A", 40);              // a slider drag still waits for the settle
            w.Advance();
            Check(fake.Writes.Count == 3, "a slider Request is still debounced (got " + Dump(fake) + ")");
            clock.Advance(Settle);
            w.Advance();
            Check(fake.Writes.Count == 4 && fake.Writes[3] == "A=40", "and lands once it settles");
        }

        private static string Dump(FakeController fake)
        {
            return string.Join(",", fake.Writes.ToArray());
        }
    }
}
