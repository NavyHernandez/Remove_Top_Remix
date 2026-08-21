namespace Remove_Top.Helpers
{
    /// <summary>
    /// Centraliza los enlaces de la versión premium de la aplicación.
    ///
    /// Para cambiar el destino al que redirige el botón
    /// "Adquiere la versión premium" (p. ej. una página de pago o una landing
    /// distinta) basta con editar la constante <see cref="UpgradeUrl"/>: todos
    /// los puntos que la usan (hoy <c>DuplicateRemovalPage</c>) la leen de aquí.
    /// </summary>
    public static class PremiumLinks
    {
        /// <summary>
        /// URL de la página premium a la que se lleva al usuario al pulsar el
        /// botón de actualización. CAMBIAR AQUÍ MANUALMENTE el enlace real.
        /// </summary>
        public const string UpgradeUrl = "https://www.top-remix.com/premium";
    }
}
