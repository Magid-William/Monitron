// EXP09 - where the surface sits.
//
// The pure half of the flyout placement: what a work area and a docked taskbar mean for a window's
// corner. No WPF, no Win32, no window - so the rule is driven headlessly by position-test.ps1.
// SystemPlacement.cs is the only part that reads the operating system; PlacementApp.cs is the only
// part that touches a window.

using System;
using System.Globalization;

namespace Monitron.App
{
    /// <summary>Which side of its monitor the taskbar is docked on.</summary>
    public enum TaskbarEdge { Bottom, Top, Left, Right }

    /// <summary>A rectangle in whatever unit the caller is working in: pixels from the OS, DIPs once converted.</summary>
    public struct PlacementBox
    {
        public double Left;
        public double Top;
        public double Right;
        public double Bottom;

        public PlacementBox(double left, double top, double right, double bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }

        public static PlacementBox FromSize(double left, double top, double width, double height)
        {
            return new PlacementBox(left, top, left + width, top + height);
        }

        public static readonly PlacementBox Empty = new PlacementBox(0, 0, 0, 0);

        public double Width { get { return Right - Left; } }
        public double Height { get { return Bottom - Top; } }
        public bool IsEmpty { get { return Width <= 0 || Height <= 0; } }

        /// <summary>True when <paramref name="inner"/> lies inside this box, allowing a tolerance in either direction.</summary>
        public bool Contains(PlacementBox inner, double tolerance)
        {
            return inner.Left >= Left - tolerance && inner.Top >= Top - tolerance
                && inner.Right <= Right + tolerance && inner.Bottom <= Bottom + tolerance;
        }

        /// <summary>True when the two boxes share more than a tolerance of area (a shared edge is not an overlap).</summary>
        public bool Intersects(PlacementBox other, double tolerance)
        {
            bool apart = other.Left >= Right - tolerance || other.Right <= Left + tolerance
                || other.Top >= Bottom - tolerance || other.Bottom <= Top + tolerance;
            return !apart;
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
                Number(Left), Number(Top), Number(Right), Number(Bottom));
        }

        private static string Number(double value)
        {
            return Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);
        }

        public override string ToString() { return Describe(); }
    }

    /// <summary>Pixels &lt;-&gt; device-independent units, through a monitor's DPI.</summary>
    public static class Dips
    {
        public const double DefaultDpi = 96.0;

        /// <summary>The scale a DPI implies (125 % scaling is 120 dpi -&gt; 1.25).</summary>
        public static double Scale(double dpi)
        {
            return (dpi > 0 ? dpi : DefaultDpi) / DefaultDpi;
        }

        public static double FromPixels(double pixels, double dpi)
        {
            return pixels / Scale(dpi);
        }

        public static double ToPixels(double dips, double dpi)
        {
            return dips * Scale(dpi);
        }
    }

    /// <summary>One OS reading, reduced to what the placement rule needs: a work area in DIPs and an edge.</summary>
    public sealed class PlacementTarget
    {
        public PlacementBox WorkArea;   // DIPs, in virtual-screen coordinates (a secondary display can be negative)
        public TaskbarEdge Edge;
        public double Dpi;

        /// <summary>
        /// The pure parse of a reading: convert the work area to DIPs and work out which side the taskbar
        /// (or the shrunken work area) is on. <paramref name="forcedEdge"/> is the verification seam.
        /// </summary>
        public static PlacementTarget From(PlacementBox monitorPx, PlacementBox workAreaPx, PlacementBox taskbarPx,
            double dpi, TaskbarEdge? forcedEdge)
        {
            double scale = Dips.Scale(dpi);
            var target = new PlacementTarget
            {
                WorkArea = new PlacementBox(workAreaPx.Left / scale, workAreaPx.Top / scale,
                    workAreaPx.Right / scale, workAreaPx.Bottom / scale),
                Dpi = dpi > 0 ? dpi : Dips.DefaultDpi,
                Edge = forcedEdge ?? PlacementGeometry.EdgeOf(monitorPx, workAreaPx, taskbarPx),
            };
            return target;
        }
    }

    /// <summary>The rule itself.</summary>
    public static class PlacementGeometry
    {
        /// <summary>Which side of its monitor a taskbar, or the remainder of one, is docked on.</summary>
        public static TaskbarEdge EdgeOf(PlacementBox monitor, PlacementBox workArea, PlacementBox taskbar)
        {
            TaskbarEdge edge;
            // The work area is authoritative: it is exactly the space we must place inside, and the OS
            // has already taken the taskbar out of it.
            if (EdgeFromShrunk(monitor, workArea, out edge)) return edge;
            if (EdgeFromBar(monitor, taskbar, out edge)) return edge;
            return TaskbarEdge.Bottom;   // an auto-hidden taskbar leaves the whole monitor free; the clock is still bottom-right
        }

        /// <summary>The side the container was shrunken on, if any: that is where a bar was taken out of it.</summary>
        public static bool EdgeFromShrunk(PlacementBox monitor, PlacementBox shrunken, out TaskbarEdge edge)
        {
            const double Tolerance = 0.5;
            double top = shrunken.Top - monitor.Top;
            double bottom = monitor.Bottom - shrunken.Bottom;
            double left = shrunken.Left - monitor.Left;
            double right = monitor.Right - shrunken.Right;

            // The deepest inset wins; ties go to the bottom, which is where Windows puts it by default.
            edge = TaskbarEdge.Bottom;
            double best = Tolerance;
            if (bottom > best) { best = bottom; edge = TaskbarEdge.Bottom; }
            if (top > best) { best = top; edge = TaskbarEdge.Top; }
            if (left > best) { best = left; edge = TaskbarEdge.Left; }
            if (right > best) { best = right; edge = TaskbarEdge.Right; }
            return best > Tolerance;
        }

        /// <summary>A bar is docked where it touches the monitor's edge and spans the monitor across, not along.</summary>
        public static bool EdgeFromBar(PlacementBox monitor, PlacementBox bar, out TaskbarEdge edge)
        {
            const double Tolerance = 1.0;
            edge = TaskbarEdge.Bottom;
            if (bar.IsEmpty) return false;

            bool spansWidth = bar.Width >= monitor.Width - Tolerance;
            bool spansHeight = bar.Height >= monitor.Height - Tolerance;
            if (Math.Abs(monitor.Bottom - bar.Bottom) <= Tolerance && spansWidth) { edge = TaskbarEdge.Bottom; return true; }
            if (Math.Abs(bar.Top - monitor.Top) <= Tolerance && spansWidth) { edge = TaskbarEdge.Top; return true; }
            if (Math.Abs(bar.Left - monitor.Left) <= Tolerance && spansHeight) { edge = TaskbarEdge.Left; return true; }
            if (Math.Abs(monitor.Right - bar.Right) <= Tolerance && spansHeight) { edge = TaskbarEdge.Right; return true; }
            return false;
        }

        /// <summary>
        /// Where a window of this size goes: hug the corner the notification area sits in for that edge -
        /// bottom-right above a bottom taskbar, top-right under a top one, bottom-left beside a left one -
        /// with <paramref name="margin"/> of clearance, then clamped so it never crosses the work area's
        /// top or left (a window too big to fit overflows the far edge, never off the top-left).
        /// </summary>
        public static PlacementBox For(PlacementTarget target, double width, double height, double margin)
        {
            PlacementBox area = target.WorkArea;
            bool hugLeft = target.Edge == TaskbarEdge.Left;
            bool hugTop = target.Edge == TaskbarEdge.Top;

            double left = hugLeft ? area.Left + margin : area.Right - margin - width;
            double top = hugTop ? area.Top + margin : area.Bottom - margin - height;

            left = Clamp(left, area.Left, Math.Max(area.Left, area.Right - width));
            top = Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height));
            return PlacementBox.FromSize(left, top, width, height);
        }

        /// <summary>Move a box back inside a container, without resizing it.</summary>
        public static PlacementBox Clamp(PlacementBox box, PlacementBox container)
        {
            double width = box.Width, height = box.Height;
            double left = Clamp(box.Left, container.Left, Math.Max(container.Left, container.Right - width));
            double top = Clamp(box.Top, container.Top, Math.Max(container.Top, container.Bottom - height));
            return PlacementBox.FromSize(left, top, width, height);
        }

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>"bottom" | "top" | "left" | "right" for a report or a command line.</summary>
        public static string Name(TaskbarEdge edge) { return edge.ToString().ToLowerInvariant(); }

        public static bool TryParseEdge(string text, out TaskbarEdge edge)
        {
            foreach (TaskbarEdge candidate in new[] { TaskbarEdge.Bottom, TaskbarEdge.Top, TaskbarEdge.Left, TaskbarEdge.Right })
            {
                if (string.Equals(Name(candidate), text, StringComparison.OrdinalIgnoreCase)) { edge = candidate; return true; }
            }
            edge = TaskbarEdge.Bottom;
            return false;
        }

        /// <summary>"L,T,R,B" in whole pixels, as the command line and the report write it.</summary>
        public static bool TryParseBox(string text, out PlacementBox box)
        {
            box = PlacementBox.Empty;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split(',');
            if (parts.Length != 4) return false;
            var values = new double[4];
            for (int i = 0; i < 4; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
            }
            box = new PlacementBox(values[0], values[1], values[2], values[3]);
            return !box.IsEmpty;
        }
    }
}
