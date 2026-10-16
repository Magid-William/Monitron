// EXP14 - drives the display-set machine and asserts what a change does.
//
// The diff and the registry are pure C# (DisplaySet.cs), so this runs as a console exe built by
// display-test.ps1 with a fake controller, a fake clock and the real writer and scheduler: a display
// change is advanced by invoking Refresh() - never by sleeping - and the state it leaves (the rows to
// add and remove, the writer's and the schedule's id sets, and a close's restore) is asserted.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monitron.App
{
    /// <summary>A display set that only changes when the test changes it.</summary>
    internal sealed class FakeDisplays : IBrightnessController
    {
        public IList<MonitorInfo> Set = new List<MonitorInfo>();

        public IList<MonitorInfo> Detect() { return Set; }

        public int? Read(string id)
        {
            foreach (MonitorInfo monitor in Set) if (monitor.Id == id) return monitor.Current;
            return null;
        }

        public bool Write(string id, int value) { return true; }
    }

    internal sealed class TestClock
    {
        private DateTimeOffset _now;
        public TestClock(DateTimeOffset start) { _now = start; }
        public DateTimeOffset Now { get { return _now; } }
    }

    internal static class DisplayTests
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ok   " + what); }
            else { _failed++; Console.WriteLine("  FAIL " + what); }
        }

        private static MonitorInfo M(string id, int current)
        {
            return new MonitorInfo(id, id, MonitorKind.External, 0, current, 100, 0);
        }

        private static IList<MonitorInfo> Set(params MonitorInfo[] monitors)
        {
            var list = new List<MonitorInfo>();
            list.AddRange(monitors);
            return list;
        }

        private static List<string> Ids(IList<string> ids) { return new List<string>(ids); }

        private static int Main()
        {
            Console.WriteLine("DisplaySet - the diff rule");
            DiffAddsEverything();
            DiffRemovesAndKeeps();
            DiffAgreesChangesNothing();

            Console.WriteLine("DisplayRegistry - a detection reconciled");
            SeedAdoptsEveryone();
            RemovingADisplayForgetsItEverywhere();
            AddingADisplaySeedsAndObservesIt();
            AnEmptyDetectionIsNotTrusted();
            AReattachedDisplayStartsFresh();
            ACloseNeverRestoresAGoneDisplay();

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} passed, {1} failed", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static void DiffAddsEverything()
        {
            DisplayDiff diff = DisplaySet.Diff(new List<string>(), Set(M("A", 50), M("B", 60)));
            Check(diff.Added.Count == 2 && diff.Removed.Count == 0 && diff.Kept.Count == 0,
                "an empty screen gains every display");
            Check(diff.Any, "and that is a change");
        }

        private static void DiffRemovesAndKeeps()
        {
            DisplayDiff diff = DisplaySet.Diff(Ids(new[] { "A", "B" }), Set(M("B", 60)));
            Check(diff.Removed.Count == 1 && diff.Removed[0] == "A", "a display that left is removed");
            Check(diff.Kept.Count == 1 && diff.Kept[0].Id == "B", "and one that stayed is kept");
            Check(diff.Added.Count == 0, "nothing was added");
        }

        private static void DiffAgreesChangesNothing()
        {
            DisplayDiff diff = DisplaySet.Diff(Ids(new[] { "A", "B" }), Set(M("A", 50), M("B", 60)));
            Check(!diff.Any && diff.Kept.Count == 2, "a re-detection that agrees is not a change");
        }

        private static void SeedAdoptsEveryone()
        {
            var fake = new FakeDisplays { Set = Set(M("A", 30), M("B", 40)) };
            var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
            var writer = new BrightnessWriter(fake, TimeSpan.FromSeconds(1), delegate { return clock.Now; });
            var scheduler = new NightLightScheduler(new ScheduleSettings(), delegate { return clock.Now; },
                TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            var registry = new DisplayRegistry(fake, writer, scheduler);

            IList<MonitorInfo> monitors = registry.Seed();
            Check(monitors.Count == 2 && registry.Ids.Count == 2, "the launch seeds every display");
            Check(writer.Count == 2 && scheduler.Count == 2, "the writer and the schedule know both");
            Check(writer.Target("A") == 30 && scheduler.Baseline("B") == 40, "seeded from each display's own reading");
            Check(registry.Detections == 1, "and the displays were read once");
        }

        private static void RemovingADisplayForgetsItEverywhere()
        {
            var fake = new FakeDisplays { Set = Set(M("A", 30), M("B", 40)) };
            var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
            var writer = new BrightnessWriter(fake, TimeSpan.FromSeconds(1), delegate { return clock.Now; });
            var scheduler = new NightLightScheduler(new ScheduleSettings(), delegate { return clock.Now; },
                TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            var registry = new DisplayRegistry(fake, writer, scheduler);
            registry.Seed();

            fake.Set = Set(M("B", 40));   // the first display goes away
            DisplayDiff diff = registry.Refresh();

            Check(diff.Removed.Count == 1 && diff.Removed[0] == "A", "the departed display is reported");
            Check(diff.Added.Count == 0 && diff.Kept.Count == 1, "and only it");
            Check(registry.Ids.Count == 1 && registry.Ids[0] == "B", "the registry now holds one display");
            Check(writer.Count == 1, "the writer forgot the departed display");
            Check(scheduler.Count == 1, "and so did the schedule");
            Check(registry.Detections == 2, "the displays were read again");
        }

        private static void AddingADisplaySeedsAndObservesIt()
        {
            var fake = new FakeDisplays { Set = Set(M("A", 30)) };
            var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
            var writer = new BrightnessWriter(fake, TimeSpan.FromSeconds(1), delegate { return clock.Now; });
            var scheduler = new NightLightScheduler(new ScheduleSettings(), delegate { return clock.Now; },
                TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            var registry = new DisplayRegistry(fake, writer, scheduler);
            registry.Seed();

            fake.Set = Set(M("A", 30), M("B", 70));   // a second display appears
            DisplayDiff diff = registry.Refresh();

            Check(diff.Added.Count == 1 && diff.Added[0].Id == "B", "the arriving display is reported");
            Check(diff.Removed.Count == 0, "nothing was removed");
            Check(writer.Count == 2 && writer.Target("B") == 70, "the writer seeded it from its reading");
            Check(scheduler.Count == 2 && scheduler.Baseline("B") == 70, "and the schedule recorded what it would hand back");
            Check(writer.Target("A") == 30, "the display that stayed keeps its value");
        }

        private static void AnEmptyDetectionIsNotTrusted()
        {
            var fake = new FakeDisplays { Set = Set(M("A", 30), M("B", 40)) };
            var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
            var writer = new BrightnessWriter(fake, TimeSpan.FromSeconds(1), delegate { return clock.Now; });
            var scheduler = new NightLightScheduler(new ScheduleSettings(), delegate { return clock.Now; },
                TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            var registry = new DisplayRegistry(fake, writer, scheduler);
            registry.Seed();

            fake.Set = new List<MonitorInfo>();   // the moment during a mode switch with no active path
            DisplayDiff diff = registry.Refresh();

            Check(!diff.Any && diff.Removed.Count == 0, "an empty reading is not a change");
            Check(registry.Ids.Count == 2 && writer.Count == 2, "so the last good set stands");
        }

        private static void AReattachedDisplayStartsFresh()
        {
            var fake = new FakeDisplays { Set = Set(M("A", 30)) };
            var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
            var writer = new BrightnessWriter(fake, TimeSpan.FromSeconds(1), delegate { return clock.Now; });
            var scheduler = new NightLightScheduler(new ScheduleSettings(), delegate { return clock.Now; },
                TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            var registry = new DisplayRegistry(fake, writer, scheduler);
            registry.Seed();

            fake.Set = new List<MonitorInfo>();
            registry.Refresh();                 // no trusted change; A is still "on screen"
            fake.Set = Set(M("A", 88));         // A goes away and comes back at a new value - but the set agrees

            // The empty reading left A in place, so A is kept, not re-added: the row is never rebuilt.
            DisplayDiff diff = registry.Refresh();
            Check(!diff.Any, "a display that was never really gone is kept");

            // And when it really goes and really returns, it is a fresh adoption.
            fake.Set = Set(M("B", 10));
            registry.Refresh();
            fake.Set = Set(M("B", 10), M("A", 55));
            DisplayDiff back = registry.Refresh();
            Check(back.Added.Count == 1 && back.Added[0].Id == "A", "a display that returns is added again");
            Check(scheduler.Baseline("A") == 55, "with its new value recorded");
        }

        private static void ACloseNeverRestoresAGoneDisplay()
        {
            var settings = new ScheduleSettings();
            settings.TrySetTurnOn(10, 0, true);    // 22:00
            settings.TrySetTurnOff(6, 0, false);   // 06:00
            var clock = new TestClock(new DateTimeOffset(2026, 10, 15, 23, 0, 0, TimeSpan.Zero));
            var fake = new FakeDisplays { Set = Set(M("A", 80), M("B", 80)) };
            var writer = new BrightnessWriter(fake, TimeSpan.FromSeconds(1), delegate { return clock.Now; });
            var scheduler = new NightLightScheduler(settings, delegate { return clock.Now; },
                TimeSpan.Zero, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            var registry = new DisplayRegistry(fake, writer, scheduler);
            registry.Seed();

            int night = settings.Strength;   // 0..100 displays: the night level is the strength itself
            IList<NightWrite> dim = scheduler.Advance();
            Check(dim.Count == 2, "the night dims both displays");

            fake.Set = Set(M("B", 80));      // the first display is unplugged mid-night
            DisplayDiff diff = registry.Refresh();
            Check(diff.Removed.Count == 1 && diff.Removed[0] == "A", "the unplugged display is reported");

            IList<NightWrite> restore = scheduler.Restore();
            Check(restore.Count == 1 && restore[0].Id == "B", "the close gives the value back to B alone");
            Check(restore[0].Value == 80, "and it is B's own day value");
            Check(writer.Count == 1, "the writer holds no entry for the display that went away");
            Check(night == 38, "the default strength is what the night used");
        }
    }
}
