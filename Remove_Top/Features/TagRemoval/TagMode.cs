namespace Remove_Top.Features.TagRemoval
{
    /// <summary>
    /// Modo de procesamiento de la página de Etiquetas:
    /// <see cref="Clear"/> borra todas las tags y <see cref="Replace"/>
    /// las reescribe con los valores nuevos.
    /// </summary>
    public enum TagMode
    {
        Clear,
        Replace
    }
}