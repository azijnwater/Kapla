using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;

namespace Kapla
{
    internal static class SvgIconFactory
    {
        private static Color accentColor = Color.FromRgb(125, 211, 252);

        public static Color AccentColor
        {
            get { return accentColor; }
            set { accentColor = value; }
        }

        public static Viewbox Load(string fileName, double width, double height)
        {
            var viewbox = new Viewbox
            {
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
                StretchDirection = StretchDirection.Both,
                IsHitTestVisible = false
            };

            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Figma", fileName);
            if (!File.Exists(filePath))
            {
                viewbox.Child = new Canvas { Width = width, Height = height };
                return viewbox;
            }

            var document = new XmlDocument();
            document.Load(filePath);
            var svg = document.DocumentElement;
            var sourceWidth = ParseNumber(svg == null ? null : svg.GetAttribute("width"), width);
            var sourceHeight = ParseNumber(svg == null ? null : svg.GetAttribute("height"), height);
            var canvas = new Canvas
            {
                Width = sourceWidth,
                Height = sourceHeight,
                ClipToBounds = false,
                IsHitTestVisible = false
            };

            if (svg != null)
            {
                AddElements(svg, canvas);
            }
            viewbox.Child = canvas;
            return viewbox;
        }

        private static void AddElements(XmlNode parent, Canvas canvas)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                var element = node as XmlElement;
                if (element == null)
                {
                    continue;
                }

                if (String.Equals(element.LocalName, "defs", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(element.LocalName, "clipPath", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (String.Equals(element.LocalName, "path", StringComparison.OrdinalIgnoreCase))
                {
                    AddPath(element, canvas);
                }
                else if (String.Equals(element.LocalName, "circle", StringComparison.OrdinalIgnoreCase))
                {
                    AddCircle(element, canvas);
                }
                else
                {
                    AddElements(element, canvas);
                }
            }
        }

        private static void AddPath(XmlElement element, Canvas canvas)
        {
            var data = element.GetAttribute("d");
            if (String.IsNullOrWhiteSpace(data))
            {
                return;
            }

            var shape = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Fill = Paint(element, "fill", "fill-opacity"),
                Stroke = Paint(element, "stroke", "stroke-opacity"),
                StrokeThickness = ParseNumber(element.GetAttribute("stroke-width"), 1),
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            if (String.Equals(element.GetAttribute("stroke-linecap"), "round", StringComparison.OrdinalIgnoreCase))
            {
                shape.StrokeStartLineCap = PenLineCap.Round;
                shape.StrokeEndLineCap = PenLineCap.Round;
            }
            canvas.Children.Add(shape);
        }

        private static void AddCircle(XmlElement element, Canvas canvas)
        {
            var radius = ParseNumber(element.GetAttribute("r"), 0);
            var centerX = ParseNumber(element.GetAttribute("cx"), 0);
            var centerY = ParseNumber(element.GetAttribute("cy"), 0);
            var circle = new System.Windows.Shapes.Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = Paint(element, "fill", "fill-opacity"),
                Stroke = Paint(element, "stroke", "stroke-opacity"),
                StrokeThickness = ParseNumber(element.GetAttribute("stroke-width"), 1),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(circle, centerX - radius);
            Canvas.SetTop(circle, centerY - radius);
            canvas.Children.Add(circle);
        }

        private static Brush Paint(XmlElement element, string colorAttribute, string opacityAttribute)
        {
            var value = element.GetAttribute(colorAttribute);
            if (String.IsNullOrWhiteSpace(value) || String.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            Color color;
            if (String.Equals(value, "white", StringComparison.OrdinalIgnoreCase))
            {
                color = Colors.White;
            }
            else
            {
                color = (Color)ColorConverter.ConvertFromString(value);
            }

            if (color.R == 255 && color.G == 109 && color.B == 17)
            {
                color = AccentColor;
            }

            var opacity = ParseNumber(element.GetAttribute(opacityAttribute), 1)
                * ParseNumber(element.GetAttribute("opacity"), 1);
            color.A = (byte)Math.Max(0, Math.Min(255, Math.Round(255 * opacity)));
            return new SolidColorBrush(color);
        }

        private static double ParseNumber(string value, double fallback)
        {
            double number;
            return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : fallback;
        }
    }
}
