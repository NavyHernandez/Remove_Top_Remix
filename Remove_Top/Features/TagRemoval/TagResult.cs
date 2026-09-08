using FluentIcons.Common;

namespace Remove_Top.Features.TagRemoval
{
    /// <summary>Resultado del procesamiento de un único archivo.</summary>
    public class TagResult
    {
        /// <summary>Nombre del archivo.</summary>
        public string FileName { get; set; } = "";

        /// <summary>Indica si la operación terminó correctamente.</summary>
        public bool Success { get; set; }

        /// <summary>Mensaje de resultado o de error.</summary>
        public string Message { get; set; } = "";

        /// <summary>Icono de estado (check verde / cruz roja).</summary>
        public Icon StatusIcon => Success ? Icon.CheckmarkCircle : Icon.DismissCircle;
    }
}