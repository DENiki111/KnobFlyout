using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using KnobFlyout.Profiles;

namespace KnobFlyout.Osd
{
    /// <summary>Bausteine des Designs „macOS“ (Designs.Mac), gemeinsam für OSD-Karten, Neue-App-Popup und Media-Flyout.</summary>
    internal static class MacParts
    {
        public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        /// <summary>Weißer runder Knopf mit feinem Rand und kleinem Schatten (Regler, Schalter, Fortschrittsleiste).</summary>
        public static Ellipse Knob(double size, HorizontalAlignment align, double margin = 0) => new()
        {
            Width = size, Height = size, Fill = Brushes.White, Stroke = UiColor.Brush("#24000000"), StrokeThickness = 1,
            HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(margin),
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.3, Color = Colors.Black },
        };

        /// <summary>macOS-Schalter: grün mit Knopf rechts (an) bzw. grau mit Knopf links (aus).</summary>
        public static Border Switch(bool on, DesignPalette pal) => new()
        {
            Width = 36, Height = 20, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center,
            Background = UiColor.Brush(on ? pal.On : pal.Off),
            Child = Knob(16, on ? HorizontalAlignment.Right : HorizontalAlignment.Left, 2),
        };

        /// <summary>Abgerundete Kachel (Radius = ¼ der Größe, wie App-Symbole) mit einem Fluent-Symbol.</summary>
        public static Border GlyphTile(string glyph, double size, double glyphSize, Brush bg, Brush fg) => new()
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 4), Background = bg,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = glyphSize, Foreground = fg,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };

        /// <summary>Profil als App-Kachel: Profilfarbe (oben etwas heller) mit weißem Logo bzw. eigenes Bild über die ganze Kachel.</summary>
        public static Border ProfileTile(ProfileDefinition def, double size, double glyphSize)
        {
            var logo = def.CreateLogo(glyphSize, Brushes.White);
            var tile = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 4), VerticalAlignment = VerticalAlignment.Center,
                Background = TileGradient(def.Color), Child = logo,
            };
            if (logo is Image img)
            {
                img.Width = img.Height = size;
                img.Stretch = Stretch.UniformToFill;
                tile.Clip = new RectangleGeometry(new Rect(0, 0, size, size), size / 4, size / 4);
            }
            return tile;
        }

        private static Brush TileGradient(string hex)
        {
            var c = UiColor.Brush(hex).Color;
            byte Up(byte v) => (byte)Math.Round(v + (255 - v) * 0.22);
            var b = new LinearGradientBrush(Color.FromRgb(Up(c.R), Up(c.G), Up(c.B)), c, 90);
            b.Freeze();
            return b;
        }
    }
}
