// =============================================================================
//  MotorTemplate.cs  —  Módulo de marcadores {{campo}} (BBiCore.Templates)
// -----------------------------------------------------------------------------
//  Mecanismo REUTILIZÁVEL de marcadores: resolução multi-fonte (objeto ->
//  variáveis do sistema -> valores computados), listagem de campos e validação.
//  Não tem nada de e-mail — serve a qualquer texto template (corpo/assunto de
//  e-mail, documentos, mensagens, títulos de relatório, etc.). O componente
//  BBiCampoTemplate (mesma pasta) é a casca visual sobre este motor.
// =============================================================================

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BBiCore.Templates
{

// #region Resultado  ->  destino sugerido: Models/

/// <summary>Descreve um campo disponível para inserção como marcador (alimenta o menu "Adicionar propriedade").</summary>
/// <param name="Nome">Nome a inserir dentro de <c>{{ }}</c>.</param>
/// <param name="Rotulo">Rótulo amigável exibido no menu.</param>
/// <param name="Grupo">Grupo do campo (objeto, variável do sistema, valor automático).</param>
public sealed record CampoTemplate(string Nome, string Rotulo, GrupoCampo Grupo);

/// <summary>Origem de um campo disponível para inserção.</summary>
public enum GrupoCampo
{
    /// <summary>Propriedade do objeto de dados (ex.: PedidoCliente).</summary>
    Objeto,

    /// <summary>Variável cadastrada pelo sistema.</summary>
    VariavelSistema,

    /// <summary>Valor computado automático (prefixo "Sistema.").</summary>
    ValorAutomatico
}

/// <summary>Combinação de fontes de campo que um ponto de edição oferece no menu. Controla apenas o que é SUGERIDO, não a validação.</summary>
[Flags]
public enum FontesCampo
{
    /// <summary>Nenhuma fonte.</summary>
    Nenhuma = 0,

    /// <summary>Propriedades do objeto de dados.</summary>
    Objeto = 1,

    /// <summary>Variáveis cadastradas do sistema.</summary>
    VariavelSistema = 2,

    /// <summary>Valores computados automáticos (Sistema.*).</summary>
    ValorAutomatico = 4,

    /// <summary>Todas as fontes (padrão de assunto e corpo).</summary>
    Todas = Objeto | VariavelSistema | ValorAutomatico,

    /// <summary>Objeto + variáveis, sem automáticos (padrão dos campos de endereço).</summary>
    EnderecoEmail = Objeto | VariavelSistema
}

/// <summary>Resultado da resolução de um texto: valor final e marcadores não resolvidos.</summary>
/// <param name="Texto">Texto com os marcadores substituídos.</param>
/// <param name="NaoResolvidos">Marcadores que não existem em nenhuma fonte (substituídos por vazio).</param>
public sealed record ResultadoTemplate(string Texto, IReadOnlyList<string> NaoResolvidos)
{
    /// <summary>Verdadeiro quando todos os marcadores foram resolvidos.</summary>
    public bool Sucesso => NaoResolvidos.Count == 0;
}

// #endregion

// #region Motor multi-fonte  ->  destino sugerido: Services/

/// <summary>
/// Resolve e valida marcadores <c>{{Campo}}</c> / <c>{{Campo:formato}}</c> contra três fontes, em ordem:
/// objeto de dados → variáveis cadastradas → valores computados (prefixo "Sistema."). Configurado uma vez
/// (variáveis + computados) e reutilizado para resolver todos os campos do mesmo e-mail.
/// </summary>
public sealed partial class MotorTemplate
{
    /// <summary>Prefixo reservado dos valores computados (evita colisão com objeto/variáveis).</summary>
    internal const string PrefixoSistema = "Sistema.";

    /// <summary>Cultura padrão de formatação (pt-BR).</summary>
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Cache de propriedades legíveis por tipo (nome → PropertyInfo, sem diferenciar maiúsculas).</summary>
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> Cache = new();

    /// <summary>Variáveis cadastradas do sistema (nome → valor).</summary>
    private readonly IReadOnlyDictionary<string, string> _variaveis;

    /// <summary>Valores computados (chave sem o prefixo → função que recebe formato e cultura).</summary>
    private readonly IReadOnlyDictionary<string, Func<string?, CultureInfo, string>> _computados;

    /// <summary>Cria o motor com as variáveis cadastradas e, opcionalmente, computados extras.</summary>
    /// <param name="variaveis">Variáveis Nome/Valor do sistema. Nulo trata como vazio.</param>
    /// <param name="computadosExtras">Computados adicionais além dos padrão (sobrescrevem os padrão em caso de mesmo nome).</param>
    public MotorTemplate(
        IReadOnlyDictionary<string, string>? variaveis = null,
        IReadOnlyDictionary<string, Func<string?, CultureInfo, string>>? computadosExtras = null)
    {
        _variaveis = variaveis is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(variaveis, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, Func<string?, CultureInfo, string>> computados = ComputadosPadrao();

        if (computadosExtras is not null)
            foreach (KeyValuePair<string, Func<string?, CultureInfo, string>> par in computadosExtras)
                computados[par.Key] = par.Value;

        _computados = computados;
    }

    /// <summary>Nomes das variáveis cadastradas disponíveis.</summary>
    public IReadOnlyCollection<string> VariaveisDisponiveis => (IReadOnlyCollection<string>)_variaveis.Keys;

    /// <summary>Nomes completos (com prefixo) dos valores computados disponíveis.</summary>
    public IReadOnlyCollection<string> ComputadosDisponiveis
        => _computados.Keys.Select(k => PrefixoSistema + k).ToList();

    /// <summary>Resolve os marcadores de um texto (assunto, corpo, destinatários…) usando as fontes configuradas.</summary>
    /// <param name="texto">Texto com marcadores.</param>
    /// <param name="dados">Objeto de dados (flat) que preenche os marcadores do objeto. Pode ser nulo.</param>
    /// <returns>Resultado com o texto final e a lista de marcadores não resolvidos.</returns>
    public ResultadoTemplate Resolver(string texto, object? dados)
    {
        ArgumentNullException.ThrowIfNull(texto);

        Dictionary<string, PropertyInfo> props = dados is null
            ? []
            : ObterPropriedades(dados.GetType());

        List<string> naoResolvidos = [];

        string final = MarcadorRegex().Replace(texto, m =>
        {
            string nome = m.Groups[1].Value;
            string? formato = m.Groups[2].Success ? m.Groups[2].Value : null;

            if (TentarResolver(nome, formato, dados, props, out string valor))
                return valor;

            if (!naoResolvidos.Contains(nome))
                naoResolvidos.Add(nome);

            return string.Empty;
        });

        return new ResultadoTemplate(final, naoResolvidos);
    }

    /// <summary>
    /// Remove do texto os marcadores indicados, junto com as chaves. Usado quando a política manda
    /// enviar mesmo com marcador sem valor: o destinatário nunca deve ver um "{{campo}}" cru.
    /// </summary>
    /// <param name="texto">Texto que ainda contém marcadores.</param>
    /// <param name="marcadores">Nomes dos campos a remover (sem as chaves). Nulo remove TODOS os marcadores restantes.</param>
    /// <returns>Texto sem os marcadores indicados.</returns>
    public static string RemoverMarcadores(string texto, IEnumerable<string>? marcadores = null)
    {
        if (string.IsNullOrEmpty(texto))
            return string.Empty;

        HashSet<string>? alvos = marcadores is null
            ? null
            : new HashSet<string>(marcadores, StringComparer.OrdinalIgnoreCase);

        return MarcadorRegex().Replace(texto, correspondencia =>
        {
            string nome = correspondencia.Groups[1].Value.Trim();

            if (alvos is null || alvos.Contains(nome))
                return string.Empty;

            return correspondencia.Value;
        });
    }

    /// <summary>Valida um texto contra um tipo (sem instância), considerando as três fontes. Uso na edição.</summary>
    /// <param name="texto">Texto a validar.</param>
    /// <param name="tipoDados">Tipo do objeto que preencheria o template.</param>
    /// <returns>Marcadores que não existem em nenhuma fonte (candidatos a destaque em vermelho).</returns>
    public IReadOnlyList<string> Validar(string texto, Type tipoDados)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ArgumentNullException.ThrowIfNull(tipoDados);

        Dictionary<string, PropertyInfo> props = ObterPropriedades(tipoDados);
        List<string> faltantes = [];

        foreach (Match m in MarcadorRegex().Matches(texto))
        {
            string nome = m.Groups[1].Value;

            if (Conhece(nome, props) || faltantes.Contains(nome))
                continue;

            faltantes.Add(nome);
        }

        return faltantes;
    }

    /// <summary>Tenta resolver um marcador na ordem objeto → variáveis → computados (computado por prefixo).</summary>
    /// <param name="nome">Nome do marcador.</param>
    /// <param name="formato">Formato opcional.</param>
    /// <param name="dados">Objeto de dados.</param>
    /// <param name="props">Propriedades do objeto.</param>
    /// <param name="valor">Recebe o valor resolvido.</param>
    /// <returns>Verdadeiro se resolveu em alguma fonte.</returns>
    private bool TentarResolver(
        string nome, string? formato, object? dados,
        Dictionary<string, PropertyInfo> props, out string valor)
    {
        // Valor computado: identificado pelo prefixo reservado.
        if (nome.StartsWith(PrefixoSistema, StringComparison.OrdinalIgnoreCase))
        {
            string chave = nome[PrefixoSistema.Length..];

            if (_computados.TryGetValue(chave, out Func<string?, CultureInfo, string>? fn))
            {
                valor = fn(formato, Cultura);
                return true;
            }

            valor = string.Empty;
            return false;
        }

        // Objeto de dados.
        if (dados is not null && props.TryGetValue(nome, out PropertyInfo? prop))
        {
            valor = Formatar(prop.GetValue(dados), formato);
            return true;
        }

        // Variável cadastrada.
        if (_variaveis.TryGetValue(nome, out string? v))
        {
            valor = v ?? string.Empty;
            return true;
        }

        valor = string.Empty;
        return false;
    }

    /// <summary>Indica se um marcador é conhecido em alguma fonte (para validação em tempo de edição).</summary>
    /// <param name="nome">Nome do marcador.</param>
    /// <param name="props">Propriedades do tipo.</param>
    /// <returns>Verdadeiro se conhecido.</returns>
    private bool Conhece(string nome, Dictionary<string, PropertyInfo> props)
    {
        if (nome.StartsWith(PrefixoSistema, StringComparison.OrdinalIgnoreCase))
            return _computados.ContainsKey(nome[PrefixoSistema.Length..]);

        return props.ContainsKey(nome) || _variaveis.ContainsKey(nome);
    }

    /// <summary>Formata um valor conforme o formato informado, sempre na cultura pt-BR.</summary>
    /// <param name="valor">Valor da propriedade.</param>
    /// <param name="formato">Formato .NET ou nulo.</param>
    /// <returns>Texto formatado (vazio quando o valor é nulo).</returns>
    private static string Formatar(object? valor, string? formato)
    {
        if (valor is null)
            return string.Empty;

        if (!string.IsNullOrEmpty(formato) && valor is IFormattable formatavel)
            return formatavel.ToString(formato, Cultura);

        return Convert.ToString(valor, Cultura) ?? string.Empty;
    }

    /// <summary>Cria o conjunto padrão de valores computados (saudação, data, hora, dia da semana).</summary>
    /// <returns>Dicionário chave → função.</returns>
    private static Dictionary<string, Func<string?, CultureInfo, string>> ComputadosPadrao()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Saudacao"] = (_, _) => Saudacao(),
            ["DataHoje"] = (formato, cultura) =>
                DateTime.Now.ToString(string.IsNullOrEmpty(formato) ? "dd/MM/yyyy" : formato, cultura),
            ["Hora"] = (formato, cultura) =>
                DateTime.Now.ToString(string.IsNullOrEmpty(formato) ? "HH:mm" : formato, cultura),
            ["DataHora"] = (formato, cultura) =>
                DateTime.Now.ToString(string.IsNullOrEmpty(formato) ? "dd/MM/yyyy HH:mm" : formato, cultura),
            ["DiaSemana"] = (_, cultura) => cultura.DateTimeFormat.GetDayName(DateTime.Now.DayOfWeek)
        };

    /// <summary>Devolve a saudação conforme a hora atual (bom dia / boa tarde / boa noite).</summary>
    /// <returns>Texto da saudação.</returns>
    private static string Saudacao()
    {
        int hora = DateTime.Now.Hour;

        // Faixa horária não é um switch natural (é intervalo), então usa if encadeado.
        if (hora < 12)
            return "Bom dia";

        if (hora < 18)
            return "Boa tarde";

        return "Boa noite";
    }

    /// <summary>Obtém (com cache) as propriedades legíveis do tipo, indexadas por nome sem diferenciar maiúsculas.</summary>
    /// <param name="tipo">Tipo a inspecionar.</param>
    /// <returns>Dicionário nome → PropertyInfo.</returns>
    private static Dictionary<string, PropertyInfo> ObterPropriedades(Type tipo)
        => Cache.GetOrAdd(tipo, t =>
        {
            Dictionary<string, PropertyInfo> mapa = new(StringComparer.OrdinalIgnoreCase);

            foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (prop.CanRead && prop.GetIndexParameters().Length == 0 && !mapa.ContainsKey(prop.Name))
                    mapa[prop.Name] = prop;

            return mapa;
        });

    /// <summary>Expressão que casa apenas o marcador duplo <c>{{Nome}}</c> ou <c>{{Nome:formato}}</c>.</summary>
    /// <returns>Regex compilada.</returns>
    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_.]*)\s*(?::(.+?))?\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex MarcadorRegex();
}

// #endregion

// #region Extrator de campos  ->  destino sugerido: Services/

/// <summary>Monta a lista de campos disponíveis (objeto + variáveis + computados) para o menu "Adicionar propriedade".</summary>
public static class ExtratorCamposTemplate
{
    /// <summary>Lista os campos "simples" do tipo de dados, em ordem alfabética, marcados como <see cref="GrupoCampo.Objeto"/>.</summary>
    /// <param name="tipoDados">Tipo do objeto de dados (flat).</param>
    /// <returns>Campos do objeto.</returns>
    private static IReadOnlyList<CampoTemplate> ListarDoObjeto(Type tipoDados)
    {
        ArgumentNullException.ThrowIfNull(tipoDados);

        List<CampoTemplate> campos = [];

        foreach (PropertyInfo prop in tipoDados.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                continue;

            Type tipo = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            if (!EhTipoSimples(tipo))
                continue;

            campos.Add(new CampoTemplate(prop.Name, $"{prop.Name} ({TipoAmigavel(tipo)})", GrupoCampo.Objeto));
        }

        campos.Sort((a, b) => string.Compare(a.Nome, b.Nome, StringComparison.OrdinalIgnoreCase));
        return campos;
    }

    /// <summary>Monta a lista completa de campos (objeto + variáveis + computados) a partir do tipo e do motor configurado.</summary>
    /// <param name="tipoDados">Tipo do objeto de dados.</param>
    /// <param name="motor">Motor já configurado com variáveis e computados.</param>
    /// <returns>Todos os campos disponíveis para inserção.</returns>
    public static IReadOnlyList<CampoTemplate> ListarTodos(Type tipoDados, MotorTemplate motor)
        => ListarTodos(tipoDados, motor, FontesCampo.Todas);

    /// <summary>Monta a lista de campos filtrada pelas fontes permitidas naquele ponto de edição.</summary>
    /// <param name="tipoDados">Tipo do objeto de dados.</param>
    /// <param name="motor">Motor já configurado com variáveis e computados.</param>
    /// <param name="fontes">Fontes que o campo oferece (ex.: endereços não oferecem automáticos).</param>
    /// <returns>Campos disponíveis para inserção, conforme as fontes.</returns>
    public static IReadOnlyList<CampoTemplate> ListarTodos(Type tipoDados, MotorTemplate motor, FontesCampo fontes)
    {
        ArgumentNullException.ThrowIfNull(motor);

        List<CampoTemplate> campos = [];

        if (fontes.HasFlag(FontesCampo.Objeto))
            campos.AddRange(ListarDoObjeto(tipoDados));

        if (fontes.HasFlag(FontesCampo.VariavelSistema))
            foreach (string nome in motor.VariaveisDisponiveis.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                campos.Add(new CampoTemplate(nome, nome, GrupoCampo.VariavelSistema));

        if (fontes.HasFlag(FontesCampo.ValorAutomatico))
            foreach (string nome in motor.ComputadosDisponiveis.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                campos.Add(new CampoTemplate(nome, nome, GrupoCampo.ValorAutomatico));

        return campos;
    }

    /// <summary>Indica se o tipo é "simples" o bastante para virar um marcador de texto.</summary>
    /// <param name="tipo">Tipo (já desembrulhado de Nullable).</param>
    /// <returns>Verdadeiro para texto, números, data/hora, booleano, enum e Guid.</returns>
    private static bool EhTipoSimples(Type tipo)
    {
        if (tipo.IsEnum) return true;
        if (tipo == typeof(string)) return true;
        if (tipo == typeof(Guid)) return true;
        if (tipo == typeof(DateTime) || tipo == typeof(DateTimeOffset)) return true;
        if (tipo == typeof(DateOnly) || tipo == typeof(TimeOnly) || tipo == typeof(TimeSpan)) return true;

        switch (Type.GetTypeCode(tipo))
        {
            case TypeCode.Boolean:
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

    /// <summary>Devolve um rótulo amigável em pt-BR para o tipo.</summary>
    /// <param name="tipo">Tipo (já desembrulhado de Nullable).</param>
    /// <returns>Rótulo como "Texto", "Data", "Número"…</returns>
    private static string TipoAmigavel(Type tipo)
    {
        if (tipo.IsEnum) return "Opção";
        if (tipo == typeof(string) || tipo == typeof(Guid)) return "Texto";
        if (tipo == typeof(bool)) return "Sim/Não";

        if (tipo == typeof(DateTime) || tipo == typeof(DateTimeOffset) || tipo == typeof(DateOnly))
            return "Data";

        if (tipo == typeof(TimeOnly) || tipo == typeof(TimeSpan))
            return "Hora";

        switch (Type.GetTypeCode(tipo))
        {
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return "Número (decimal)";
            default:
                return "Número";
        }
    }
}

// #endregion

} // fim namespace BBiCore.Templates
