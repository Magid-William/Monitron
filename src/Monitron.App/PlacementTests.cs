// EXP09 - drives the placement model and asserts the corner it produces.
//
// No test framework, no WPF, no OS: Placement.cs is pure, so this runs as a console exe (a winexe
// cannot print) built by position-test.ps1, and deliberately NOT part of build.ps1 - it has its own
// Main and must not race with Program.Main.
//
// The rectangles used are the ones this machine reports (Windows 11 25H2: a 1920x1080 primary at
// 125 %, a 2560x1440 secondary at a negative origin, each with its own taskbar), so the arithmetic
// being asserted is the arithmetic the app will do here.

using System;

namespace Monitron.App
{
    internal static class PlacementTests
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ok   " + what); }
            else { _failed++; Console.WriteLine("  FAIL " + what); }
        }

        private static bool Near(double a, double b)
        {
            return Math.Abs(a - b) < 0.0001;
        }

        private static bool Box(PlacementBox box, double l, double t, double r, double b)
        {
            return Near(box.Left, l) && Near(box.Top, t) && Near(box.Right, r) && Near(box.Bottom, b);
        }

        // this machine
        private static readonly PlacementBox PrimaryMonitor = new PlacementBox(0, 0, 1920, 1080);
        private static readonly PlacementBox PrimaryWork = new PlacementBox(0, 0, 1920, 1020);
        private static readonly PlacementBox PrimaryBar = new PlacementBox(0, 1020, 1920, 1080);
        private static readonly PlacementBox SecondMonitor = new PlacementBox(-2560, -846, 0, 594);
        private static readonly PlacementBox SecondWork = new PlacementBox(-2560, -846, 0, 534);
        private static readonly PlacementBox SecondBar = new PlacementBox(-2560, 534, 0, 594);

        private static int Main()
        {
            Console.WriteLine("Position - pixels and DIPs");

            Check(Near(Dips.Scale(96), 1.0), "96 dpi is 1.0x");
            Check(Near(Dips.Scale(120), 1.25), "120 dpi is 1.25x (this machine)");
            Check(Near(Dips.Scale(144), 1.5), "144 dpi is 1.5x");
            Check(Near(Dips.Scale(192), 2.0), "192 dpi is 2.0x");
            Check(Near(Dips.Scale(0), 1.0), "an unknown dpi is treated as 96, not as a division by zero");
            Check(Near(Dips.FromPixels(1920, 120), 1536), "1920 px at 125 % is 1536 DIP");
            Check(Near(Dips.FromPixels(1080, 120), 864), "1080 px at 125 % is 864 DIP");
            Check(Near(Dips.ToPixels(1536, 120), 1920), "and back again");
            Check(Near(Dips.ToPixels(8, 120), 10), "the 8 DIP margin is 10 px at 125 %");

            Console.WriteLine("which side the taskbar is on - from the work area");
            TaskbarEdge edge;
            Check(PlacementGeometry.EdgeFromShrunk(PrimaryMonitor, PrimaryWork, out edge) && edge == TaskbarEdge.Bottom,
                "a work area 60 px shorter at the bottom is a bottom taskbar");
            Check(PlacementGeometry.EdgeFromShrunk(PrimaryMonitor, new PlacementBox(0, 60, 1920, 1080), out edge) && edge == TaskbarEdge.Top,
                "shrunken at the top is a top taskbar");
            Check(PlacementGeometry.EdgeFromShrunk(PrimaryMonitor, new PlacementBox(60, 0, 1920, 1080), out edge) && edge == TaskbarEdge.Left,
                "shrunken on the left is a left taskbar");
            Check(PlacementGeometry.EdgeFromShrunk(PrimaryMonitor, new PlacementBox(0, 0, 1860, 1080), out edge) && edge == TaskbarEdge.Right,
                "shrunken on the right is a right taskbar");
            Check(!PlacementGeometry.EdgeFromShrunk(PrimaryMonitor, PrimaryMonitor, out edge),
                "an auto-hidden taskbar leaves the whole monitor free, so there is no edge to read");
            Check(PlacementGeometry.EdgeFromShrunk(SecondMonitor, SecondWork, out edge) && edge == TaskbarEdge.Bottom,
                "the secondary display's own work area says bottom too");

            Console.WriteLine("which side the taskbar is on - from the taskbar's own rectangle");
            Check(PlacementGeometry.EdgeFromBar(PrimaryMonitor, PrimaryBar, out edge) && edge == TaskbarEdge.Bottom,
                "(0,1020)-(1920,1080) touches the bottom and spans the width");
            Check(PlacementGeometry.EdgeFromBar(PrimaryMonitor, new PlacementBox(0, 0, 1920, 60), out edge) && edge == TaskbarEdge.Top,
                "the same bar at the top is a top taskbar");
            Check(PlacementGeometry.EdgeFromBar(PrimaryMonitor, new PlacementBox(0, 0, 60, 1080), out edge) && edge == TaskbarEdge.Left,
                "60 x 1080 down the left is a left taskbar");
            Check(PlacementGeometry.EdgeFromBar(PrimaryMonitor, new PlacementBox(1860, 0, 1920, 1080), out edge) && edge == TaskbarEdge.Right,
                "60 x 1080 down the right is a right taskbar");
            Check(!PlacementGeometry.EdgeFromBar(PrimaryMonitor, PlacementBox.Empty, out edge), "no taskbar, no edge");
            Check(PlacementGeometry.EdgeOf(PrimaryMonitor, PrimaryMonitor, PrimaryBar) == TaskbarEdge.Bottom,
                "with the work area untouched the bar still decides");
            Check(PlacementGeometry.EdgeOf(PrimaryMonitor, new PlacementBox(0, 60, 1920, 1080), PrimaryBar) == TaskbarEdge.Top,
                "when they disagree the work area wins - it is the space we must place inside");
            Check(PlacementGeometry.EdgeOf(PrimaryMonitor, PrimaryMonitor, PlacementBox.Empty) == TaskbarEdge.Bottom,
                "nothing readable at all defaults to the bottom, where the clock is");

            Console.WriteLine("one reading, reduced to DIPs");
            PlacementTarget primary = PlacementTarget.From(PrimaryMonitor, PrimaryWork, PrimaryBar, 120, null);
            Check(Box(primary.WorkArea, 0, 0, 1536, 816), "the primary work area is (0,0,1536,816) in DIPs");
            Check(Near(primary.Dpi, 120), "and it keeps the monitor's dpi");
            Check(primary.Edge == TaskbarEdge.Bottom, "and the edge it read");
            PlacementTarget second = PlacementTarget.From(SecondMonitor, SecondWork, SecondBar, 120, null);
            Check(Box(second.WorkArea, -2048, -676.8, 0, 427.2),
                "the secondary keeps its negative origin in DIPs (" + second.WorkArea.Describe() + ")");
            Check(PlacementTarget.From(PrimaryMonitor, PrimaryWork, PrimaryBar, 0, null).Dpi == 96,
                "an unreadable dpi becomes 96");
            Check(PlacementTarget.From(PrimaryMonitor, PrimaryWork, PrimaryBar, 120, TaskbarEdge.Top).Edge == TaskbarEdge.Top,
                "a forced edge beats what the rectangles say (the verification seam)");

            Console.WriteLine("where the window goes");
            PlacementBox box = PlacementGeometry.For(primary, 400, 312, 8);
            Check(Box(box, 1128, 496, 1528, 808), "400x312 in the primary corner is (1128,496)-(1528,808) (" + box.Describe() + ")");
            Check(Near(Dips.ToPixels(box.Right, 120), 1910), "its right edge is 8 DIP (10 px) in from the screen's right edge");
            Check(Near(Dips.ToPixels(box.Bottom, 120), 1010), "and its bottom edge is 8 DIP above the taskbar's top (1010 < 1020)");
            Check(!box.Intersects(PrimaryBar, 0), "so the window does not touch the taskbar");
            Check(PrimaryWork.Contains(
                new PlacementBox(Dips.ToPixels(box.Left, 120), Dips.ToPixels(box.Top, 120),
                    Dips.ToPixels(box.Right, 120), Dips.ToPixels(box.Bottom, 120)), 0),
                "and it is inside the work area, in the OS's own pixels");

            Console.WriteLine("the corner follows the edge");
            Check(Near(PlacementGeometry.For(PlacementTarget.From(PrimaryMonitor, PrimaryWork, PrimaryBar, 120, TaskbarEdge.Top), 400, 312, 8).Top, 8),
                "a top taskbar hugs the top: 8 DIP down the work area");
            Check(Box(PlacementGeometry.For(PlacementTarget.From(PrimaryMonitor, PrimaryWork, PrimaryBar, 120, TaskbarEdge.Left), 400, 312, 8),
                8, 496, 408, 808), "a left taskbar hugs the bottom-left instead");
            Check(Box(PlacementGeometry.For(PlacementTarget.From(PrimaryMonitor, PrimaryWork, PrimaryBar, 120, TaskbarEdge.Right), 400, 312, 8),
                1128, 496, 1528, 808), "a right taskbar keeps the bottom-right corner");
            Check(Near(PlacementGeometry.For(primary, 400, 312, 0).Right, 1536), "margin 0 puts the right edge exactly on the work area's");
            Check(Near(PlacementGeometry.For(primary, 400, 312, 0).Bottom, 816), "and the bottom edge exactly on it");

            Console.WriteLine("a window too big for the work area");
            Check(Box(PlacementGeometry.For(primary, 1600, 100, 8), 0, 708, 1600, 808),
                "1600 wide cannot sit 8 DIP in, so it is clamped to the left edge - not off the screen");
            Check(Box(PlacementGeometry.For(primary, 2000, 900, 8), 0, 0, 2000, 900),
                "a window bigger than the whole work area overflows the far edge, never the near one");
            Check(Box(PlacementGeometry.For(primary, 1536, 816, 8), 0, 0, 1536, 816),
                "an exact fit loses only the margin, not the window");

            Console.WriteLine("keeping a window on screen (the settings window)");
            Check(Box(PlacementGeometry.Clamp(new PlacementBox(1138, 448, 1518, 848), primary.WorkArea), 1138, 416, 1518, 816),
                "a 380x400 settings window centred on the owner is pulled up 32 DIP into the work area");
            Check(Box(PlacementGeometry.Clamp(new PlacementBox(100, 100, 480, 500), primary.WorkArea), 100, 100, 480, 500),
                "one that already fits is left alone");
            Check(Box(PlacementGeometry.Clamp(new PlacementBox(-50, -50, 350, 350), primary.WorkArea), 0, 0, 400, 400),
                "one off the top-left is pulled in, not resized");
            Check(Box(PlacementGeometry.Clamp(new PlacementBox(0, 0, 2000, 900), primary.WorkArea), 0, 0, 2000, 900),
                "one bigger than the work area stays where it is");

            Console.WriteLine("which monitor the surface follows (EXP15)");
            Check(PlacementChoice.Choose(true, false) == PlacementAnchor.Tray,
                "a readable tray wins - the surface follows the notification area");
            Check(PlacementChoice.Choose(true, true) == PlacementAnchor.Window,
                "unless the seam asks for the window (the pre-EXP15 baseline)");
            Check(PlacementChoice.Choose(false, false) == PlacementAnchor.Window,
                "with no taskbar to read there is nothing to follow, so the window decides");
            Check(PlacementChoice.Name(PlacementAnchor.Tray) == "tray" && PlacementChoice.Name(PlacementAnchor.Window) == "window",
                "the two anchors have their report spelling");
            PlacementAnchor anchorChoice;
            Check(PlacementChoice.TryParseAnchor("tray", out anchorChoice) && anchorChoice == PlacementAnchor.Tray, "'tray' parses");
            Check(PlacementChoice.TryParseAnchor("Window", out anchorChoice) && anchorChoice == PlacementAnchor.Window, "and 'Window' is case-insensitive");
            Check(!PlacementChoice.TryParseAnchor("screen", out anchorChoice), "anything else does not");

            Console.WriteLine("the command-line text");
            Check(PlacementGeometry.Name(TaskbarEdge.Left) == "left", "Name(left) is 'left'");
            Check(PlacementGeometry.TryParseEdge("bottom", out edge) && edge == TaskbarEdge.Bottom, "'bottom' parses");
            Check(PlacementGeometry.TryParseEdge("Top", out edge) && edge == TaskbarEdge.Top, "and it is case-insensitive");
            Check(PlacementGeometry.TryParseEdge("right", out edge) && edge == TaskbarEdge.Right, "'right' parses");
            Check(!PlacementGeometry.TryParseEdge("sideways", out edge), "'sideways' does not");
            PlacementBox parsed;
            Check(PlacementGeometry.TryParseBox("0,1020,1920,1080", out parsed) && Box(parsed, 0, 1020, 1920, 1080),
                "'0,1020,1920,1080' parses into a rectangle");
            Check(!PlacementGeometry.TryParseBox("0,1020,1920", out parsed), "three numbers do not");
            Check(!PlacementGeometry.TryParseBox("a,b,c,d", out parsed), "letters do not");
            Check(!PlacementGeometry.TryParseBox("0,0,0,0", out parsed), "an empty rectangle does not");
            Check(!PlacementGeometry.TryParseBox("", out parsed), "nothing does not");
            Check(new PlacementBox(0, 0, 1536, 816).Describe() == "0,0,1536,816", "a whole rectangle describes as whole numbers");
            Check(new PlacementBox(-2048, -676.8, 0, 427.2).Describe() == "-2048,-676.8,0,427.2",
                "and fractions are kept, because a scaled monitor produces them");

            Console.WriteLine("the relations the verifier asserts on the real window");
            Check(box.Intersects(box, 0), "a box overlaps itself");
            Check(!new PlacementBox(0, 0, 10, 10).Intersects(new PlacementBox(10, 0, 20, 10), 0),
                "boxes sharing only an edge do not overlap");
            Check(new PlacementBox(0, 0, 10, 10).Intersects(new PlacementBox(9, 0, 20, 10), 0),
                "one pixel of overlap is overlap");
            Check(PrimaryWork.Contains(box, 0), "the placed box is inside the work area with no tolerance at all");
            Check(!PrimaryBar.Contains(box, 0), "and it is not inside the taskbar");

            Console.WriteLine();
            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
