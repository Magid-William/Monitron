// EXP05 - the actuator seam and the debounce machine.
//
// This file holds NO P/Invoke and NO WMI on purpose: it is the part that decides *when* and
// *what* to write, so it can be compiled into the headless test (writer-test.ps1) and driven
// with a fake controller and an injected clock. The hardware lives in BrightnessController.cs.
//
// C# 5 only - the in-box csc.exe (.NET Framework 4.0 compiler) is what builds this.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monitorn.App
{
    internal enum MonitorKind
    {
        External,   // DDC/CI over dxva2
        Internal,   // the WMI brightness API
    }

    /// <summary>One display we can control: identity, name, kind and the last value read.</summary>
    internal sealed class MonitorInfo
    {
        public MonitorInfo(string id, string name, MonitorKind kind, int min, int current, int max, int index)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Min = min;
            Max = max;
            Current = current;
            Index = index;
        }

        /// <summary>Stable key the controller rounds-trips; opaque to the UI.</summary>
        public string Id { get; private set; }
        public string Name { get; private set; }
        public MonitorKind Kind { get; private set; }
        public int Min { get; private set; }
        public int Max { get; private set; }
        public int Current { get; private set; }
        public int Index { get; private set; }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Name) ? "Display " + Index.ToString(CultureInfo.InvariantCulture) : Name; }
        }
    }

    /// <summary>The actuator seam EXP02 reserved: enumerate, read, write. No UI, no clock.</summary>
    internal interface IBrightnessController
    {
        /// <summary>The displays we may control (active rendering targets), already read once.</summary>
        IList<MonitorInfo> Detect();

        /// <summary>Null when the monitor cannot be read right now.</summary>
        int? Read(string id);

        bool Write(string id, int value);
    }

    /// <summary>
    /// Coalesces a stream of slider values into at most one write per settle window, keeping the
    /// last value the user asked for. Pure state plus transitions: the caller supplies the clock
    /// (`now`) and the timer that calls `Advance`, so a test drives it with no real time at all.
    ///
    /// Rules:
    ///   * Request() only moves the target and restarts the settle deadline - it never writes.
    ///   * Advance() writes a monitor once `now` has passed its deadline and the target differs
    ///     from what is applied; writing the same value again is a no-op.
    ///   * A failed write is recorded and NOT retried until the next Request (the user's next move),
    ///     so a dead monitor cannot spin.
    /// </summary>
    internal sealed class BrightnessWriter
    {
        private sealed class Entry
        {
            public int Target;
            public int Applied;
            public DateTimeOffset Due;
            public string Error;
        }

        private readonly IBrightnessController _controller;
        private readonly TimeSpan _settle;
        private readonly Func<DateTimeOffset> _now;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        public BrightnessWriter(IBrightnessController controller, TimeSpan settle, Func<DateTimeOffset> now)
        {
            _controller = controller;
            _settle = settle;
            _now = now;
        }

        /// <summary>Raised after a settled value was written to the monitor.</summary>
        public event Action<string, int> Written;

        /// <summary>Raised when a write was refused or failed; the monitor keeps its last value.</summary>
        public event Action<string, string> Failed;

        /// <summary>Seeds a monitor's applied value from its detected state so an unmoved slider never writes.</summary>
        public void Seed(string id, int applied)
        {
            Entry e = Get(id);
            e.Target = applied;
            e.Applied = applied;
        }

        /// <summary>The user moved a slider. Records the latest value and restarts the settle window.</summary>
        public void Request(string id, int value)
        {
            Entry e = Get(id);
            e.Target = value;
            e.Due = _now() + _settle;
            e.Error = null;   // a new move is the retry
        }

        /// <summary>Writes every monitor whose value has settled. Returns how many writes were made.</summary>
        public int Advance()
        {
            int writes = 0;
            DateTimeOffset now = _now();
            foreach (KeyValuePair<string, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (e.Error != null) continue;              // failed: surfaced, not retried
                if (e.Target == e.Applied) continue;         // already where it should be
                if (now < e.Due) continue;                   // still settling
                if (_controller.Write(kv.Key, e.Target))
                {
                    e.Applied = e.Target;
                    writes++;
                    if (Written != null) Written(kv.Key, e.Applied);
                }
                else
                {
                    e.Error = "write failed";
                    if (Failed != null) Failed(kv.Key, e.Error);
                }
            }
            return writes;
        }

        /// <summary>Applies anything still pending right now - used when the window closes.</summary>
        public int Flush()
        {
            int writes = 0;
            foreach (KeyValuePair<string, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (e.Error != null || e.Target == e.Applied) continue;
                if (_controller.Write(kv.Key, e.Target))
                {
                    e.Applied = e.Target;
                    writes++;
                    if (Written != null) Written(kv.Key, e.Applied);
                }
                else
                {
                    e.Error = "write failed";
                    if (Failed != null) Failed(kv.Key, e.Error);
                }
            }
            return writes;
        }

        public int Target(string id) { return Get(id).Target; }
        public int Applied(string id) { return Get(id).Applied; }
        public string Error(string id) { return Get(id).Error; }

        /// <summary>True when nothing is waiting to be written (all settled or failed).</summary>
        public bool IsIdle
        {
            get
            {
                foreach (KeyValuePair<string, Entry> kv in _entries)
                {
                    Entry e = kv.Value;
                    if (e.Error == null && e.Target != e.Applied) return false;
                }
                return true;
            }
        }

        private Entry Get(string id)
        {
            Entry e;
            if (!_entries.TryGetValue(id, out e))
            {
                e = new Entry();
                _entries[id] = e;
            }
            return e;
        }
    }
}
