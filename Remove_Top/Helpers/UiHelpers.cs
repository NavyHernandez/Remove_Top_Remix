using FluentIcons.Common;
using FluentIcons.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Remove_Top.Helpers
{
    /// <summary>
    /// Utilidades para construir iconos Fluent y contenido de botones
    /// de forma consistente en toda la aplicación.
    /// </summary>
    public static class UiHelpers
    {
        /// <summary>
        /// Crea un <see cref="FluentIcon"/> con la variante, tamaño y color indicados.
        /// </summary>
        public static FluentIcon Icon(
            Icon symbol,
            IconVariant variant = IconVariant.Regular,
            IconSize size = IconSize.Size20,
            Brush? foreground = null)
        {
            var icon = new FluentIcon
            {
                Icon = symbol,
                IconVariant = variant,
                IconSize = size
            };
            if (foreground != null) icon.Foreground = foreground;
            return icon;
        }

        /// <summary>
        /// Construye el contenido de un botón: un icono Fluent + texto,
        /// respetando el color del primer plano si se indica.
        /// </summary>
        public static object Content(
            Icon symbol,
            string text,
            double textSize = 14,
            Brush? foreground = null,
            bool semibold = true,
            IconSize iconSize = IconSize.Size16)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var icon = Icon(symbol, IconVariant.Regular, iconSize, foreground);
            panel.Children.Add(icon);

            var label = new TextBlock
            {
                Text = text,
                FontSize = textSize,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
            };
            if (foreground != null) label.Foreground = foreground;
            panel.Children.Add(label);

            return panel;
        }
    }
}
