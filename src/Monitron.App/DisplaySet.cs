// EXP14 - what a fresh detection means for the displays already on screen.
//
// The app read its displays once, at launch. This is the machine that lets it read them again: given
// the ids it is currently showing and a freshly detected list, it says which displays appeared, which
// disappeared, and which stayed. Pure - no WPF, no P/Invoke, no clock - so display-test.ps1 drives it
// with a fake controller and asserts the diff and the two machines it prunes.
//
// C# 5 only - the in-box csc.exe is what builds this.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monitron.App
{
    /// <summary>The change a detection made: which displays to add a row for, which to drop.</summary>
    internal sealed class DisplayDiff
    {
        public readonly List<MonitorInfo> Added = new List<MonitorInfo>();
        public readonly List<string> Removed = new List<string>();
        public readonly List<MonitorInfo> Kept = new List<MonitorInfo>();

        /// <summary>True when the set actually changed - a re-detection that agrees changes nothing.</summary>
        public bool Any { get { return Added.Count > 0 || Removed.Count > 0; } }
    }

    /// <summary>The diff rule itself: the current ids against the freshly detected list.</summary>
    internal static class DisplaySet
    {
        public static DisplayDiff Diff(IList<string> current, IList<MonitorInfo> found)
        {
            var diff = new DisplayDiff();
            var present = new List<string>();

            foreach (MonitorInfo monitor in found)
            {
                string id = monitor.Id;
                present.Add(id);
                if (Contains(current, id)) diff.Kept.Add(monitor);
                else diff.Added.Add(monitor);
            }
            foreach (string id in current)
            {
                if (!Contains(present, id)) diff.Removed.Add(id);
            }
            return diff;
        }

        private static bool Contains(IList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Owns the app's picture of the displays: it detects, keeps the ids it is showing, and reconciles
    /// a detection into what the writer, the scheduler and the surface must do. The writer and the
    /// scheduler are handed in, so this is the one place that seeds an arriving display and forgets a
    /// departed one - and the place a headless test drives with a fake controller.
    /// </summary>
    internal sealed class DisplayRegistry
    {
        private readonly IBrightnessController _controller;
        private readonly BrightnessWriter _writer;
        private readonly NightLightScheduler _scheduler;
        private readonly List<string> _ids = new List<string>();

        public DisplayRegistry(IBrightnessController controller, BrightnessWriter writer, NightLightScheduler scheduler)
        {
            _controller = controller;
            _writer = writer;
            _scheduler = scheduler;
        }

        /// <summary>The displays on screen right now, in view order.</summary>
        public IList<string> Ids { get { return _ids; } }

        /// <summary>How many times the displays have been read - evidence for a report.</summary>
        public int Detections { get; private set; }

        /// <summary>The launch's detection: adopt every display - a row, a seeded writer, an observed schedule.</summary>
        public IList<MonitorInfo> Seed()
        {
            IList<MonitorInfo> found = Detect();
            Adopt(found);
            return found;
        }

        /// <summary>
        /// Read the displays again and reconcile. Removed displays are forgotten by the writer and the
        /// scheduler (so neither keeps writing to a monitor that has gone); added ones are seeded and
        /// observed like a launch. Returns what the surface must add and remove.
        /// </summary>
        public DisplayDiff Refresh()
        {
            IList<MonitorInfo> found = Detect();

            // Changing the projection has a moment with no active path. An empty reading is not
            // trusted: the last good set stands rather than every row disappearing and coming back.
            if (found.Count == 0 && _ids.Count > 0) return new DisplayDiff();

            DisplayDiff diff = DisplaySet.Diff(_ids, found);
            foreach (string id in diff.Removed)
            {
                _writer.Forget(id);
                _scheduler.Forget(id);
            }
            foreach (MonitorInfo monitor in diff.Added) Adopt(monitor);
            Rebase(found);
            return diff;
        }

        private void Adopt(IList<MonitorInfo> found)
        {
            foreach (MonitorInfo monitor in found) Adopt(monitor);
            Rebase(found);
        }

        private void Adopt(MonitorInfo monitor)
        {
            _writer.Seed(monitor.Id, monitor.Current);
            _scheduler.Observe(monitor.Id, monitor.Min, monitor.Max, monitor.Current);
        }

        private void Rebase(IList<MonitorInfo> found)
        {
            _ids.Clear();
            foreach (MonitorInfo monitor in found) _ids.Add(monitor.Id);
        }

        private IList<MonitorInfo> Detect()
        {
            Detections++;
            try { return _controller.Detect(); }
            catch { return new List<MonitorInfo>(); }
        }
    }

    /// <summary>
    /// A display set as the --monitors seam writes it: items separated by ';', each `id|name|current`
    /// (name and current optional). The id keeps the app's own spelling (`DDC:<device path>` /
    /// `WMI:<InstanceName>`), so a row keeps its identity across a scripted change and the verifier
    /// sees a display appear or disappear rather than every row churn.
    /// </summary>
    internal static class MonitorSpec
    {
        public static IList<MonitorInfo> Parse(string text)
        {
            var list = new List<MonitorInfo>();
            if (string.IsNullOrEmpty(text)) return list;

            int index = 0;
            foreach (string raw in text.Split(';'))
            {
                string item = raw.Trim();
                if (item.Length == 0) continue;

                string[] parts = item.Split('|');
                string id = parts[0].Trim();
                if (id.Length == 0) continue;
                string name = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : id;

                int current = 50;
                if (parts.Length > 2) int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out current);

                MonitorKind kind = id.StartsWith("WMI:", StringComparison.OrdinalIgnoreCase)
                    ? MonitorKind.Internal : MonitorKind.External;

                index++;
                list.Add(new MonitorInfo(id, name, kind, 0, current, 100, index));
            }
            return list;
        }
    }

    /// <summary>
    /// The IBrightnessController the --monitors seam stands in with: it answers a scripted set, so a
    /// display change can be driven without touching the user's real displays. Read and Write touch
    /// nothing - a scripted run never moves a monitor.
    /// </summary>
    internal sealed class ScriptedController : IBrightnessController
    {
        private readonly List<string> _writes = new List<string>();
        private IList<MonitorInfo> _monitors;

        public ScriptedController(string spec) { _monitors = MonitorSpec.Parse(spec); }

        /// <summary>The set a later detection will answer - what a scripted display change switches to.</summary>
        public void Set(string spec) { _monitors = MonitorSpec.Parse(spec); }

        public IList<MonitorInfo> Detect() { return _monitors; }

        public int? Read(string id)
        {
            foreach (MonitorInfo monitor in _monitors) if (monitor.Id == id) return monitor.Current;
            return null;
        }

        public bool Write(string id, int value)
        {
            _writes.Add(id + "=" + value.ToString(CultureInfo.InvariantCulture));
            return true;
        }
    }
}
