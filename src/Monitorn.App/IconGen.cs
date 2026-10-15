// EXP12 - the rasteriser: logo.svg to the frames an .ico is made of.
//
// This is the one half of the icon tool that touches WPF, and it is deliberately thin: the reader
// (SvgIcon) has already turned the file into paths, and the container (IcoFile) will turn frames
// into bytes. Here a path becomes a WPF Geometry, the geometries are fitted to the canvas, and each
// size is drawn into a RenderTargetBitmap and handed back as straight BGRA pixels (or as a PNG for
// the two large frames, where Windows prefers one).
//
// "Fit" is the whole point: the art in logo.svg sits in the middle ~37% of its viewBox, so drawing
// the viewBox as-is would leave the mark tiny in a wide empty icon. The renderer measures the ink
// and scales THAT to the canvas, with a small margin.
//
// icogen.exe is built from this file alone (csc /target:exe), never shipped, and prints what it did.
// C# 5 only.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Monitorn.App
{
    /// <summary>One shape, ready to draw: its geometry, its brush and its pen (either may be null).</summary>
    internal sealed class DrawnShape
    {
        public Geometry Geometry;
        public Brush Fill;
        public Pen Stroke;
    }

    internal static class IconGen
    {
        /// <summary>The frames an app icon is expected to carry, small to large.</summary>
        public static readonly int[] DefaultSizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

        /// <summary>Blank space kept around the mark, as a fraction of the canvas. 3% each side.</summary>
        public const double Margin = 0.03;

        /// <summary>Frames from a size up are a PNG; below that Windows prefers a DIB.</summary>
        public const int PngFrom = 128;

        /// <summary>The whole .ico for an SVG document, at the default sizes.</summary>
        public static byte[] Ico(string svg)
        {
            return Ico(svg, DefaultSizes);
        }

        public static byte[] Ico(string svg, IList<int> sizes)
        {
            return IcoFile.Write(Frames(svg, sizes));
        }

        /// <summary>The frames themselves, in the order given.</summary>
        public static IList<IcoFrame> Frames(string svg, IList<int> sizes)
        {
            SvgDocument document = SvgDocument.Parse(svg);
            IList<DrawnShape> shapes = Paint(document);
            Rect ink = Ink(shapes);

            var frames = new List<IcoFrame>(sizes.Count);
            foreach (int size in sizes)
            {
                if (size <= 0 || size > 256) throw new ArgumentOutOfRangeException("sizes", size, "1..256");
                byte[] bgra = Rasterize(shapes, ink, size);
                frames.Add(size >= PngFrom
                    ? IcoFrame.FromPng(size, Png(bgra, size))
                    : IcoFrame.FromDib(size, bgra));
            }
            return frames;
        }

        /// <summary>Turns the reader's paths into brush/pen shapes. The only Geometry.Parse in the app.</summary>
        public static IList<DrawnShape> Paint(SvgDocument document)
        {
            var shapes = new List<DrawnShape>(document.Shapes.Count);
            foreach (SvgShape shape in document.Shapes)
            {
                // Geometry.Parse hands back a frozen PathGeometry (its default rule is even-odd);
                // clone it, because both the rule and the transform are ours to set. SVG's own
                // default rule is nonzero, so say which one this document asked for either way.
                Geometry geometry = Geometry.Parse(shape.Data).Clone();
                var path = geometry as PathGeometry;
                if (path != null) path.FillRule = shape.EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero;
                if (shape.HasTransform)
                {
                    geometry.Transform = new MatrixTransform(new Matrix(shape.Matrix[0], shape.Matrix[1],
                        shape.Matrix[2], shape.Matrix[3], shape.Matrix[4], shape.Matrix[5]));
                }
                geometry.Freeze();

                var drawn = new DrawnShape { Geometry = geometry };
                if (shape.Fill != 0)
                {
                    var brush = new SolidColorBrush(Colour(shape.Fill));
                    brush.Freeze();
                    drawn.Fill = brush;
                }
                if (shape.Stroke != 0)
                {
                    var pen = new Pen(Brush(Colour(shape.Stroke)), shape.StrokeWidth);
                    pen.Freeze();
                    drawn.Stroke = pen;
                }
                shapes.Add(drawn);
            }
            return shapes;
        }

        /// <summary>The bounds of everything that will be painted, stroke included.</summary>
        public static Rect Ink(IList<DrawnShape> shapes)
        {
            Rect ink = Rect.Empty;
            foreach (DrawnShape shape in shapes)
            {
                Rect bounds = shape.Geometry.Bounds;
                if (shape.Stroke != null && shape.Stroke.Thickness > 0)
                {
                    // A stroke straddles its path, so half of it lies outside the geometry's own bounds.
                    double half = shape.Stroke.Thickness / 2d;
                    bounds.Inflate(half, half);
                }
                ink.Union(bounds);
            }
            if (ink.IsEmpty || ink.Width <= 0 || ink.Height <= 0)
                throw new InvalidDataException("the document paints nothing");
            return ink;
        }

        /// <summary>
        /// The ink scaled to fill the canvas (less the margin) and centred, as straight BGRA pixels,
        /// top-down, size*size*4 bytes.
        /// </summary>
        public static byte[] Rasterize(IList<DrawnShape> shapes, Rect ink, int size)
        {
            double inner = size * (1d - 2d * Margin);
            double scale = inner / Math.Max(ink.Width, ink.Height);
            double offsetX = (size - ink.Width * scale) / 2d - ink.X * scale;
            double offsetY = (size - ink.Height * scale) / 2d - ink.Y * scale;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(offsetX, offsetY));
                dc.PushTransform(new ScaleTransform(scale, scale));
                foreach (DrawnShape shape in shapes)
                    dc.DrawGeometry(shape.Fill, shape.Stroke, shape.Geometry);
                dc.Pop();
                dc.Pop();
            }

            var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);

            // Pbgra32 is premultiplied; Bgra32 is not, which is what an .ico wants. (The logo is
            // black-on-transparent, where the two agree, but a future colour would not.)
            var straight = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
            var pixels = new WriteableBitmap(straight);
            var bytes = new byte[size * size * 4];
            pixels.CopyPixels(bytes, size * 4, 0);
            return bytes;
        }

        /// <summary>Encodes straight BGRA pixels as a PNG, for the frames Windows wants one for.</summary>
        public static byte[] Png(byte[] bgra, int size)
        {
            var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, bgra, size * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static Color Colour(uint argb)
        {
            return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }

        private static Brush Brush(Color colour)
        {
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>The tool: icogen.exe logo.svg Monitorn.ico [--png &lt;dir&gt;] [size ...]</summary>
    internal static class IconTool
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: icogen <logo.svg> <out.ico> [--png <dir>] [size ...]");
                return 2;
            }

            string svg = File.ReadAllText(args[0]);
            string outPath = args[1];
            string pngDir = null;
            var sizes = new List<int>();

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--png" && i + 1 < args.Length) pngDir = args[++i];
                else sizes.Add(int.Parse(args[i], CultureInfo.InvariantCulture));
            }
            if (sizes.Count == 0) sizes.AddRange(IconGen.DefaultSizes);

            IList<IcoFrame> frames = IconGen.Frames(svg, sizes);
            File.WriteAllBytes(outPath, IcoFile.Write(frames));
            Console.WriteLine("wrote " + outPath + " with " + frames.Count + " frames");

            foreach (IcoFrame frame in frames)
            {
                Console.WriteLine("  " + frame.Width + "x" + frame.Height + " "
                    + (frame.Png ? "png" : "dib") + " " + frame.Data.Length + " bytes");
            }

            if (pngDir != null)
            {
                // So a human can look at every frame, which is how the small ones get judged.
                Directory.CreateDirectory(pngDir);
                SvgDocument document = SvgDocument.Parse(svg);
                IList<DrawnShape> shapes = IconGen.Paint(document);
                Rect ink = IconGen.Ink(shapes);
                foreach (int size in sizes)
                {
                    File.WriteAllBytes(Path.Combine(pngDir, "icon-" + size + ".png"),
                        IconGen.Png(IconGen.Rasterize(shapes, ink, size), size));
                }
                Console.WriteLine("wrote " + sizes.Count + " png previews to " + pngDir);
            }

            return 0;
        }
    }
}
