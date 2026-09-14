// =============================================================================
//  DemoBBiCore.Suporte.cs  —  Suporte do app de demonstração, num único arquivo
// -----------------------------------------------------------------------------
//  Consolidado por decisão do dono do repositório (facilita copiar/colar no
//  GitHub). Duas namespaces no mesmo arquivo: DemoBBiCore.Modelos (domínio/mapas)
//  e DemoBBiCore (implementações de apoio do harness). Nada disso é da BBiCore.
// =============================================================================

using System.Globalization;
using BBiCore.Importacao;

namespace DemoBBiCore.Modelos
{
    // ─────────────────────────────────────────────────────────────────────
    // Seção: DemoBBiCore.Modelos.cs
    // ─────────────────────────────────────────────────────────────────────
// =============================================================================
//  DemoBBiCore.Modelos.cs  —  Modelos e mapas do app de demonstração
// -----------------------------------------------------------------------------
//  Reúne, em um arquivo só, os tipos de domínio e os mapas de importação usados
//  pelas páginas de teste. Consome a RCL BBiCore.
//
//  >>> NOTA PARA REORGANIZAÇÃO: na separação, cada tipo vira um arquivo em
//  Modelos/ (Anime, StatusAnime, FatoMercado) e os mapas em Modelos/ ou
//  Mapping/ (MapaAnimes, MapaMercado). Namespace atual: DemoBBiCore.Modelos.
// =============================================================================
// #region Domínio: animes  ->  destino sugerido: Modelos/

/// <summary>Situação de exibição de um anime no catálogo.</summary>
public enum StatusAnime
{
    /// <summary>Status não reconhecido no arquivo de origem.</summary>
    Desconhecido,

    /// <summary>Série finalizada.</summary>
    Concluido,

    /// <summary>Série em exibição.</summary>
    EmLancamento,

    /// <summary>Série pausada.</summary>
    Hiato
}

/// <summary>Entidade de anime produzida pela importação do catálogo.</summary>
public sealed class Anime
{
    /// <summary>Identificador do anime.</summary>
    public int Id { get; set; }

    /// <summary>Título do anime.</summary>
    public string Titulo { get; set; } = string.Empty;

    /// <summary>Gênero (texto livre).</summary>
    public string Genero { get; set; } = string.Empty;

    /// <summary>Nota média (usa ponto decimal na origem).</summary>
    public decimal NotaMedia { get; set; }

    /// <summary>Quantidade de episódios.</summary>
    public int Episodios { get; set; }

    /// <summary>Ano de lançamento.</summary>
    public int AnoLancamento { get; set; }

    /// <summary>Situação de exibição.</summary>
    public StatusAnime Status { get; set; }
}

// #endregion

// #region Domínio: mercado (despivot)  ->  destino sugerido: Modelos/

/// <summary>Registro despivotado de um relatório rede × cidade.</summary>
public sealed class FatoMercado
{
    /// <summary>Nome da rede de supermercado.</summary>
    public string Rede { get; set; } = string.Empty;

    /// <summary>Cidade (coluna despivotada).</summary>
    public string Cidade { get; set; } = string.Empty;

    /// <summary>Quantidade registrada na célula rede × cidade.</summary>
    public int Quantidade { get; set; }

    /// <summary>Linha física da rede no arquivo original (rastreabilidade do despivot).</summary>
    public int LinhaOrigem { get; set; }
}

// #endregion

// #region Mapas  ->  destino sugerido: Mapping/ (ou Modelos/)

/// <summary>Mapa de importação do catálogo de animes (cabeçalho: id, titulo, genero, nota_media, episodios, ano_lancamento, status).</summary>
public static class MapaAnimes
{
    /// <summary>Cria o mapa padrão para o CSV/Excel de animes.</summary>
    /// <returns>Mapa configurado.</returns>
    public static MapaImportacao<Anime> Padrao()
        => MapaImportacao<Anime>.ComCabecalho()
            .Coluna(a => a.Id, "id", obrigatoria: true)
            .Coluna(a => a.Titulo, "titulo", obrigatoria: true)
            .Coluna(a => a.Genero, "genero")
            // nota_media usa PONTO decimal → cultura invariante nesta coluna.
            .Coluna(a => a.NotaMedia, "nota_media", cultura: CultureInfo.InvariantCulture)
            .Coluna(a => a.Episodios, "episodios")
            .Coluna(a => a.AnoLancamento, "ano_lancamento")
            // status vira enum via callback; valor desconhecido cai no padrão (não é falha).
            .Coluna(a => a.Status, "status", valorPadrao: StatusAnime.Desconhecido,
                    callback: (bruto, _) => MapearStatus(bruto));

    /// <summary>Converte o texto de status do arquivo no enum correspondente.</summary>
    /// <param name="bruto">Texto de status lido do arquivo.</param>
    /// <returns>Status correspondente, ou <see cref="StatusAnime.Desconhecido"/>.</returns>
    private static StatusAnime MapearStatus(string bruto)
    {
        switch (bruto.Trim().ToLowerInvariant())
        {
            case "concluído":
            case "concluido":
                return StatusAnime.Concluido;
            case "em lançamento":
            case "em lancamento":
                return StatusAnime.EmLancamento;
            case "hiato":
                return StatusAnime.Hiato;
            default:
                return StatusAnime.Desconhecido;
        }
    }
}

/// <summary>Mapa para o relatório rede × cidade já despivotado em { rede, cidade, quantidade }.</summary>
public static class MapaMercado
{
    /// <summary>Cria o mapa longo. Combine com um <see cref="LeitorDespivotado"/> no consumo.</summary>
    /// <returns>Mapa configurado.</returns>
    public static MapaImportacao<FatoMercado> Padrao()
        => MapaImportacao<FatoMercado>.ComCabecalho()
            .Coluna(x => x.Rede, "rede", obrigatoria: true)
            .Coluna(x => x.Cidade, "cidade", obrigatoria: true)
            .Coluna(x => x.Quantidade, "quantidade")
            .Coluna(x => x.LinhaOrigem, "linha_origem");
}

// #endregion

// #region Domínio: e-mail  ->  destino sugerido: Modelos/

/// <summary>Objeto de dados de exemplo para os templates de e-mail (flat, um nível).</summary>
public sealed class PedidoCliente
{
    /// <summary>Nome do cliente.</summary>
    public string NomeCliente { get; set; } = string.Empty;

    /// <summary>E-mail do cliente (destinatário).</summary>
    public string EmailCliente { get; set; } = string.Empty;

    /// <summary>Número do pedido.</summary>
    public int NumeroPedido { get; set; }

    /// <summary>Valor total do pedido.</summary>
    public decimal Total { get; set; }

    /// <summary>Data do pedido.</summary>
    public DateTime DataPedido { get; set; }

    /// <summary>Situação do pedido.</summary>
    public string Status { get; set; } = string.Empty;
}

// #endregion
}



namespace DemoBBiCore
{
    using BBiCore.Email;
    // ─────────────────────────────────────────────────────────────────────
    // Seção: RepositorioAnexosMemoria.cs
    // ─────────────────────────────────────────────────────────────────────
    // Implementação em memória do acervo de anexos — só para o projeto de demonstração
    // funcionar sem banco. Em produção, cada sistema implementa contra as suas tabelas
    // (ver BBiCore/Email/BBiCore-Email.sql).
    /// <summary>Acervo de anexos e vínculos guardados em memória (apenas para a demonstração).</summary>
    public sealed class RepositorioAnexosMemoria : IRepositorioAnexos
{
    /// <summary>Arquivos do acervo, por id.</summary>
    private readonly Dictionary<int, IAnexoAcervo> _acervo = [];

    /// <summary>Vínculos entre templates e arquivos.</summary>
    private readonly List<IVinculoAnexo> _vinculos = [];

    /// <summary>Próximo id de anexo.</summary>
    private int _proximoAnexo = 1;

    /// <summary>Próximo id de vínculo.</summary>
    private int _proximoVinculo = 1;

    /// <inheritdoc/>
    public Task<IReadOnlyList<IAnexoAcervo>> ListarDisponiveisAsync(int idTemplate, CancellationToken cancelamento = default)
    {
        // Todos do acervo, menos os reivindicados com exclusividade por OUTROS templates.
        HashSet<int> reivindicados = [.. _vinculos
            .Where(v => v.Exclusivo && v.IdTemplate != idTemplate)
            .Select(v => v.IdAnexo)];

        IReadOnlyList<IAnexoAcervo> disponiveis = [.. _acervo.Values.Where(a => !reivindicados.Contains(a.Id))];
        return Task.FromResult(disponiveis);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AnexoVinculado>> ListarDoTemplateAsync(int idTemplate, CancellationToken cancelamento = default)
    {
        IReadOnlyList<AnexoVinculado> itens = [.. _vinculos
            .Where(v => v.IdTemplate == idTemplate)
            .Where(v => _acervo.ContainsKey(v.IdAnexo))
            .Select(v => new AnexoVinculado(_acervo[v.IdAnexo], v))];

        return Task.FromResult(itens);
    }

    /// <inheritdoc/>
    public Task<bool> EstaReivindicadoPorOutroAsync(int idAnexo, int idTemplateAtual, CancellationToken cancelamento = default)
        => Task.FromResult(_vinculos.Any(v => v.IdAnexo == idAnexo && v.Exclusivo && v.IdTemplate != idTemplateAtual));

    /// <inheritdoc/>
    public Task<bool> EstaEmUsoPorOutroAsync(int idAnexo, int idTemplateAtual, CancellationToken cancelamento = default)
        => Task.FromResult(_vinculos.Any(v => v.IdAnexo == idAnexo && v.IdTemplate != idTemplateAtual));

    /// <inheritdoc/>
    public Task<int> SalvarAnexoAsync(IAnexoAcervo anexo, CancellationToken cancelamento = default)
    {
        if (anexo.Id == 0)
            anexo.Id = _proximoAnexo++;

        _acervo[anexo.Id] = anexo;
        return Task.FromResult(anexo.Id);
    }

    /// <inheritdoc/>
    public Task SalvarVinculoAsync(IVinculoAnexo vinculo, CancellationToken cancelamento = default)
    {
        if (vinculo.Id == 0)
        {
            vinculo.Id = _proximoVinculo++;
            _vinculos.Add(vinculo);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoverVinculoAsync(int idVinculo, CancellationToken cancelamento = default)
    {
        _vinculos.RemoveAll(v => v.Id == idVinculo);
        return Task.CompletedTask;
    }
}

    // ─────────────────────────────────────────────────────────────────────
    // Seção: SemeadorAcervo.cs
    // ─────────────────────────────────────────────────────────────────────
// Semeia o ACERVO de anexos com arquivos de exemplo, para a demonstração exercitar
// os quatro papéis (cabeçalho, rodapé, imagem no corpo e anexo) e os três modos de
// obtenção. Só existe no harness — não faz parte da biblioteca.
/// <summary>Popula o acervo de anexos com exemplos, na subida da aplicação.</summary>
public static class SemeadorAcervo
{
    /// <summary>Imagem de cabeçalho (600x80), em base64.</summary>
    private const string CabecalhoBase64 = "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";

    /// <summary>Imagem de rodapé (600x50), em base64.</summary>
    private const string RodapeBase64 = "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";

    /// <summary>Selo para inserir no meio do corpo (120x120), em base64.</summary>
    private const string SeloBase64 = "iVBORw0KGgoAAAANSUhEUgAAAHgAAAB4CAIAAAC2BqGFAAADGElEQVR4nO3cT0iTcRzH8Z9zzkaGLloUZVB0SAlqhyDtUlAGEalBBEFElAc9VV4qrItUEFgG5SU8lGDdVPxDaDZPbiWUqYV6mJaampILjTWZWwclPJh/gr0d9XndHh6+D9+9N348p8UllOUbiT7Lai/wv1BoiEJDFBqi0BCFhig0RKEhCg1RaIhCQxQaotAQhYYoNEShIQoNUWiIQkMUGqLQEIWGKDREoSEKDVFoiEJDFBqi0BCFhig0RKEhCg1RaIhCQxQaotAQhYYoNEShIQoNUWiIQkMUGqLQEIWGKDREoSEKDVFoiEJDFBqi0BCFhig0xLraCyzA5Uy9tT8nwRIfCofz3BWDUxPf8x60fe2fvVvX31H6vnnsQomzvHD+1LldGQW7D06HQzaL9WGnu6LHuwqr/1kshn586Gx2Q9nQlD93h+tu5skzjeXT4dDhmvuLjGSlpp9PyzxaW+oPBlIS7dXHCr788DcPdmM7LykWQzvt69bEJxhj6vo7xgKTyxm5svfI1dYqfzBgjPEHA9c8VTf3HVfoJdx4XePOKXzxuauy903LUO9yRtIcm9rHB35fvhsbSHdsjtqCfyMWQz/t9tb2dZzYvqfkwKnqvvbitnqbxfoy+/Ls3SJvjXfUt/gT4uJMxESiv+kKxFxopz1pZ/JGz4jvSben4VPn29NFxW31S57RHyeGXc5Uz8jcF+DasO3Dt2Fk3+WKude7SMQ8y7q4NclhjFmfmDQwObGcqXvtTXcycpNtdmNMSqL9dkZOSXtjdBddoZj7RY//nMpvqXyelReYmZ4JR/LcFcaY+UeHd9RX5K2xWawtuXOvd63Dvuveqi1rHU3Zl4IzIZvF+qjL/WqwZ9U+w0Li9P/RjJg7Ov5VCg1RaIhCQxQaotAQhYYoNEShIQoNUWiIQkMUGqLQEIWGKDREoSEKDVFoiEJDFBqi0BCFhig0RKEhCg1RaIhCQxQaotAQhYYoNEShIQoNUWiIQkMUGqLQEIWGKDREoSEKDVFoiEJDFBqi0BCFhig0RKEhCg1RaIhCQxQaotAQhYb8Anp6ulxXbgFkAAAAAElFTkSuQmCC";

    /// <summary>Cadastra os arquivos de exemplo no acervo.</summary>
    /// <param name="repositorio">Acervo a popular.</param>
    public static async Task SemearAsync(IRepositorioAnexos repositorio)
    {
        ArgumentNullException.ThrowIfNull(repositorio);

        // IMAGENS — podem ser cabeçalho, rodapé, imagem do corpo ou anexo comum.
        await repositorio.SalvarAnexoAsync(new AnexoAcervoDto
        {
            NomeArquivo = "cabecalho-institucional.png",
            ContentType = "image/png",
            Descricao = "Faixa vermelha com o nome do sistema",
            ModoObtencao = ModoObtencaoAnexo.BytesNoBanco,
            Conteudo = Convert.FromBase64String(CabecalhoBase64)
        });

        await repositorio.SalvarAnexoAsync(new AnexoAcervoDto
        {
            NomeArquivo = "rodape-institucional.png",
            ContentType = "image/png",
            Descricao = "Rodapé escuro com aviso de mensagem automática",
            ModoObtencao = ModoObtencaoAnexo.BytesNoBanco,
            Conteudo = Convert.FromBase64String(RodapeBase64)
        });

        await repositorio.SalvarAnexoAsync(new AnexoAcervoDto
        {
            NomeArquivo = "selo-aprovado.png",
            ContentType = "image/png",
            Descricao = "Selo para posicionar no meio do corpo (modo avançado)",
            ModoObtencao = ModoObtencaoAnexo.BytesNoBanco,
            Conteudo = Convert.FromBase64String(SeloBase64)
        });

        // NÃO-IMAGEM — só pode ser anexo. Tentar usá-lo como cabeçalho é RECUSADO pela biblioteca.
        await repositorio.SalvarAnexoAsync(new AnexoAcervoDto
        {
            NomeArquivo = "termos-de-uso.pdf",
            ContentType = "application/pdf",
            Descricao = "Documento fixo, guardado no acervo",
            ModoObtencao = ModoObtencaoAnexo.BytesNoBanco,
            Conteudo = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 (exemplo)")
        });

        // CAMINHO DINÂMICO — o arquivo não existe agora: o caminho é resolvido a cada envio.
        await repositorio.SalvarAnexoAsync(new AnexoAcervoDto
        {
            NomeArquivo = "relatorio-do-pedido.csv",
            ContentType = "text/csv",
            Descricao = "Gerado por pedido — o caminho tem marcador",
            ModoObtencao = ModoObtencaoAnexo.CaminhoDinamico,
            Caminho = "relatorios/pedido{{NumeroPedido}}.csv"
        });
    }
}

}