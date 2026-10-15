// EXP12 - drives the icon pipeline and asserts the state it exposes.
//
// No window and no shell: the reader is handed the real logo.svg, the rasteriser is driven at known
// sizes, and the container is parsed back. Then the committed Monitron.ico is compared, byte for
// byte, with a fresh render - the claim that the asset in the repo is exactly what the tool writes.
//
// Built as a console exe by icon-test.ps1 (a winexe cannot print) with /main:, because IconGen.cs
// also carries icogen's own entry point. Deliberately NOT part of build.ps1.
// C# 5 only.

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Monitron.App
{
    internal static class IconTests
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ok   " + what); }
            else { _failed++; Console.WriteLine("  FAIL " + what); }
        }

        private static void Throws(Action action, string what)
        {
            try
            {
                action();
                Check(false, what + " (nothing was thrown)");
            }
            catch (Exception) { Check(true, what); }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Console.WriteLine("Icon - the logo read, rasterised, packed, and compared with the asset");

            string svgPath = args.Length > 0 ? args[0] : Path.Combine("..", "..", "logo.svg");
            string icoPath = args.Length > 1 ? args[1] : "Monitron.ico";
            string svg = File.ReadAllText(svgPath);

            Console.WriteLine("the document");
            SvgDocument document = SvgDocument.Parse(svg);
            Check(Math.Abs(document.Width - 1124.04) < 0.01 && Math.Abs(document.Height - 1085.64) < 0.01,
                "the viewBox is the canvas (1124.04 x 1085.64)");
            Check(document.Shapes.Count == 6, "the logo is six paths");

            int strokes = 0, fills = 0, evenOdd = 0, transformed = 0, grey = 0;
            SvgShape ring = null;
            foreach (SvgShape shape in document.Shapes)
            {
                if (shape.Stroke != 0) { strokes++; ring = shape; }
                else fills++;
                if (shape.EvenOdd) evenOdd++;
                if (shape.HasTransform) transformed++;
                if (shape.Fill == 0xFF02DA96 || shape.Stroke == 0xFF02DA96) grey++;
            }
            Check(strokes == 1 && fills == 5, "one stroked ring and five filled shapes");
            Check(evenOdd == 2, "two of them fill with the even-odd rule");
            Check(transformed == 6, "every path carries a transform");
            Check(grey == 6, "and the logo is one neon green");
            Check(ring != null && ring.StrokeWidth == 12 && ring.Fill == 0,
                "the ring is a 12-unit stroke with no fill");

            Console.WriteLine("the reader's own semantics");
            double x, y;
            SvgDocument.Map(SvgDocument.Transform("translate(10,20) scale(2)"), 1, 0, out x, out y);
            Check(Math.Abs(x - 12) < 1e-9 && Math.Abs(y - 20) < 1e-9,
                "translate(10,20) scale(2) maps (1,0) to (12,20) - SVG's order");
            SvgDocument.Map(SvgDocument.Transform("scale(2) translate(10,20)"), 1, 0, out x, out y);
            Check(Math.Abs(x - 22) < 1e-9 && Math.Abs(y - 40) < 1e-9, "and the other order maps it to (22,40)");
            SvgDocument.Map(SvgDocument.Transform("rotate(90)"), 1, 0, out x, out y);
            Check(Math.Abs(x) < 1e-9 && Math.Abs(y - 1) < 1e-9, "rotate(90) maps (1,0) to (0,1)");
            SvgDocument.Map(SvgDocument.Transform("matrix(1 0 0 1 5 6)"), 1, 1, out x, out y);
            Check(Math.Abs(x - 6) < 1e-9 && Math.Abs(y - 7) < 1e-9, "a matrix is read as a b c d e f");
            SvgDocument.Map(SvgDocument.Transform(null), 3, 4, out x, out y);
            Check(x == 3 && y == 4, "no transform is the identity");

            Console.WriteLine("what the reader refuses");
            Throws(delegate
            {
                SvgDocument.Parse("<svg viewBox=\"0 0 10 10\"><g transform=\"translate(1)\">"
                    + "<path d=\"M0 0L1 1\" fill=\"#000000\"/></g></svg>");
            }, "a <g> that carries a transform");
            Throws(delegate
            {
                SvgDocument.Parse("<svg viewBox=\"0 0 10 10\"><style>p{}</style>"
                    + "<path d=\"M0 0L1 1\" fill=\"#000000\"/></svg>");
            }, "a <style> block");
            Throws(delegate
            {
                SvgDocument.Parse("<svg viewBox=\"0 0 10 10\"><path d=\"M0 0L1 1\" fill=\"url(#g)\"/></svg>");
            }, "a gradient paint");
            Throws(delegate
            {
                SvgDocument.Parse("<svg viewBox=\"0 0 10 10\"><path d=\"M0 0L1 1\"/></svg>");
            }, "a path with neither a fill nor a stroke");
            Throws(delegate { SvgDocument.Parse("<svg viewBox=\"0 0 10 10\"><path d=\"M0 0L1 1\" fill=\"red\"/></svg>"); },
                "a colour name it does not know");

            Console.WriteLine("the paint");
            SvgDocument short3 = SvgDocument.Parse(
                "<svg viewBox=\"0 0 10 10\"><path d=\"M0 0L1 1\" fill=\"#abc\"/></svg>");
            Check(short3.Shapes[0].Fill == 0xFFAABBCC, "#abc is AABBCC");
            SvgDocument none = SvgDocument.Parse(
                "<svg viewBox=\"0 0 10 10\"><path d=\"M0 0L1 1\" fill=\"none\" stroke=\"rgb(1,2,3)\"/></svg>");
            Check(none.Shapes[0].Fill == 0, "fill=\"none\" paints nothing");
            Check(none.Shapes[0].Stroke == 0xFF010203, "and rgb(1,2,3) is read");

            Console.WriteLine("the ink and why the renderer crops");
            IList<DrawnShape> shapes = IconGen.Paint(document);
            Rect ink = IconGen.Ink(shapes);
            Console.WriteLine("       ink " + ink);
            Check(ink.Left > 300 && ink.Top > 300 && ink.Right < 800 && ink.Bottom < 800,
                "the art sits somewhere in the middle of the viewBox, clear of every edge");
            double share = ink.Width * ink.Height / (document.Width * document.Height);
            Check(share < 0.2, "it covers under a fifth of the canvas, so the viewBox is not the icon");

            Console.WriteLine("the frames");
            IList<IcoFrame> frames = IconGen.Frames(svg, IconGen.DefaultSizes);
            Check(frames.Count == 9, "nine frames");
            int[] want = IconGen.DefaultSizes;
            bool sizes = frames.Count == want.Length;
            for (int i = 0; i < frames.Count && sizes; i++) sizes = frames[i].Width == want[i];
            Check(sizes, "at 16, 20, 24, 32, 40, 48, 64, 128 and 256");
            bool smallDib = true, largePng = true;
            foreach (IcoFrame frame in frames)
            {
                if (frame.Width < IconGen.PngFrom && frame.Png) smallDib = false;
                if (frame.Width >= IconGen.PngFrom && !frame.Png) largePng = false;
            }
            Check(smallDib, "the small frames are DIBs");
            Check(largePng, "128 and 256 are PNGs");

            Console.WriteLine("the pixels of a frame");
            byte[] pixels = IconGen.Rasterize(shapes, ink, 256);
            Check(pixels.Length == 256 * 256 * 4, "256x256 straight BGRA");
            int painted = 0, left = 256, right = -1, top = 256, bottom = -1;
            for (int row = 0; row < 256; row++)
            {
                for (int column = 0; column < 256; column++)
                {
                    if (pixels[(row * 256 + column) * 4 + 3] == 0) continue;
                    painted++;
                    if (column < left) left = column;
                    if (column > right) right = column;
                    if (row < top) top = row;
                    if (row > bottom) bottom = row;
                }
            }
            double coverage = painted / (double)(256 * 256);
            Check(coverage > 0.05 && coverage < 0.5, "the mark paints between 5% and 50% of it (" + coverage.ToString("0.000") + ")");
            Check(left > 0 && top > 0 && right < 255 && bottom < 255,
                "and it stops short of every edge - the margin is real");
            Check((right - left) > 256 * 0.90 && (bottom - top) > 256 * 0.90,
                "and it is scaled to fill the canvas, not left at the viewBox's ~37%");
            Check(pixels[3] == 0 && pixels[(255 * 256 + 255) * 4 + 3] == 0,
                "the corners are transparent, so it is not a black square");
            Check(pixels[(128 * 256 + 128) * 4 + 3] > 0,
                "and the mark crosses the middle of the canvas");

            Console.WriteLine("the container");
            byte[] bytes = IcoFile.Write(frames);
            int images = 0;
            foreach (IcoFrame frame in frames) images += frame.Data.Length;
            Check(bytes.Length == 6 + frames.Count * 16 + images,
                "the file is a 6-byte directory, 16 bytes an entry, then the images");
            Check(BitConverter.ToInt16(bytes, 0) == 0 && BitConverter.ToInt16(bytes, 2) == 1,
                "and it says icon");
            Check(BitConverter.ToInt16(bytes, 4) == 9, "with nine entries");

            IList<IcoFrame> back = IcoFile.Read(bytes);
            Check(back.Count == 9, "the reader finds nine frames again");
            bool same = back.Count == frames.Count;
            for (int i = 0; i < back.Count && same; i++)
            {
                same = back[i].Width == frames[i].Width && back[i].Png == frames[i].Png
                    && back[i].Data.Length == frames[i].Data.Length;
            }
            Check(same, "at the same sizes, in the same encodings, with the same bytes");

            byte[] dib = frames[0].Data;
            Check(BitConverter.ToInt32(dib, 0) == 40, "a DIB frame starts with its header");
            Check(BitConverter.ToInt32(dib, 4) == 16 && BitConverter.ToInt32(dib, 8) == 32,
                "16 wide, and its height stacks the XOR bitmap and the mask");
            Check(BitConverter.ToInt16(dib, 14) == 32, "at 32 bits per pixel");
            int maskStride = 4, maskStart = 40 + 16 * 16 * 4;
            Check(dib.Length == maskStart + maskStride * 16, "the AND mask is padded to four bytes a row");
            bool emptyMask = true;
            for (int i = maskStart; i < dib.Length; i++) if (dib[i] != 0) emptyMask = false;
            Check(emptyMask, "the mask is zeroed - the alpha channel does the work");

            PngBitmapDecoder decoder = new PngBitmapDecoder(new MemoryStream(frames[8].Data),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(decoder.Frames.Count == 1 && decoder.Frames[0].PixelWidth == 256
                && decoder.Frames[0].PixelHeight == 256, "the 256 frame really is a 256x256 PNG");

            Console.WriteLine("refusals in the container");
            Throws(delegate { IcoFile.Write(new List<IcoFrame>()); }, "no frames at all");
            Throws(delegate
            {
                IcoFile.Write(new List<IcoFrame> { IcoFrame.FromDib(16, new byte[16 * 16]), new IcoFrame
                {
                    Width = 32, Height = 16, Data = new byte[4]
                } });
            }, "a frame that is not square");

            Console.WriteLine("the same input gives the same file");
            byte[] again = IcoFile.Write(IconGen.Frames(svg, IconGen.DefaultSizes));
            Check(again.Length == bytes.Length, "a second render is the same length");
            bool identical = again.Length == bytes.Length;
            for (int i = 0; i < bytes.Length && identical; i++) identical = again[i] == bytes[i];
            Check(identical, "and the same bytes, so the asset is reproducible");

            Console.WriteLine("the committed asset");
            if (File.Exists(icoPath))
            {
                byte[] committed = File.ReadAllBytes(icoPath);
                bool match = committed.Length == bytes.Length;
                for (int i = 0; i < committed.Length && match; i++) match = committed[i] == bytes[i];
                Check(match, "src/Monitron.App/Monitron.ico is exactly what icogen writes today");
                IList<IcoFrame> committedFrames = IcoFile.Read(committed);
                Check(committedFrames.Count == 9, "and it holds the nine frames");
            }
            else
            {
                Console.WriteLine("  ---  " + icoPath + " does not exist (run icogen.ps1)");
                _failed++;
            }

            Console.WriteLine();
            Console.WriteLine("  " + _passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
