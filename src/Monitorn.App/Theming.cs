// EXP08 - the palette as WPF brushes, and the control templates WPF takes from the OS theme.
//
// Two mechanisms, on purpose, because one does not cover the other:
//   * Directly-assigned brushes - a window's Background, a label's Foreground, a row's card, a
//     field's face - are shared, mutable SolidColorBrushes. A theme switch only reassigns their
//     Color, and every element holding one repaints: no walk, no rebuild.
//   * A brush that appears as a Setter value inside a ControlTemplate is FROZEN when that template
//     is sealed, so those cannot be mutated. Every brush a template uses is therefore a private
//     copy, and Equip() throws the built templates away so the next Apply() rebuilds and reassigns
//     them with the new colors.
//
// Why the templates exist at all: a WPF control's look comes from the OS theme's template (Aero2),
// not from the control's own Background/Foreground. Setting those properties recolours only what
// that template happens to bind, so the CheckBox and RadioButton marks and the Slider's whole track
// stay light until each is given a template of its own. --theme-no-templates exists to show exactly
// that gap, and is what the verifier measures.
//
// The Slider is the awkward one: its look lives on a Track's Thumb and two RepeatButtons, and Track
// exposes those as plain CLR properties - not dependency properties - so a Track cannot be built
// with FrameworkElementFactory at all. The OS template's Track is kept, and only its three parts are
// re-templated, found by the name the Slider itself looks up (PART_Track).
//
// C# 5 only - the in-box csc.exe is what builds this.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Monitorn.App
{
    /// <summary>The palette, as brushes the windows share and mutate - plus the templates they need.</summary>
    internal sealed class ThemeBrushes
    {
        public readonly SolidColorBrush WindowBg = new SolidColorBrush();
        public readonly SolidColorBrush Ink = new SolidColorBrush();
        public readonly SolidColorBrush Muted = new SolidColorBrush();
        public readonly SolidColorBrush Warn = new SolidColorBrush();
        public readonly SolidColorBrush RowFill = new SolidColorBrush();
        public readonly SolidColorBrush RowEdge = new SolidColorBrush();
        public readonly SolidColorBrush ControlFill = new SolidColorBrush();
        public readonly SolidColorBrush ControlEdge = new SolidColorBrush();
        public readonly SolidColorBrush ControlHover = new SolidColorBrush();
        public readonly SolidColorBrush Accent = new SolidColorBrush();

        // Built from private copies, and dropped whenever the palette changes (see the header).
        private ControlTemplate _check;
        private ControlTemplate _radio;
        private ControlTemplate _button;
        private ControlTemplate _textBox;
        private ControlTemplate _thumb;
        private ControlTemplate _barAccent;
        private ControlTemplate _barRest;
        private ControlTemplate _menuItem;
        private ControlTemplate _separator;
        private ControlTemplate _contextMenu;

        public ThemeBrushes(ThemePalette palette) { Equip(palette); }

        /// <summary>Reassigns every shared brush's color and drops the templates built from the old one.</summary>
        public void Equip(ThemePalette p)
        {
            WindowBg.Color = ColorOf(p.WindowBg);
            Ink.Color = ColorOf(p.Ink);
            Muted.Color = ColorOf(p.Muted);
            Warn.Color = ColorOf(p.Warn);
            RowFill.Color = ColorOf(p.RowFill);
            RowEdge.Color = ColorOf(p.RowEdge);
            ControlFill.Color = ColorOf(p.ControlFill);
            ControlEdge.Color = ColorOf(p.ControlEdge);
            ControlHover.Color = ColorOf(p.ControlHover);
            Accent.Color = ColorOf(p.Accent);

            _check = null; _radio = null; _button = null; _textBox = null;
            _thumb = null; _barAccent = null; _barRest = null;
            _menuItem = null; _separator = null; _contextMenu = null;
        }

        public static Color ColorOf(uint argb)
        {
            return Color.FromArgb(ThemePalette.A(argb), ThemePalette.R(argb),
                ThemePalette.G(argb), ThemePalette.B(argb));
        }

        /// <summary>A private copy, safe to put in a template because nothing needs to mutate it.</summary>
        public Brush Copy(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            return solid == null ? brush : new SolidColorBrush(solid.Color);
        }

        public ControlTemplate Check
        {
            get { if (_check == null) _check = Theming.BuildCheck(this, false); return _check; }
        }

        public ControlTemplate Radio
        {
            get { if (_radio == null) _radio = Theming.BuildCheck(this, true); return _radio; }
        }

        public ControlTemplate ButtonFace
        {
            get { if (_button == null) _button = Theming.BuildButton(this); return _button; }
        }

        public ControlTemplate TextBox
        {
            get { if (_textBox == null) _textBox = Theming.BuildTextBox(this); return _textBox; }
        }

        public ControlTemplate ThumbRound
        {
            get { if (_thumb == null) _thumb = Theming.BuildDot(Copy(Accent)); return _thumb; }
        }

        /// <summary>The slider's filled side, left of the thumb.</summary>
        public ControlTemplate BarAccent
        {
            get { if (_barAccent == null) _barAccent = Theming.BuildBar(Copy(Accent)); return _barAccent; }
        }

        /// <summary>And its empty side, right of the thumb.</summary>
        public ControlTemplate BarRest
        {
            get { if (_barRest == null) _barRest = Theming.BuildBar(Copy(ControlEdge)); return _barRest; }
        }

        /// <summary>A tray menu line (EXP11): the place a check mark shows, and the OS highlight.</summary>
        public ControlTemplate MenuItemFace
        {
            get { if (_menuItem == null) _menuItem = Theming.BuildMenuItem(this); return _menuItem; }
        }

        /// <summary>The thin line between the tray menu's groups (EXP11).</summary>
        public ControlTemplate SeparatorLine
        {
            get { if (_separator == null) _separator = Theming.BuildSeparator(this); return _separator; }
        }

        /// <summary>The tray menu's frame (EXP11): the OS one draws SystemColors, not our background.</summary>
        public ControlTemplate ContextMenuFrame
        {
            get { if (_contextMenu == null) _contextMenu = Theming.BuildContextMenu(this); return _contextMenu; }
        }
    }

    /// <summary>Wires a window to a ThemeState: paints it, and repaints it when the mode changes.</summary>
    internal static class Theming
    {
        public static void Bind(Window window, ThemeState theme)
        {
            Apply(window, theme);
            SystemTheme.ApplyTitleBar(window, theme.Mode == ThemeMode.Dark);

            // Once the template has applied, a Slider's Track parts exist to be re-styled; before
            // the window is loaded they may not.
            window.Loaded += delegate { Apply(window, theme); };

            theme.Changed += delegate
            {
                Apply(window, theme);   // shared brushes recolour in place; templates are rebuilt here
                SystemTheme.ApplyTitleBar(window, theme.Mode == ThemeMode.Dark);
            };

            // Windows tells every top-level window the color set changed; re-resolve on that.
            SystemTheme.Follow(window, delegate { theme.Refresh(); });
        }

        /// <summary>
        /// Paints a built window: its background, and - when templates are on - a template for each
        /// control the OS one would keep light. Idempotent, so it runs again on every switch.
        /// </summary>
        public static void Apply(Window window, ThemeState theme)
        {
            ThemeBrushes t = theme.Brushes;
            window.Background = t.WindowBg;

            List<Control> controls = new List<Control>();
            Collect(window, controls);
            foreach (Control c in controls)
            {
                // The property assignments below apply in both modes: they are what a brush swap
                // alone buys, and they are the measurement's baseline. Only the templates are
                // gated. Order matters - RadioButton and CheckBox are both ToggleButtons, not
                // Buttons - and a Slider has no property that reaches its track at all.
                if (c is TextBox)
                {
                    TextBox box = (TextBox)c;
                    box.Background = t.ControlFill;
                    box.BorderBrush = t.ControlEdge;
                    box.Foreground = t.Ink;
                    box.CaretBrush = t.Ink;
                    box.SelectionBrush = t.Accent;
                    if (theme.Templates) box.Template = t.TextBox;
                }
                else if (c is Button)
                {
                    c.Background = t.ControlFill;
                    c.BorderBrush = t.ControlEdge;
                    c.Foreground = t.Ink;
                    if (theme.Templates) c.Template = t.ButtonFace;
                }
                else if (c is Slider)
                {
                    if (theme.Templates) StyleSlider((Slider)c, t);
                }
                else if (c is RadioButton)
                {
                    c.Foreground = t.Ink;
                    if (theme.Templates) c.Template = t.Radio;
                }
                else if (c is CheckBox)
                {
                    c.Foreground = t.Ink;
                    if (theme.Templates) c.Template = t.Check;
                }
            }
        }

        /// <summary>
        /// Paints a ContextMenu from the same palette (EXP11). The menu is its own popup, not part of
        /// a window's logical tree, so <see cref="Apply"/> never reaches it - it is styled here, each
        /// time it opens, which also picks up a theme change between openings.
        /// </summary>
        public static void StyleMenu(ContextMenu menu, ThemeState theme)
        {
            ThemeBrushes t = theme.Brushes;
            menu.Background = t.WindowBg;
            menu.Foreground = t.Ink;
            menu.BorderBrush = t.RowEdge;
            menu.Padding = new Thickness(0, 4, 0, 4);
            if (theme.Templates) menu.Template = t.ContextMenuFrame;

            foreach (object item in menu.Items)
            {
                MenuItem line = item as MenuItem;
                if (line != null)
                {
                    line.Foreground = t.Ink;
                    line.Background = Brushes.Transparent;
                    if (theme.Templates) line.Template = t.MenuItemFace;
                    continue;
                }
                Separator gap = item as Separator;
                if (gap != null && theme.Templates) gap.Template = t.SeparatorLine;
            }
        }

        /// <summary>
        /// Re-templates what the OS Slider template draws on: the Track's thumb and its two repeat
        /// buttons. Safe to call before that template has applied - it simply finds nothing then.
        /// </summary>
        public static void StyleSlider(Slider slider, ThemeBrushes t)
        {
            try
            {
                slider.ApplyTemplate();
                if (slider.Template == null) return;

                Track track = slider.Template.FindName("PART_Track", slider) as Track;
                if (track == null) return;

                if (track.Thumb != null) track.Thumb.Template = t.ThumbRound;
                if (track.DecreaseRepeatButton != null) track.DecreaseRepeatButton.Template = t.BarAccent;
                if (track.IncreaseRepeatButton != null) track.IncreaseRepeatButton.Template = t.BarRest;
            }
            catch
            {
                // a Slider whose template is not the one we expect keeps the OS look
            }
        }

        /// <summary>The logical tree, so this works before the window's own template has applied.</summary>
        private static void Collect(object node, List<Control> found)
        {
            Control control = node as Control;
            if (control != null) found.Add(control);

            DependencyObject d = node as DependencyObject;
            if (d == null) return;
            foreach (object child in LogicalTreeHelper.GetChildren(d)) Collect(child, found);
        }

        // --- the templates ---

        /// <summary>A check box, or - the same shape - a radio button's ring and dot.</summary>
        public static ControlTemplate BuildCheck(ThemeBrushes t, bool radio)
        {
            Brush fill = t.Copy(t.ControlFill);
            Brush edge = t.Copy(t.ControlEdge);
            Brush accent = t.Copy(t.Accent);

            FrameworkElementFactory root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            FrameworkElementFactory box = new FrameworkElementFactory(typeof(Border));
            box.Name = "Box";
            box.SetValue(FrameworkElement.WidthProperty, 16.0);
            box.SetValue(FrameworkElement.HeightProperty, 16.0);
            box.SetValue(Border.BackgroundProperty, fill);
            box.SetValue(Border.BorderBrushProperty, edge);
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(radio ? 8 : 3));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            FrameworkElementFactory mark = new FrameworkElementFactory(
                radio ? typeof(Border) : typeof(TextBlock));
            mark.Name = "Mark";
            if (radio)
            {
                mark.SetValue(FrameworkElement.WidthProperty, 8.0);
                mark.SetValue(FrameworkElement.HeightProperty, 8.0);
                mark.SetValue(Border.BackgroundProperty, accent);
                mark.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            }
            else
            {
                mark.SetValue(TextBlock.TextProperty, "\uE73E");   // Segoe MDL2 Assets: check
                mark.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
                mark.SetValue(TextBlock.FontSizeProperty, 10.0);
                mark.SetValue(TextBlock.ForegroundProperty, accent);
            }
            mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(mark);
            root.AppendChild(box);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 0, 0, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(content);

            ControlTemplate template = new ControlTemplate(radio ? typeof(RadioButton) : typeof(CheckBox));
            template.VisualTree = root;

            Trigger on = new Trigger();
            on.Property = ToggleButton.IsCheckedProperty;
            on.Value = true;
            on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Mark"));
            template.Triggers.Add(on);

            Trigger hot = new Trigger();
            hot.Property = UIElement.IsMouseOverProperty;
            hot.Value = true;
            hot.Setters.Add(new Setter(Border.BorderBrushProperty, accent, "Box"));
            template.Triggers.Add(hot);

            Trigger off = new Trigger();
            off.Property = UIElement.IsEnabledProperty;
            off.Value = false;
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            template.Triggers.Add(off);

            return template;
        }

        /// <summary>A button's face, plus the hover and pressed states the OS template owns.</summary>
        public static ControlTemplate BuildButton(ThemeBrushes t)
        {
            Brush fill = t.Copy(t.ControlFill);
            Brush edge = t.Copy(t.ControlEdge);
            Brush hover = t.Copy(t.ControlHover);
            Brush accent = t.Copy(t.Accent);

            FrameworkElementFactory face = new FrameworkElementFactory(typeof(Border));
            face.Name = "Face";
            face.SetValue(Border.BackgroundProperty, fill);
            face.SetValue(Border.BorderBrushProperty, edge);
            face.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            face.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            face.AppendChild(content);

            ControlTemplate template = new ControlTemplate(typeof(Button));
            template.VisualTree = face;

            Trigger hot = new Trigger();
            hot.Property = UIElement.IsMouseOverProperty;
            hot.Value = true;
            hot.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Face"));
            template.Triggers.Add(hot);

            Trigger pressed = new Trigger();
            pressed.Property = ButtonBase.IsPressedProperty;
            pressed.Value = true;
            pressed.Setters.Add(new Setter(Border.BorderBrushProperty, accent, "Face"));
            template.Triggers.Add(pressed);

            Trigger off = new Trigger();
            off.Property = UIElement.IsEnabledProperty;
            off.Value = false;
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            template.Triggers.Add(off);

            return template;
        }

        /// <summary>A field's face. A TextBox demands a ScrollViewer named "PART_ContentHost".</summary>
        public static ControlTemplate BuildTextBox(ThemeBrushes t)
        {
            Brush fill = t.Copy(t.ControlFill);
            Brush edge = t.Copy(t.ControlEdge);
            Brush accent = t.Copy(t.Accent);

            FrameworkElementFactory face = new FrameworkElementFactory(typeof(Border));
            face.Name = "Face";
            face.SetValue(Border.BackgroundProperty, fill);
            face.SetValue(Border.BorderBrushProperty, edge);
            face.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            face.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

            FrameworkElementFactory host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            host.SetValue(UIElement.FocusableProperty, false);
            host.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 1, 3, 1));
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            face.AppendChild(host);

            ControlTemplate template = new ControlTemplate(typeof(TextBox));
            template.VisualTree = face;

            Trigger focus = new Trigger();
            focus.Property = UIElement.IsKeyboardFocusWithinProperty;
            focus.Value = true;
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, accent, "Face"));
            template.Triggers.Add(focus);

            Trigger off = new Trigger();
            off.Property = UIElement.IsEnabledProperty;
            off.Value = false;
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            template.Triggers.Add(off);

            return template;
        }

        public static ControlTemplate BuildDot(Brush brush)
        {
            FrameworkElementFactory dot = new FrameworkElementFactory(typeof(Border));
            dot.SetValue(Border.BackgroundProperty, brush);
            dot.SetValue(Border.CornerRadiusProperty, new CornerRadius(100));   // the OS thumb's box sets the size

            ControlTemplate template = new ControlTemplate(typeof(Thumb));
            template.VisualTree = dot;
            return template;
        }

        /// <summary>One side of a slider's track: a thin bar centred in whatever box the OS gives it.</summary>
        public static ControlTemplate BuildBar(Brush brush)
        {
            FrameworkElementFactory bar = new FrameworkElementFactory(typeof(Border));
            bar.SetValue(Border.BackgroundProperty, brush);
            bar.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            bar.SetValue(FrameworkElement.HeightProperty, 4.0);
            bar.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            ControlTemplate template = new ControlTemplate(typeof(RepeatButton));
            template.VisualTree = bar;
            return template;
        }

        /// <summary>
        /// A tray menu line (EXP11): a fixed check column (so a checkmark never shifts the text), the
        /// header, the OS-like highlight, and the disabled fade. The check mark is shown by the
        /// template's IsChecked trigger - which is what makes the checkable "Run on Windows startup" read
        /// as checked through UI Automation's TogglePattern.
        ///
        /// A horizontal StackPanel with the check mark given a fixed width, rather than a Grid: a
        /// Grid's Column attached property cannot be set from a FrameworkElementFactory, and a
        /// ContentPresenter with Header content source is what actually draws the line.
        /// </summary>
        public static ControlTemplate BuildMenuItem(ThemeBrushes t)
        {
            Brush ink = t.Copy(t.Ink);
            Brush hover = t.Copy(t.ControlHover);

            FrameworkElementFactory face = new FrameworkElementFactory(typeof(Border));
            face.Name = "Face";
            face.SetValue(Border.BackgroundProperty, Brushes.Transparent);

            FrameworkElementFactory row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            FrameworkElementFactory mark = new FrameworkElementFactory(typeof(TextBlock));
            mark.Name = "Check";
            mark.SetValue(TextBlock.TextProperty, "\uE73E");   // Segoe MDL2 Assets: check
            mark.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
            mark.SetValue(TextBlock.FontSizeProperty, 12.0);
            mark.SetValue(TextBlock.ForegroundProperty, ink);
            mark.SetValue(FrameworkElement.WidthProperty, 22.0);
            mark.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            // Hidden, not Collapsed: Collapsed takes no space, which would let the text slide to the
            // left edge and put it back only when checked. Hidden reserves the check column always.
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            row.AppendChild(mark);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 16, 6));
            row.AppendChild(content);

            face.AppendChild(row);

            ControlTemplate template = new ControlTemplate(typeof(MenuItem));
            template.VisualTree = face;

            Trigger hot = new Trigger();
            hot.Property = MenuItem.IsHighlightedProperty;
            hot.Value = true;
            hot.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Face"));
            template.Triggers.Add(hot);

            Trigger marked = new Trigger();
            marked.Property = MenuItem.IsCheckedProperty;
            marked.Value = true;
            marked.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Check"));
            template.Triggers.Add(marked);

            Trigger off = new Trigger();
            off.Property = UIElement.IsEnabledProperty;
            off.Value = false;
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            template.Triggers.Add(off);

            return template;
        }

        /// <summary>
        /// The menu's own frame (EXP11). The OS ContextMenu template draws the system menu brush for
        /// its background, so setting ContextMenu.Background is not enough - the frame has to be
        /// ours, exactly as the CheckBox and Slider templates are.
        /// </summary>
        public static ControlTemplate BuildContextMenu(ThemeBrushes t)
        {
            FrameworkElementFactory frame = new FrameworkElementFactory(typeof(Border));
            frame.Name = "Frame";
            frame.SetValue(Border.BackgroundProperty, t.Copy(t.WindowBg));
            frame.SetValue(Border.BorderBrushProperty, t.Copy(t.RowEdge));
            frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            frame.SetValue(Border.PaddingProperty, new Thickness(0, 4, 0, 4));

            FrameworkElementFactory host = new FrameworkElementFactory(typeof(StackPanel));
            host.Name = "Items";
            host.SetValue(Panel.IsItemsHostProperty, true);
            frame.AppendChild(host);

            ControlTemplate template = new ControlTemplate(typeof(ContextMenu));
            template.VisualTree = frame;
            return template;
        }

        /// <summary>The thin line between a menu's groups, in the row edge color.</summary>
        public static ControlTemplate BuildSeparator(ThemeBrushes t)
        {
            FrameworkElementFactory line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(Border.BackgroundProperty, t.Copy(t.RowEdge));
            line.SetValue(FrameworkElement.HeightProperty, 1.0);
            line.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 4, 10, 4));

            ControlTemplate template = new ControlTemplate(typeof(Separator));
            template.VisualTree = line;
            return template;
        }
    }
}
