// =============================================================================
//  Importacao.cs  —  Módulo de importação tabular (CSV/Excel) e despivot
//  Namespaces: BBiCore.Importacao (motor/leitores) e BBiCore.Componentes
//  (tipos de apoio dos componentes de importação). Peça o BBiImportadorArquivo
//  na mesma pasta como a porta de entrada visual.
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

namespace BBiCore.Importacao
{

// #region Enums e opções  ->  destino sugerido: Enums/ e Options/ (ou Models/)

/// <summary>Define como o motor reage a falhas durante a importação.</summary>
public enum ModoImportacao
{
    /// <summary>Processa o arquivo inteiro e reporta todas as falhas encontradas.</summary>
    Completo,

    /// <summary>Encerra a importação assim que a primeira falha ocorre.</summary>
    PararNaPrimeiraFalha
}

/// <summary>Controla como linhas em branco na região de dados são tratadas.</summary>
public enum TratamentoLinhasEmBranco
{
    /// <summary>Ignora qualquer linha em branco, em qualquer posição (padrão).</summary>
    Todas,

    /// <summary>Ignora linhas em branco apenas antes da primeira linha de dados; a partir daí, uma linha em branco é tratada como dado.</summary>
    SomenteIniciais,

    /// <summary>Não ignora nenhuma linha em branco; toda linha vazia é tratada como dado.</summary>
    Nao
}

/// <summary>Situação de uma linha processada.</summary>
public enum StatusLinha
{
    /// <summary>Todas as colunas (e o callback de linha) converteram com sucesso.</summary>
    Ok,

    /// <summary>Pelo menos uma coluna ou o callback de linha falhou.</summary>
    Falha
}

/// <summary>Opções que valem para a importação inteira (não para uma coluna específica).</summary>
public sealed class OpcoesImportacao
{
    /// <summary>Cultura padrão das colunas que não definem a própria. Padrão: pt-BR.</summary>
    public CultureInfo CulturaGlobal { get; set; } = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Comportamento diante de falhas. Padrão: <see cref="ModoImportacao.Completo"/>.</summary>
    public ModoImportacao Modo { get; set; } = ModoImportacao.Completo;

    /// <summary>
    /// Linha física do cabeçalho (1 = primeira linha). Nulo autodetecta: usa a primeira linha
    /// não-vazia. Ignorado quando o mapa é sem cabeçalho.
    /// </summary>
    public int? LinhaCabecalho { get; set; }

    /// <summary>
    /// Linha física onde os dados começam (1 = primeira linha). Nulo faz os dados começarem logo
    /// após o cabeçalho. Use para o caso "cabeçalho na linha 2, dados na linha 10".
    /// </summary>
    public int? LinhaInicioDados { get; set; }

    /// <summary>
    /// Coluna onde a tabela começa (1 = primeira coluna). Recorta as colunas à esquerda, tanto do
    /// cabeçalho quanto dos dados. As posições de <c>ColunaPosicao</c> passam a ser relativas a
    /// este início. Padrão: 1 (sem recorte).
    /// </summary>
    public int ColunaInicio { get; set; } = 1;

    /// <summary>Tratamento de linhas em branco na região de dados. Padrão: <see cref="TratamentoLinhasEmBranco.Todas"/>.</summary>
    public TratamentoLinhasEmBranco LinhasEmBranco { get; set; } = TratamentoLinhasEmBranco.Todas;
}

// #endregion

// #region Resultado  ->  destino sugerido: Models/ (ou Results/)

/// <summary>Descreve uma falha ocorrida em uma coluna (ou no callback de linha).</summary>
/// <param name="Origem">Coluna de origem no arquivo (nome ou posição). Nulo quando a falha é da linha inteira.</param>
/// <param name="Destino">Propriedade de destino no objeto.</param>
/// <param name="ValorBruto">Valor lido do arquivo que causou a falha, quando aplicável.</param>
/// <param name="Mensagem">Mensagem descritiva da falha.</param>
public sealed record FalhaImportacao(string? Origem, string Destino, string? ValorBruto, string Mensagem);

/// <summary>Resultado do processamento de uma única linha do arquivo.</summary>
/// <typeparam name="T">Tipo da entidade produzida.</typeparam>
public sealed class LinhaResultado<T>
{
    /// <summary>Nome do arquivo de origem.</summary>
    public required string Arquivo { get; init; }

    /// <summary>Número FÍSICO da linha no arquivo (linhas puladas e em branco também são contadas).</summary>
    public required int Linha { get; init; }

    /// <summary>Situação da linha.</summary>
    public StatusLinha Status { get; init; }

    /// <summary>Entidade produzida. Preenchida apenas quando <see cref="Status"/> é <see cref="StatusLinha.Ok"/>.</summary>
    public T? Item { get; init; }

    /// <summary>Falhas da linha. Vazia quando a linha é Ok.</summary>
    public List<FalhaImportacao> Falhas { get; init; } = [];

    /// <summary>Indica se a linha foi processada com sucesso.</summary>
    public bool Ok => Status == StatusLinha.Ok;
}

/// <summary>Resultado consolidado de uma importação: relatório por linha e lista tipada.</summary>
/// <typeparam name="T">Tipo da entidade produzida.</typeparam>
public sealed class ResultadoImportacao<T>
{
    /// <summary>Relatório com uma entrada por linha de dados lida (Ok ou Falha).</summary>
    public List<LinhaResultado<T>> Relatorio { get; init; } = [];

    /// <summary>Itens importados com sucesso — projeção das linhas Ok do relatório.</summary>
    public List<T> Itens => Relatorio.Where(l => l.Ok && l.Item is not null).Select(l => l.Item!).ToList();

    /// <summary>Total de linhas de dados processadas.</summary>
    public int Total => Relatorio.Count;

    /// <summary>Quantidade de linhas Ok.</summary>
    public int TotalOk => Relatorio.Count(l => l.Ok);

    /// <summary>Quantidade de linhas com falha.</summary>
    public int TotalFalha => Relatorio.Count(l => !l.Ok);

    /// <summary>Verdadeiro quando não houve nenhuma falha.</summary>
    public bool Sucesso => TotalFalha == 0;

    /// <summary>Permite desestruturar em (relatório, itens).</summary>
    /// <param name="relatorio">Recebe o relatório completo.</param>
    /// <param name="itens">Recebe a lista de itens Ok.</param>
    public void Deconstruct(out List<LinhaResultado<T>> relatorio, out List<T> itens)
    {
        relatorio = Relatorio;
        itens = Itens;
    }
}

// #endregion

// #region Contratos  ->  destino sugerido: Interfaces/ (ou Abstractions/)

/// <summary>Lê um arquivo tabular como sequência de linhas de células com valor nativo.</summary>
public interface ILeitorTabular
{
    /// <summary>Indica se este leitor suporta o arquivo informado (pela extensão).</summary>
    /// <param name="nomeArquivo">Nome do arquivo, com extensão.</param>
    /// <returns>Verdadeiro se o formato é suportado.</returns>
    bool Suporta(string nomeArquivo);

    /// <summary>Lê o conteúdo do stream, devolvendo cada linha como um vetor de células.</summary>
    /// <param name="stream">Fluxo do arquivo.</param>
    /// <returns>Sequência de linhas; cada linha é um vetor de valores nativos.</returns>
    IEnumerable<object?[]> LerLinhas(Stream stream);
}

/// <summary>Acesso às células cruas de uma linha, entregue ao callback de linha.</summary>
public interface IAcessorLinha
{
    /// <summary>Lê uma célula pelo nome da coluna (exige cabeçalho).</summary>
    /// <param name="nome">Nome da coluna.</param>
    /// <returns>Texto da célula, ou nulo se a coluna não existir.</returns>
    string? PorNome(string nome);

    /// <summary>Lê uma célula pela posição (0-based).</summary>
    /// <param name="indice">Índice da coluna.</param>
    /// <returns>Texto da célula, ou nulo se fora do intervalo.</returns>
    string? PorPosicao(int indice);
}

// #endregion

// #region Mapa  ->  destino sugerido: Mapping/ (ColunaMapeada é internal)

/// <summary>Definição interna de uma coluna do mapa. Construída via <see cref="MapaImportacao{T}"/>; não é exposta ao consumidor.</summary>
/// <typeparam name="T">Tipo da entidade de destino.</typeparam>
internal sealed class ColunaMapeada<T>
{
    /// <summary>Ação que grava o valor convertido na propriedade de destino.</summary>
    public required Action<T, object?> Setter { get; init; }

    /// <summary>Nome da propriedade de destino.</summary>
    public required string Destino { get; init; }

    /// <summary>Tipo da propriedade de destino.</summary>
    public required Type TipoDestino { get; init; }

    /// <summary>Nomes de coluna aceitos na origem (quando o mapeamento é por nome).</summary>
    public IReadOnlyList<string> NomesOrigem { get; init; } = [];

    /// <summary>Posição da coluna na origem (quando o mapeamento é por índice, 0-based).</summary>
    public int? PosicaoOrigem { get; init; }

    /// <summary>Indica se um valor vazio nesta coluna gera falha.</summary>
    public bool Obrigatoria { get; init; }

    /// <summary>Cultura específica desta coluna; sobrescreve a global quando informada.</summary>
    public CultureInfo? Cultura { get; init; }

    /// <summary>Formato explícito de conversão (ex.: data), quando informado.</summary>
    public string? Formato { get; init; }

    /// <summary>Indica se há um valor padrão para célula vazia (coluna não obrigatória).</summary>
    public bool TemValorPadrao { get; init; }

    /// <summary>Valor padrão aplicado quando a célula vem vazia.</summary>
    public object? ValorPadrao { get; init; }

    /// <summary>Conversor próprio da coluna; quando presente, substitui a conversão padrão.</summary>
    public Func<string, CultureInfo, object?>? Callback { get; init; }

    /// <summary>Descrição legível da origem (nomes aceitos ou posição), usada no relatório de falhas.</summary>
    public string Origem =>
        NomesOrigem.Count > 0 ? string.Join(" / ", NomesOrigem) : $"posição {PosicaoOrigem}";
}

/// <summary>Mapa de importação: descreve como cada coluna do arquivo alimenta uma propriedade de <typeparamref name="T"/>.</summary>
/// <typeparam name="T">Tipo da entidade produzida (precisa de construtor sem parâmetros).</typeparam>
public sealed class MapaImportacao<T> where T : new()
{
    /// <summary>Colunas configuradas, na ordem de declaração.</summary>
    private readonly List<ColunaMapeada<T>> _colunas = [];

    /// <summary>Indica se o arquivo possui linha de cabeçalho.</summary>
    public bool TemCabecalho { get; }

    /// <summary>Colunas mapeadas (uso interno do motor).</summary>
    internal IReadOnlyList<ColunaMapeada<T>> Colunas => _colunas;

    /// <summary>Callback executado ao final de cada linha Ok (uso interno do motor).</summary>
    internal Action<T, IAcessorLinha>? CallbackLinha { get; private set; }

    /// <summary>Construtor privado; use <see cref="ComCabecalho"/> ou <see cref="SemCabecalho"/>.</summary>
    /// <param name="temCabecalho">Se o arquivo tem cabeçalho.</param>
    private MapaImportacao(bool temCabecalho) => TemCabecalho = temCabecalho;

    /// <summary>Cria um mapa para arquivo COM linha de cabeçalho (colunas por nome).</summary>
    /// <returns>Novo mapa.</returns>
    public static MapaImportacao<T> ComCabecalho() => new(true);

    /// <summary>Cria um mapa para arquivo SEM cabeçalho (colunas por posição).</summary>
    /// <returns>Novo mapa.</returns>
    public static MapaImportacao<T> SemCabecalho() => new(false);

    /// <summary>Mapeia uma propriedade a partir de uma coluna identificada por nome.</summary>
    /// <typeparam name="TProp">Tipo da propriedade de destino.</typeparam>
    /// <param name="destino">Propriedade de destino (ex.: <c>x =&gt; x.Nome</c>).</param>
    /// <param name="nome">Nome da coluna no cabeçalho.</param>
    /// <param name="obrigatoria">Se verdadeiro, valor vazio gera falha.</param>
    /// <param name="cultura">Cultura desta coluna; sobrescreve a global.</param>
    /// <param name="formato">Formato explícito (ex.: <c>"dd/MM/yyyy"</c>).</param>
    /// <param name="valorPadrao">Valor usado quando a célula vem vazia (coluna não obrigatória).</param>
    /// <param name="callback">Conversor próprio desta coluna; quando informado, substitui a conversão padrão.</param>
    /// <returns>O próprio mapa, para encadeamento.</returns>
    public MapaImportacao<T> Coluna<TProp>(
        Expression<Func<T, TProp>> destino,
        string nome,
        bool obrigatoria = false,
        CultureInfo? cultura = null,
        string? formato = null,
        object? valorPadrao = null,
        Func<string, CultureInfo, object?>? callback = null)
        => AdicionarNome(destino, [nome], obrigatoria, cultura, formato, valorPadrao, callback);

    /// <summary>Mapeia uma propriedade aceitando vários nomes possíveis de coluna (cabeçalho variável).</summary>
    /// <typeparam name="TProp">Tipo da propriedade de destino.</typeparam>
    /// <param name="destino">Propriedade de destino.</param>
    /// <param name="nomesAceitos">Nomes aceitos, em ordem de preferência.</param>
    /// <param name="obrigatoria">Se verdadeiro, valor vazio gera falha.</param>
    /// <param name="cultura">Cultura desta coluna; sobrescreve a global.</param>
    /// <param name="formato">Formato explícito.</param>
    /// <param name="valorPadrao">Valor usado quando a célula vem vazia (coluna não obrigatória).</param>
    /// <param name="callback">Conversor próprio desta coluna.</param>
    /// <returns>O próprio mapa, para encadeamento.</returns>
    public MapaImportacao<T> Coluna<TProp>(
        Expression<Func<T, TProp>> destino,
        string[] nomesAceitos,
        bool obrigatoria = false,
        CultureInfo? cultura = null,
        string? formato = null,
        object? valorPadrao = null,
        Func<string, CultureInfo, object?>? callback = null)
        => AdicionarNome(destino, nomesAceitos, obrigatoria, cultura, formato, valorPadrao, callback);

    /// <summary>Mapeia uma propriedade a partir da posição da coluna (0-based, relativa a <see cref="OpcoesImportacao.ColunaInicio"/>).</summary>
    /// <typeparam name="TProp">Tipo da propriedade de destino.</typeparam>
    /// <param name="destino">Propriedade de destino.</param>
    /// <param name="posicao">Índice da coluna (0-based).</param>
    /// <param name="obrigatoria">Se verdadeiro, valor vazio gera falha.</param>
    /// <param name="cultura">Cultura desta coluna; sobrescreve a global.</param>
    /// <param name="formato">Formato explícito.</param>
    /// <param name="valorPadrao">Valor usado quando a célula vem vazia (coluna não obrigatória).</param>
    /// <param name="callback">Conversor próprio desta coluna.</param>
    /// <returns>O próprio mapa, para encadeamento.</returns>
    public MapaImportacao<T> ColunaPosicao<TProp>(
        Expression<Func<T, TProp>> destino,
        int posicao,
        bool obrigatoria = false,
        CultureInfo? cultura = null,
        string? formato = null,
        object? valorPadrao = null,
        Func<string, CultureInfo, object?>? callback = null)
    {
        (Action<T, object?> setter, string nome, Type tipo) = Compilar(destino);
        _colunas.Add(new ColunaMapeada<T>
        {
            Setter = setter, Destino = nome, TipoDestino = tipo,
            PosicaoOrigem = posicao, Obrigatoria = obrigatoria,
            Cultura = cultura, Formato = formato,
            TemValorPadrao = valorPadrao is not null, ValorPadrao = valorPadrao,
            Callback = callback
        });
        return this;
    }

    /// <summary>Define um callback executado após todas as colunas (apenas quando a linha está Ok). Recebe a entidade e um acessor às células cruas — resolve origem composta e ajustes finais.</summary>
    /// <param name="callback">Ação a executar sobre a entidade já preenchida.</param>
    /// <returns>O próprio mapa, para encadeamento.</returns>
    public MapaImportacao<T> ComCallbackLinha(Action<T, IAcessorLinha> callback)
    {
        CallbackLinha = callback;
        return this;
    }

    /// <summary>Adiciona uma coluna mapeada por nome (implementação comum das sobrecargas de <c>Coluna</c>).</summary>
    /// <typeparam name="TProp">Tipo da propriedade de destino.</typeparam>
    /// <param name="destino">Expressão da propriedade.</param>
    /// <param name="nomes">Nomes aceitos.</param>
    /// <param name="obrigatoria">Se é obrigatória.</param>
    /// <param name="cultura">Cultura específica.</param>
    /// <param name="formato">Formato explícito.</param>
    /// <param name="valorPadrao">Valor padrão.</param>
    /// <param name="callback">Conversor próprio.</param>
    /// <returns>O próprio mapa.</returns>
    private MapaImportacao<T> AdicionarNome<TProp>(
        Expression<Func<T, TProp>> destino, string[] nomes, bool obrigatoria,
        CultureInfo? cultura, string? formato, object? valorPadrao,
        Func<string, CultureInfo, object?>? callback)
    {
        if (!TemCabecalho)
            throw new InvalidOperationException(
                "Mapeamento por nome exige um mapa com cabeçalho (use ComCabecalho()).");

        (Action<T, object?> setter, string nome, Type tipo) = Compilar(destino);
        _colunas.Add(new ColunaMapeada<T>
        {
            Setter = setter, Destino = nome, TipoDestino = tipo,
            NomesOrigem = nomes, Obrigatoria = obrigatoria,
            Cultura = cultura, Formato = formato,
            TemValorPadrao = valorPadrao is not null, ValorPadrao = valorPadrao,
            Callback = callback
        });
        return this;
    }

    /// <summary>Compila uma expressão de propriedade em um setter fortemente tipado e extrai nome e tipo.</summary>
    /// <typeparam name="TProp">Tipo da propriedade.</typeparam>
    /// <param name="expr">Expressão que aponta para a propriedade.</param>
    /// <returns>Tupla com o setter, o nome e o tipo da propriedade.</returns>
    private static (Action<T, object?>, string, Type) Compilar<TProp>(Expression<Func<T, TProp>> expr)
    {
        MemberExpression? membro = expr.Body as MemberExpression
            ?? (expr.Body as UnaryExpression)?.Operand as MemberExpression;

        if (membro is null || membro.Member is not PropertyInfo prop)
            throw new ArgumentException("O destino deve apontar para uma propriedade.", nameof(expr));

        ParameterExpression alvo = Expression.Parameter(typeof(T), "alvo");
        ParameterExpression valor = Expression.Parameter(typeof(object), "valor");
        BinaryExpression corpo = Expression.Assign(
            Expression.Property(alvo, prop),
            Expression.Convert(valor, prop.PropertyType));

        Action<T, object?> setter = Expression
            .Lambda<Action<T, object?>>(corpo, alvo, valor)
            .Compile();

        return (setter, prop.Name, prop.PropertyType);
    }
}

// #endregion

// #region Conversão  ->  destino sugerido: Conversion/ (ConversorValor é internal)

/// <summary>Converte valores brutos (texto do CSV ou nativos do Excel) para o tipo de destino. Uso interno do motor.</summary>
internal sealed class ConversorValor
{
    /// <summary>Converte um valor bruto para o tipo de destino, respeitando cultura e formato.</summary>
    /// <param name="bruto">Valor lido do arquivo (string do CSV ou nativo do Excel).</param>
    /// <param name="tipoAlvo">Tipo de destino (pode ser anulável).</param>
    /// <param name="cultura">Cultura para parse de texto.</param>
    /// <param name="formato">Formato explícito (ex.: data), quando houver.</param>
    /// <returns>Valor convertido, ou nulo quando o bruto é vazio.</returns>
    public object? Converter(object? bruto, Type tipoAlvo, CultureInfo cultura, string? formato)
    {
        if (bruto is null)
            return null;

        Type tipo = Nullable.GetUnderlyingType(tipoAlvo) ?? tipoAlvo;

        // 1) Já é do tipo destino (valor tipado do Excel: DateTime, double, bool…).
        if (tipo.IsInstanceOfType(bruto))
            return bruto;

        // 2) Nativo não-string (Excel) → converte sem passar por texto/cultura.
        if (bruto is not string)
        {
            if (tipo == typeof(string))
                return Convert.ToString(bruto, cultura);

            if (bruto is IConvertible && (EhNumerico(tipo) || tipo == typeof(DateTime) || tipo == typeof(bool)))
                return Convert.ChangeType(bruto, tipo, CultureInfo.InvariantCulture);
        }

        // 3) Texto (CSV) → parseia com cultura + formato.
        string texto = (bruto as string ?? bruto.ToString() ?? string.Empty).Trim();
        if (texto.Length == 0)
            return null;

        return ConverterTexto(texto, tipo, cultura, formato);
    }

    /// <summary>Converte um texto já validado como não-vazio para o tipo de destino.</summary>
    /// <param name="texto">Texto de origem.</param>
    /// <param name="tipo">Tipo de destino (não anulável).</param>
    /// <param name="cultura">Cultura para parse.</param>
    /// <param name="formato">Formato explícito, quando houver.</param>
    /// <returns>Valor convertido.</returns>
    private static object? ConverterTexto(string texto, Type tipo, CultureInfo cultura, string? formato)
    {
        if (tipo == typeof(string)) return texto;
        if (tipo.IsEnum) return Enum.Parse(tipo, texto, ignoreCase: true);
        if (tipo == typeof(Guid)) return Guid.Parse(texto);
        if (tipo == typeof(bool)) return ConverterBool(texto);

        if (tipo == typeof(DateTime))
        {
            if (!string.IsNullOrEmpty(formato))
                return DateTime.ParseExact(texto, formato, cultura, DateTimeStyles.None);
            return DateTime.Parse(texto, cultura, DateTimeStyles.None);
        }

        switch (Type.GetTypeCode(tipo))
        {
            case TypeCode.Int16:
                return short.Parse(texto, NumberStyles.Integer, cultura);
            case TypeCode.Int32:
                return int.Parse(texto, NumberStyles.Integer, cultura);
            case TypeCode.Int64:
                return long.Parse(texto, NumberStyles.Integer, cultura);
            case TypeCode.Decimal:
                return decimal.Parse(texto, NumberStyles.Number, cultura);
            case TypeCode.Double:
                return double.Parse(texto, NumberStyles.Number, cultura);
            case TypeCode.Single:
                return float.Parse(texto, NumberStyles.Number, cultura);
            default:
                return Convert.ChangeType(texto, tipo, cultura);
        }
    }

    /// <summary>Converte texto em booleano aceitando formas em pt-BR (sim/não, s/n, 1/0, verdadeiro/falso).</summary>
    /// <param name="bruto">Texto de origem.</param>
    /// <returns>Booleano correspondente.</returns>
    private static bool ConverterBool(string bruto)
    {
        switch (bruto.Trim().ToLowerInvariant())
        {
            case "1":
            case "s":
            case "sim":
            case "true":
            case "verdadeiro":
                return true;
            case "0":
            case "n":
            case "nao":
            case "não":
            case "false":
            case "falso":
                return false;
            default:
                return bool.Parse(bruto);
        }
    }

    /// <summary>Indica se o tipo é numérico (inteiro ou de ponto flutuante).</summary>
    /// <param name="tipo">Tipo a testar.</param>
    /// <returns>Verdadeiro para tipos numéricos.</returns>
    private static bool EhNumerico(Type tipo)
    {
        switch (Type.GetTypeCode(tipo))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
            default:
                return false;
        }
    }
}

// #endregion

// #region Motor  ->  destino sugerido: Services/ (ImportadorTabular)

/// <summary>Motor de importação: lê, mapeia e converte linhas em entidades tipadas. .NET puro, sem dependência de UI.</summary>
public static class ImportadorTabular
{
    /// <summary>Importa o conteúdo de um stream em uma lista tipada, com relatório por linha.</summary>
    /// <typeparam name="T">Tipo da entidade produzida.</typeparam>
    /// <param name="stream">Fluxo do arquivo.</param>
    /// <param name="nomeArquivo">Nome do arquivo (usado no relatório).</param>
    /// <param name="mapa">Mapa de colunas.</param>
    /// <param name="leitor">Leitor compatível com o formato.</param>
    /// <param name="opcoes">Opções da importação. Se nulo, usa os padrões.</param>
    /// <returns>Resultado consolidado com relatório e itens.</returns>
    public static ResultadoImportacao<T> Importar<T>(
        Stream stream,
        string nomeArquivo,
        MapaImportacao<T> mapa,
        ILeitorTabular leitor,
        OpcoesImportacao? opcoes = null)
        where T : new()
    {
        opcoes ??= new OpcoesImportacao();
        ConversorValor conversor = new();
        ResultadoImportacao<T> resultado = new();
        int colunaInicio = opcoes.ColunaInicio;

        using IEnumerator<object?[]> e = leitor.LerLinhas(stream).GetEnumerator();

        // Contador FÍSICO: incrementa em TODA linha crua. A "linha" do relatório é sempre a
        // posição real no arquivo — mesmo com âncoras de início ou linhas em branco no meio.
        int numeroLinha = 0;
        object?[] atual = [];

        // Avança o enumerador uma linha, atualizando o contador físico e a linha atual.
        bool Avancar()
        {
            if (e.MoveNext())
            {
                numeroLinha++;
                atual = e.Current;
                return true;
            }

            atual = [];
            return false;
        }

        Dictionary<string, int>? cabecalho = null;

        if (mapa.TemCabecalho)
        {
            object?[] linhaCabecalho = [];
            bool achou = false;

            if (opcoes.LinhaCabecalho is int lc)
            {
                // Âncora explícita: o cabeçalho está exatamente nesta linha física.
                while (numeroLinha < lc)
                    if (!Avancar())
                        return resultado;

                linhaCabecalho = Recortar(atual, colunaInicio);
                achou = true;
            }
            else
            {
                // Autodetect: primeira linha não-vazia (já recortada) é o cabeçalho.
                while (Avancar())
                {
                    object?[] rec = Recortar(atual, colunaInicio);
                    if (!LinhaVazia(rec))
                    {
                        linhaCabecalho = rec;
                        achou = true;
                        break;
                    }
                }
            }

            if (!achou)
                return resultado;

            cabecalho = MontarCabecalho(linhaCabecalho);

            List<FalhaImportacao> estruturais = mapa.Colunas
                .Where(c => c.Obrigatoria && c.PosicaoOrigem is null)
                .Where(c => !c.NomesOrigem.Any(n => cabecalho.ContainsKey(n.Trim())))
                .Select(c => new FalhaImportacao(c.Origem, c.Destino, null,
                    "Coluna obrigatória não encontrada no cabeçalho."))
                .ToList();

            if (estruturais.Count > 0)
            {
                resultado.Relatorio.Add(new LinhaResultado<T>
                {
                    Arquivo = nomeArquivo,
                    Linha = numeroLinha, // linha FÍSICA do cabeçalho
                    Status = StatusLinha.Falha,
                    Falhas = estruturais
                });
                return resultado;
            }

            // Posiciona nos dados, se houver âncora explícita.
            if (opcoes.LinhaInicioDados is int ld)
                while (numeroLinha < ld - 1)
                    if (!Avancar())
                        return resultado;
        }
        else if (opcoes.LinhaInicioDados is int ld)
        {
            // Sem cabeçalho: apenas posiciona o início dos dados.
            while (numeroLinha < ld - 1)
                if (!Avancar())
                    return resultado;
        }

        // Dados.
        bool jaViuDado = false;

        while (Avancar())
        {
            object?[] celulas = Recortar(atual, colunaInicio);

            if (LinhaVazia(celulas))
            {
                bool tratarComoDado = false;

                switch (opcoes.LinhasEmBranco)
                {
                    case TratamentoLinhasEmBranco.Todas:
                        continue;
                    case TratamentoLinhasEmBranco.SomenteIniciais:
                        if (!jaViuDado)
                            continue;
                        tratarComoDado = true;
                        break;
                    case TratamentoLinhasEmBranco.Nao:
                        tratarComoDado = true;
                        break;
                }

                if (!tratarComoDado)
                    continue;
            }
            else
            {
                jaViuDado = true;
            }

            LinhaResultado<T> linhaResultado = ProcessarLinha(
                celulas, numeroLinha, nomeArquivo, mapa, cabecalho, conversor, opcoes);

            resultado.Relatorio.Add(linhaResultado);

            if (opcoes.Modo == ModoImportacao.PararNaPrimeiraFalha && !linhaResultado.Ok)
                break;
        }

        return resultado;
    }

    /// <summary>Recorta as colunas à esquerda conforme <see cref="OpcoesImportacao.ColunaInicio"/>.</summary>
    /// <param name="celulas">Linha original.</param>
    /// <param name="colunaInicio">Coluna inicial (1-based).</param>
    /// <returns>Linha recortada (ou a original se o início é 1).</returns>
    private static object?[] Recortar(object?[] celulas, int colunaInicio)
    {
        if (colunaInicio <= 1)
            return celulas;

        int corte = colunaInicio - 1;

        if (corte >= celulas.Length)
            return [];

        return celulas[corte..];
    }

    /// <summary>Processa uma linha de dados, aplicando o mapa e produzindo a entidade ou as falhas.</summary>
    /// <typeparam name="T">Tipo da entidade.</typeparam>
    /// <param name="celulas">Células da linha (já recortadas).</param>
    /// <param name="numeroLinha">Número físico da linha.</param>
    /// <param name="nomeArquivo">Nome do arquivo.</param>
    /// <param name="mapa">Mapa de colunas.</param>
    /// <param name="cabecalho">Índice nome→posição, quando há cabeçalho.</param>
    /// <param name="conversor">Conversor de valores.</param>
    /// <param name="opcoes">Opções da importação.</param>
    /// <returns>Resultado da linha (Ok ou Falha).</returns>
    private static LinhaResultado<T> ProcessarLinha<T>(
        object?[] celulas, int numeroLinha, string nomeArquivo,
        MapaImportacao<T> mapa, Dictionary<string, int>? cabecalho,
        ConversorValor conversor, OpcoesImportacao opcoes)
        where T : new()
    {
        T entidade = new();
        List<FalhaImportacao> falhas = [];

        foreach (ColunaMapeada<T> col in mapa.Colunas)
        {
            CultureInfo cultura = col.Cultura ?? opcoes.CulturaGlobal;
            int indice = ResolverIndice(col, cabecalho);
            object? bruto = indice >= 0 && indice < celulas.Length ? celulas[indice] : null;

            bool vazio = bruto is null || (bruto is string s && string.IsNullOrWhiteSpace(s));

            if (vazio)
            {
                if (col.Obrigatoria)
                    falhas.Add(new FalhaImportacao(col.Origem, col.Destino, null, "Campo obrigatório vazio."));
                else if (col.TemValorPadrao)
                    col.Setter(entidade, col.ValorPadrao);

                continue;
            }

            try
            {
                object? valor;

                if (col.Callback is not null)
                {
                    string texto = Convert.ToString(bruto, cultura) ?? string.Empty;
                    valor = col.Callback(texto, cultura);
                }
                else
                {
                    valor = conversor.Converter(bruto, col.TipoDestino, cultura, col.Formato);
                }

                if (valor is not null)
                    col.Setter(entidade, valor);
            }
            catch (Exception ex)
            {
                string? brutoTexto = Convert.ToString(bruto, cultura);
                falhas.Add(new FalhaImportacao(col.Origem, col.Destino, brutoTexto, ex.Message));
            }
        }

        if (falhas.Count == 0 && mapa.CallbackLinha is not null)
        {
            try
            {
                IAcessorLinha acessor = new AcessorLinha(celulas, cabecalho, opcoes.CulturaGlobal);
                mapa.CallbackLinha(entidade, acessor);
            }
            catch (Exception ex)
            {
                falhas.Add(new FalhaImportacao(null, "(linha)", null, ex.Message));
            }
        }

        return falhas.Count == 0
            ? new LinhaResultado<T> { Arquivo = nomeArquivo, Linha = numeroLinha, Status = StatusLinha.Ok, Item = entidade }
            : new LinhaResultado<T> { Arquivo = nomeArquivo, Linha = numeroLinha, Status = StatusLinha.Falha, Falhas = falhas };
    }

    /// <summary>Resolve o índice físico de uma coluna, por posição fixa ou por nome no cabeçalho.</summary>
    /// <typeparam name="T">Tipo da entidade.</typeparam>
    /// <param name="col">Coluna mapeada.</param>
    /// <param name="cabecalho">Índice nome→posição, quando há cabeçalho.</param>
    /// <returns>Índice da coluna, ou -1 se não encontrada.</returns>
    private static int ResolverIndice<T>(ColunaMapeada<T> col, Dictionary<string, int>? cabecalho)
    {
        if (col.PosicaoOrigem is int pos)
            return pos;

        if (cabecalho is not null)
            foreach (string nome in col.NomesOrigem)
                if (cabecalho.TryGetValue(nome.Trim(), out int idx))
                    return idx;

        return -1;
    }

    /// <summary>Monta o índice nome→posição a partir da linha de cabeçalho.</summary>
    /// <param name="linha">Linha de cabeçalho.</param>
    /// <returns>Dicionário insensível a maiúsculas/minúsculas.</returns>
    private static Dictionary<string, int> MontarCabecalho(object?[] linha)
    {
        Dictionary<string, int> mapa = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < linha.Length; i++)
        {
            string nome = (linha[i]?.ToString() ?? string.Empty).Trim();
            if (nome.Length > 0 && !mapa.ContainsKey(nome))
                mapa[nome] = i;
        }

        return mapa;
    }

    /// <summary>Indica se todas as células da linha estão vazias.</summary>
    /// <param name="celulas">Células da linha.</param>
    /// <returns>Verdadeiro se a linha está em branco.</returns>
    private static bool LinhaVazia(object?[] celulas)
        => celulas.All(c => c is null || (c is string s && string.IsNullOrWhiteSpace(s)));

    /// <summary>Implementação de <see cref="IAcessorLinha"/> sobre as células cruas de uma linha.</summary>
    /// <param name="celulas">Células da linha.</param>
    /// <param name="cabecalho">Índice nome→posição, quando há cabeçalho.</param>
    /// <param name="cultura">Cultura usada para converter célula em texto.</param>
    private sealed class AcessorLinha(
        object?[] celulas, Dictionary<string, int>? cabecalho, CultureInfo cultura) : IAcessorLinha
    {
        /// <summary>Lê uma célula pela posição (0-based).</summary>
        /// <param name="indice">Índice da coluna.</param>
        /// <returns>Texto da célula, ou nulo se fora do intervalo.</returns>
        public string? PorPosicao(int indice)
            => indice >= 0 && indice < celulas.Length ? Convert.ToString(celulas[indice], cultura) : null;

        /// <summary>Lê uma célula pelo nome da coluna.</summary>
        /// <param name="nome">Nome da coluna.</param>
        /// <returns>Texto da célula, ou nulo se não existir.</returns>
        public string? PorNome(string nome)
            => cabecalho is not null && cabecalho.TryGetValue(nome.Trim(), out int idx) ? PorPosicao(idx) : null;
    }
}

// #endregion

// #region Leitores  ->  destino sugerido: Readers/ (um arquivo por leitor)

/// <summary>Leitor de arquivos CSV/TXT. Delimitador e codificação são opcionais; por padrão o delimitador é autodetectado.</summary>
public sealed class LeitorCsv : ILeitorTabular
{
    /// <summary>Delimitador fixo; nulo aciona autodetecção.</summary>
    private readonly char? _delimitador;

    /// <summary>Codificação usada na leitura do arquivo.</summary>
    private readonly Encoding _encoding;

    /// <summary>Cria o leitor de CSV.</summary>
    /// <param name="delimitador">Delimitador de campo. Se nulo, é autodetectado (';', ',' ou tabulação) pela primeira linha.</param>
    /// <param name="encoding">Codificação do arquivo. Se nulo, usa UTF-8 com detecção de BOM.</param>
    public LeitorCsv(char? delimitador = null, Encoding? encoding = null)
    {
        _delimitador = delimitador;
        _encoding = encoding ?? Encoding.UTF8;
    }

    /// <inheritdoc/>
    public bool Suporta(string nomeArquivo)
        => nomeArquivo.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
        || nomeArquivo.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IEnumerable<object?[]> LerLinhas(Stream stream)
    {
        string conteudo;
        using (StreamReader leitor = new(stream, _encoding, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true))
            conteudo = leitor.ReadToEnd();

        if (string.IsNullOrEmpty(conteudo))
            yield break;

        char delim = _delimitador ?? DetectarDelimitador(conteudo);

        StringBuilder campo = new();
        List<string?> linha = [];
        bool emAspas = false;

        for (int i = 0; i < conteudo.Length; i++)
        {
            char c = conteudo[i];

            if (emAspas)
            {
                if (c == '"')
                {
                    if (i + 1 < conteudo.Length && conteudo[i + 1] == '"')
                    {
                        campo.Append('"');
                        i++;
                    }
                    else
                    {
                        emAspas = false;
                    }
                }
                else
                {
                    campo.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    emAspas = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    linha.Add(campo.ToString());
                    yield return linha.ToArray();
                    linha = [];
                    campo.Clear();
                    break;
                default:
                    if (c == delim)
                    {
                        linha.Add(campo.ToString());
                        campo.Clear();
                    }
                    else
                    {
                        campo.Append(c);
                    }
                    break;
            }
        }

        if (campo.Length > 0 || linha.Count > 0)
        {
            linha.Add(campo.ToString());
            yield return linha.ToArray();
        }
    }

    /// <summary>Detecta o delimitador mais provável a partir da primeira linha do conteúdo.</summary>
    /// <param name="conteudo">Conteúdo completo do arquivo.</param>
    /// <returns>O delimitador detectado (';', ',' ou tabulação).</returns>
    private static char DetectarDelimitador(string conteudo)
    {
        int fim = conteudo.IndexOf('\n');
        string primeira = fim >= 0 ? conteudo[..fim] : conteudo;

        int pontoVirgula = primeira.Count(c => c == ';');
        int virgula = primeira.Count(c => c == ',');
        int tab = primeira.Count(c => c == '\t');

        if (tab > pontoVirgula && tab > virgula) return '\t';
        return pontoVirgula >= virgula ? ';' : ',';
    }
}

/// <summary>Leitor de arquivos Excel (.xlsx/.xls) via ExcelDataReader. Lê a primeira planilha, devolvendo valores nativos.</summary>
public sealed class LeitorExcel : ILeitorTabular
{
    /// <summary>Registra o provedor de code pages exigido pelo ExcelDataReader para arquivos .xls legados.</summary>
    static LeitorExcel()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <inheritdoc/>
    public bool Suporta(string nomeArquivo)
        => nomeArquivo.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
        || nomeArquivo.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IEnumerable<object?[]> LerLinhas(Stream stream)
    {
        using IExcelDataReader leitor = ExcelReaderFactory.CreateReader(stream);

        // Primeira planilha. Valores saem TIPADOS (double/DateTime/bool); o motor converte
        // sem stringificar — evitando o erro de separador decimal.
        while (leitor.Read())
        {
            object?[] linha = new object?[leitor.FieldCount];

            for (int i = 0; i < leitor.FieldCount; i++)
                linha[i] = leitor.GetValue(i);

            yield return linha;
        }
    }
}

/// <summary>
/// Leitor que "despivota" (unpivot) uma matriz larga em registros longos. É uma PEÇA opcional:
/// envolve outro <see cref="ILeitorTabular"/> e não altera o motor nem o componente — o importador
/// continua enxergando "1 linha lida → 1 objeto".
/// </summary>
/// <remarks>
/// Exemplo: um relatório rede × cidade (colunas: <c>rede, SP, RJ, Fortaleza…</c>) vira registros
/// <c>{ rede, cidade, quantidade }</c>. Cada linha de origem, com N colunas de valor, gera N linhas.
/// </remarks>
public sealed class LeitorDespivotado : ILeitorTabular
{
    /// <summary>Leitor interno que fornece a matriz larga.</summary>
    private readonly ILeitorTabular _interno;

    /// <summary>Nomes das colunas que se repetem em cada registro gerado.</summary>
    private readonly string[] _identificadoras;

    /// <summary>Nome da coluna sintética que recebe o nome da coluna despivotada.</summary>
    private readonly string _colunaChave;

    /// <summary>Nome da coluna sintética que recebe o valor da célula.</summary>
    private readonly string _colunaValor;

    /// <summary>Colunas a despivotar; nulo significa "todas menos as identificadoras".</summary>
    private readonly string[]? _colunasValor;

    /// <summary>Quando verdadeiro, célula vazia não gera registro.</summary>
    private readonly bool _ignorarVazias;

    /// <summary>Nome da coluna sintética de linha de origem; nulo desativa a rastreabilidade.</summary>
    private readonly string? _colunaLinhaOrigem;

    /// <summary>Linha física do cabeçalho largo; nulo autodetecta.</summary>
    private readonly int? _linhaInicio;

    /// <summary>Coluna onde a matriz começa (1-based).</summary>
    private readonly int _colunaInicio;

    /// <summary>Cria o leitor despivotado.</summary>
    /// <param name="interno">Leitor que entrega a matriz larga (ex.: <see cref="LeitorCsv"/> ou <see cref="LeitorExcel"/>).</param>
    /// <param name="colunasIdentificadoras">Colunas que se repetem em cada registro gerado (ex.: <c>["rede"]</c>).</param>
    /// <param name="nomeColunaChave">Nome da coluna sintética que recebe o nome da coluna despivotada (ex.: <c>"cidade"</c>).</param>
    /// <param name="nomeColunaValor">Nome da coluna sintética que recebe o valor da célula (ex.: <c>"quantidade"</c>).</param>
    /// <param name="colunasValor">Colunas a despivotar. Se nulo, tudo que não for identificador vira valor.</param>
    /// <param name="ignorarCelulasVazias">Quando verdadeiro, célula vazia não gera registro. Padrão: verdadeiro.</param>
    /// <param name="nomeColunaLinhaOrigem">Se informado, emite uma coluna sintética com a linha física da origem, para rastreabilidade.</param>
    /// <param name="linhaInicio">Linha física do cabeçalho largo (1 = primeira linha). Nulo autodetecta a primeira linha não-vazia.</param>
    /// <param name="colunaInicio">Coluna onde a matriz começa (1 = primeira coluna). Recorta as colunas à esquerda. Padrão: 1.</param>
    public LeitorDespivotado(
        ILeitorTabular interno,
        string[] colunasIdentificadoras,
        string nomeColunaChave,
        string nomeColunaValor,
        string[]? colunasValor = null,
        bool ignorarCelulasVazias = true,
        string? nomeColunaLinhaOrigem = null,
        int? linhaInicio = null,
        int colunaInicio = 1)
    {
        if (colunasIdentificadoras.Length == 0)
            throw new ArgumentException("Informe ao menos uma coluna identificadora.", nameof(colunasIdentificadoras));

        _interno = interno;
        _identificadoras = colunasIdentificadoras;
        _colunaChave = nomeColunaChave;
        _colunaValor = nomeColunaValor;
        _colunasValor = colunasValor;
        _ignorarVazias = ignorarCelulasVazias;
        _colunaLinhaOrigem = nomeColunaLinhaOrigem;
        _linhaInicio = linhaInicio;
        _colunaInicio = colunaInicio;
    }

    /// <inheritdoc/>
    public bool Suporta(string nomeArquivo) => _interno.Suporta(nomeArquivo);

    /// <inheritdoc/>
    public IEnumerable<object?[]> LerLinhas(Stream stream)
    {
        int linhaFisica = 0;
        bool emiteLinhaOrigem = _colunaLinhaOrigem is not null;

        List<(string Nome, int Indice)>? colunas = null;
        int[] idxIdentificadoras = [];
        List<(int Indice, string Nome)> idxValores = [];

        foreach (object?[] bruta in _interno.LerLinhas(stream))
        {
            linhaFisica++;

            // Antes da linha de início: ignora tudo (título, metadados…).
            if (_linhaInicio is int li && linhaFisica < li)
                continue;

            object?[] linha = Recortar(bruta, _colunaInicio);

            // Primeira linha útil = cabeçalho largo.
            if (colunas is null)
            {
                // No autodetect, pula brancos até achar o cabeçalho. Com âncora, confia na linha.
                if (_linhaInicio is null && LinhaVazia(linha))
                    continue;

                colunas = MontarColunas(linha);
                idxIdentificadoras = ResolverIdentificadoras(colunas);
                idxValores = ResolverValores(colunas, idxIdentificadoras);

                yield return MontarCabecalhoSintetico(emiteLinhaOrigem);
                continue;
            }

            if (LinhaVazia(linha))
                continue;

            foreach ((int indice, string nome) in idxValores)
            {
                object? valor = indice < linha.Length ? linha[indice] : null;

                if (_ignorarVazias && EhVazio(valor))
                    continue;

                yield return MontarRegistro(linha, idxIdentificadoras, nome, valor, linhaFisica, emiteLinhaOrigem);
            }
        }
    }

    /// <summary>Recorta as colunas à esquerda conforme a coluna de início.</summary>
    /// <param name="celulas">Linha original.</param>
    /// <param name="colunaInicio">Coluna inicial (1-based).</param>
    /// <returns>Linha recortada (ou a original se o início é 1).</returns>
    private static object?[] Recortar(object?[] celulas, int colunaInicio)
    {
        if (colunaInicio <= 1)
            return celulas;

        int corte = colunaInicio - 1;

        if (corte >= celulas.Length)
            return [];

        return celulas[corte..];
    }

    /// <summary>Monta a lista ordenada de (nome, índice) das colunas não-vazias do cabeçalho largo.</summary>
    /// <param name="linha">Linha de cabeçalho.</param>
    /// <returns>Colunas na ordem física.</returns>
    private static List<(string Nome, int Indice)> MontarColunas(object?[] linha)
    {
        List<(string Nome, int Indice)> colunas = [];

        for (int i = 0; i < linha.Length; i++)
        {
            string nome = (linha[i]?.ToString() ?? string.Empty).Trim();
            if (nome.Length > 0)
                colunas.Add((nome, i));
        }

        return colunas;
    }

    /// <summary>Resolve os índices das colunas identificadoras no cabeçalho.</summary>
    /// <param name="colunas">Colunas do cabeçalho.</param>
    /// <returns>Índices na mesma ordem das identificadoras configuradas.</returns>
    private int[] ResolverIdentificadoras(List<(string Nome, int Indice)> colunas)
    {
        int[] indices = new int[_identificadoras.Length];

        for (int i = 0; i < _identificadoras.Length; i++)
        {
            string alvo = _identificadoras[i].Trim();
            int achado = -1;

            foreach ((string nome, int indice) in colunas)
                if (string.Equals(nome, alvo, StringComparison.OrdinalIgnoreCase))
                {
                    achado = indice;
                    break;
                }

            if (achado < 0)
                throw new InvalidOperationException($"Coluna identificadora '{alvo}' não encontrada no cabeçalho.");

            indices[i] = achado;
        }

        return indices;
    }

    /// <summary>Resolve as colunas de valor a despivotar (lista explícita ou "todas menos identificadoras").</summary>
    /// <param name="colunas">Colunas do cabeçalho.</param>
    /// <param name="idxIdentificadoras">Índices das identificadoras.</param>
    /// <returns>Pares (índice, nome) das colunas de valor.</returns>
    private List<(int Indice, string Nome)> ResolverValores(
        List<(string Nome, int Indice)> colunas, int[] idxIdentificadoras)
    {
        List<(int Indice, string Nome)> valores = [];

        if (_colunasValor is not null)
        {
            foreach (string alvo in _colunasValor)
            {
                string nomeAlvo = alvo.Trim();

                foreach ((string nome, int indice) in colunas)
                    if (string.Equals(nome, nomeAlvo, StringComparison.OrdinalIgnoreCase))
                    {
                        valores.Add((indice, nome));
                        break;
                    }
            }

            return valores;
        }

        foreach ((string nome, int indice) in colunas)
            if (Array.IndexOf(idxIdentificadoras, indice) < 0)
                valores.Add((indice, nome));

        return valores;
    }

    /// <summary>Monta o cabeçalho sintético emitido ao motor: identificadoras + chave + valor (+ linha de origem).</summary>
    /// <param name="emiteLinhaOrigem">Se deve incluir a coluna de linha de origem.</param>
    /// <returns>Vetor de nomes de coluna.</returns>
    private object?[] MontarCabecalhoSintetico(bool emiteLinhaOrigem)
    {
        int total = _identificadoras.Length + 2 + (emiteLinhaOrigem ? 1 : 0);
        object?[] cabecalho = new object?[total];

        int pos = 0;
        foreach (string nome in _identificadoras)
            cabecalho[pos++] = nome;

        cabecalho[pos++] = _colunaChave;
        cabecalho[pos++] = _colunaValor;

        if (emiteLinhaOrigem)
            cabecalho[pos] = _colunaLinhaOrigem;

        return cabecalho;
    }

    /// <summary>Monta um registro longo para uma célula de valor específica.</summary>
    /// <param name="linha">Linha larga de origem.</param>
    /// <param name="idxIdentificadoras">Índices das colunas identificadoras.</param>
    /// <param name="chave">Nome da coluna despivotada (valor da coluna-chave).</param>
    /// <param name="valor">Valor da célula.</param>
    /// <param name="linhaFisica">Linha física de origem.</param>
    /// <param name="emiteLinhaOrigem">Se deve incluir a linha de origem.</param>
    /// <returns>Vetor de células do registro longo.</returns>
    private object?[] MontarRegistro(
        object?[] linha, int[] idxIdentificadoras, string chave, object? valor,
        int linhaFisica, bool emiteLinhaOrigem)
    {
        int total = _identificadoras.Length + 2 + (emiteLinhaOrigem ? 1 : 0);
        object?[] registro = new object?[total];

        int pos = 0;
        foreach (int idx in idxIdentificadoras)
            registro[pos++] = idx < linha.Length ? linha[idx] : null;

        registro[pos++] = chave;
        registro[pos++] = valor;

        if (emiteLinhaOrigem)
            registro[pos] = linhaFisica;

        return registro;
    }

    /// <summary>Indica se todas as células da linha estão vazias.</summary>
    /// <param name="celulas">Células da linha.</param>
    /// <returns>Verdadeiro se a linha está em branco.</returns>
    private static bool LinhaVazia(object?[] celulas)
        => celulas.All(EhVazio);

    /// <summary>Indica se uma célula está vazia (nula ou texto em branco).</summary>
    /// <param name="valor">Valor da célula.</param>
    /// <returns>Verdadeiro se vazia.</returns>
    private static bool EhVazio(object? valor)
        => valor is null || (valor is string s && string.IsNullOrWhiteSpace(s));
}

// #endregion

} // fim namespace BBiCore.Importacao

namespace BBiCore.Componentes
{

// #region Apoio do componente  ->  destino sugerido: Componentes/ (junto do .razor)

/// <summary>Define quando a importação é disparada após a seleção de arquivos.</summary>
public enum ModoDisparo
{
    /// <summary>A importação começa automaticamente ao selecionar os arquivos.</summary>
    Automatico,

    /// <summary>A seleção apenas guarda os arquivos; a importação é disparada por botão (interno ou via <c>ImportarAsync</c>).</summary>
    Manual
}

/// <summary>Descreve um arquivo grande depositado em pasta para processamento assíncrono.</summary>
/// <param name="NomeOriginal">Nome original do arquivo enviado.</param>
/// <param name="CaminhoFinal">Caminho final do arquivo já gravado na pasta de destino.</param>
/// <param name="Tamanho">Tamanho do arquivo em bytes.</param>
public sealed record ArquivoDepositado(string NomeOriginal, string CaminhoFinal, long Tamanho);

// #endregion

} // fim namespace BBiCore.Componentes
