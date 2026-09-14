/// <summary>
/// Contrato de reordenação devolvido pelo JS.
/// Fase 1 (lista única): ListaOrigem == ListaDestino.
/// Campos de origem/destino já preparados para a fase 2 (múltiplas listas).
/// </summary>
public sealed class ReordenacaoDto
{
    public string? ChaveMovida { get; set; }
    public int IndiceOrigem { get; set; }
    public int IndiceDestino { get; set; }
    public string? ListaOrigem { get; set; }
    public string? ListaDestino { get; set; }
    public List<string> NovaOrdemChaves { get; set; } = new();
}