using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace Remove_Top.Features.QuickRename
{
    /// <summary>
    /// Convierte un <see cref="NamePart"/> en el color de fondo del chip:
    ///   - Fuente de unión (IsJoinSource): naranja sólido al 45 % (seleccionado).
    ///   - Bloque unido (IsMerged): naranja al 30 %.
    ///   - Chip normal: naranja al 12 %.
    /// Solo se usa en la sección de reordenar partes.
    /// </summary>
    public class ChipBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is NamePart part)
            {
                if (part.IsJoinSource)
                    return new SolidColorBrush(Color.FromArgb(0x73, 0xE6, 0x7E, 0x22));
                if (part.IsMerged)
                    return new SolidColorBrush(Color.FromArgb(0x4D, 0xE6, 0x7E, 0x22));
            }

            return new SolidColorBrush(Color.FromArgb(0x1F, 0xE6, 0x7E, 0x22));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}