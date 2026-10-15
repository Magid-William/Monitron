// EXP08 - the verification seam: what the app actually paints, as numbers.
//
// UI Automation can drive a window and read the state it reports, but it cannot see color - so a
// theme claim needs a different witness. This renders a built, shown window to a RenderTargetBitmap
// and samples named regions of the real visual tree: the window background, a row card, the
// slider's band, a check or radio box, a field's face, a button's face. Each line records the
// region, its average color, and the share of light and dark pixels in it.
//
// The same report is what measures the gap the experiment is about: run with --theme-no-templates
// and the OS-styled controls still report light, because their look is a template the palette
// cannot reach. `rendered=ok|blank|not-laid-out` says whether there was anything to sample.
//
// The report is written atomically, at launch and again on every theme change, so a verifier can
// poll it and watch a switch land. C# 5 only - the in-box csc.exe is what builds this.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Monitron.App
{
    internal static class ThemeReport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Shows both windows, reports them, and stays up long enough to watch a switch.</summary>
        public static void Run(Application app, ThemeState theme, string directory, Window surface,
            Window settings, double seconds)
        {
            Directory.CreateDirectory(directory);

            Window[] windows = new Window[] { surface, settings };
            string[] names = new string[] { "surface", "settings" };

            foreach (Window w in windows)
            {
                w.ShowInTaskbar = false;
                w.Show();
                w.UpdateLayout();
            }

            Write(theme, directory, names, windows);
            // A switch lands on this same dispatcher turn, and the re-templated controls are only
            // rebuilt and painted afterwards - so a report written now would show the old frame.
            // ContextIdle is below Render and Layout, so by then the frame exists.
            theme.Changed += delegate
            {
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                    (Action)delegate { Write(theme, directory, names, windows); });
            };

            DispatcherTimer quit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            quit.Tick += delegate
            {
                quit.Stop();
                foreach (Window w in windows) { try { w.Close(); } catch { } }
                app.Shutdown();
            };
            quit.Start();

            app.Run();
        }

        private static void Write(ThemeState theme, string directory, string[] names, Window[] windows)
        {
            for (int i = 0; i < windows.Length; i++) WriteOne(theme, directory, names[i], windows[i]);
        }

        private static void WriteOne(ThemeState theme, string directory, string name, Window window)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("theme.report=1").Append('\n');
            sb.Append("window=").Append(name).Append('\n');
            sb.Append("mode=").Append(ThemeChoice.Name(theme.Mode)).Append('\n');
            sb.Append("preference=").Append(ThemeChoice.Name(theme.Preference)).Append('\n');
            sb.Append("osLight=").Append(theme.OsLight ? "true" : "false").Append('\n');
            sb.Append("templates=").Append(theme.Templates ? "true" : "false").Append('\n');

            ThemePalette p = ThemePalette.For(theme.Mode);
            sb.Append("palette.windowBg=").Append(ThemePalette.Hex(p.WindowBg)).Append('\n');
            sb.Append("palette.ink=").Append(ThemePalette.Hex(p.Ink)).Append('\n');
            sb.Append("palette.rowFill=").Append(ThemePalette.Hex(p.RowFill)).Append('\n');
            sb.Append("palette.controlFill=").Append(ThemePalette.Hex(p.ControlFill)).Append('\n');

            bool? dark = SystemTheme.IsTitleBarDark(window);
            sb.Append("titlebar=").Append(!dark.HasValue ? "unknown" : (dark.Value ? "dark" : "light")).Append('\n');

            int w = (int)Math.Ceiling(window.ActualWidth);
            int h = (int)Math.Ceiling(window.ActualHeight);
            sb.Append("size=").Append(w).Append('x').Append(h).Append('\n');

            if (w < 10 || h < 10)
            {
                sb.Append("rendered=not-laid-out").Append('\n');
                Persist(System.IO.Path.Combine(directory, name + ".txt"), sb.ToString());
                return;
            }

            RenderTargetBitmap bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);

            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            bitmap.CopyPixels(pixels, stride, 0);

            sb.Append("rendered=").Append(Distinct(pixels, stride, w, h) < 2 ? "blank" : "ok").Append('\n');
            Probes(sb, window, theme, pixels, stride, w, h);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = new FileStream(System.IO.Path.Combine(directory, name + ".png"), FileMode.Create))
                encoder.Save(file);

            Persist(System.IO.Path.Combine(directory, name + ".txt"), sb.ToString());
        }

        /// <summary>How many different colors the frame holds: one means nothing was drawn.</summary>
        private static int Distinct(byte[] pixels, int stride, int w, int h)
        {
            HashSet<int> seen = new HashSet<int>();
            for (int y = 0; y < h; y += 3)
                for (int x = 0; x < w; x += 3)
                {
                    int i = y * stride + x * 4;
                    seen.Add((pixels[i] << 16) | (pixels[i + 1] << 8) | pixels[i + 2]);
                    if (seen.Count > 8) return seen.Count;
                }
            return seen.Count;
        }

        private static void Probes(StringBuilder sb, Window window, ThemeState theme,
            byte[] pixels, int stride, int w, int h)
        {
            FrameworkElement content = window.Content as FrameworkElement;
            if (content != null)
            {
                // A point just outside the content's margin: the window's own background.
                Rect root = Bounds(content, window);
                if (root.Width > 0) Sample(sb, "bg", pixels, stride, w, h, new Rect(root.Left - 9, root.Top - 8, 6, 6));
            }

            Border row = First<Border>(window);
            if (row != null) { Rect b = Bounds(row, window); Sample(sb, "row", pixels, stride, w, h, new Rect(b.Left + 5, b.Top + 5, 8, 8)); }

            Slider slider = First<Slider>(window);
            if (slider != null)
            {
                Rect b = Bounds(slider, window);
                Sample(sb, "slider", pixels, stride, w, h, Band(b, 1.0));
                // The unfilled side, which is where a brush swap shows most plainly: the OS track
                // is a light line, ours is ControlEdge. Read from the Slider's own value, so the
                // region is wherever the thumb actually is.
                Sample(sb, "sliderRest", pixels, stride, w, h, RestOfBand(b, slider));
            }

            CheckBox check = First<CheckBox>(window);
            if (check != null) { Rect b = Bounds(check, window); Sample(sb, "check", pixels, stride, w, h, LeftBox(b)); }

            RadioButton radio = First<RadioButton>(window);
            if (radio != null) { Rect b = Bounds(radio, window); Sample(sb, "radio", pixels, stride, w, h, LeftBox(b)); }

            TextBox edit = First<TextBox>(window);
            if (edit != null) { Rect b = Bounds(edit, window); Sample(sb, "edit", pixels, stride, w, h, Centre(b)); }

            Button button = First<Button>(window);
            if (button != null) { Rect b = Bounds(button, window); Sample(sb, "button", pixels, stride, w, h, Centre(b)); }

            // Not a claim, a diagnostic: whether our brushes and our template actually reached
            // these controls. It separates "the palette was applied" from "and the template took".
            BrushLine(sb, "edit", edit, theme, theme.Brushes.TextBox);
            BrushLine(sb, "button", button, theme, theme.Brushes.ButtonFace);

            TextBlock text = First<TextBlock>(window);
            if (text != null) { Rect b = Bounds(text, window); Sample(sb, "text", pixels, stride, w, h, b); }
        }

        private static void BrushLine(StringBuilder sb, string label, Control control, ThemeState theme, object ours)
        {
            if (control == null) return;
            sb.Append("elem.").Append(label).Append(".paint back=").Append(Fill(control.Background))
              .Append(" ink=").Append(Fill(control.Foreground))
              .Append(" ourTemplate=").Append(object.ReferenceEquals(control.Template, ours) ? "true" : "false")
              .Append('\n');
        }

        private static string Fill(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null) return brush == null ? "none" : "other";
            return ThemePalette.Hex((uint)(0xFF000000 | ((uint)solid.Color.R << 16)
                | ((uint)solid.Color.G << 8) | solid.Color.B));
        }

        /// <summary>An element's box in the coordinate space of the visual that was rendered.</summary>
        private static Rect Bounds(FrameworkElement element, Visual root)
        {
            try
            {
                return element.TransformToAncestor(root).TransformBounds(
                    new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            catch
            {
                return Rect.Empty;   // an element the render root cannot reach: report nothing
            }
        }

        private static Rect Band(Rect b, double fraction)
        {
            double cy = b.Top + b.Height / 2.0;
            return new Rect(b.Left + 2, cy - 4, Math.Max(1, (b.Width - 4) * fraction), 8);
        }

        /// <summary>From just right of the thumb to the end of the track - the unfilled side.</summary>
        private static Rect RestOfBand(Rect b, Slider slider)
        {
            double span = slider.Maximum - slider.Minimum;
            double at = span > 0 ? (slider.Value - slider.Minimum) / span : 0.0;
            double left = b.Left + 2 + (b.Width - 4) * at + 4;   // clear of the thumb
            double right = b.Right - 2;
            if (right - left < 6) return Rect.Empty;             // the thumb is at the far end
            double cy = b.Top + b.Height / 2.0;
            return new Rect(left, cy - 4, right - left, 8);
        }

        private static Rect LeftBox(Rect b)
        {
            double cy = b.Top + b.Height / 2.0;
            return new Rect(b.Left + 1, cy - 8, 16, 16);
        }

        private static Rect Centre(Rect b)
        {
            return new Rect(b.Left + b.Width * 0.25, b.Top + b.Height * 0.25,
                Math.Max(1, b.Width * 0.5), Math.Max(1, b.Height * 0.5));
        }

        private static void Sample(StringBuilder sb, string label, byte[] pixels, int stride, int w, int h, Rect region)
        {
            if (region.IsEmpty || region.Width < 1 || region.Height < 1) return;

            int x0 = Clamp((int)Math.Floor(region.Left), 0, w - 1);
            int y0 = Clamp((int)Math.Floor(region.Top), 0, h - 1);
            int x1 = Clamp((int)Math.Ceiling(region.Right), x0 + 1, w);
            int y1 = Clamp((int)Math.Ceiling(region.Bottom), y0 + 1, h);

            long r = 0, g = 0, b = 0;
            int n = 0, light = 0, dark = 0;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int i = y * stride + x * 4;
                    byte blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
                    r += red; g += green; b += blue; n++;

                    double lum = (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255.0;
                    if (lum > 0.6) light++;
                    else if (lum < 0.25) dark++;
                }
            }
            if (n == 0) return;

            uint avg = 0xFF000000u
                | (uint)(r / n) << 16
                | (uint)(g / n) << 8
                | (uint)(b / n);

            sb.Append("elem.").Append(label)
              .Append(" box=").Append(Box(region))
              .Append(" avg=").Append(ThemePalette.Hex(avg))
              .Append(" light=").Append(Ratio(light, n))
              .Append(" dark=").Append(Ratio(dark, n))
              .Append(" n=").Append(n.ToString(Inv))
              .Append('\n');
        }

        private static string Box(Rect r)
        {
            return string.Format(Inv, "{0},{1},{2},{3}",
                (int)Math.Round(r.Left), (int)Math.Round(r.Top),
                (int)Math.Round(r.Width), (int)Math.Round(r.Height));
        }

        private static string Ratio(int part, int total)
        {
            return ((double)part / total).ToString("0.00", Inv);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>The first element of a type, in the window's logical tree.</summary>
        private static T First<T>(object node) where T : class
        {
            T hit = node as T;
            if (hit != null) return hit;

            DependencyObject d = node as DependencyObject;
            if (d == null) return null;
            foreach (object child in LogicalTreeHelper.GetChildren(d))
            {
                T found = First<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Temp file, then replace: a poller never reads half a report.</summary>
        private static void Persist(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
