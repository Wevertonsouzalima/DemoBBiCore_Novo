// =============================================================================
//  Email.cs  —  Módulo de e-mail (BBiCore.Email), consolidado num único arquivo
// -----------------------------------------------------------------------------
//  Arquivo único por decisão do dono da DLL (facilita copiar/colar direto na
//  interface web do GitHub, sem precisar baixar o repositório). Cada seção abaixo
//  corresponde a um antigo arquivo separado — os comentários de cabeçalho de cada
//  um foram preservados dentro da sua seção, então a #region ainda indica a
//  origem e o destino sugerido numa futura separação de arquivos.
//
//  EXCEÇÃO DE PROPÓSITO: TransporteExchange.cs FICA DE FORA deste arquivo — o
//  .csproj exclui esse arquivo da compilação por padrão (chave
//  IncluirTransporteExchange=false), pois o pacote da EWS não tem versão estável.
//  Fundir esse código aqui obrigaria TODO o Email.cs a depender do pacote da EWS
//  o tempo todo, quebrando o build padrão do projeto.
// =============================================================================

using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using BBiCore.Templates;
using HtmlAgilityPack;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace BBiCore.Email
{
    // ───────────────────────────────────────────────────────────────────────
    // Seção: Email.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  Email.cs  —  Módulo de e-mail: motor de template, contrato, envio e
//  repositório (namespace BBiCore.Email). Componentes BBiEmail/BBiComporEmail
//  na mesma pasta.
// =============================================================================
// #region Enums do contrato  ->  destino sugerido: Models/

/// <summary>Indica em qual modo o template foi criado.</summary>
public enum OrigemCriacaoTemplate
{
    /// <summary>Modo assistido: cabeçalho/rodapé por imagem + textos simples com marcadores.</summary>
    Normal,

    /// <summary>Modo avançado: HTML editado livremente.</summary>
    Avancado
}

/// <summary>Política quando um anexo não pode ser resolvido no momento do envio.</summary>
public enum AcaoFalhaAnexo
{
    /// <summary>Envia o e-mail mesmo sem o anexo que falhou.</summary>
    EnviarMesmoAssim,

    /// <summary>Falha o envio inteiro se algum anexo não puder ser resolvido.</summary>
    FalharEnvio
}

/// <summary>
/// O que fazer quando, no momento do envio, sobrar algum marcador SEM resolver (o campo não existe
/// no objeto de dados, ou veio nulo). Espelha a política de anexos.
/// </summary>
public enum AcaoFalhaMarcador
{
    /// <summary>Aborta o envio e informa quais marcadores ficaram sem valor. É o padrão seguro.</summary>
    FalharEnvio,

    /// <summary>Envia assim mesmo, REMOVENDO do texto os trechos não resolvidos (o destinatário nunca vê um "{{campo}}" cru).</summary>
    EnviarMesmoAssim
}

/// <summary>Situação do template no cadastro.</summary>
public enum SituacaoTemplate
{
    /// <summary>Em edição: pode ser salvo incompleto (sem destinatário, com marcador faltando, corpo vazio).</summary>
    Rascunho,

    /// <summary>Pronto para uso: passou pelas validações de envio.</summary>
    Publicado
}

/// <summary>Rótulo de sensibilidade do e-mail.</summary>
public enum ClassificacaoEmail
{
    /// <summary>Uso interno.</summary>
    Interno,

    /// <summary>Conteúdo confidencial.</summary>
    Confidencial,

    /// <summary>Conteúdo público.</summary>
    Publico
}

// #endregion

// #region Contrato (interfaces)  ->  destino sugerido: Models/ (ou Contracts/)



/// <summary>Contrato mínimo de um template de e-mail. Cada sistema implementa na sua entidade <c>Email.Templates</c>.</summary>
public interface ITemplateEmail
{
    /// <summary>Identificador do template (chave da tabela). É por ele que os anexos são vinculados.</summary>
    int Id { get; set; }

    /// <summary>Nome/identificação do template (usado como chave na persistência).</summary>
    string Nome { get; set; }

    /// <summary>Assunto do e-mail (pode conter marcadores).</summary>
    string Assunto { get; set; }

    /// <summary>Destinatários (pode conter marcadores). Lista separada por ';'.</summary>
    string Destinatarios { get; set; }

    /// <summary>Cópia (Cc), separada por ';'. Pode conter marcadores.</summary>
    string? Cc { get; set; }

    /// <summary>Cópia oculta (Cco), separada por ';'. Pode conter marcadores.</summary>
    string? Cco { get; set; }

    /// <summary>Corpo do e-mail em HTML (fonte única de verdade; pode conter marcadores).</summary>
    string Corpo { get; set; }

    /// <summary>Modo em que o template foi criado.</summary>
    OrigemCriacaoTemplate OrigemCriacao { get; set; }

    /// <summary>Situação do template: rascunho (em edição, pode estar incompleto) ou publicado (liberado para os sistemas usarem).</summary>
    SituacaoTemplate Situacao { get; set; }

    /// <summary>O que fazer quando sobrar marcador sem resolver no envio.</summary>
    AcaoFalhaMarcador AcaoNaFalhaDeMarcador { get; set; }

    /// <summary>Nome do tipo de dados que este template espera preencher (ex.: "PedidoCliente"). Apenas referência/validação.</summary>
    string TipoDadosNome { get; set; }

    /// <summary>Política a aplicar quando um anexo falhar no envio.</summary>
    AcaoFalhaAnexo AcaoNaFalhaDeAnexo { get; set; }

    /// <summary>Rótulo de sensibilidade do e-mail.</summary>
    ClassificacaoEmail Classificacao { get; set; }
}

// #endregion

// #region DTOs de conveniência  ->  destino sugerido: Models/



/// <summary>Implementação default de <see cref="ITemplateEmail"/>. Um sistema pode usá-la ou mapear sua própria entidade.</summary>
public sealed class TemplateEmailDto : ITemplateEmail
{
    /// <inheritdoc/>
    public int Id { get; set; }

    /// <inheritdoc/>
    public string Nome { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string Assunto { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string Destinatarios { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string? Cc { get; set; }

    /// <inheritdoc/>
    public string? Cco { get; set; }

    /// <inheritdoc/>
    public string Corpo { get; set; } = string.Empty;

    /// <inheritdoc/>
    public OrigemCriacaoTemplate OrigemCriacao { get; set; }

    /// <inheritdoc/>
    public SituacaoTemplate Situacao { get; set; } = SituacaoTemplate.Rascunho;

    /// <inheritdoc/>
    public AcaoFalhaMarcador AcaoNaFalhaDeMarcador { get; set; } = AcaoFalhaMarcador.FalharEnvio;

    /// <inheritdoc/>
    public string TipoDadosNome { get; set; } = string.Empty;

    /// <inheritdoc/>
    public AcaoFalhaAnexo AcaoNaFalhaDeAnexo { get; set; }

    /// <inheritdoc/>
    public ClassificacaoEmail Classificacao { get; set; }

    /// <summary>Cria uma cópia independente de um template (incluindo anexos), para persistência defensiva.</summary>
    /// <param name="origem">Template de origem.</param>
    /// <returns>Nova instância copiada.</returns>
    public static TemplateEmailDto Copiar(ITemplateEmail origem)
    {
        TemplateEmailDto copia = new()
        {
            Id = origem.Id,          // sem o Id, o template perderia o vínculo com os seus anexos
            Nome = origem.Nome,
            Assunto = origem.Assunto,
            Destinatarios = origem.Destinatarios,
            Cc = origem.Cc,
            Cco = origem.Cco,
            Corpo = origem.Corpo,
            OrigemCriacao = origem.OrigemCriacao,
            Situacao = origem.Situacao,
            AcaoNaFalhaDeMarcador = origem.AcaoNaFalhaDeMarcador,
            TipoDadosNome = origem.TipoDadosNome,
            AcaoNaFalhaDeAnexo = origem.AcaoNaFalhaDeAnexo,
            Classificacao = origem.Classificacao
        };

        // Os anexos NÃO são copiados aqui: eles vivem no acervo e são ligados por vínculo (IdTemplate).
        return copia;
    }
}

// #endregion

// #region Repositório de templates  ->  destino sugerido: Services/

/// <summary>Persistência de templates de e-mail. Cada sistema implementa (tipicamente com EF) contra a sua tabela local.</summary>
public interface IRepositorioTemplateEmail
{
    /// <summary>
    /// Lista os nomes dos templates salvos. Informe <paramref name="situacao"/> para filtrar —
    /// numa tela de seleção, o normal é pedir só os <see cref="SituacaoTemplate.Publicado"/>, para
    /// não oferecer rascunhos ao usuário.
    /// </summary>
    /// <param name="situacao">Situação desejada; nulo traz todos.</param>
    /// <param name="cancelamento">Token de cancelamento.</param>
    /// <returns>Nomes disponíveis.</returns>
    Task<IReadOnlyList<string>> ListarNomesAsync(SituacaoTemplate? situacao = null, CancellationToken cancelamento = default);

    /// <summary>Obtém um template pelo nome.</summary>
    /// <param name="nome">Nome do template.</param>
    /// <param name="cancelamento">Token de cancelamento.</param>
    /// <returns>O template, ou nulo se não existir.</returns>
    Task<ITemplateEmail?> ObterAsync(string nome, CancellationToken cancelamento = default);

    /// <summary>Salva (cria ou substitui) um template pelo nome.</summary>
    /// <param name="template">Template a salvar.</param>
    /// <param name="cancelamento">Token de cancelamento.</param>
    Task SalvarAsync(ITemplateEmail template, CancellationToken cancelamento = default);

    /// <summary>Exclui um template pelo nome.</summary>
    /// <param name="nome">Nome do template.</param>
    /// <param name="cancelamento">Token de cancelamento.</param>
    Task ExcluirAsync(string nome, CancellationToken cancelamento = default);
}

/// <summary>
/// Implementação de referência de <see cref="IRepositorioTemplateEmail"/> em memória (thread-safe). Serve para
/// testes e demonstração; em produção, cada sistema implementa a persistência real (EF Core na tabela local).
/// Guarda cópias independentes (não referências), evitando que edições posteriores afetem o que foi salvo.
/// </summary>
public sealed class RepositorioTemplateEmailMemoria : IRepositorioTemplateEmail
{
    /// <summary>Próximo identificador a atribuir (no banco, isto é a coluna IDENTITY).</summary>
    private int _proximoId = 1;

    /// <summary>Armazenamento por nome (case-insensitive).</summary>
    private readonly ConcurrentDictionary<string, ITemplateEmail> _dados =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListarNomesAsync(SituacaoTemplate? situacao = null, CancellationToken cancelamento = default)
    {
        IEnumerable<KeyValuePair<string, ITemplateEmail>> itens = _dados;

        if (situacao is not null)
            itens = itens.Where(par => par.Value.Situacao == situacao.Value);

        IReadOnlyList<string> nomes = [.. itens
            .Select(par => par.Key)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];

        return Task.FromResult(nomes);
    }

    /// <inheritdoc/>
    public Task<ITemplateEmail?> ObterAsync(string nome, CancellationToken cancelamento = default)
    {
        if (_dados.TryGetValue(nome, out ITemplateEmail? achado))
            return Task.FromResult<ITemplateEmail?>(TemplateEmailDto.Copiar(achado));

        return Task.FromResult<ITemplateEmail?>(null);
    }

    /// <inheritdoc/>
    public Task SalvarAsync(ITemplateEmail template, CancellationToken cancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        // Template novo ganha identificador — é o que permite vincular anexos a ele.
        if (template.Id == 0)
            template.Id = _proximoId++;

        if (string.IsNullOrWhiteSpace(template.Nome))
            throw new InvalidOperationException("O template precisa de um Nome para ser salvo.");

        _dados[template.Nome] = TemplateEmailDto.Copiar(template);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ExcluirAsync(string nome, CancellationToken cancelamento = default)
    {
        _dados.TryRemove(nome, out _);
        return Task.CompletedTask;
    }
}


/// <summary>E-mail de template com todos os marcadores já resolvidos, pronto para o serviço de envio.</summary>
/// <param name="Assunto">Assunto final.</param>
/// <param name="Destinatarios">Destinatários (já separados).</param>
/// <param name="Cc">Cópia.</param>
/// <param name="Cco">Cópia oculta.</param>
/// <param name="Corpo">Corpo HTML final.</param>
/// <param name="Classificacao">Rótulo de sensibilidade.</param>
/// <param name="AcaoNaFalhaDeAnexo">Política em caso de anexo que falhe.</param>
/// <param name="MarcadoresNaoResolvidos">Marcadores que ficaram SEM valor (nome do campo, sem as chaves). Vazio quando tudo resolveu.</param>
/// <param name="AcaoNaFalhaDeMarcador">Política em caso de marcador não resolvido.</param>
public sealed record EmailResolvido(
    string Assunto,
    IReadOnlyList<string> Destinatarios,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Cco,
    string Corpo,
    ClassificacaoEmail Classificacao,
    AcaoFalhaAnexo AcaoNaFalhaDeAnexo,
    IReadOnlyList<string> MarcadoresNaoResolvidos,
    AcaoFalhaMarcador AcaoNaFalhaDeMarcador);

/// <summary>Resolve um <see cref="ITemplateEmail"/> (com marcadores) em um <see cref="EmailResolvido"/> pronto para envio.</summary>
public static class ResolvedorEmail
{
    /// <summary>Resolve todos os campos do template com os dados informados.</summary>
    /// <param name="template">Template com marcadores.</param>
    /// <param name="dados">Objeto de dados (pode ser nulo).</param>
    /// <param name="motor">Motor de template configurado.</param>
    /// <returns>E-mail pronto para envio.</returns>
    public static EmailResolvido Resolver(ITemplateEmail template, object? dados, MotorTemplate motor)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(motor);

        // Acumula, de TODOS os campos, os marcadores que não encontraram valor.
        List<string> naoResolvidos = [];

        string assunto = ResolverCampo(motor, template.Assunto, dados, naoResolvidos);
        IReadOnlyList<string> para = Separar(ResolverCampo(motor, template.Destinatarios, dados, naoResolvidos));
        IReadOnlyList<string> cc = Separar(ResolverCampo(motor, template.Cc, dados, naoResolvidos));
        IReadOnlyList<string> cco = Separar(ResolverCampo(motor, template.Cco, dados, naoResolvidos));
        string corpo = ResolverCampo(motor, template.Corpo, dados, naoResolvidos);

        return new EmailResolvido(
            assunto,
            para,
            cc,
            cco,
            corpo,
            template.Classificacao,
            template.AcaoNaFalhaDeAnexo,
            naoResolvidos.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            template.AcaoNaFalhaDeMarcador);
    }

    /// <summary>Resolve um campo e acumula os marcadores que ficaram sem valor.</summary>
    /// <param name="motor">Motor de template.</param>
    /// <param name="texto">Texto do campo (pode ser nulo).</param>
    /// <param name="dados">Objeto de dados.</param>
    /// <param name="naoResolvidos">Lista que recebe os marcadores sem valor.</param>
    /// <returns>Texto resolvido (marcadores sem valor permanecem, para o serviço decidir o que fazer).</returns>
    private static string ResolverCampo(MotorTemplate motor, string? texto, object? dados, List<string> naoResolvidos)
    {
        ResultadoTemplate resultado = motor.Resolver(texto ?? string.Empty, dados);

        if (resultado.NaoResolvidos.Count > 0)
            naoResolvidos.AddRange(resultado.NaoResolvidos);

        return resultado.Texto;
    }

    /// <summary>Separa uma lista de endereços por ';', descartando vazios e espaços.</summary>
    /// <param name="valor">Texto com endereços.</param>
    /// <returns>Endereços individuais.</returns>
    private static IReadOnlyList<string> Separar(string valor)
        => valor.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Problemas encontrados em um campo de endereços.</summary>
/// <param name="Vazio">Verdadeiro quando o campo é obrigatório e não tem nenhum endereço.</param>
/// <param name="Invalidos">Endereços com formato inválido.</param>
/// <param name="Duplicados">Endereços repetidos no mesmo campo.</param>
public sealed record ProblemasEndereco(bool Vazio, IReadOnlyList<string> Invalidos, IReadOnlyList<string> Duplicados)
{
    /// <summary>Verdadeiro quando não há nenhum problema.</summary>
    public bool Ok => !Vazio && Invalidos.Count == 0 && Duplicados.Count == 0;

    /// <summary>Descrição legível dos problemas (vazia quando <see cref="Ok"/>).</summary>
    public string Mensagem
    {
        get
        {
            List<string> partes = [];
            if (Vazio) partes.Add("nenhum destinatário");
            if (Invalidos.Count > 0) partes.Add("inválido(s): " + string.Join(", ", Invalidos));
            if (Duplicados.Count > 0) partes.Add("duplicado(s): " + string.Join(", ", Duplicados));
            return string.Join("; ", partes);
        }
    }
}

/// <summary>Valida campos de endereços de e-mail (formato, vazios e duplicados).</summary>
public static class ValidadorEnderecos
{
    /// <summary>Valida uma lista de endereços separada por ';'.</summary>
    /// <param name="listaSeparadaPorPontoEVirgula">Texto do campo (já resolvido, sem marcadores).</param>
    /// <param name="obrigatorio">Se o campo precisa ter ao menos um endereço.</param>
    /// <returns>Os problemas encontrados.</returns>
    public static ProblemasEndereco Validar(string? listaSeparadaPorPontoEVirgula, bool obrigatorio)
    {
        string[] itens = (listaSeparadaPorPontoEVirgula ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool vazio = obrigatorio && itens.Length == 0;

        List<string> invalidos = [];
        List<string> duplicados = [];
        HashSet<string> vistos = new(StringComparer.OrdinalIgnoreCase);

        foreach (string item in itens)
        {
            if (!EhEmailValido(item))
                invalidos.Add(item);

            if (!vistos.Add(item) && !duplicados.Contains(item, StringComparer.OrdinalIgnoreCase))
                duplicados.Add(item);
        }

        return new ProblemasEndereco(vazio, invalidos, duplicados);
    }

    /// <summary>Indica se um endereço tem formato de e-mail válido.</summary>
    /// <param name="endereco">Endereço a testar.</param>
    /// <returns>Verdadeiro se válido.</returns>
    public static bool EhEmailValido(string? endereco)
    {
        if (string.IsNullOrWhiteSpace(endereco))
            return false;

        // MailAddress cobre o formato geral; exige domínio com ponto para evitar "a@b".
        if (!MailAddress.TryCreate(endereco, out MailAddress? addr))
            return false;

        int ponto = addr.Host.LastIndexOf('.');
        return ponto > 0 && ponto < addr.Host.Length - 1;
    }
}

// #endregion

    // ───────────────────────────────────────────────────────────────────────
    // Seção: Anexos.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  Anexos.cs  —  Acervo de anexos e vínculo com templates (BBiCore.Email)
// -----------------------------------------------------------------------------
//  TRÊS EIXOS INDEPENDENTES, que antes estavam misturados num "tipo" só:
//
//   1. O QUE O ARQUIVO É        -> ContentType ("image/png", "application/pdf")
//   2. COMO O CONTEÚDO É OBTIDO -> ModoObtencao (bytes no banco, caminho fixo,
//                                  caminho dinâmico). Vale para QUALQUER arquivo.
//   3. QUAL O PAPEL NO E-MAIL   -> Papel (cabeçalho, rodapé, inline, anexo).
//                                  Existe só para IMAGEM: apenas ela tem quatro
//                                  destinos. PDF, CSV e afins só sabem ser anexo.
//
//  DUAS COISAS DIFERENTES que só coincidem no mecanismo MIME:
//    · RECURSO DO CORPO (cabeçalho, rodapé, inline) — vai embutido por cid:,
//      o destinatário NÃO o vê no clipe do Outlook. É parte da renderização.
//    · ANEXO — o destinatário baixa. Aparece no clipe.
//
//  DUAS TABELAS:
//    · Anexo         — o ACERVO: o arquivo em si, reutilizável entre templates.
//    · TemplateAnexo — o VÍNCULO: como um template usa um arquivo do acervo
//                      (com que papel, em que ordem, exclusivo ou não).
//
//  O papel mora no VÍNCULO, não no acervo: a mesma imagem pode ser cabeçalho num
//  template e inline em outro.
//
//  >>> NOTA PARA REORGANIZAÇÃO: cada tipo em sua #region, nomeada pelo destino.
// =============================================================================
    // #region Enums  ->  destino sugerido: Models/

    /// <summary>Como o conteúdo do arquivo é obtido. Vale para qualquer arquivo, em qualquer papel.</summary>
    public enum ModoObtencaoAnexo
    {
        /// <summary>Bytes gravados no acervo, junto com o cadastro do arquivo.</summary>
        BytesNoBanco,

        /// <summary>Caminho fixo no servidor: sempre o mesmo arquivo (ex.: <c>templates/logo.png</c>), lido no envio.</summary>
        CaminhoFixo,

        /// <summary>Caminho com marcadores (ex.: <c>relatorios/pedido{{NumeroPedido}}.csv</c>), resolvido a cada envio.</summary>
        CaminhoDinamico
    }

    /// <summary>
    /// Papel do arquivo dentro do e-mail. Os três primeiros são RECURSOS DO CORPO (vão embutidos por
    /// <c>cid:</c> e não aparecem no clipe) e só valem para IMAGEM — apenas ela tem quatro destinos.
    /// Qualquer outro arquivo (PDF, CSV, Excel) só pode ser <see cref="Anexo"/>.
    /// </summary>
    public enum PapelAnexo
    {
        /// <summary>Imagem do topo do e-mail (modo normal). No máximo uma por template.</summary>
        Cabecalho,

        /// <summary>Imagem do rodapé do e-mail (modo normal). No máximo uma por template.</summary>
        Rodape,

        /// <summary>Imagem posicionada pelo usuário dentro do corpo (modo avançado). Quantas quiser.</summary>
        Inline,

        /// <summary>Arquivo que o destinatário baixa. Único papel possível para quem não é imagem.</summary>
        Anexo
    }

    // #endregion

    // #region Acervo  ->  destino sugerido: Models/

    /// <summary>
    /// Um arquivo do ACERVO: cadastrado uma vez e reutilizável por vários templates (a logo
    /// institucional, o PDF de termos). Não sabe nada sobre e-mail — só sobre o arquivo.
    /// </summary>
    public interface IAnexoAcervo
    {
        /// <summary>Identificador do arquivo no acervo.</summary>
        int Id { get; set; }

        /// <summary>Nome do arquivo apresentado ao usuário e ao destinatário.</summary>
        string NomeArquivo { get; set; }

        /// <summary>Content-type (ex.: <c>image/png</c>). É ele que define se o arquivo pode ser recurso de corpo.</summary>
        string? ContentType { get; set; }

        /// <summary>Como o conteúdo é obtido.</summary>
        ModoObtencaoAnexo ModoObtencao { get; set; }

        /// <summary>Bytes do arquivo. Preenchido apenas quando o modo é <see cref="ModoObtencaoAnexo.BytesNoBanco"/>.</summary>
        byte[]? Conteudo { get; set; }

        /// <summary>Caminho do arquivo (fixo ou com marcadores). Preenchido nos modos de caminho.</summary>
        string? Caminho { get; set; }

        /// <summary>Descrição livre, para o usuário reconhecer o arquivo na lista de seleção.</summary>
        string? Descricao { get; set; }
    }

    /// <summary>Implementação pronta de <see cref="IAnexoAcervo"/>, útil quando o sistema não tem entidade própria.</summary>
    public sealed class AnexoAcervoDto : IAnexoAcervo
    {
        /// <inheritdoc/>
        public int Id { get; set; }

        /// <inheritdoc/>
        public string NomeArquivo { get; set; } = string.Empty;

        /// <inheritdoc/>
        public string? ContentType { get; set; }

        /// <inheritdoc/>
        public ModoObtencaoAnexo ModoObtencao { get; set; } = ModoObtencaoAnexo.BytesNoBanco;

        /// <inheritdoc/>
        public byte[]? Conteudo { get; set; }

        /// <inheritdoc/>
        public string? Caminho { get; set; }

        /// <inheritdoc/>
        public string? Descricao { get; set; }
    }

    // #endregion

    // #region Vínculo  ->  destino sugerido: Models/

    /// <summary>
    /// Liga um template a um arquivo do acervo e diz COMO aquele template usa aquele arquivo.
    /// A mesma imagem pode ser cabeçalho num template e inline em outro — por isso o papel mora aqui.
    /// </summary>
    public interface IVinculoAnexo
    {
        /// <summary>Identificador do vínculo.</summary>
        int Id { get; set; }

        /// <summary>Template ao qual o arquivo está vinculado.</summary>
        int IdTemplate { get; set; }

        /// <summary>Arquivo do acervo.</summary>
        int IdAnexo { get; set; }

        /// <summary>Papel do arquivo NESTE template.</summary>
        PapelAnexo Papel { get; set; }

        /// <summary>
        /// Quando verdadeiro, este template REIVINDICA o arquivo: ele deixa de ser oferecido aos demais.
        /// A marca fica no vínculo (e não no acervo) porque a regra é verificada na CRIAÇÃO do vínculo —
        /// que é o momento em que dá para barrar sem afetar templates já existentes.
        /// </summary>
        bool Exclusivo { get; set; }

        /// <summary>
        /// Identificador usado no HTML como <c>cid:{ContentId}</c>. Existe SE E SOMENTE SE o papel for de
        /// corpo (cabeçalho, rodapé ou inline). É GERADO pela biblioteca — nunca digitado pelo usuário.
        /// </summary>
        string? ContentId { get; set; }

        /// <summary>Ordem de exibição na tela e de anexação no e-mail.</summary>
        int Ordem { get; set; }

        /// <summary>Se o arquivo de origem deve ser excluído após o envio. Só faz sentido nos modos de caminho.</summary>
        bool ExcluirAposAnexar { get; set; }
    }

    /// <summary>Implementação pronta de <see cref="IVinculoAnexo"/>.</summary>
    public sealed class VinculoAnexoDto : IVinculoAnexo
    {
        /// <inheritdoc/>
        public int Id { get; set; }

        /// <inheritdoc/>
        public int IdTemplate { get; set; }

        /// <inheritdoc/>
        public int IdAnexo { get; set; }

        /// <inheritdoc/>
        public PapelAnexo Papel { get; set; } = PapelAnexo.Anexo;

        /// <inheritdoc/>
        public bool Exclusivo { get; set; }

        /// <inheritdoc/>
        public string? ContentId { get; set; }

        /// <inheritdoc/>
        public int Ordem { get; set; }

        /// <inheritdoc/>
        public bool ExcluirAposAnexar { get; set; }
    }

    /// <summary>Um arquivo do acervo junto com o papel que ele cumpre num template. É o que a tela e o envio consomem.</summary>
    /// <param name="Anexo">Arquivo do acervo.</param>
    /// <param name="Vinculo">Como este template usa o arquivo.</param>
    public sealed record AnexoVinculado(IAnexoAcervo Anexo, IVinculoAnexo Vinculo)
    {
        /// <summary>Indica que o arquivo vai EMBUTIDO no corpo (cabeçalho, rodapé ou inline), e não no clipe.</summary>
        public bool EhRecursoDeCorpo => RegrasAnexo.EhRecursoDeCorpo(Vinculo.Papel);
    }

    // #endregion

    // #region Regras  ->  destino sugerido: Services/

    /// <summary>Regras que a biblioteca garante sobre anexos — não são opcionais nem configuráveis.</summary>
    public static class RegrasAnexo
    {
        /// <summary>Prefixo dos ContentId gerados, para não colidir com nada que o usuário escreva.</summary>
        private const string PrefixoContentId = "bbi";

        /// <summary>Indica se o papel é um recurso do corpo (vai embutido por cid:, fora do clipe).</summary>
        /// <param name="papel">Papel do arquivo.</param>
        /// <returns>Verdadeiro para cabeçalho, rodapé e inline.</returns>
        public static bool EhRecursoDeCorpo(PapelAnexo papel)
        {
            switch (papel)
            {
                case PapelAnexo.Cabecalho:
                case PapelAnexo.Rodape:
                case PapelAnexo.Inline:
                    return true;
                case PapelAnexo.Anexo:
                default:
                    return false;
            }
        }

        /// <summary>Indica se o content-type é de imagem (só imagem pode ser recurso de corpo).</summary>
        /// <param name="contentType">Content-type do arquivo.</param>
        /// <returns>Verdadeiro quando é imagem.</returns>
        public static bool EhImagem(string? contentType)
            => !string.IsNullOrWhiteSpace(contentType)
               && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        /// <summary>Indica se um papel é válido para um arquivo — cabeçalho, rodapé e inline exigem imagem.</summary>
        /// <param name="papel">Papel pretendido.</param>
        /// <param name="contentType">Content-type do arquivo.</param>
        /// <returns>Verdadeiro quando a combinação é permitida.</returns>
        public static bool PapelPermitido(PapelAnexo papel, string? contentType)
        {
            if (!EhRecursoDeCorpo(papel))
                return true;

            return EhImagem(contentType);
        }

        /// <summary>Gera o ContentId de um recurso de corpo. Único e sem colisão — nunca vem do usuário.</summary>
        /// <param name="papel">Papel do recurso.</param>
        /// <returns>ContentId pronto para o <c>cid:</c>; nulo quando o papel não é de corpo.</returns>
        public static string? GerarContentId(PapelAnexo papel)
        {
            if (!EhRecursoDeCorpo(papel))
                return null;

            switch (papel)
            {
                case PapelAnexo.Cabecalho:
                    return $"{PrefixoContentId}-cabecalho";
                case PapelAnexo.Rodape:
                    return $"{PrefixoContentId}-rodape";
                case PapelAnexo.Inline:
                default:
                    // Imagens do corpo podem ser várias: cada uma ganha um identificador próprio.
                    return $"{PrefixoContentId}-img-{Guid.NewGuid():N}"[..24];
            }
        }

        /// <summary>Indica se o papel admite apenas UM vínculo por template (cabeçalho e rodapé).</summary>
        /// <param name="papel">Papel do arquivo.</param>
        /// <returns>Verdadeiro para cabeçalho e rodapé.</returns>
        public static bool EhPapelUnico(PapelAnexo papel)
        {
            switch (papel)
            {
                case PapelAnexo.Cabecalho:
                case PapelAnexo.Rodape:
                    return true;
                case PapelAnexo.Inline:
                case PapelAnexo.Anexo:
                default:
                    return false;
            }
        }

        /// <summary>Rótulo do papel em português, para a tela.</summary>
        /// <param name="papel">Papel do arquivo.</param>
        /// <returns>Texto exibível.</returns>
        public static string Rotulo(PapelAnexo papel)
        {
            switch (papel)
            {
                case PapelAnexo.Cabecalho:
                    return "Cabeçalho";
                case PapelAnexo.Rodape:
                    return "Rodapé";
                case PapelAnexo.Inline:
                    return "Imagem no corpo";
                case PapelAnexo.Anexo:
                default:
                    return "Anexo";
            }
        }
    }

    // #endregion

    // #region Repositório  ->  destino sugerido: Services/

    /// <summary>
    /// Acesso ao acervo de anexos e aos vínculos. Cada sistema implementa contra as suas tabelas
    /// (o mínimo de colunas está em <see cref="IAnexoAcervo"/> e <see cref="IVinculoAnexo"/>).
    /// </summary>
    public interface IRepositorioAnexos
    {
        /// <summary>
        /// Lista os arquivos do acervo DISPONÍVEIS para um template: todos, menos os reivindicados
        /// com exclusividade por OUTROS templates.
        /// </summary>
        /// <param name="idTemplate">Template que está montando a lista.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Arquivos que este template pode usar.</returns>
        Task<IReadOnlyList<IAnexoAcervo>> ListarDisponiveisAsync(int idTemplate, CancellationToken cancelamento = default);

        /// <summary>Lista os arquivos já vinculados a um template, com o papel de cada um.</summary>
        /// <param name="idTemplate">Template.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Arquivos vinculados.</returns>
        Task<IReadOnlyList<AnexoVinculado>> ListarDoTemplateAsync(int idTemplate, CancellationToken cancelamento = default);

        /// <summary>Indica se um arquivo do acervo já foi reivindicado com exclusividade por outro template.</summary>
        /// <param name="idAnexo">Arquivo do acervo.</param>
        /// <param name="idTemplateAtual">Template que está tentando usá-lo (é ignorado na checagem).</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Verdadeiro quando outro template o reivindicou.</returns>
        Task<bool> EstaReivindicadoPorOutroAsync(int idAnexo, int idTemplateAtual, CancellationToken cancelamento = default);

        /// <summary>Indica se um arquivo do acervo está vinculado a algum template além do informado.</summary>
        /// <param name="idAnexo">Arquivo do acervo.</param>
        /// <param name="idTemplateAtual">Template que está tentando reivindicá-lo.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Verdadeiro quando há outro template usando o arquivo.</returns>
        Task<bool> EstaEmUsoPorOutroAsync(int idAnexo, int idTemplateAtual, CancellationToken cancelamento = default);

        /// <summary>Grava um arquivo novo no acervo.</summary>
        /// <param name="anexo">Arquivo a cadastrar.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Identificador gerado.</returns>
        Task<int> SalvarAnexoAsync(IAnexoAcervo anexo, CancellationToken cancelamento = default);

        /// <summary>Grava (ou atualiza) o vínculo entre um template e um arquivo.</summary>
        /// <param name="vinculo">Vínculo a gravar.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        Task SalvarVinculoAsync(IVinculoAnexo vinculo, CancellationToken cancelamento = default);

        /// <summary>Remove um vínculo (o arquivo continua no acervo).</summary>
        /// <param name="idVinculo">Vínculo a remover.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        Task RemoverVinculoAsync(int idVinculo, CancellationToken cancelamento = default);
    }

    // #endregion

    // ───────────────────────────────────────────────────────────────────────
    // Seção: CadastroSistema.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  CadastroSistema.cs  —  Acesso ao cadastro do aplicativo (BBiCore.Email)
// -----------------------------------------------------------------------------
//  ESTE ARQUIVO É O PONTO DE INTEGRAÇÃO COM O BANCO CENTRALIZADOR.
//
//  A aplicação só se IDENTIFICA (OpcoesEmail.NomeSistema, vindo do appsettings:
//  "meu nome é X"). A partir daí é a biblioteca que busca o cadastro do app —
//  conta de e-mail, usuário e senha — e descriptografa a senha. Nenhuma
//  aplicação passa credencial, e nenhum dev implementa contrato para isso.
//
//  PARA IMPLEMENTAR (marcado com "TODO" abaixo):
//    · ObterCredenciaisAsync -> ler o cadastro no centralizador pelo nome do
//      sistema e devolver as credenciais JÁ descriptografadas.
//
//  Enquanto o acesso não estiver ligado, a busca cai no que estiver preenchido
//  em OpcoesEmail (usuário/senha), o que mantém o desenvolvimento local e o
//  projeto de demonstração funcionando fora da rede corporativa.
// =============================================================================
    /// <summary>
    /// Busca, no banco centralizador, os dados de e-mail cadastrados para a aplicação em execução.
    /// É usado pelos transportes: eles nunca recebem credencial de fora.
    /// </summary>
    public sealed class CadastroSistema
    {
        /// <summary>Configurações da aplicação (traz o nome do sistema, usado na busca).</summary>
        private readonly OpcoesEmail _opcoes;

        /// <summary>Cria o acesso ao cadastro.</summary>
        /// <param name="opcoes">Configurações da aplicação.</param>
        public CadastroSistema(OpcoesEmail opcoes)
            => _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));

        /// <summary>Obtém as credenciais de e-mail da aplicação em execução, já prontas para autenticar.</summary>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Credenciais com a senha em texto claro.</returns>
        public Task<CredenciaisEmail> ObterCredenciaisAsync(CancellationToken cancelamento = default)
        {
            // TODO: buscar no banco CENTRALIZADOR o cadastro cujo nome seja _opcoes.NomeSistema
            //       e devolver conta de e-mail, usuário e senha DESCRIPTOGRAFADA.
            //
            //       A conexão do centralizador e a rotina de descriptografia vivem na própria
            //       biblioteca (decisão tomada: a aplicação não participa disso).
            //
            //       Enquanto isso não existe, vale o que estiver no appsettings da aplicação:

            CredenciaisEmail credenciais = new(
                _opcoes.Usuario,
                _opcoes.Senha,
                _opcoes.EnderecoRemetente);

            return Task.FromResult(credenciais);
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Seção: CorpoNormal.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  CorpoNormal.cs  —  O corpo do MODO NORMAL (BBiCore.Email)
// -----------------------------------------------------------------------------
//  NO MODO NORMAL, A BIBLIOTECA É DONA DE 100% DO HTML. Ela emite a tabela, a
//  célula da imagem de cabeçalho (cid:), a célula de texto e a do rodapé. Dentro
//  da célula de texto só existe: texto ESCAPADO, quebra de linha, parágrafo e
//  {{marcador}}. Não há atributo externo, não há tag que a lib não tenha escrito.
//
//  POR ISSO O TEXTO PODE SER LIDO DE VOLTA. Não é "parsear HTML arbitrário" — é
//  ler um formato FECHADO que nós mesmos produzimos, com gramática conhecida e
//  finita. É isso que permite ao assistente REABRIR um template salvo em modo
//  normal, sem precisar de uma segunda coluna guardando o texto (o corpo continua
//  sendo a única fonte da verdade).
//
//  A garantia de que o HTML está neste formato vem de OrigemCriacao = Normal. Se
//  o template foi para o avançado, não se volta. E se um HTML marcado como normal
//  não corresponder ao formato (alguém editou no banco), a leitura AVISA em vez
//  de adivinhar — quem chama decide abrir no avançado.
// =============================================================================
    /// <summary>Monta e relê o corpo do modo normal (imagem de cabeçalho + texto + imagem de rodapé).</summary>
    public static partial class CorpoNormal
    {
        /// <summary>Marca que identifica a célula de texto na volta da leitura.</summary>
        private const string MarcaTexto = "bbi-texto";

        /// <summary>
        /// Gera o HTML do modo normal, em tabela Outlook-safe. As imagens entram por <c>cid:</c>, usando
        /// os ContentId dos vínculos de cabeçalho e rodapé (quando existirem).
        /// </summary>
        /// <param name="texto">Texto digitado pelo usuário (pode conter marcadores).</param>
        /// <param name="contentIdCabecalho">ContentId da imagem de cabeçalho; nulo quando não há.</param>
        /// <param name="contentIdRodape">ContentId da imagem de rodapé; nulo quando não há.</param>
        /// <returns>HTML do corpo.</returns>
        public static string Gerar(string? texto, string? contentIdCabecalho, string? contentIdRodape)
        {
            StringBuilder sb = new();

            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"border-collapse:collapse\">");

            if (!string.IsNullOrWhiteSpace(contentIdCabecalho))
                sb.Append("<tr><td align=\"center\" style=\"padding:0\">")
                  .Append($"<img src=\"cid:{contentIdCabecalho}\" alt=\"\" style=\"display:block;max-width:100%;height:auto;border:0\">")
                  .Append("</td></tr>");

            sb.Append($"<tr><td class=\"{MarcaTexto}\" style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.5;color:#000000;padding:16px\">");
            sb.Append(TextoParaHtml(texto ?? string.Empty));
            sb.Append("</td></tr>");

            if (!string.IsNullOrWhiteSpace(contentIdRodape))
                sb.Append("<tr><td align=\"center\" style=\"padding:0\">")
                  .Append($"<img src=\"cid:{contentIdRodape}\" alt=\"\" style=\"display:block;max-width:100%;height:auto;border:0\">")
                  .Append("</td></tr>");

            sb.Append("</table>");
            return sb.ToString();
        }

        /// <summary>
        /// Lê de volta o texto do usuário a partir de um HTML gerado por <see cref="Gerar"/>. É a operação
        /// que permite reabrir o assistente sem guardar o texto numa segunda coluna.
        /// </summary>
        /// <param name="html">Corpo do template (deve ter sido gerado no modo normal).</param>
        /// <param name="texto">Texto recuperado, quando o formato é reconhecido.</param>
        /// <returns>Verdadeiro quando o HTML está no formato esperado e o texto foi recuperado.</returns>
        public static bool TentarLerTexto(string? html, out string texto)
        {
            texto = string.Empty;

            if (string.IsNullOrWhiteSpace(html))
                return true;   // corpo vazio é um normal válido (template recém-criado)

            HtmlDocument documento = new();
            documento.LoadHtml(html);

            HtmlNode? celula = documento.DocumentNode
                .Descendants("td")
                .FirstOrDefault(n => n.GetAttributeValue("class", string.Empty).Contains(MarcaTexto, StringComparison.OrdinalIgnoreCase));

            // Formato não reconhecido: alguém editou o HTML fora do assistente.
            // Quem chamou decide o que fazer (o certo é avisar e abrir no avançado).
            if (celula is null)
                return false;

            texto = HtmlParaTexto(celula.InnerHtml);
            return true;
        }

        /// <summary>Converte o texto do usuário em HTML seguro: escapa tags e transforma quebras em &lt;br&gt;. Marcadores passam intactos.</summary>
        /// <param name="texto">Texto simples.</param>
        /// <returns>Fragmento HTML.</returns>
        private static string TextoParaHtml(string texto)
        {
            string escapado = WebUtility.HtmlEncode(texto);

            escapado = escapado
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            return escapado.Replace("\n", "<br>\n");
        }

        /// <summary>Converte de volta o fragmento HTML da célula de texto no texto original do usuário.</summary>
        /// <param name="html">Conteúdo da célula de texto.</param>
        /// <returns>Texto como o usuário digitou.</returns>
        private static string HtmlParaTexto(string html)
        {
            // O caminho de volta é exatamente o inverso da ida: cada <br> (com a nova linha que o
            // acompanha) vira uma quebra, e as entidades voltam ao caractere original. Note que a
            // regex já consome o "\n" que segue o <br> — mexer nisso colapsa as linhas em branco.
            string texto = QuebraDeLinha().Replace(html, "\n");

            return WebUtility.HtmlDecode(texto).Trim();
        }

        /// <summary>Reconhece a quebra de linha gerada na ida (com ou sem a nova linha que a acompanha).</summary>
        /// <returns>Regex compilada.</returns>
        [GeneratedRegex(@"<br\s*/?>(\r?\n)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex QuebraDeLinha();
    }

    // ───────────────────────────────────────────────────────────────────────
    // Seção: EnvioEmail.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  EnvioEmail.cs  —  Núcleo do envio de e-mail (BBiCore.Email)
// -----------------------------------------------------------------------------
//  ARQUITETURA POR COMPOSIÇÃO (não herança):
//
//      IServicoEmail  (o que o DEV usa)
//           |
//           +--> ServicoEmail  ── faz o que é COMUM a todo envio:
//           |        · resolve o template (quando o e-mail vem de um template)
//           |        · materializa anexos (lê caminho, confina à pasta base,
//           |          aplica a política de falha, agenda a exclusão)
//           |        · aplica o redirecionamento de homologação
//           |        · repete em falha transitória (retry com espera crescente)
//           |
//           +--> ITransporteEmail  ── só ENTREGA a mensagem pronta:
//                    · TransporteSmtp     (MailKit)
//                    · TransporteExchange (EWS)
//                    · TransporteSimulado (grava .eml em disco)
//
//  DUAS SEMÂNTICAS, DE PROPÓSITO:
//   · E-mail AVULSO (EmailAvulso)  -> texto LITERAL. Um "{{campo}}" digitado
//     pelo dev vai para o destinatário exatamente assim. Não há resolução.
//   · E-mail POR TEMPLATE          -> aí sim os marcadores {{campo}} são
//     resolvidos com o objeto de dados. É o caminho que o componente usa.
//
//  >>> NOTA PARA REORGANIZAÇÃO: cada tipo em sua #region, nomeada pelo destino.
// =============================================================================
    // #region Mensagem e anexos  ->  destino sugerido: Models/

    /// <summary>Anexo já materializado em memória (bytes lidos e política de falha aplicada).</summary>
    /// <param name="NomeArquivo">Nome final do anexo.</param>
    /// <param name="ContentType">Content-type, quando conhecido.</param>
    /// <param name="Conteudo">Bytes do anexo.</param>
    /// <param name="ContentId">ContentId quando é imagem embutida (cid:); nulo para anexo comum.</param>
    public sealed record AnexoMensagem(string NomeArquivo, string? ContentType, byte[] Conteudo, string? ContentId)
    {
        /// <summary>Indica que o anexo é uma imagem embutida no corpo (referenciada por cid:).</summary>
        public bool EhInline => ContentId is not null;
    }

    /// <summary>Mensagem pronta para o transporte: tudo já resolvido, anexos já em memória.</summary>
    /// <param name="Remetente">Endereço do remetente.</param>
    /// <param name="NomeRemetente">Nome de exibição do remetente.</param>
    /// <param name="Destinatarios">Destinatários.</param>
    /// <param name="Cc">Cópia.</param>
    /// <param name="Cco">Cópia oculta.</param>
    /// <param name="Assunto">Assunto final.</param>
    /// <param name="CorpoHtml">Corpo em HTML.</param>
    /// <param name="Classificacao">Rótulo de sensibilidade.</param>
    /// <param name="Anexos">Anexos materializados.</param>
    public sealed record MensagemEmail(
        string Remetente,
        string? NomeRemetente,
        IReadOnlyList<string> Destinatarios,
        IReadOnlyList<string> Cc,
        IReadOnlyList<string> Cco,
        string Assunto,
        string CorpoHtml,
        ClassificacaoEmail Classificacao,
        IReadOnlyList<AnexoMensagem> Anexos);

    /// <summary>Resultado de uma tentativa de envio.</summary>
    /// <param name="Sucesso">Se o e-mail foi enviado.</param>
    /// <param name="Mensagem">Detalhe do erro (quando não houve sucesso) ou confirmação.</param>
    public sealed record ResultadoEnvio(bool Sucesso, string? Mensagem);

    // #endregion

    // #region E-mail avulso (uso programático pelo dev)  ->  destino sugerido: Models/

    /// <summary>
    /// Anexo de um e-mail avulso. Use <see cref="DeBytes"/> quando o conteúdo já estiver em memória,
    /// ou <see cref="DeCaminho"/> para um arquivo em disco (confinado à pasta base configurada).
    /// </summary>
    public sealed class AnexoAvulso
    {
        /// <summary>Nome do arquivo apresentado ao destinatário.</summary>
        public string NomeArquivo { get; set; } = string.Empty;

        /// <summary>Content-type do anexo (opcional).</summary>
        public string? ContentType { get; set; }

        /// <summary>Bytes do anexo; nulo quando vem de um caminho.</summary>
        public byte[]? Conteudo { get; set; }

        /// <summary>Caminho do arquivo em disco; nulo quando o conteúdo já está em memória.</summary>
        public string? Caminho { get; set; }

        /// <summary>Se o arquivo de origem deve ser excluído após o envio bem-sucedido.</summary>
        public bool ExcluirAposEnviar { get; set; }

        /// <summary>ContentId, quando o anexo é uma imagem embutida referenciada por cid: no corpo.</summary>
        public string? ContentId { get; set; }

        /// <summary>Cria um anexo a partir de bytes em memória.</summary>
        /// <param name="nomeArquivo">Nome do arquivo.</param>
        /// <param name="conteudo">Bytes do anexo.</param>
        /// <param name="contentType">Content-type (opcional).</param>
        /// <returns>Anexo pronto.</returns>
        public static AnexoAvulso DeBytes(string nomeArquivo, byte[] conteudo, string? contentType = null)
            => new() { NomeArquivo = nomeArquivo, Conteudo = conteudo, ContentType = contentType };

        /// <summary>Cria um anexo a partir de um arquivo em disco.</summary>
        /// <param name="caminho">Caminho do arquivo (relativo à pasta base de anexos).</param>
        /// <param name="excluirAposEnviar">Se o arquivo deve ser excluído após o envio.</param>
        /// <returns>Anexo pronto.</returns>
        public static AnexoAvulso DeCaminho(string caminho, bool excluirAposEnviar = false)
            => new()
            {
                Caminho = caminho,
                NomeArquivo = Path.GetFileName(caminho),
                ExcluirAposEnviar = excluirAposEnviar
            };
    }

    /// <summary>
    /// E-mail montado pelo próprio dev, no código, sem template. O conteúdo é LITERAL: se você escrever
    /// "{{algo}}" no assunto ou no corpo, isso chega ao destinatário exatamente assim — marcadores só
    /// têm efeito no envio por template.
    /// </summary>
    public sealed class EmailAvulso
    {
        /// <summary>Destinatários.</summary>
        public IList<string> Para { get; } = [];

        /// <summary>Cópia.</summary>
        public IList<string> Cc { get; } = [];

        /// <summary>Cópia oculta.</summary>
        public IList<string> Cco { get; } = [];

        /// <summary>Assunto (texto literal).</summary>
        public string Assunto { get; set; } = string.Empty;

        /// <summary>Corpo em HTML (texto literal). Para texto simples, use <see cref="DefinirCorpoTexto"/>.</summary>
        public string CorpoHtml { get; set; } = string.Empty;

        /// <summary>Rótulo de sensibilidade da mensagem.</summary>
        public ClassificacaoEmail Classificacao { get; set; } = ClassificacaoEmail.Interno;

        /// <summary>Anexos do e-mail.</summary>
        public IList<AnexoAvulso> Anexos { get; } = [];

        /// <summary>Política quando um anexo falhar (ler arquivo inexistente, por exemplo).</summary>
        public AcaoFalhaAnexo AcaoNaFalhaDeAnexo { get; set; } = AcaoFalhaAnexo.FalharEnvio;

        /// <summary>Remetente alternativo; nulo usa o configurado em <see cref="OpcoesEmail"/>.</summary>
        public string? Remetente { get; set; }

        /// <summary>Define o corpo a partir de texto simples, convertendo para HTML seguro (escapa tags, quebras viram &lt;br&gt;).</summary>
        /// <param name="texto">Texto simples.</param>
        public void DefinirCorpoTexto(string texto)
        {
            string escapado = System.Net.WebUtility.HtmlEncode(texto ?? string.Empty);
            escapado = escapado.Replace("\r\n", "\n").Replace("\r", "\n");
            CorpoHtml = escapado.Replace("\n", "<br>\n");
        }
    }

    // #endregion

    // #region Configuração e credenciais  ->  destino sugerido: Models/

    /// <summary>Configurações de e-mail de um sistema. Uma instância por aplicação.</summary>
    public sealed class OpcoesEmail
    {
        /// <summary>
        /// Nome com que a aplicação se identifica ("meu nome é X", no appsettings). É por ele que a
        /// biblioteca acha o cadastro do app no banco centralizador e obtém as credenciais de envio.
        /// </summary>
        public string NomeSistema { get; set; } = string.Empty;

        /// <summary>Endereço do remetente (o "de").</summary>
        public string EnderecoRemetente { get; set; } = string.Empty;

        /// <summary>Nome de exibição do remetente (opcional).</summary>
        public string? NomeExibicao { get; set; }

        /// <summary>Usuário de autenticação. Usado apenas enquanto a busca no cadastro (CadastroSistema) não estiver ligada.</summary>
        public string Usuario { get; set; } = string.Empty;

        /// <summary>Senha em texto claro. Usada apenas enquanto a busca no cadastro (CadastroSistema) não estiver ligada.</summary>
        public string Senha { get; set; } = string.Empty;

        /// <summary>Servidor SMTP (transporte SMTP).</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>Porta do servidor SMTP (587 para STARTTLS, 25 para relay interno).</summary>
        public int Porta { get; set; } = 587;

        /// <summary>Se a conexão SMTP usa TLS.</summary>
        public bool UsarSsl { get; set; } = true;

        /// <summary>Endereço do serviço EWS (transporte Exchange). Ex.: https://servidor/ews/exchange.asmx</summary>
        public string UrlEws { get; set; } = string.Empty;

        /// <summary>Versão do Exchange usada pelo EWS (ex.: "Exchange2010_SP2").</summary>
        public string VersaoExchange { get; set; } = "Exchange2010_SP2";

        /// <summary>Se guarda o e-mail na pasta de Itens Enviados da conta (transporte Exchange).</summary>
        public bool SalvarCopiaEmEnviados { get; set; } = true;

        /// <summary>Se valida o certificado do servidor. Desligar aceita QUALQUER certificado — use só em ambiente interno controlado.</summary>
        public bool ValidarCertificadoServidor { get; set; } = true;

        /// <summary>
        /// Trava de homologação: quando preenchida, TODOS os e-mails vão só para estes endereços
        /// (Cc e Cco são descartados e o assunto ganha um prefixo). Deixe vazia em produção.
        /// </summary>
        public IList<string> RedirecionarPara { get; } = [];

        /// <summary>Prefixo aplicado ao assunto quando o redirecionamento está ativo.</summary>
        public string PrefixoAssuntoTeste { get; set; } = "[TESTE] ";

        /// <summary>Pasta base dos anexos lidos de disco. Confina o acesso (barreira contra path traversal).</summary>
        public string? PastaBaseAnexos { get; set; }

        /// <summary>Pasta onde o transporte simulado grava os .eml (padrão: subpasta temporária).</summary>
        public string? PastaSimulacao { get; set; }

        /// <summary>Tempo limite do envio, em segundos.</summary>
        public int TimeoutSegundos { get; set; } = 60;

        /// <summary>Quantas vezes repetir o envio em falha transitória de rede (0 desliga).</summary>
        public int TentativasEmFalha { get; set; } = 2;

        /// <summary>Espera inicial entre tentativas, em milissegundos (dobra a cada tentativa).</summary>
        public int EsperaEntreTentativasMs { get; set; } = 500;
    }

    /// <summary>Credenciais de envio prontas para uso (senha já descriptografada).</summary>
    /// <param name="Usuario">Usuário de autenticação.</param>
    /// <param name="Senha">Senha em texto claro.</param>
    /// <param name="EnderecoRemetente">Conta de e-mail do sistema; nulo mantém o das opções.</param>
    public sealed record CredenciaisEmail(string Usuario, string Senha, string? EnderecoRemetente = null);

    // #endregion

    // #region Rascunho  ->  destino sugerido: Models/

    /// <summary>
    /// Para onde vai o rascunho gerado. É combinável: o dev pode pedir mais de um destino ao mesmo
    /// tempo (ex.: <c>Bytes | Arquivo</c>).
    /// </summary>
    [Flags]
    public enum DestinoRascunho
    {
        /// <summary>Nenhum destino (não gera nada).</summary>
        Nenhum = 0,

        /// <summary>Devolve os bytes do .eml (para o dev baixar pelo navegador ou tratar como quiser).</summary>
        Bytes = 1,

        /// <summary>Grava o .eml num arquivo em disco.</summary>
        Arquivo = 2,

        /// <summary>Salva na pasta Rascunhos da caixa postal (exige um transporte que suporte — hoje, o Exchange).</summary>
        CaixaPostal = 4
    }

    /// <summary>Opções da geração do rascunho.</summary>
    public sealed class OpcoesRascunho
    {
        /// <summary>Destinos desejados (combináveis). Padrão: apenas os bytes.</summary>
        public DestinoRascunho Destino { get; set; } = DestinoRascunho.Bytes;

        /// <summary>Pasta onde gravar o .eml quando <see cref="DestinoRascunho.Arquivo"/> estiver ligado. Nulo usa a pasta de simulação das opções.</summary>
        public string? PastaArquivo { get; set; }

        /// <summary>Nome do arquivo .eml (sem caminho). Nulo gera um nome a partir do assunto e do horário.</summary>
        public string? NomeArquivo { get; set; }
    }

    /// <summary>Resultado da geração de um rascunho.</summary>
    /// <param name="Sucesso">Se a geração ocorreu.</param>
    /// <param name="Mensagem">Detalhe do erro ou confirmação.</param>
    /// <param name="Conteudo">Bytes do .eml, quando o destino incluiu <see cref="DestinoRascunho.Bytes"/>.</param>
    /// <param name="NomeArquivo">Nome sugerido do arquivo .eml.</param>
    /// <param name="CaminhoArquivo">Caminho gravado, quando o destino incluiu <see cref="DestinoRascunho.Arquivo"/>.</param>
    /// <param name="SalvoNaCaixaPostal">Se foi salvo na pasta Rascunhos da caixa postal.</param>
    public sealed record ResultadoRascunho(
        bool Sucesso,
        string? Mensagem,
        byte[]? Conteudo = null,
        string? NomeArquivo = null,
        string? CaminhoArquivo = null,
        bool SalvoNaCaixaPostal = false);

    // #endregion

    // #region Transporte (contrato)  ->  destino sugerido: Services/

    /// <summary>
    /// Entrega de fato a mensagem. É a ÚNICA parte que conhece o protocolo — trocar Exchange por SMTP
    /// (ou pelo simulado) é trocar a implementação registrada, sem tocar em mais nada.
    /// </summary>
    public interface ITransporteEmail
    {
        /// <summary>Entrega a mensagem já pronta.</summary>
        /// <param name="mensagem">Mensagem com tudo resolvido e anexos em memória.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        Task EntregarAsync(MensagemEmail mensagem, CancellationToken cancelamento = default);

        /// <summary>
        /// Autentica no servidor e desconecta, SEM enviar nada. Serve para o usuário descobrir que a
        /// credencial ou o endereço estão errados na hora de configurar — e não no primeiro envio real.
        /// </summary>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Sucesso, ou a mensagem do erro encontrado.</returns>
        Task<ResultadoEnvio> TestarConexaoAsync(CancellationToken cancelamento = default);
    }

    /// <summary>
    /// Capacidade OPCIONAL de um transporte: salvar a mensagem na pasta Rascunhos da caixa postal
    /// (o e-mail aparece no Outlook da conta, pronto para revisão e disparo manual). Hoje só o
    /// transporte do Exchange implementa; pedir <see cref="DestinoRascunho.CaixaPostal"/> com um
    /// transporte que não implementa devolve erro explicativo, sem quebrar os outros destinos.
    /// </summary>
    public interface ITransporteRascunho
    {
        /// <summary>Salva a mensagem como rascunho na caixa postal da conta.</summary>
        /// <param name="mensagem">Mensagem pronta.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        Task SalvarRascunhoAsync(MensagemEmail mensagem, CancellationToken cancelamento = default);
    }

    /// <summary>Falha ao preparar um anexo, quando a política manda abortar o envio.</summary>
    public sealed class FalhaAnexoException : Exception
    {
        /// <summary>Cria a exceção com a mensagem informada.</summary>
        /// <param name="mensagem">Descrição da falha.</param>
        public FalhaAnexoException(string mensagem) : base(mensagem)
        {
        }
    }

    // #endregion

    // #region Serviço de e-mail (o que o dev usa)  ->  destino sugerido: Services/

    /// <summary>
    /// Fachada de e-mail da biblioteca. É o que o dev injeta para enviar — nas três formas:
    /// avulso (literal), por template salvo (resolve marcadores) e a partir de um template já em mãos
    /// (usado pelo componente).
    /// </summary>
    public interface IServicoEmail
    {
        /// <summary>Envia um e-mail montado no código. O conteúdo é LITERAL: marcadores não são resolvidos.</summary>
        /// <param name="email">E-mail avulso.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado do envio.</returns>
        Task<ResultadoEnvio> EnviarAsync(EmailAvulso email, CancellationToken cancelamento = default);

        /// <summary>
        /// Envia a partir de um template SALVO, resolvendo os marcadores com o objeto de dados.
        /// Só envia templates PUBLICADOS: um template em rascunho é recusado (é a trava que impede um
        /// template pela metade de sair para o cliente).
        /// </summary>
        /// <param name="nomeTemplate">Nome do template no repositório.</param>
        /// <param name="dados">Objeto que preenche os marcadores.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado do envio.</returns>
        Task<ResultadoEnvio> EnviarPorTemplateAsync(string nomeTemplate, object dados, CancellationToken cancelamento = default);

        /// <summary>
        /// Envia a partir de um template já em mãos, resolvendo os marcadores. Template em RASCUNHO é
        /// recusado — a menos que <paramref name="permitirRascunho"/> seja verdadeiro, o que o
        /// componente usa no "enviar teste" (testar antes de publicar é justamente o ponto).
        /// </summary>
        /// <param name="template">Template de e-mail.</param>
        /// <param name="dados">Objeto que preenche os marcadores.</param>
        /// <param name="permitirRascunho">Se permite enviar um template ainda não publicado (uso consciente: teste).</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado do envio.</returns>
        Task<ResultadoEnvio> EnviarTemplateAsync(ITemplateEmail template, object dados, bool permitirRascunho = false, CancellationToken cancelamento = default);

        /// <summary>
        /// Gera o RASCUNHO de um e-mail avulso em vez de enviá-lo: monta a MESMA mensagem que seria
        /// transmitida e a entrega como .eml (bytes e/ou arquivo) e/ou na caixa postal.
        /// </summary>
        /// <param name="email">E-mail avulso (conteúdo literal, como no envio).</param>
        /// <param name="opcoes">Destinos do rascunho; nulo usa apenas os bytes.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado com os bytes e/ou o caminho gravado.</returns>
        Task<ResultadoRascunho> GerarRascunhoAsync(EmailAvulso email, OpcoesRascunho? opcoes = null, CancellationToken cancelamento = default);

        /// <summary>
        /// Gera o RASCUNHO de um template, já RESOLVIDO com o objeto de dados — o .eml sai exatamente
        /// como o e-mail que seria enviado (sem marcadores), pronto para revisar e disparar.
        /// </summary>
        /// <param name="template">Template de e-mail.</param>
        /// <param name="dados">Objeto que preenche os marcadores.</param>
        /// <param name="opcoes">Destinos do rascunho; nulo usa apenas os bytes.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado com os bytes e/ou o caminho gravado.</returns>
        Task<ResultadoRascunho> GerarRascunhoTemplateAsync(ITemplateEmail template, object dados, OpcoesRascunho? opcoes = null, CancellationToken cancelamento = default);

        /// <summary>
        /// Salva o template, SANITIZANDO o corpo antes de gravar (automático: não é escolha do dev nem
        /// do usuário). Se algo for removido do HTML, o resultado avisa — e o log registra o que saiu.
        /// </summary>
        /// <param name="template">Template a salvar.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado do salvamento, com o aviso de sanitização quando houver.</returns>
        Task<ResultadoSalvamento> SalvarTemplateAsync(ITemplateEmail template, CancellationToken cancelamento = default);

        /// <summary>Autentica no servidor e desconecta, sem enviar nada. Serve para conferir a configuração.</summary>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Sucesso, ou a mensagem do erro.</returns>
        Task<ResultadoEnvio> TestarConexaoAsync(CancellationToken cancelamento = default);
    }

    /// <summary>Resultado do salvamento de um template.</summary>
    /// <param name="Sucesso">Se foi salvo.</param>
    /// <param name="Mensagem">Confirmação ou erro.</param>
    /// <param name="HtmlAlterado">Verdadeiro quando a sanitização mudou o corpo em relação ao que o usuário escreveu.</param>
    /// <param name="Remocoes">O que a sanitização removeu (vazio quando nada mudou).</param>
    public sealed record ResultadoSalvamento(
        bool Sucesso,
        string? Mensagem,
        bool HtmlAlterado = false,
        IReadOnlyList<RemocaoHtml>? Remocoes = null);

    /// <summary>
    /// Implementação da fachada. Concentra tudo o que é comum ao envio e delega a entrega ao
    /// <see cref="ITransporteEmail"/> registrado.
    /// </summary>
    public sealed class ServicoEmail : IServicoEmail
    {
        /// <summary>Transporte que entrega a mensagem (Exchange, SMTP ou simulado).</summary>
        private readonly ITransporteEmail _transporte;

        /// <summary>Configurações do sistema.</summary>
        private readonly OpcoesEmail _opcoes;

        /// <summary>Motor de marcadores, usado apenas no envio por template.</summary>
        private readonly MotorTemplate _motor;

        /// <summary>Repositório de templates; necessário apenas para <see cref="EnviarPorTemplateAsync"/>.</summary>
        private readonly IRepositorioTemplateEmail? _repositorio;

        /// <summary>Registrador do log de envios. Acionado em TODO envio — sucesso e falha.</summary>
        private readonly RegistradorEnvioEmail _log = new();

        /// <summary>Acesso aos anexos vinculados ao template (acervo + vínculo).</summary>
        private readonly ServicoAnexos? _anexos;

        /// <summary>Cria o serviço de e-mail.</summary>
        /// <param name="transporte">Transporte registrado.</param>
        /// <param name="opcoes">Configurações do sistema.</param>
        /// <param name="motor">Motor de marcadores (envio por template).</param>
        /// <param name="repositorio">Repositório de templates (opcional; exigido só no envio por nome).</param>
        /// <param name="anexos">Serviço de anexos; necessário quando os templates têm anexos vinculados.</param>
        public ServicoEmail(
            ITransporteEmail transporte,
            OpcoesEmail opcoes,
            MotorTemplate motor,
            IRepositorioTemplateEmail? repositorio = null,
            ServicoAnexos? anexos = null)
        {
            _transporte = transporte ?? throw new ArgumentNullException(nameof(transporte));
            _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));
            _motor = motor ?? throw new ArgumentNullException(nameof(motor));
            _repositorio = repositorio;
            _anexos = anexos;
        }

        /// <inheritdoc/>
        public async Task<ResultadoEnvio> EnviarAsync(EmailAvulso email, CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(email);

            List<string> excluirAposEnvio = [];

            try
            {
                // Avulso: NADA é resolvido — o texto vai literal, marcadores inclusive.
                IReadOnlyList<AnexoMensagem> anexos = MaterializarAvulsos(email, excluirAposEnvio);

                MensagemEmail mensagem = new(
                    string.IsNullOrWhiteSpace(email.Remetente) ? _opcoes.EnderecoRemetente : email.Remetente,
                    _opcoes.NomeExibicao,
                    [.. email.Para],
                    [.. email.Cc],
                    [.. email.Cco],
                    email.Assunto,
                    email.CorpoHtml,
                    email.Classificacao,
                    anexos);

                // Avulso: não há template de origem.
                return await DespacharAsync(mensagem, excluirAposEnvio, nomeTemplate: null, cancelamento);
            }
            catch (FalhaAnexoException ex)
            {
                return new ResultadoEnvio(false, ex.Message);
            }
            catch (Exception ex)
            {
                return new ResultadoEnvio(false, $"Falha no envio: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<ResultadoEnvio> EnviarPorTemplateAsync(string nomeTemplate, object dados, CancellationToken cancelamento = default)
        {
            if (_repositorio is null)
                return new ResultadoEnvio(false, "Nenhum IRepositorioTemplateEmail registrado — não há como buscar o template pelo nome.");

            ITemplateEmail? template = await _repositorio.ObterAsync(nomeTemplate, cancelamento);

            if (template is null)
                return new ResultadoEnvio(false, $"Template '{nomeTemplate}' não encontrado.");

            return await EnviarTemplateAsync(template, dados, permitirRascunho: false, cancelamento);
        }

        /// <inheritdoc/>
        public async Task<ResultadoEnvio> EnviarTemplateAsync(
            ITemplateEmail template,
            object dados,
            bool permitirRascunho = false,
            CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(template);

            // TRAVA: rascunho não vai para o mundo. Publicar é o que libera o template para uso.
            if (template.Situacao == SituacaoTemplate.Rascunho && !permitirRascunho)
                return new ResultadoEnvio(
                    false,
                    $"O template '{template.Nome}' está em rascunho — publique-o antes de enviar.");

            List<string> excluirAposEnvio = [];

            try
            {
                // Template: AQUI os marcadores {{campo}} são resolvidos com o objeto de dados.
                EmailResolvido resolvido = ResolvedorEmail.Resolver(template, dados, _motor);

                // Sobrou marcador sem valor? A política decide: abortar, ou enviar sem aquele trecho.
                (EmailResolvido tratado, string? erroMarcador) = AplicarPoliticaDeMarcadores(resolvido);

                if (erroMarcador is not null)
                    return new ResultadoEnvio(false, erroMarcador);

                resolvido = tratado;

                IReadOnlyList<AnexoMensagem> anexos = await MaterializarDoTemplateAsync(
                    template, dados, resolvido.AcaoNaFalhaDeAnexo, excluirAposEnvio, cancelamento);

                MensagemEmail mensagem = new(
                    _opcoes.EnderecoRemetente,
                    _opcoes.NomeExibicao,
                    resolvido.Destinatarios,
                    resolvido.Cc,
                    resolvido.Cco,
                    resolvido.Assunto,
                    resolvido.Corpo,
                    resolvido.Classificacao,
                    anexos);

                return await DespacharAsync(mensagem, excluirAposEnvio, template.Nome, cancelamento);
            }
            catch (FalhaAnexoException ex)
            {
                return new ResultadoEnvio(false, ex.Message);
            }
            catch (Exception ex)
            {
                return new ResultadoEnvio(false, $"Falha no envio: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<ResultadoRascunho> GerarRascunhoAsync(
            EmailAvulso email,
            OpcoesRascunho? opcoes = null,
            CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(email);

            try
            {
                // Rascunho não é envio: os anexos são materializados, mas NADA é excluído do disco
                // (o arquivo de origem ainda pode ser necessário quando o e-mail for realmente enviado).
                List<string> ignorado = [];
                IReadOnlyList<AnexoMensagem> anexos = MaterializarAvulsos(email, ignorado);

                MensagemEmail mensagem = new(
                    string.IsNullOrWhiteSpace(email.Remetente) ? _opcoes.EnderecoRemetente : email.Remetente,
                    _opcoes.NomeExibicao,
                    [.. email.Para],
                    [.. email.Cc],
                    [.. email.Cco],
                    email.Assunto,
                    email.CorpoHtml,
                    email.Classificacao,
                    anexos);

                return await MaterializarRascunhoAsync(mensagem, opcoes, cancelamento);
            }
            catch (FalhaAnexoException ex)
            {
                return new ResultadoRascunho(false, ex.Message);
            }
            catch (Exception ex)
            {
                return new ResultadoRascunho(false, $"Falha ao gerar o rascunho: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<ResultadoRascunho> GerarRascunhoTemplateAsync(
            ITemplateEmail template,
            object dados,
            OpcoesRascunho? opcoes = null,
            CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(template);

            try
            {
                // O rascunho sai SEMPRE resolvido: é exatamente o e-mail que seria enviado —
                // por isso passa pela MESMA política de marcadores (o .eml nunca leva "{{campo}}" cru).
                EmailResolvido resolvido = ResolvedorEmail.Resolver(template, dados, _motor);

                (EmailResolvido tratado, string? erroMarcador) = AplicarPoliticaDeMarcadores(resolvido);

                if (erroMarcador is not null)
                    return new ResultadoRascunho(false, erroMarcador);

                resolvido = tratado;

                // No rascunho, os anexos de disco NÃO são excluídos: o e-mail ainda não foi enviado.
                List<string> ignorado = [];

                IReadOnlyList<AnexoMensagem> anexos = await MaterializarDoTemplateAsync(
                    template, dados, resolvido.AcaoNaFalhaDeAnexo, ignorado, cancelamento);

                MensagemEmail mensagem = new(
                    _opcoes.EnderecoRemetente,
                    _opcoes.NomeExibicao,
                    resolvido.Destinatarios,
                    resolvido.Cc,
                    resolvido.Cco,
                    resolvido.Assunto,
                    resolvido.Corpo,
                    resolvido.Classificacao,
                    anexos);

                return await MaterializarRascunhoAsync(mensagem, opcoes, cancelamento);
            }
            catch (FalhaAnexoException ex)
            {
                return new ResultadoRascunho(false, ex.Message);
            }
            catch (Exception ex)
            {
                return new ResultadoRascunho(false, $"Falha ao gerar o rascunho: {ex.Message}");
            }
        }

        /// <summary>Gera o .eml e o entrega nos destinos pedidos (bytes, arquivo e/ou caixa postal).</summary>
        /// <param name="mensagem">Mensagem pronta (a mesma que seria enviada).</param>
        /// <param name="opcoes">Destinos desejados.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado com o que foi produzido.</returns>
        private async Task<ResultadoRascunho> MaterializarRascunhoAsync(
            MensagemEmail mensagem,
            OpcoesRascunho? opcoes,
            CancellationToken cancelamento)
        {
            OpcoesRascunho destino = opcoes ?? new OpcoesRascunho();

            if (destino.Destino == DestinoRascunho.Nenhum)
                return new ResultadoRascunho(false, "Nenhum destino de rascunho informado.");

            string nomeArquivo = string.IsNullOrWhiteSpace(destino.NomeArquivo)
                ? GerarNomeArquivo(mensagem.Assunto)
                : destino.NomeArquivo;

            byte[]? conteudo = null;
            string? caminhoGravado = null;
            bool naCaixaPostal = false;
            List<string> avisos = [];

            // O .eml é a MESMA mensagem que o transporte enviaria.
            bool precisaDoEml = destino.Destino.HasFlag(DestinoRascunho.Bytes)
                || destino.Destino.HasFlag(DestinoRascunho.Arquivo);

            byte[]? eml = precisaDoEml ? MontadorMime.GerarEml(mensagem) : null;

            if (destino.Destino.HasFlag(DestinoRascunho.Bytes))
                conteudo = eml;

            if (destino.Destino.HasFlag(DestinoRascunho.Arquivo) && eml is not null)
            {
                string pasta = string.IsNullOrWhiteSpace(destino.PastaArquivo)
                    ? (string.IsNullOrWhiteSpace(_opcoes.PastaSimulacao)
                        ? Path.Combine(Path.GetTempPath(), "bbi-rascunhos")
                        : _opcoes.PastaSimulacao)
                    : destino.PastaArquivo;

                Directory.CreateDirectory(pasta);
                caminhoGravado = Path.Combine(pasta, nomeArquivo);

                await File.WriteAllBytesAsync(caminhoGravado, eml, cancelamento);
            }

            if (destino.Destino.HasFlag(DestinoRascunho.CaixaPostal))
            {
                if (_transporte is ITransporteRascunho suportaRascunho)
                {
                    await suportaRascunho.SalvarRascunhoAsync(mensagem, cancelamento);
                    naCaixaPostal = true;
                }
                else
                {
                    avisos.Add("o transporte registrado não salva na caixa postal (só o Exchange faz isso)");
                }
            }

            string resumo = avisos.Count > 0
                ? "Rascunho gerado, com ressalva: " + string.Join("; ", avisos) + "."
                : "Rascunho gerado.";

            return new ResultadoRascunho(true, resumo, conteudo, nomeArquivo, caminhoGravado, naCaixaPostal);
        }

        /// <summary>Gera um nome de arquivo .eml a partir do assunto e do horário, sem caracteres inválidos.</summary>
        /// <param name="assunto">Assunto do e-mail.</param>
        /// <returns>Nome do arquivo (ex.: "aviso-pedido-20260711-1530.eml").</returns>
        private static string GerarNomeArquivo(string assunto)
        {
            string baseNome = string.IsNullOrWhiteSpace(assunto) ? "rascunho" : assunto;

            foreach (char invalido in Path.GetInvalidFileNameChars())
                baseNome = baseNome.Replace(invalido, '-');

            baseNome = baseNome.Trim().Replace(' ', '-');

            if (baseNome.Length > 60)
                baseNome = baseNome[..60];

            return $"{baseNome}-{DateTime.Now:yyyyMMdd-HHmmss}.eml";
        }

        /// <inheritdoc/>
        public async Task<ResultadoSalvamento> SalvarTemplateAsync(ITemplateEmail template, CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(template);

            if (_repositorio is null)
                return new ResultadoSalvamento(false, "Nenhum IRepositorioTemplateEmail registrado.");

            IReadOnlyList<RemocaoHtml> remocoes = [];

            // A sanitização vale para o corpo do MODO AVANÇADO — o normal é gerado pela própria
            // biblioteca e não tem como conter código.
            if (template.OrigemCriacao == OrigemCriacaoTemplate.Avancado)
            {
                ResultadoSanitizacao limpeza = SanitizadorHtml.Sanitizar(template.Corpo);

                if (limpeza.Alterado)
                {
                    template.Corpo = limpeza.Html;
                    remocoes = limpeza.Remocoes;

                    // O que foi removido FICA REGISTRADO: se o usuário reclamar depois de que o e-mail
                    // saiu diferente do que ele colou, sabemos exatamente o que saiu e de onde veio.
                    await _log.RegistrarSanitizacaoAsync(template.Nome, remocoes, cancelamento);
                }
            }

            await _repositorio.SalvarAsync(template, cancelamento);

            string mensagem = remocoes.Count > 0
                ? $"Template salvo. Removemos do HTML elementos não permitidos: {string.Join(", ", remocoes.Select(r => r.Elemento).Distinct())}."
                : "Template salvo.";

            return new ResultadoSalvamento(true, mensagem, remocoes.Count > 0, remocoes);
        }

        /// <inheritdoc/>
        public Task<ResultadoEnvio> TestarConexaoAsync(CancellationToken cancelamento = default)
            => _transporte.TestarConexaoAsync(cancelamento);

        /// <summary>
        /// Trata os marcadores que ficaram SEM valor, conforme a política do template:
        /// aborta o envio, ou REMOVE os trechos não resolvidos do texto (o destinatário nunca vê
        /// um "{{campo}}" cru). Depois da remoção, confere se ainda sobrou destinatário.
        /// </summary>
        /// <param name="resolvido">E-mail já resolvido, possivelmente com marcadores pendentes.</param>
        /// <returns>O e-mail tratado e, quando o envio deve ser abortado, a mensagem de erro.</returns>
        private static (EmailResolvido Email, string? Erro) AplicarPoliticaDeMarcadores(EmailResolvido resolvido)
        {
            if (resolvido.MarcadoresNaoResolvidos.Count == 0)
                return (resolvido, null);

            string lista = string.Join(", ", resolvido.MarcadoresNaoResolvidos.Select(m => "{{" + m + "}}"));

            switch (resolvido.AcaoNaFalhaDeMarcador)
            {
                case AcaoFalhaMarcador.FalharEnvio:
                    return (resolvido, $"Envio abortado — marcadores sem valor: {lista}.");

                case AcaoFalhaMarcador.EnviarMesmoAssim:
                default:
                    IReadOnlyList<string> pendentes = resolvido.MarcadoresNaoResolvidos;

                    // Os anexos NÃO entram aqui: eles vivem no acervo e são materializados à parte
                    // (o caminho dinâmico é resolvido lá, com o mesmo objeto de dados).
                    EmailResolvido limpo = resolvido with
                    {
                        Assunto = MotorTemplate.RemoverMarcadores(resolvido.Assunto, pendentes),
                        Corpo = MotorTemplate.RemoverMarcadores(resolvido.Corpo, pendentes),
                        Destinatarios = LimparEnderecos(resolvido.Destinatarios, pendentes),
                        Cc = LimparEnderecos(resolvido.Cc, pendentes),
                        Cco = LimparEnderecos(resolvido.Cco, pendentes)
                    };

                    // Remover o marcador de um endereço pode ter zerado a lista — aí não há e-mail a enviar.
                    if (limpo.Destinatarios.Count == 0)
                        return (limpo, $"Envio abortado — sem destinatário depois de remover os marcadores sem valor: {lista}.");

                    return (limpo, null);
            }
        }

        /// <summary>Remove os marcadores pendentes de uma lista de endereços e descarta os que ficaram vazios.</summary>
        /// <param name="enderecos">Endereços resolvidos.</param>
        /// <param name="pendentes">Marcadores sem valor.</param>
        /// <returns>Endereços válidos restantes.</returns>
        private static IReadOnlyList<string> LimparEnderecos(IReadOnlyList<string> enderecos, IReadOnlyList<string> pendentes)
        {
            List<string> limpos = [];

            foreach (string endereco in enderecos)
            {
                string tratado = MotorTemplate.RemoverMarcadores(endereco, pendentes).Trim();

                if (!string.IsNullOrWhiteSpace(tratado))
                    limpos.Add(tratado);
            }

            return limpos;
        }

        /// <summary>Aplica o redirecionamento, entrega (com repetição em falha transitória) e faz a limpeza pós-envio.</summary>
        /// <param name="mensagem">Mensagem pronta.</param>
        /// <param name="excluirAposEnvio">Arquivos a excluir depois do sucesso.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado do envio.</returns>
        private async Task<ResultadoEnvio> DespacharAsync(
            MensagemEmail mensagem,
            List<string> excluirAposEnvio,
            string? nomeTemplate,
            CancellationToken cancelamento)
        {
            MensagemEmail efetiva = AplicarRedirecionamento(mensagem);

            try
            {
                await EntregarComRepeticaoAsync(efetiva, cancelamento);
            }
            catch (OperationCanceledException)
            {
                // Cancelamento também é falha do ponto de vista do log: houve tentativa.
                await _log.RegistrarFalhaAsync(
                    RegistroEnvioEmail.De(efetiva, nomeTemplate, sucesso: false, "Envio cancelado."),
                    CancellationToken.None);

                return new ResultadoEnvio(false, "Envio cancelado.");
            }
            catch (Exception ex)
            {
                // LOG DE FALHA: grava o e-mail que se tentou enviar e a causa.
                await _log.RegistrarFalhaAsync(
                    RegistroEnvioEmail.De(efetiva, nomeTemplate, sucesso: false, ex.Message),
                    cancelamento);

                return new ResultadoEnvio(false, $"Falha no envio: {ex.Message}");
            }

            // LOG DE SUCESSO: logo após o envio, antes de qualquer limpeza.
            await _log.RegistrarSucessoAsync(
                RegistroEnvioEmail.De(efetiva, nomeTemplate, sucesso: true, null),
                cancelamento);

            foreach (string caminho in excluirAposEnvio)
                TentarExcluir(caminho);

            return new ResultadoEnvio(true, "E-mail enviado.");
        }

        /// <summary>Entrega a mensagem, repetindo em falha transitória com espera crescente.</summary>
        /// <param name="mensagem">Mensagem pronta.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        private async Task EntregarComRepeticaoAsync(MensagemEmail mensagem, CancellationToken cancelamento)
        {
            int tentativasRestantes = Math.Max(0, _opcoes.TentativasEmFalha);
            int espera = Math.Max(1, _opcoes.EsperaEntreTentativasMs);

            while (true)
            {
                try
                {
                    await _transporte.EntregarAsync(mensagem, cancelamento);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception) when (tentativasRestantes > 0)
                {
                    tentativasRestantes--;
                    await Task.Delay(espera, cancelamento);
                    espera *= 2;
                }
            }
        }

        /// <summary>Aplica a trava de homologação: troca os destinatários e prefixa o assunto.</summary>
        /// <param name="mensagem">Mensagem original.</param>
        /// <returns>A própria mensagem, ou uma cópia redirecionada.</returns>
        private MensagemEmail AplicarRedirecionamento(MensagemEmail mensagem)
        {
            if (_opcoes.RedirecionarPara.Count == 0)
                return mensagem;

            return mensagem with
            {
                Assunto = _opcoes.PrefixoAssuntoTeste + mensagem.Assunto,
                Destinatarios = [.. _opcoes.RedirecionarPara],
                Cc = [],
                Cco = []
            };
        }

        /// <summary>Materializa os anexos de um e-mail avulso.</summary>
        /// <param name="email">E-mail avulso.</param>
        /// <param name="excluirAposEnvio">Recebe os caminhos a excluir depois do envio.</param>
        /// <returns>Anexos prontos.</returns>
        private IReadOnlyList<AnexoMensagem> MaterializarAvulsos(EmailAvulso email, List<string> excluirAposEnvio)
        {
            List<AnexoMensagem> materializados = [];

            foreach (AnexoAvulso anexo in email.Anexos)
            {
                byte[]? bytes = ObterBytes(
                    anexo.Conteudo,
                    anexo.Caminho,
                    anexo.NomeArquivo,
                    anexo.ExcluirAposEnviar,
                    email.AcaoNaFalhaDeAnexo,
                    excluirAposEnvio);

                if (bytes is null)
                    continue;

                materializados.Add(new AnexoMensagem(anexo.NomeArquivo, anexo.ContentType, bytes, anexo.ContentId));
            }

            return materializados;
        }

        /// <summary>
        /// Materializa os anexos VINCULADOS ao template: busca cada arquivo do acervo, resolve os
        /// marcadores do caminho (quando dinâmico) e lê os bytes. O ContentId do vínculo é o que
        /// transforma a imagem em recurso do corpo (cid:) em vez de anexo no clipe.
        /// </summary>
        /// <param name="template">Template de origem.</param>
        /// <param name="dados">Objeto de dados (resolve o caminho dinâmico).</param>
        /// <param name="politica">Política em caso de anexo que falhe.</param>
        /// <param name="excluirAposEnvio">Recebe os caminhos a excluir depois do envio.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Anexos prontos.</returns>
        private async Task<IReadOnlyList<AnexoMensagem>> MaterializarDoTemplateAsync(
            ITemplateEmail template,
            object dados,
            AcaoFalhaAnexo politica,
            List<string> excluirAposEnvio,
            CancellationToken cancelamento)
        {
            if (_anexos is null)
                return [];

            IReadOnlyList<AnexoVinculado> vinculados = await _anexos.ListarDoTemplateAsync(template.Id, cancelamento);
            List<AnexoMensagem> materializados = [];

            foreach (AnexoVinculado item in vinculados.OrderBy(v => v.Vinculo.Ordem))
            {
                string? caminho = null;

                switch (item.Anexo.ModoObtencao)
                {
                    case ModoObtencaoAnexo.CaminhoFixo:
                        caminho = item.Anexo.Caminho;
                        break;
                    case ModoObtencaoAnexo.CaminhoDinamico:
                        // O caminho tem marcadores: resolve com o objeto de dados.
                        caminho = _motor.Resolver(item.Anexo.Caminho ?? string.Empty, dados).Texto;
                        break;
                    case ModoObtencaoAnexo.BytesNoBanco:
                    default:
                        break;
                }

                byte[]? bytes = ObterBytes(
                    item.Anexo.Conteudo,
                    caminho,
                    item.Anexo.NomeArquivo,
                    item.Vinculo.ExcluirAposAnexar,
                    politica,
                    excluirAposEnvio);

                if (bytes is null)
                    continue;

                materializados.Add(new AnexoMensagem(
                    item.Anexo.NomeArquivo,
                    item.Anexo.ContentType,
                    bytes,
                    item.Vinculo.ContentId));   // presente = recurso do corpo; nulo = anexo no clipe
            }

            return materializados;
        }

        /// <summary>Obtém os bytes de um anexo (de memória ou de disco), aplicando a política de falha.</summary>
        /// <param name="conteudo">Bytes já em memória, quando houver.</param>
        /// <param name="caminho">Caminho em disco, quando houver.</param>
        /// <param name="nomeArquivo">Nome do anexo (para a mensagem de erro).</param>
        /// <param name="excluirDepois">Se o arquivo deve ser excluído após o envio.</param>
        /// <param name="politica">Política em caso de falha.</param>
        /// <param name="excluirAposEnvio">Lista que recebe o caminho a excluir.</param>
        /// <returns>Bytes do anexo, ou nulo para pular (quando a política permite).</returns>
        private byte[]? ObterBytes(
            byte[]? conteudo,
            string? caminho,
            string nomeArquivo,
            bool excluirDepois,
            AcaoFalhaAnexo politica,
            List<string> excluirAposEnvio)
        {
            if (conteudo is not null)
                return conteudo;

            if (string.IsNullOrWhiteSpace(caminho))
                return TratarFalha(politica, $"anexo '{nomeArquivo}' sem conteúdo nem caminho");

            try
            {
                string seguro = CaminhoSeguro(caminho);
                byte[] bytes = File.ReadAllBytes(seguro);

                if (excluirDepois)
                    excluirAposEnvio.Add(seguro);

                return bytes;
            }
            catch (FalhaAnexoException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return TratarFalha(politica, $"falha ao ler o anexo '{nomeArquivo}': {ex.Message}");
            }
        }

        /// <summary>Aplica a política de falha: pula o anexo ou aborta o envio.</summary>
        /// <param name="politica">Política configurada.</param>
        /// <param name="motivo">Descrição da falha.</param>
        /// <returns>Nulo quando o anexo deve ser pulado.</returns>
        private static byte[]? TratarFalha(AcaoFalhaAnexo politica, string motivo)
        {
            switch (politica)
            {
                case AcaoFalhaAnexo.FalharEnvio:
                    throw new FalhaAnexoException($"Envio abortado — {motivo}.");
                case AcaoFalhaAnexo.EnviarMesmoAssim:
                default:
                    return null;
            }
        }

        /// <summary>Resolve o caminho do anexo confinando-o à pasta base (barreira contra path traversal).</summary>
        /// <param name="caminho">Caminho informado.</param>
        /// <returns>Caminho absoluto seguro, dentro da pasta base.</returns>
        private string CaminhoSeguro(string caminho)
        {
            if (string.IsNullOrWhiteSpace(_opcoes.PastaBaseAnexos))
                throw new FalhaAnexoException("PastaBaseAnexos não configurada — necessária para anexar arquivos de disco.");

            string baseDir = Path.GetFullPath(_opcoes.PastaBaseAnexos);
            string baseComBarra = baseDir.EndsWith(Path.DirectorySeparatorChar)
                ? baseDir
                : baseDir + Path.DirectorySeparatorChar;

            string combinado = Path.GetFullPath(Path.Combine(baseDir, caminho));

            if (!combinado.StartsWith(baseComBarra, StringComparison.OrdinalIgnoreCase))
                throw new FalhaAnexoException($"Caminho de anexo fora da pasta base permitida: {caminho}");

            return combinado;
        }

        /// <summary>Tenta excluir um arquivo (melhor esforço; não derruba um envio bem-sucedido).</summary>
        /// <param name="caminho">Caminho do arquivo.</param>
        private static void TentarExcluir(string caminho)
        {
            try
            {
                if (File.Exists(caminho))
                    File.Delete(caminho);
            }
            catch
            {
                // Exclusão é melhor esforço.
            }
        }
    }

    // #endregion

    // ───────────────────────────────────────────────────────────────────────
    // Seção: LogEmail.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  LogEmail.cs  —  Registro (log) dos envios de e-mail (BBiCore.Email)
// -----------------------------------------------------------------------------
//  ESTE ARQUIVO É O PONTO DE INTEGRAÇÃO COM O BANCO DE LOGS.
//
//  A estrutura do processo já está pronta e LIGADA ao fluxo de envio: o
//  ServicoEmail chama o registrador SEMPRE — no sucesso e na falha —, sem
//  depender de o dev de cada aplicação lembrar de nada. O que falta é apenas o
//  CORPO dos métodos (a gravação em si), que depende dos bancos corporativos.
//
//  PARA IMPLEMENTAR (marcados com "TODO" abaixo):
//    · RegistrarSucessoAsync  -> grava a linha de log do envio bem-sucedido.
//    · RegistrarFalhaAsync    -> grava a linha de log com o erro ocorrido.
//
//  OBSERVAÇÃO SOBRE RETENÇÃO (decisão já tomada): o registro do envio é
//  permanente e leve; o CORPO é o dado pesado e sensível, e deve ter prazo. O
//  expurgo previsto ANULA o corpo (e os nomes de anexo, se for o caso) e MANTÉM
//  a linha — assim não se perde a prova de que houve envio. Uma coluna de data
//  indexada é o que torna esse job viável.
//
//  OBSERVAÇÃO SOBRE DADO PESSOAL: o corpo resolvido contém o que os marcadores
//  trouxeram (nome, documento, valores). Ele é o campo a criptografar na
//  gravação.
// =============================================================================
    // #region Registro  ->  destino sugerido: Models/

    /// <summary>
    /// Retrato de um envio, pronto para ser gravado no log. Traz tudo o que a biblioteca sabe no
    /// momento do envio — o que gravar (e o que criptografar ou descartar) é decidido na gravação.
    /// </summary>
    /// <param name="DataHora">Momento do envio.</param>
    /// <param name="Remetente">Conta que enviou.</param>
    /// <param name="Destinatarios">Destinatários já resolvidos.</param>
    /// <param name="Cc">Cópia já resolvida.</param>
    /// <param name="Cco">Cópia oculta já resolvida.</param>
    /// <param name="Assunto">Assunto já resolvido.</param>
    /// <param name="CorpoHtml">Corpo já resolvido. É o dado sensível: criptografe na gravação e expurgue no prazo.</param>
    /// <param name="NomesAnexos">Nomes dos anexos (apenas os nomes — o conteúdo NÃO entra no log).</param>
    /// <param name="Classificacao">Rótulo de sensibilidade do e-mail.</param>
    /// <param name="NomeTemplate">Nome do template de origem; nulo quando o envio foi avulso.</param>
    /// <param name="Sucesso">Se o envio foi concluído.</param>
    /// <param name="MensagemErro">Descrição do erro, quando houve falha.</param>
    public sealed record RegistroEnvioEmail(
        DateTime DataHora,
        string Remetente,
        IReadOnlyList<string> Destinatarios,
        IReadOnlyList<string> Cc,
        IReadOnlyList<string> Cco,
        string Assunto,
        string CorpoHtml,
        IReadOnlyList<string> NomesAnexos,
        ClassificacaoEmail Classificacao,
        string? NomeTemplate,
        bool Sucesso,
        string? MensagemErro)
    {
        /// <summary>Cria o registro a partir da mensagem enviada.</summary>
        /// <param name="mensagem">Mensagem entregue (ou tentada).</param>
        /// <param name="nomeTemplate">Template de origem; nulo no envio avulso.</param>
        /// <param name="sucesso">Se o envio deu certo.</param>
        /// <param name="mensagemErro">Erro, quando houve.</param>
        /// <returns>Registro pronto para gravação.</returns>
        public static RegistroEnvioEmail De(
            MensagemEmail mensagem,
            string? nomeTemplate,
            bool sucesso,
            string? mensagemErro)
        {
            ArgumentNullException.ThrowIfNull(mensagem);

            return new RegistroEnvioEmail(
                DateTime.Now,
                mensagem.Remetente,
                mensagem.Destinatarios,
                mensagem.Cc,
                mensagem.Cco,
                mensagem.Assunto,
                mensagem.CorpoHtml,
                [.. mensagem.Anexos.Select(a => a.NomeArquivo)],
                mensagem.Classificacao,
                nomeTemplate,
                sucesso,
                mensagemErro);
        }
    }

    // #endregion

    // #region Registrador  ->  destino sugerido: Services/

    /// <summary>
    /// Grava os envios no banco de logs. É acionado pelo <see cref="ServicoEmail"/> em TODO envio —
    /// não há caminho de envio que não passe por aqui, e nenhuma aplicação precisa registrar nada
    /// por conta própria.
    /// </summary>
    public sealed class RegistradorEnvioEmail
    {
        /// <summary>Registra um envio bem-sucedido.</summary>
        /// <param name="registro">Retrato do envio.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        public Task RegistrarSucessoAsync(RegistroEnvioEmail registro, CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(registro);

            // TODO: gravar a linha no banco de LOGS.
            //   · corpo (registro.CorpoHtml) -> criptografar antes de gravar;
            //   · anexos -> gravar apenas os nomes (registro.NomesAnexos);
            //   · gravar a data (registro.DataHora) em coluna INDEXADA, para viabilizar o expurgo.
            return Task.CompletedTask;
        }

        /// <summary>
        /// Registra que a sanitização ALTEROU o HTML salvo pelo usuário. É registrado sempre que algo
        /// é removido — assim, se o usuário reclamar depois ("meu e-mail está diferente do que colei"),
        /// sabemos exatamente o que saiu, quando e de qual template.
        /// </summary>
        /// <param name="nomeTemplate">Template que estava sendo salvo.</param>
        /// <param name="remocoes">O que foi removido do HTML.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        public Task RegistrarSanitizacaoAsync(
            string nomeTemplate,
            IReadOnlyList<RemocaoHtml> remocoes,
            CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(remocoes);

            // TODO: gravar no banco de LOGS que o HTML de 'nomeTemplate' foi alterado na gravação,
            //       com a lista de remoções (cada uma traz Elemento e Motivo).
            return Task.CompletedTask;
        }

        /// <summary>Registra uma tentativa de envio que FALHOU, com o erro ocorrido.</summary>
        /// <param name="registro">Retrato do envio (o e-mail que se tentou enviar).</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        public Task RegistrarFalhaAsync(RegistroEnvioEmail registro, CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(registro);

            // TODO: gravar a linha no banco de LOGS, marcando a falha.
            //   · registro.MensagemErro traz a causa;
            //   · o e-mail que se tentou enviar vai junto (mesmo tratamento do sucesso:
            //     corpo criptografado, só nomes de anexo).
            return Task.CompletedTask;
        }
    }

    // #endregion

    // ───────────────────────────────────────────────────────────────────────
    // Seção: MontadorMime.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  MontadorMime.cs  —  Montagem da mensagem MIME (BBiCore.Email)
// -----------------------------------------------------------------------------
//  Ponto ÚNICO de montagem MIME, usado por quem precisa de um MimeMessage:
//    · TransporteSmtp     (envia pelo MailKit)
//    · TransporteSimulado (grava o .eml em disco)
//    · GeradorRascunho    (gera o .eml do rascunho)
//
//  Assim o .eml gerado é EXATAMENTE a mensagem que seria enviada — mesmo corpo,
//  mesmas imagens embutidas, mesmos anexos, mesma classificação.
//
//  Usa MimeKit (vem junto com o pacote MailKit).
// =============================================================================
    /// <summary>Monta a mensagem MIME a partir de uma <see cref="MensagemEmail"/> já pronta.</summary>
    internal static class MontadorMime
    {
        /// <summary>Monta a mensagem MIME, com imagens embutidas (cid:) e anexos comuns.</summary>
        /// <param name="mensagem">Mensagem pronta (tudo resolvido, anexos em memória).</param>
        /// <returns>Mensagem MIME equivalente.</returns>
        public static MimeMessage Montar(MensagemEmail mensagem)
        {
            ArgumentNullException.ThrowIfNull(mensagem);

            MimeMessage mime = new()
            {
                Subject = mensagem.Assunto
            };

            mime.From.Add(new MailboxAddress(
                mensagem.NomeRemetente ?? mensagem.Remetente,
                mensagem.Remetente));

            foreach (string destino in mensagem.Destinatarios)
                mime.To.Add(MailboxAddress.Parse(destino));

            foreach (string cc in mensagem.Cc)
                mime.Cc.Add(MailboxAddress.Parse(cc));

            foreach (string cco in mensagem.Cco)
                mime.Bcc.Add(MailboxAddress.Parse(cco));

            string? sensibilidade = MapearSensibilidade(mensagem.Classificacao);

            if (sensibilidade is not null)
                mime.Headers.Add("Sensitivity", sensibilidade);

            BodyBuilder corpo = new()
            {
                HtmlBody = mensagem.CorpoHtml
            };

            foreach (AnexoMensagem anexo in mensagem.Anexos)
            {
                if (anexo.EhInline)
                {
                    MimeEntity embutido = corpo.LinkedResources.Add(
                        anexo.NomeArquivo,
                        anexo.Conteudo,
                        ObterTipoConteudo(anexo.ContentType));

                    // O corpo referencia esta imagem por cid:{ContentId}.
                    embutido.ContentId = anexo.ContentId;
                }
                else
                {
                    corpo.Attachments.Add(
                        anexo.NomeArquivo,
                        anexo.Conteudo,
                        ObterTipoConteudo(anexo.ContentType));
                }
            }

            mime.Body = corpo.ToMessageBody();
            return mime;
        }

        /// <summary>Serializa a mensagem MIME como bytes de um arquivo .eml.</summary>
        /// <param name="mensagem">Mensagem pronta.</param>
        /// <returns>Bytes do .eml (abre no Outlook).</returns>
        public static byte[] GerarEml(MensagemEmail mensagem)
        {
            using MimeMessage mime = Montar(mensagem);
            using MemoryStream ms = new();

            mime.WriteTo(ms);
            return ms.ToArray();
        }

        /// <summary>Converte o content-type textual no tipo do MimeKit; usa binário genérico quando não informado.</summary>
        /// <param name="contentType">Content-type (ex.: "image/png").</param>
        /// <returns>Tipo de conteúdo correspondente.</returns>
        public static MimeKit.ContentType ObterTipoConteudo(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return new MimeKit.ContentType("application", "octet-stream");

            if (MimeKit.ContentType.TryParse(contentType, out MimeKit.ContentType? tipo) && tipo is not null)
                return tipo;

            return new MimeKit.ContentType("application", "octet-stream");
        }

        /// <summary>Mapeia a classificação para o cabeçalho Sensitivity do MIME.</summary>
        /// <param name="classificacao">Classificação do e-mail.</param>
        /// <returns>Valor do cabeçalho, ou nulo quando não se aplica.</returns>
        private static string? MapearSensibilidade(ClassificacaoEmail classificacao)
        {
            switch (classificacao)
            {
                case ClassificacaoEmail.Confidencial:
                    return "Company-Confidential";
                case ClassificacaoEmail.Interno:
                case ClassificacaoEmail.Publico:
                default:
                    return null;
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Seção: SanitizadorHtml.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  SanitizadorHtml.cs  —  Limpeza do HTML do e-mail (BBiCore.Email)
// -----------------------------------------------------------------------------
//  AUTOMÁTICO E NÃO NEGOCIÁVEL: roda sempre ao salvar um corpo escrito no modo
//  avançado. Não há parâmetro para desligar e não é decisão do dev — se fosse
//  opcional, faltaria em algum sistema.
//
//  FRONTEIRA: passa TUDO que é aparência e navegação; barra APENAS execução de
//  código.
//
//   PASSA  · tabelas completas, listas, formatação, parágrafos
//          · imagens (cid:, data:, http/https, GIF), imagem de fundo
//          · links http/https/mailto/tel — INCLUSIVE estilizados como botão
//            ("clique aqui para acessar o Registro" continua funcionando)
//          · style inline, bgcolor, background, width, height, align, border...
//
//   BARRA  · <script>, <iframe>, <object>, <embed>, <form>, <base>
//          · atributos de evento (onclick, onerror, onload — qualquer on*)
//          · href/src com esquema javascript: ou vbscript:
//          · dentro de style: expression() e url(javascript:)
//
//  TRANSPARÊNCIA: o que for removido é DEVOLVIDO na lista de remoções. A tela
//  avisa o usuário (ele precisa entender por que o que colou mudou) e o log
//  registra — assim, se ele reclamar depois, sabemos de onde veio.
//
//  Depende de HtmlAgilityPack (MIT). Análise por árvore, não por expressão
//  regular: HTML malformado é justamente o que engana filtros baseados em regex.
// =============================================================================
    // #region Resultado  ->  destino sugerido: Models/

    /// <summary>Um item retirado do HTML pela sanitização.</summary>
    /// <param name="Elemento">Tag ou atributo removido (ex.: "script", "onclick").</param>
    /// <param name="Motivo">Explicação em português, para exibir ao usuário e gravar no log.</param>
    public sealed record RemocaoHtml(string Elemento, string Motivo);

    /// <summary>Resultado da sanitização: o HTML limpo e o que precisou ser retirado.</summary>
    /// <param name="Html">HTML já limpo — é o que deve ser gravado.</param>
    /// <param name="Remocoes">Itens removidos. Vazio quando nada foi alterado.</param>
    public sealed record ResultadoSanitizacao(string Html, IReadOnlyList<RemocaoHtml> Remocoes)
    {
        /// <summary>Indica que o HTML gravado é DIFERENTE do que o usuário escreveu.</summary>
        public bool Alterado => Remocoes.Count > 0;

        /// <summary>Resumo das remoções, pronto para a mensagem de tela e para o log.</summary>
        /// <returns>Texto único descrevendo o que saiu.</returns>
        public string Resumo()
        {
            if (Remocoes.Count == 0)
                return string.Empty;

            IEnumerable<string> itens = Remocoes
                .Select(r => r.Elemento)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join(", ", itens);
        }
    }

    // #endregion

    // #region Sanitizador  ->  destino sugerido: Services/

    /// <summary>Remove do HTML tudo o que pode executar código, preservando toda a parte visual.</summary>
    public static class SanitizadorHtml
    {
        /// <summary>Tags que não têm uso legítimo em e-mail e são removidas inteiras (com o conteúdo).</summary>
        private static readonly HashSet<string> TagsProibidas = new(StringComparer.OrdinalIgnoreCase)
        {
            "script", "iframe", "object", "embed", "applet", "form", "input",
            "button", "select", "textarea", "base", "link", "meta", "frame", "frameset"
        };

        /// <summary>Atributos que carregam URL e por isso precisam ter o esquema conferido.</summary>
        private static readonly HashSet<string> AtributosDeUrl = new(StringComparer.OrdinalIgnoreCase)
        {
            "href", "src", "background", "action", "formaction", "poster"
        };

        /// <summary>Esquemas de URL que executam código e nunca são aceitos.</summary>
        private static readonly string[] EsquemasProibidos = ["javascript:", "vbscript:", "data:text/html"];

        /// <summary>Construções de CSS que executam código (IE antigo e url(javascript:)).</summary>
        private static readonly Regex CssPerigoso = new(
            @"expression\s*\(|url\s*\(\s*['""]?\s*(javascript|vbscript)\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Limpa o HTML: remove execução de código e devolve o que foi retirado. O HTML devolvido é o
        /// que deve ser gravado.
        /// </summary>
        /// <param name="html">HTML escrito pelo usuário (modo avançado).</param>
        /// <returns>HTML limpo e a lista de remoções.</returns>
        public static ResultadoSanitizacao Sanitizar(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return new ResultadoSanitizacao(string.Empty, []);

            HtmlDocument documento = new();
            documento.LoadHtml(html);

            List<RemocaoHtml> remocoes = [];

            RemoverTagsProibidas(documento, remocoes);
            LimparAtributos(documento, remocoes);
            RemoverComentarios(documento);

            return new ResultadoSanitizacao(documento.DocumentNode.OuterHtml, remocoes);
        }

        /// <summary>Remove as tags que executam código ou não fazem sentido em e-mail.</summary>
        /// <param name="documento">Documento HTML.</param>
        /// <param name="remocoes">Lista que acumula o que foi retirado.</param>
        private static void RemoverTagsProibidas(HtmlDocument documento, List<RemocaoHtml> remocoes)
        {
            List<HtmlNode> alvos = documento.DocumentNode
                .Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Element && TagsProibidas.Contains(n.Name))
                .ToList();

            foreach (HtmlNode no in alvos)
            {
                remocoes.Add(new RemocaoHtml(
                    $"<{no.Name}>",
                    $"a tag <{no.Name}> não é permitida em e-mail (pode executar código)."));

                no.Remove();
            }
        }

        /// <summary>Remove atributos de evento, URLs com esquema perigoso e CSS que executa código.</summary>
        /// <param name="documento">Documento HTML.</param>
        /// <param name="remocoes">Lista que acumula o que foi retirado.</param>
        private static void LimparAtributos(HtmlDocument documento, List<RemocaoHtml> remocoes)
        {
            List<HtmlNode> elementos = documento.DocumentNode
                .Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Element && n.HasAttributes)
                .ToList();

            foreach (HtmlNode elemento in elementos)
            {
                List<HtmlAttribute> atributos = [.. elemento.Attributes];

                foreach (HtmlAttribute atributo in atributos)
                {
                    // Eventos: onclick, onerror, onload... nada disso é visual.
                    if (atributo.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    {
                        remocoes.Add(new RemocaoHtml(
                            atributo.Name,
                            $"o atributo {atributo.Name} executa código e foi removido."));

                        elemento.Attributes.Remove(atributo);
                        continue;
                    }

                    // URLs: o link fica; o esquema que executa código sai.
                    if (AtributosDeUrl.Contains(atributo.Name) && EsquemaProibido(atributo.Value))
                    {
                        remocoes.Add(new RemocaoHtml(
                            $"{atributo.Name}=javascript:",
                            $"o endereço em {atributo.Name} executava código (javascript:) e foi removido."));

                        elemento.Attributes.Remove(atributo);
                        continue;
                    }

                    // CSS: mantém o estilo legítimo, tira só a DECLARAÇÃO que executa código
                    // (remover apenas o trecho deixaria resíduo do tipo "background:alert(1))").
                    if (atributo.Name.Equals("style", StringComparison.OrdinalIgnoreCase)
                        && CssPerigoso.IsMatch(atributo.Value ?? string.Empty))
                    {
                        string limpo = LimparEstilo(atributo.Value ?? string.Empty);

                        remocoes.Add(new RemocaoHtml(
                            "style",
                            "o estilo continha uma instrução que executa código (expression/javascript) e ela foi removida."));

                        if (string.IsNullOrWhiteSpace(limpo))
                            elemento.Attributes.Remove(atributo);
                        else
                            atributo.Value = limpo;
                    }
                }
            }
        }

        /// <summary>
        /// Limpa o atributo style descartando as DECLARAÇÕES que executam código e preservando as
        /// demais (ex.: de "width:100%;background:expression(x)" sobra "width:100%").
        /// </summary>
        /// <param name="estilo">Conteúdo do atributo style.</param>
        /// <returns>Estilo sem as declarações perigosas.</returns>
        private static string LimparEstilo(string estilo)
        {
            List<string> mantidas = [];

            foreach (string declaracao in estilo.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (CssPerigoso.IsMatch(declaracao))
                    continue;

                string limpa = declaracao.Trim();

                if (!string.IsNullOrEmpty(limpa))
                    mantidas.Add(limpa);
            }

            return mantidas.Count == 0 ? string.Empty : string.Join("; ", mantidas);
        }

        /// <summary>Remove comentários HTML (podem esconder conteúdo condicional executável).</summary>
        /// <param name="documento">Documento HTML.</param>
        private static void RemoverComentarios(HtmlDocument documento)
        {
            List<HtmlNode> comentarios = documento.DocumentNode
                .Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Comment)
                .ToList();

            foreach (HtmlNode comentario in comentarios)
                comentario.Remove();
        }

        /// <summary>Confere se o valor de uma URL usa um esquema que executa código.</summary>
        /// <param name="valor">Valor do atributo.</param>
        /// <returns>Verdadeiro quando o esquema é proibido.</returns>
        private static bool EsquemaProibido(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return false;

            // Espaços, quebras e maiúsculas são usados para disfarçar o esquema.
            string normalizado = valor
                .Replace(" ", string.Empty)
                .Replace("\t", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\r", string.Empty)
                .ToLowerInvariant();

            foreach (string esquema in EsquemasProibidos)
                if (normalizado.StartsWith(esquema, StringComparison.Ordinal))
                    return true;

            return false;
        }
    }

    // #endregion

    // ───────────────────────────────────────────────────────────────────────
    // Seção: ServicoAnexos.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  ServicoAnexos.cs  —  Regras de anexo do template (BBiCore.Email)
// -----------------------------------------------------------------------------
//  As regras aqui NÃO são opcionais: a biblioteca as garante em toda gravação,
//  sem parâmetro para desligar e sem depender de o dev de cada aplicação
//  lembrar de validar.
//
//   · Papel de corpo (cabeçalho, rodapé, inline) exige IMAGEM. Subir um PDF como
//     cabeçalho é recusado.
//   · Cabeçalho e rodapé: no máximo UM por template. Vincular um novo SUBSTITUI
//     o anterior (é o que a tela sugere: um campo, não uma lista).
//   · ContentId é GERADO pela biblioteca para todo recurso de corpo — nunca vem
//     do usuário, então não há colisão nem erro de digitação.
//   · Exclusividade: um template pode REIVINDICAR um arquivo do acervo. A regra
//     é verificada na CRIAÇÃO do vínculo, quando ainda dá para barrar sem afetar
//     quem já usava.
// =============================================================================
    /// <summary>Resultado de uma tentativa de vincular um arquivo do acervo a um template.</summary>
    /// <param name="Sucesso">Se o vínculo foi criado.</param>
    /// <param name="Mensagem">Motivo da recusa, quando houver.</param>
    /// <param name="Vinculo">Vínculo criado, em caso de sucesso.</param>
    public sealed record ResultadoVinculo(bool Sucesso, string? Mensagem, IVinculoAnexo? Vinculo = null);

    /// <summary>Aplica as regras de anexo e conversa com o repositório. É por aqui que a tela vincula arquivos.</summary>
    public sealed class ServicoAnexos
    {
        /// <summary>Acesso ao acervo e aos vínculos.</summary>
        private readonly IRepositorioAnexos _repositorio;

        /// <summary>Cria o serviço de anexos.</summary>
        /// <param name="repositorio">Repositório de anexos do sistema.</param>
        public ServicoAnexos(IRepositorioAnexos repositorio)
            => _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));

        /// <summary>Lista os arquivos do acervo que este template pode usar, filtrados pelo papel pretendido.</summary>
        /// <param name="idTemplate">Template que está montando a lista.</param>
        /// <param name="papel">Papel pretendido: cabeçalho/rodapé/inline mostram só imagens.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Arquivos oferecíveis ao usuário naquele contexto.</returns>
        public async Task<IReadOnlyList<IAnexoAcervo>> ListarParaEscolhaAsync(
            int idTemplate,
            PapelAnexo papel,
            CancellationToken cancelamento = default)
        {
            IReadOnlyList<IAnexoAcervo> disponiveis = await _repositorio.ListarDisponiveisAsync(idTemplate, cancelamento);

            // A lista é filtrada pelo CONTEXTO: escolhendo um cabeçalho, o usuário só vê imagens.
            List<IAnexoAcervo> filtrados = [];

            foreach (IAnexoAcervo anexo in disponiveis)
                if (RegrasAnexo.PapelPermitido(papel, anexo.ContentType))
                    filtrados.Add(anexo);

            return filtrados;
        }

        /// <summary>Lista os arquivos já vinculados ao template, com o papel de cada um.</summary>
        /// <param name="idTemplate">Template.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Arquivos vinculados.</returns>
        public Task<IReadOnlyList<AnexoVinculado>> ListarDoTemplateAsync(int idTemplate, CancellationToken cancelamento = default)
            => _repositorio.ListarDoTemplateAsync(idTemplate, cancelamento);

        /// <summary>
        /// Vincula um arquivo do acervo a um template, aplicando TODAS as regras. Cabeçalho e rodapé
        /// substituem o anterior; o ContentId é gerado aqui.
        /// </summary>
        /// <param name="idTemplate">Template.</param>
        /// <param name="anexo">Arquivo do acervo.</param>
        /// <param name="papel">Papel pretendido.</param>
        /// <param name="exclusivo">Se este template reivindica o arquivo para si.</param>
        /// <param name="excluirAposAnexar">Se o arquivo de origem deve ser excluído após o envio (só nos modos de caminho).</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado do vínculo.</returns>
        public async Task<ResultadoVinculo> VincularAsync(
            int idTemplate,
            IAnexoAcervo anexo,
            PapelAnexo papel,
            bool exclusivo = false,
            bool excluirAposAnexar = false,
            CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(anexo);

            // REGRA 1 — papel de corpo exige imagem.
            if (!RegrasAnexo.PapelPermitido(papel, anexo.ContentType))
                return new ResultadoVinculo(
                    false,
                    $"'{anexo.NomeArquivo}' não é uma imagem — só imagens podem ser {RegrasAnexo.Rotulo(papel).ToLowerInvariant()}.");

            // REGRA 2 — o arquivo não pode estar reivindicado por outro template.
            if (await _repositorio.EstaReivindicadoPorOutroAsync(anexo.Id, idTemplate, cancelamento))
                return new ResultadoVinculo(
                    false,
                    $"'{anexo.NomeArquivo}' foi reservado com exclusividade por outro template.");

            // REGRA 3 — para reivindicar, o arquivo não pode estar em uso por mais ninguém.
            if (exclusivo && await _repositorio.EstaEmUsoPorOutroAsync(anexo.Id, idTemplate, cancelamento))
                return new ResultadoVinculo(
                    false,
                    $"'{anexo.NomeArquivo}' já é usado por outros templates e não pode ser reservado com exclusividade.");

            // REGRA 4 — cabeçalho e rodapé são únicos: o novo substitui o anterior.
            if (RegrasAnexo.EhPapelUnico(papel))
                await RemoverPapelAsync(idTemplate, papel, cancelamento);

            // REGRA 5 — excluir-após-anexar só faz sentido quando o arquivo vem de disco.
            bool excluir = excluirAposAnexar && anexo.ModoObtencao != ModoObtencaoAnexo.BytesNoBanco;

            IReadOnlyList<AnexoVinculado> atuais = await _repositorio.ListarDoTemplateAsync(idTemplate, cancelamento);

            VinculoAnexoDto vinculo = new()
            {
                IdTemplate = idTemplate,
                IdAnexo = anexo.Id,
                Papel = papel,
                Exclusivo = exclusivo,
                ContentId = RegrasAnexo.GerarContentId(papel),   // gerado pela lib, nunca digitado
                Ordem = atuais.Count,
                ExcluirAposAnexar = excluir
            };

            await _repositorio.SalvarVinculoAsync(vinculo, cancelamento);
            return new ResultadoVinculo(true, null, vinculo);
        }

        /// <summary>Remove o vínculo de um arquivo com o template (o arquivo continua no acervo).</summary>
        /// <param name="idVinculo">Vínculo a remover.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        public Task DesvincularAsync(int idVinculo, CancellationToken cancelamento = default)
            => _repositorio.RemoverVinculoAsync(idVinculo, cancelamento);

        /// <summary>Cadastra um arquivo novo no acervo e já o vincula ao template.</summary>
        /// <param name="idTemplate">Template.</param>
        /// <param name="anexo">Arquivo a cadastrar.</param>
        /// <param name="papel">Papel pretendido.</param>
        /// <param name="exclusivo">Se o template reivindica o arquivo.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Resultado do vínculo.</returns>
        public async Task<ResultadoVinculo> CadastrarEVincularAsync(
            int idTemplate,
            IAnexoAcervo anexo,
            PapelAnexo papel,
            bool exclusivo = false,
            CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(anexo);

            if (!RegrasAnexo.PapelPermitido(papel, anexo.ContentType))
                return new ResultadoVinculo(
                    false,
                    $"'{anexo.NomeArquivo}' não é uma imagem — só imagens podem ser {RegrasAnexo.Rotulo(papel).ToLowerInvariant()}.");

            anexo.Id = await _repositorio.SalvarAnexoAsync(anexo, cancelamento);
            return await VincularAsync(idTemplate, anexo, papel, exclusivo, false, cancelamento);
        }

        /// <summary>Remove o vínculo do papel único (cabeçalho ou rodapé) que porventura já exista no template.</summary>
        /// <param name="idTemplate">Template.</param>
        /// <param name="papel">Papel único a liberar.</param>
        /// <param name="cancelamento">Token de cancelamento.</param>
        private async Task RemoverPapelAsync(int idTemplate, PapelAnexo papel, CancellationToken cancelamento)
        {
            IReadOnlyList<AnexoVinculado> atuais = await _repositorio.ListarDoTemplateAsync(idTemplate, cancelamento);

            foreach (AnexoVinculado atual in atuais)
                if (atual.Vinculo.Papel == papel)
                    await _repositorio.RemoverVinculoAsync(atual.Vinculo.Id, cancelamento);
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Seção: TransporteSimulado.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  TransporteSimulado.cs  —  Entrega simulada em disco (BBiCore.Email)
// -----------------------------------------------------------------------------
//  Em vez de transmitir, grava o e-mail montado como arquivo .eml numa pasta.
//  Serve para testar o fluxo inteiro — inclusive anexos e imagens embutidas —
//  sem depender de servidor. O .eml gerado abre no Outlook.
//
//  Registre este transporte em desenvolvimento/homologação; em produção, troque
//  pelo TransporteExchange ou pelo TransporteSmtp. Nada mais muda.
// =============================================================================
    /// <summary>Transporte de SIMULAÇÃO: grava o e-mail em disco (.eml) em vez de enviá-lo.</summary>
    public sealed class TransporteSimulado : ITransporteEmail
    {
        /// <summary>Configurações do sistema.</summary>
        private readonly OpcoesEmail _opcoes;

        /// <summary>Cria o transporte simulado.</summary>
        /// <param name="opcoes">Configurações do sistema (usa PastaSimulacao).</param>
        public TransporteSimulado(OpcoesEmail opcoes)
            => _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));

        /// <summary>Pasta onde os arquivos são gravados.</summary>
        public string PastaDestino => string.IsNullOrWhiteSpace(_opcoes.PastaSimulacao)
            ? Path.Combine(Path.GetTempPath(), "bbi-emails")
            : _opcoes.PastaSimulacao;

        /// <inheritdoc/>
        public async Task EntregarAsync(MensagemEmail mensagem, CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(mensagem);

            Directory.CreateDirectory(PastaDestino);

            // Mesma montagem do envio real: o .eml reflete fielmente o que seria transmitido.
            byte[] eml = MontadorMime.GerarEml(mensagem);
            string caminho = Path.Combine(PastaDestino, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.eml");

            await File.WriteAllBytesAsync(caminho, eml, cancelamento);
        }

        /// <inheritdoc/>
        public Task<ResultadoEnvio> TestarConexaoAsync(CancellationToken cancelamento = default)
        {
            try
            {
                // Não há servidor: o "teste" é confirmar que a pasta de destino pode ser criada/escrita.
                Directory.CreateDirectory(PastaDestino);

                return Task.FromResult(new ResultadoEnvio(
                    true,
                    $"Transporte simulado pronto — os e-mails serão gravados em {PastaDestino}."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ResultadoEnvio(false, $"Pasta de simulação inacessível: {ex.Message}"));
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Seção: TransporteSmtp.cs
    // ───────────────────────────────────────────────────────────────────────
// =============================================================================
//  TransporteSmtp.cs  —  Entrega via SMTP usando MailKit (BBiCore.Email)
// -----------------------------------------------------------------------------
//  Depende do pacote MailKit (NuGet público). É o caminho moderno: o SmtpClient
//  da BCL está obsoleto e a própria Microsoft recomenda o MailKit no lugar.
//
//  Se preferir o Exchange (EWS), use o TransporteExchange — a troca é só o
//  registro na injeção de dependência; nada mais muda.
// =============================================================================
    /// <summary>Entrega e-mails por SMTP (relay interno ou servidor autenticado), via MailKit.</summary>
    public sealed class TransporteSmtp : ITransporteEmail
    {
        /// <summary>Configurações do sistema.</summary>
        private readonly OpcoesEmail _opcoes;

        /// <summary>Cadastro do sistema: é dele que saem as credenciais (a aplicação não as fornece).</summary>
        private readonly CadastroSistema _cadastro;

        /// <summary>Cria o transporte SMTP.</summary>
        /// <param name="opcoes">Configurações do sistema.</param>
        /// <param name="cadastro">Acesso ao cadastro do sistema (fonte das credenciais).</param>
        public TransporteSmtp(OpcoesEmail opcoes, CadastroSistema cadastro)
        {
            _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));
            _cadastro = cadastro ?? throw new ArgumentNullException(nameof(cadastro));
        }

        /// <inheritdoc/>
        public async Task EntregarAsync(MensagemEmail mensagem, CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(mensagem);

            CredenciaisEmail credenciais = await ObterCredenciaisAsync(cancelamento);
            MimeMessage mime = MontadorMime.Montar(AjustarRemetente(mensagem, credenciais));

            using MailKit.Net.Smtp.SmtpClient cliente = new()
            {
                Timeout = _opcoes.TimeoutSegundos * 1000
            };

            if (!_opcoes.ValidarCertificadoServidor)
                cliente.ServerCertificateValidationCallback = (remetente, certificado, cadeia, erros) => true;

            SecureSocketOptions seguranca = _opcoes.UsarSsl
                ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.None;

            await cliente.ConnectAsync(_opcoes.Host, _opcoes.Porta, seguranca, cancelamento);

            // Relay interno costuma não exigir autenticação: só autentica se houver usuário.
            if (!string.IsNullOrWhiteSpace(credenciais.Usuario))
                await cliente.AuthenticateAsync(credenciais.Usuario, credenciais.Senha, cancelamento);

            await cliente.SendAsync(mime, cancelamento);
            await cliente.DisconnectAsync(true, cancelamento);
        }

        /// <inheritdoc/>
        public async Task<ResultadoEnvio> TestarConexaoAsync(CancellationToken cancelamento = default)
        {
            try
            {
                CredenciaisEmail credenciais = await ObterCredenciaisAsync(cancelamento);

                using MailKit.Net.Smtp.SmtpClient cliente = new()
                {
                    Timeout = _opcoes.TimeoutSegundos * 1000
                };

                if (!_opcoes.ValidarCertificadoServidor)
                    cliente.ServerCertificateValidationCallback = (remetente, certificado, cadeia, erros) => true;

                SecureSocketOptions seguranca = _opcoes.UsarSsl
                    ? SecureSocketOptions.StartTlsWhenAvailable
                    : SecureSocketOptions.None;

                await cliente.ConnectAsync(_opcoes.Host, _opcoes.Porta, seguranca, cancelamento);

                if (!string.IsNullOrWhiteSpace(credenciais.Usuario))
                    await cliente.AuthenticateAsync(credenciais.Usuario, credenciais.Senha, cancelamento);

                await cliente.DisconnectAsync(true, cancelamento);

                return new ResultadoEnvio(true, $"Conexão com {_opcoes.Host}:{_opcoes.Porta} bem-sucedida.");
            }
            catch (Exception ex)
            {
                return new ResultadoEnvio(false, $"Falha na conexão: {ex.Message}");
            }
        }

        /// <summary>Obtém as credenciais do cadastro do sistema.</summary>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Credenciais prontas.</returns>
        private async Task<CredenciaisEmail> ObterCredenciaisAsync(CancellationToken cancelamento)
            => await _cadastro.ObterCredenciaisAsync(cancelamento);

        /// <summary>Troca o remetente da mensagem pela conta das credenciais, quando elas informam uma.</summary>
        /// <param name="mensagem">Mensagem pronta.</param>
        /// <param name="credenciais">Credenciais em uso.</param>
        /// <returns>A mensagem, com o remetente ajustado quando necessário.</returns>
        private static MensagemEmail AjustarRemetente(MensagemEmail mensagem, CredenciaisEmail credenciais)
        {
            if (string.IsNullOrWhiteSpace(credenciais.EnderecoRemetente))
                return mensagem;

            return mensagem with { Remetente = credenciais.EnderecoRemetente };
        }
    }

}