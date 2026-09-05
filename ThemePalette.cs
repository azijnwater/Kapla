using System;
using System.Globalization;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;

namespace Kapla
{
    internal static class ThemePalette
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SolidColorBrush, SolidColorBrush> themeSources =
            new System.Runtime.CompilerServices.ConditionalWeakTable<SolidColorBrush, SolidColorBrush>();

        internal static void Apply(DependencyObject element, bool dark)
        {
            if (element == null)
            {
                return;
            }
            var frameworkElement = element as FrameworkElement;
            // A control template must keep its TemplateBindings. Replacing them with
            // local brushes freezes tab selection, hover, and disabled visuals.
            if (frameworkElement != null && frameworkElement.TemplatedParent is Control)
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                    Apply(VisualTreeHelper.GetChild(element, index), dark);
                return;
            }
            var border = element as Border;
            if (border != null)
            {
                border.Background = Map(border.Background, dark);
                border.BorderBrush = Map(border.BorderBrush, dark);
            }
            var panel = element as Panel;
            if (panel != null) panel.Background = Map(panel.Background, dark);
            var text = element as TextBlock;
            if (text != null && DependencyPropertyHelper.GetValueSource(text, TextBlock.ForegroundProperty).BaseValueSource != BaseValueSource.Inherited)
                text.Foreground = Map(text.Foreground, dark);
            var control = element as Control;
            if (control != null)
            {
                control.Background = Map(control.Background, dark);
                control.Foreground = Map(control.Foreground, dark);
                control.BorderBrush = Map(control.BorderBrush, dark);
            }
            var shape = element as System.Windows.Shapes.Shape;
            if (shape != null)
            {
                shape.Fill = Map(shape.Fill, dark);
                shape.Stroke = Map(shape.Stroke, dark);
            }
            var count = VisualTreeHelper.GetChildrenCount(element);
            for (var index = 0; index < count; index++)
            {
                Apply(VisualTreeHelper.GetChild(element, index), dark);
            }
        }

        internal static Brush Map(Brush value, bool dark)
        {
            var solid = value as SolidColorBrush;
            if (solid == null)
            {
                return value;
            }
            var color = solid.Color;
            SolidColorBrush source;
            if (themeSources.TryGetValue(solid, out source))
            {
                return dark ? solid : source;
            }
            var rgb = String.Format(CultureInfo.InvariantCulture, "{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            string replacement = null;
            if (dark)
            {
                if (rgb == "FDF8F4") replacement = "#171A1F";
                else if (rgb == "FFFFFF") replacement = "#232830";
                else if (rgb == "EFE8E8" || rgb == "F7F0EA" || rgb == "E7E0DC" || rgb == "DDF3FC") replacement = "#2A3038";
                else if (rgb == "1A1111" || rgb == "261D1B") replacement = "#F4F0EC";
                else if (rgb == "8A7E7A" || rgb == "AB9F9A" || rgb == "6F625E" || rgb == "9E9490" || rgb == "9B908C") replacement = "#AAB3BD";
                else if (rgb == "4D9FC4" || rgb == "285D78" || rgb == "5FAED2") replacement = "#55B8F6";
                else if (rgb == "E8DDD7" || rgb == "DED4CF" || rgb == "D7DFDA" || rgb == "A7DDF7") replacement = "#38414C";
            }
            else
            {
                if (rgb == "171A1F") replacement = "#FDF8F4";
                else if (rgb == "232830") replacement = "#FFFFFF";
                else if (rgb == "2A3038") replacement = "#EFE8E8";
                else if (rgb == "F4F0EC" || rgb == "DCE3EA") replacement = "#1A1111";
                else if (rgb == "AAB3BD") replacement = "#8A7E7A";
                else if (rgb == "55B8F6" || rgb == "8DD3FF") replacement = "#7DD3FC";
                else if (rgb == "38414C" || rgb == "405063") replacement = "#E8DDD7";
            }
            if (replacement == null)
            {
                return value;
            }
            var mapped = (Color)ColorConverter.ConvertFromString(replacement);
            mapped.A = color.A;
            var result = new SolidColorBrush(mapped);
            if (dark) themeSources.Add(result, solid);
            return result;
        }

    }
}
