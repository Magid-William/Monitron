// EXP12 - the app's icon, as the app sees it.
//
// Monitron.ico is generated from logo.svg by icogen.ps1 and then embedded in the exe twice over:
// csc's /win32icon puts it in the Win32 icon resource (which is what Explorer, Alt-Tab and the
// taskbar read) and /resource makes the same bytes a manifest resource, which this class hands to
// System.Drawing and WPF for the tray icon and the window icon. One asset, every surface.
//
// Windows picks a frame by the size it is asked for, so each caller names the size it is drawing at:
// the notification area's own small-icon size for the tray, 32 px for the window. If the resource is
// somehow absent (a stripped build) this falls back to the stock application icon rather than
// leaving a window or a tray with none - a tray icon with no way back into the app is a dead end.
// C# 5 only.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Monitron.App
{
    internal static class IconAssets
    {
        /// <summary>The resource name csc gives the file, and the file beside this source.</summary>
        public const string ResourceName = "Monitron.ico";

        private const int SM_CXSMICON = 49;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        private static byte[] _bytes;
        private static bool _read;
        // Icon(Stream) keeps the stream for the life of the Icon, so this one is never disposed.
        private static MemoryStream _stream;

        /// <summary>The .ico's bytes, or null when the build did not embed one.</summary>
        public static byte[] Bytes()
        {
            if (!_read)
            {
                _read = true;
                try
                {
                    Assembly self = Assembly.GetExecutingAssembly();
                    string name = null;
                    foreach (string candidate in self.GetManifestResourceNames())
                    {
                        if (candidate.Equals(ResourceName, StringComparison.OrdinalIgnoreCase)
                            || candidate.EndsWith(ResourceName, StringComparison.OrdinalIgnoreCase))
                        {
                            name = candidate;
                            break;
                        }
                    }
                    if (name != null)
                    {
                        using (Stream source = self.GetManifestResourceStream(name))
                        {
                            var buffer = new MemoryStream();
                            source.CopyTo(buffer);
                            _bytes = buffer.ToArray();
                        }
                    }
                }
                catch { _bytes = null; }
            }
            return _bytes;
        }

        public static bool Embedded { get { return Bytes() != null; } }

        /// <summary>The frame sizes the asset really holds, small to large, for the report.</summary>
        public static IList<int> FrameSizes()
        {
            var sizes = new List<int>();
            byte[] bytes = Bytes();
            if (bytes == null) return sizes;
            try
            {
                foreach (IcoFrame frame in IcoFile.Read(bytes)) sizes.Add(frame.Width);
            }
            catch { sizes.Clear(); }
            return sizes;
        }

        /// <summary>
        /// A fresh, disposable <see cref="Icon"/> whose frame best matches <paramref name="size"/>.
        /// Never null: without the asset the stock application icon stands in.
        /// </summary>
        public static Icon Load(int size)
        {
            byte[] bytes = Bytes();
            if (bytes != null)
            {
                try
                {
                    if (_stream == null) _stream = new MemoryStream(bytes);
                    _stream.Position = 0;
                    return new Icon(_stream, new System.Drawing.Size(size, size));
                }
                catch { /* fall through to the stock icon */ }
            }
            var stock = SystemIcons.Application;
            return (Icon)stock.Clone();   // cloned, so the caller's Dispose cannot take the shared one
        }

        /// <summary>The icon for the notification area, at the size the shell draws it.</summary>
        public static Icon Tray()
        {
            // SM_CXSMICON, the shell's own small-icon size: 16 px at 100%, larger on a scaled display.
            int small = GetSystemMetrics(SM_CXSMICON);
            return Load(small > 0 ? small : 16);
        }

        /// <summary>
        /// The window's icon as a WPF image. The largest frame is used on purpose: WPF hands Windows
        /// one bitmap for the whole window icon, and the shell scales it down to taskbar and Alt-Tab
        /// sizes - so the 256 px frame scaled down looks better than the 32 px one scaled up.
        /// </summary>
        public static BitmapSource Window()
        {
            int largest = 0;
            foreach (int size in FrameSizes()) if (size > largest) largest = size;
            using (Icon icon = Load(largest > 0 ? largest : 32))
            {
                try
                {
                    return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                catch { return null; }
            }
        }
    }
}
