using FluentIcons.Common;
using Microsoft.UI.Xaml.Data;
using System;
using System.IO;

namespace Remove_Top.Helpers
{
    /// <summary>
    /// Convierte el nombre/extension de un archivo en un icono Fluent
    /// representativo de su tipo (audio, video, imagen o documento).
    /// Se usa en la vista previa de archivos afectados.
    /// </summary>
    public class FileTypeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var name = value as string;
            var ext = Path.GetExtension(name ?? "")?.ToLowerInvariant();

            return ext switch
            {
                ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg" or ".wma" or ".aiff" or ".aif" or ".wv" => Icon.MusicNote2,
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" or ".ts" => Icon.Video,
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff" or ".tif" or ".webp" or ".svg" => Icon.Image,
                _ => Icon.Document
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
