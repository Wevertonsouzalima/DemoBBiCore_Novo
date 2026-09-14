// =============================================================================
//  ModoSeletorData.cs  —  Enums do BBISeletorData (BBiCore.SeletorData)
// =============================================================================

namespace BBiCore.SeletorData
{
    /// <summary>Define o que o <c>BBISeletorData</c> permite selecionar.</summary>
    public enum ModoSeletorData
    {
        /// <summary>Uma única data. Exibe e aceita <c>dd/MM/yyyy</c>.</summary>
        Data,

        /// <summary>Data e hora. Exibe e aceita <c>dd/MM/yyyy HH:mm</c>.</summary>
        DataHora,

        /// <summary>Um intervalo entre duas datas (início e fim).</summary>
        Periodo
    }

    /// <summary>
    /// Nível atual do calendário durante a navegação em drill-down (dias → meses → anos).
    /// Uso interno do componente.
    /// </summary>
    internal enum NivelNavegacao
    {
        /// <summary>Grade de dias do mês.</summary>
        Dias,

        /// <summary>Grade de meses do ano.</summary>
        Meses,

        /// <summary>Grade de anos da década.</summary>
        Anos
    }
}
