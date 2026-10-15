// EXP12 - the narrow SVG reader behind the app icon.
//
// logo.svg comes from a design tool, but only a small slice of SVG is used here: <path> elements
// with a d attribute, a fill and/or a stroke, an optional fill-rule, and an optional transform.
// This class reads exactly that subset into a plain list - no rendering, no OS - so the reader is
// testable on its own and the rasteriser (IconGen) is the only part that touches WPF.
//
// Anything outside the subset is a parse error rather than a silent omission: an icon that quietly
// drops a shape, or paints the wrong colour, is worse than no icon. A bare <g> (a group with no
// attributes) is the one tolerated wrapper, because the designer's export uses it; a <g> that
// carries a transform is refused, since this reader does not track group state.
// C# 5 only.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Monitron.App
{
    /// <summary>One painted shape: a path in SVG user units, plus how it is painted.</summary>
    internal sealed class SvgShape
    {
        /// <summary>The d attribute, verbatim - Geometry.Parse understands the same commands.</summary>
        public string Data;
        /// <summary>0 means no fill; otherwise 0xAARRGGBB.</summary>
        public uint Fill;
        /// <summary>0 means no stroke.</summary>
        public uint Stroke;
        public double StrokeWidth = 1d;
        public bool EvenOdd;
        /// <summary>a b c d e f. Identity when the element has no transform.</summary>
        public double[] Matrix = new double[] { 1, 0, 0, 1, 0, 0 };

        public bool HasTransform
        {
            get
            {
                return !(Matrix[0] == 1 && Matrix[1] == 0 && Matrix[2] == 0 && Matrix[3] == 1
                    && Matrix[4] == 0 && Matrix[5] == 0);
            }
        }
    }

    /// <summary>An SVG file read as the paths it paints. The viewBox is the coordinate space.</summary>
    internal sealed class SvgDocument
    {
        public double Width;
        public double Height;
        public readonly IList<SvgShape> Shapes = new List<SvgShape>();

        /// <summary>
        /// Reads the subset above, or throws. Every path must be paintable: a <path> with neither a
        /// fill nor a stroke is refused rather than turned into an invisible shape.
        /// </summary>
        public static SvgDocument Parse(string svg)
        {
            if (svg == null) throw new ArgumentNullException("svg");

            // A group is fine only when it has no attributes - a transform on it would be state this
            // reader does not carry, and half-applied geometry is a wrong icon, not a partial one.
            foreach (Match g in Regex.Matches(svg, @"<\s*g\b([^>]*)>", RegexOptions.IgnoreCase))
            {
                if (g.Groups[1].Value.Trim().Length != 0)
                    throw new NotSupportedException("<g> with attributes: " + g.Groups[1].Value.Trim());
            }
            foreach (string tag in new string[] { "style", "defs", "use", "symbol", "linearGradient",
                "radialGradient", "filter", "image", "text" })
            {
                if (Regex.IsMatch(svg, @"<\s*" + tag + @"\b", RegexOptions.IgnoreCase))
                    throw new NotSupportedException("<" + tag + "> is outside the reader's subset");
            }

            var document = new SvgDocument();
            document.ReadCanvas(svg);

            foreach (Match path in Regex.Matches(svg, @"<\s*path\b([^>]*?)/?>", RegexOptions.IgnoreCase))
            {
                string attrs = path.Groups[1].Value;
                if (Attribute(attrs, "style") != null || Attribute(attrs, "class") != null)
                    throw new NotSupportedException("a styled <path> is outside the reader's subset");

                var shape = new SvgShape();
                shape.Data = Attribute(attrs, "d");
                if (string.IsNullOrEmpty(shape.Data)) throw new FormatException("<path> without a d");

                string fill = Attribute(attrs, "fill");
                string stroke = Attribute(attrs, "stroke");
                shape.Fill = Paint(fill);
                shape.Stroke = Paint(stroke);
                string fillRule = Attribute(attrs, "fill-rule");
                shape.EvenOdd = fillRule != null && fillRule.Trim().ToLowerInvariant() == "evenodd";
                string strokeWidth = Attribute(attrs, "stroke-width");
                if (strokeWidth != null)
                    shape.StrokeWidth = double.Parse(strokeWidth, CultureInfo.InvariantCulture);
                if (shape.Fill == 0 && shape.Stroke == 0)
                    throw new FormatException("<path> with neither a fill nor a stroke");
                if (shape.Stroke != 0 && shape.StrokeWidth <= 0)
                    throw new FormatException("<path> stroked with a width of " + shape.StrokeWidth);

                shape.Matrix = Transform(Attribute(attrs, "transform"));
                document.Shapes.Add(shape);
            }

            if (document.Shapes.Count == 0) throw new FormatException("no <path> in the document");
            return document;
        }

        /// <summary>The viewBox - the user-unit box the paths are drawn in. width/height fall back.</summary>
        private void ReadCanvas(string svg)
        {
            string open = Regex.Match(svg, @"<\s*svg\b([^>]*)>", RegexOptions.IgnoreCase).Groups[1].Value;
            string viewBox = Attribute(open, "viewBox");
            if (viewBox != null)
            {
                string[] parts = viewBox.Split(new char[] { ' ', ',', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 4) throw new FormatException("viewBox: " + viewBox);
                double minX = Number(parts[0]), minY = Number(parts[1]);
                Width = Number(parts[2]);
                Height = Number(parts[3]);
                // A non-zero origin is a translate this reader would have to carry; refuse it.
                if (minX != 0 || minY != 0) throw new NotSupportedException("viewBox origin " + viewBox);
            }
            else
            {
                Width = Number(Required(open, "width"));
                Height = Number(Required(open, "height"));
            }
            if (Width <= 0 || Height <= 0) throw new FormatException("non-positive canvas");
        }

        private static double Number(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("expected a number, got nothing");
            return double.Parse(text.Trim(), CultureInfo.InvariantCulture);
        }

        private static string Required(string attrs, string name)
        {
            string value = Attribute(attrs, name);
            if (value == null) throw new FormatException("missing " + name);
            return value;
        }

        /// <summary>The value of one attribute, or null. Only the quoted form is read.</summary>
        internal static string Attribute(string attrs, string name)
        {
            var match = Regex.Match(attrs, @"(?:^|\s)" + name + @"\s*=\s*""([^""]*)""",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>"none", "#rgb", "#rrggbb", "rgb(r,g,b)" or a couple of named colours.</summary>
        private static uint Paint(string value)
        {
            if (value == null) return 0;
            string text = value.Trim().ToLowerInvariant();
            if (text.Length == 0 || text == "none" || text == "transparent") return 0;
            if (text.StartsWith("url(")) throw new NotSupportedException("a url() paint: " + value);
            if (text == "black") return 0xFF000000;
            if (text == "white") return 0xFFFFFFFF;

            if (text.StartsWith("#"))
            {
                string hex = text.Substring(1);
                if (hex.Length == 3)
                    hex = new string(new char[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
                if (hex.Length != 6) throw new FormatException("colour: " + value);
                return 0xFF000000u | uint.Parse(hex, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            }

            var rgb = Regex.Match(text, @"^rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)$");
            if (rgb.Success)
            {
                uint r = uint.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture);
                uint g = uint.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture);
                uint b = uint.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture);
                if (r > 255 || g > 255 || b > 255) throw new FormatException("colour: " + value);
                return 0xFF000000u | (r << 16) | (g << 8) | b;
            }
            throw new FormatException("colour: " + value);
        }

        /// <summary>
        /// The transform list as one matrix, [a b c d e f] with p' = (a*x + c*y + e, b*x + d*y + f).
        /// The list composes left to right, as SVG says: "A B" is A*B, so B applies to the point first.
        /// </summary>
        internal static double[] Transform(string list)
        {
            double[] total = new double[] { 1, 0, 0, 1, 0, 0 };
            if (string.IsNullOrEmpty(list)) return total;

            var calls = Regex.Matches(list, @"([a-zA-Z]+)\s*\(([^)]*)\)");
            string consumed = Regex.Replace(list, @"([a-zA-Z]+)\s*\([^)]*\)", "").Trim();
            if (calls.Count == 0 || consumed.Length != 0)
                throw new NotSupportedException("transform: " + list);

            foreach (Match call in calls)
            {
                double[] part = TransformPart(call.Groups[1].Value, call.Groups[2].Value);
                total = Multiply(total, part);
            }
            return total;
        }

        private static double[] TransformPart(string name, string args)
        {
            double[] a = Numbers(args);
            string fn = name.ToLowerInvariant();
            if (fn == "matrix" && a.Length == 6) return a;
            if (fn == "translate" && (a.Length == 1 || a.Length == 2))
                return new double[] { 1, 0, 0, 1, a[0], a.Length > 1 ? a[1] : 0 };
            if (fn == "scale" && (a.Length == 1 || a.Length == 2))
                return new double[] { a[0], 0, 0, a.Length > 1 ? a[1] : a[0], 0, 0 };
            if (fn == "rotate" && a.Length == 1)
            {
                double r = a[0] * Math.PI / 180d;
                return new double[] { Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), 0, 0 };
            }
            if (fn == "rotate" && a.Length == 3)
            {
                // rotate(angle cx cy) is translate(cx,cy) rotate(angle) translate(-cx,-cy).
                double[] back = new double[] { 1, 0, 0, 1, -a[1], -a[2] };
                double r = a[0] * Math.PI / 180d;
                double[] turn = new double[] { Math.Cos(r), Math.Sin(r), -Math.Sin(r), Math.Cos(r), 0, 0 };
                double[] there = new double[] { 1, 0, 0, 1, a[1], a[2] };
                return Multiply(Multiply(back, turn), there);
            }
            throw new NotSupportedException("transform function: " + name + "(" + args + ")");
        }

        private static double[] Numbers(string args)
        {
            string[] parts = args.Split(new char[] { ' ', ',', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            var values = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++) values[i] = Number(parts[i]);
            return values;
        }

        /// <summary>The matrix product u*v: v applies to a point first, then u.</summary>
        internal static double[] Multiply(double[] u, double[] v)
        {
            return new double[]
            {
                u[0] * v[0] + u[2] * v[1],
                u[1] * v[0] + u[3] * v[1],
                u[0] * v[2] + u[2] * v[3],
                u[1] * v[2] + u[3] * v[3],
                u[0] * v[4] + u[2] * v[5] + u[4],
                u[1] * v[4] + u[3] * v[5] + u[5]
            };
        }

        /// <summary>Applies the matrix to a point - the reader's own semantics, for the tests.</summary>
        internal static void Map(double[] m, double x, double y, out double outX, out double outY)
        {
            outX = m[0] * x + m[2] * y + m[4];
            outY = m[1] * x + m[3] * y + m[5];
        }
    }
}
