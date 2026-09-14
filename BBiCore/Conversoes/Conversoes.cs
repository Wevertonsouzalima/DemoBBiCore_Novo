// =============================================================================
//  Conversoes.cs  —  Conversões genéricas (BBiCore.Conversoes)
// -----------------------------------------------------------------------------
//  Ponto único de conversão da DLL: bool, numérico (int/long/decimal/double/
//  float), data/hora, char e enum — sempre nos dois sentidos, sempre com
//  proteção de nulo/erro, sempre em extension method (ex.: texto.ParaInt()).
//
//  NÃO é o lugar de CPF/CNPJ/telefone/CEP — isso já existe em Mascaras.cs
//  (BBiCore.Mascaras). Aqui é conversão de tipo genérica, não documento.
//
//  REGRA DE ERRO — três variantes por conversão de texto, sempre disponíveis:
//    texto.ParaInt()                 -> lança ConversaoException se falhar
//    texto.ParaInt(emCasoErro: 0)     -> devolve o fallback se falhar
//    texto.ParaIntOuNulo()            -> devolve null se falhar (nunca lança)
//
//  CULTURA E CASAS DECIMAIS — prioridade em toda conversão numérica/data:
//    parâmetro explícito na chamada  >  appsettings do site (ConversaoConfiguracao)  >  pt-BR / 2 casas (padrão hardcoded)
//
//  Para o site consumidor usar sua própria cultura/casas decimais, no Program.cs:
//    ConversaoConfiguracao.Inicializar(builder.Configuration);
//  lendo a seção (todas as chaves são opcionais — ausência cai no padrão pt-BR):
//    "BBiCore": { "Conversao": { "Cultura": "pt-BR", "CasasDecimaisPadrao": 2 } }
//
//  >>> NOTA PARA REORGANIZAÇÃO: cada região em seu #region, nomeada pelo destino.
// =============================================================================

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace BBiCore.Conversoes
{
    // #region Configuração  ->  destino sugerido: Configuracao/

    /// <summary>
    /// Cultura e casas decimais padrão usadas por toda conversão desta DLL quando a chamada
    /// não informa nada explicitamente. Lida (opcionalmente) do appsettings do site consumidor.
    /// Sem chamar <see cref="Inicializar"/>, o padrão é sempre pt-BR / 2 casas.
    /// </summary>
    public static class ConversaoConfiguracao
    {
        private static CultureInfo _culturaPadrao = CultureInfo.GetCultureInfo("pt-BR");
        private static int _casasDecimaisPadrao = 2;

        /// <summary>Cultura padrão atual (pt-BR se ninguém configurou nada).</summary>
        public static CultureInfo CulturaPadrao => _culturaPadrao;

        /// <summary>Casas decimais padrão atuais (2 se ninguém configurou nada).</summary>
        public static int CasasDecimaisPadrao => _casasDecimaisPadrao;

        /// <summary>
        /// Lê a seção <c>BBiCore:Conversao</c> do appsettings do site consumidor. Chave ausente
        /// ou inválida mantém o padrão anterior (pt-BR / 2 casas) — nunca lança exceção.
        /// Chame uma vez no Program.cs: <c>ConversaoConfiguracao.Inicializar(builder.Configuration);</c>
        /// </summary>
        /// <param name="configuracao">Configuração do site (builder.Configuration).</param>
        public static void Inicializar(IConfiguration configuracao)
        {
            string? cultura = configuracao["BBiCore:Conversao:Cultura"];

            if (!string.IsNullOrWhiteSpace(cultura))
            {
                try
                {
                    _culturaPadrao = CultureInfo.GetCultureInfo(cultura);
                }
                catch (CultureNotFoundException)
                {
                    // Nome de cultura inválido no appsettings: mantém o padrão anterior.
                }
            }

            string? casas = configuracao["BBiCore:Conversao:CasasDecimaisPadrao"];

            if (!string.IsNullOrWhiteSpace(casas)
                && int.TryParse(casas, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c)
                && c >= 0)
            {
                _casasDecimaisPadrao = c;
            }
        }

        /// <summary>Define a cultura padrão diretamente em código (alternativa ao appsettings).</summary>
        /// <param name="cultura">Cultura a usar como padrão.</param>
        public static void DefinirCulturaPadrao(CultureInfo cultura) => _culturaPadrao = cultura;

        /// <summary>Define as casas decimais padrão diretamente em código (alternativa ao appsettings).</summary>
        /// <param name="casas">Quantidade de casas decimais (não pode ser negativa).</param>
        public static void DefinirCasasDecimaisPadrao(int casas)
        {
            if (casas >= 0)
                _casasDecimaisPadrao = casas;
        }
    }

    // #endregion

    // #region Exceção  ->  destino sugerido: Excecoes/

    /// <summary>
    /// Erro de conversão desta DLL. Guarda o texto original e o tipo alvo para facilitar log —
    /// é a única exceção que os métodos <c>ParaXxx()</c> (sem fallback) podem lançar.
    /// </summary>
    public sealed class ConversaoException : Exception
    {
        /// <summary>Texto (ou valor, convertido para texto) que não pôde ser convertido.</summary>
        public string? TextoOriginal { get; }

        /// <summary>Tipo de destino que a conversão tentou alcançar.</summary>
        public Type TipoAlvo { get; }

        /// <summary>Cria a exceção com o texto original e o tipo alvo.</summary>
        /// <param name="textoOriginal">Texto (ou valor) que falhou ao converter.</param>
        /// <param name="tipoAlvo">Tipo de destino da conversão.</param>
        /// <param name="interna">Exceção original do .NET, se houver.</param>
        public ConversaoException(string? textoOriginal, Type tipoAlvo, Exception? interna = null)
            : base($"Não foi possível converter \"{textoOriginal}\" para {tipoAlvo.Name}.", interna)
        {
            TextoOriginal = textoOriginal;
            TipoAlvo = tipoAlvo;
        }
    }

    // #endregion

    /// <summary>
    /// Extension methods de conversão da DLL. Sintaxe direta (<c>texto.ParaInt()</c>,
    /// <c>valor.ParaTexto()</c>), sempre com proteção de nulo e escolha entre lançar ou
    /// receber um valor padrão em caso de erro.
    /// </summary>
    public static class ConversaoExtensions
    {
        // #region Núcleo interno  ->  destino sugerido: Helpers/

        /// <summary>Resolve a cultura efetiva: parâmetro explícito &gt; configuração &gt; pt-BR.</summary>
        /// <param name="explicita">Cultura informada na chamada, se houver.</param>
        /// <returns>Cultura a usar.</returns>
        private static CultureInfo Cultura(CultureInfo? explicita) => explicita ?? ConversaoConfiguracao.CulturaPadrao;

        /// <summary>Resolve as casas decimais efetivas: parâmetro explícito &gt; configuração &gt; 2.</summary>
        /// <param name="explicita">Casas decimais informadas na chamada, se houver.</param>
        /// <returns>Casas decimais a usar.</returns>
        private static int Casas(int? explicita) => explicita ?? ConversaoConfiguracao.CasasDecimaisPadrao;

        /// <summary>Indica se o texto é nulo, vazio ou só espaços.</summary>
        /// <param name="texto">Texto a checar.</param>
        /// <returns>Verdadeiro quando não há nada útil para converter.</returns>
        private static bool EhNuloOuVazio(string? texto) => string.IsNullOrWhiteSpace(texto);

        // #endregion

        // #region Booleano  ->  destino sugerido: Conversao/Booleano/

        /// <summary>Palavras reconhecidas como VERDADEIRO na conversão de texto para bool.</summary>
        private static readonly HashSet<string> ValoresVerdadeiros = new(StringComparer.OrdinalIgnoreCase)
        {
            "sim", "s", "ativo", "verdadeiro", "habilitado", "true", "1"
        };

        /// <summary>Palavras reconhecidas como FALSO na conversão de texto para bool.</summary>
        private static readonly HashSet<string> ValoresFalsos = new(StringComparer.OrdinalIgnoreCase)
        {
            "não", "nao", "n", "inativo", "falso", "desabilitado", "false", "0"
        };

        /// <summary>Formato de exibição de um valor booleano.</summary>
        public enum FormatoBooleano
        {
            /// <summary>"Sim" / "Não".</summary>
            SimNao,

            /// <summary>"Ativo" / "Inativo".</summary>
            AtivoInativo,

            /// <summary>"Verdadeiro" / "Falso".</summary>
            VerdadeiroFalso,

            /// <summary>"S" / "N".</summary>
            SN,

            /// <summary>"Habilitado" / "Desabilitado".</summary>
            HabilitadoDesabilitado
        }

        /// <summary>Tenta reconhecer o texto como um dos pares Sim/Não, Ativo/Inativo etc.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="resultado">Valor reconhecido, quando a conversão dá certo.</param>
        /// <returns>Verdadeiro se o texto bateu com algum par conhecido.</returns>
        private static bool TentarBool(string? texto, out bool resultado)
        {
            resultado = false;

            if (EhNuloOuVazio(texto))
                return false;

            string t = texto!.Trim();

            if (ValoresVerdadeiros.Contains(t))
            {
                resultado = true;
                return true;
            }

            if (ValoresFalsos.Contains(t))
            {
                resultado = false;
                return true;
            }

            return false;
        }

        /// <summary>Converte texto (Sim/Não, Ativo/Inativo, S/N, 1/0 etc.) para bool. Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <returns>Valor booleano reconhecido.</returns>
        /// <exception cref="ConversaoException">Quando o texto não bate com nenhum par conhecido.</exception>
        public static bool ParaBool(this string? texto)
            => TentarBool(texto, out bool resultado) ? resultado : throw new ConversaoException(texto, typeof(bool));

        /// <summary>Converte texto para bool, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é reconhecido.</param>
        /// <returns>Valor booleano reconhecido, ou <paramref name="emCasoErro"/>.</returns>
        public static bool ParaBool(this string? texto, bool emCasoErro)
            => TentarBool(texto, out bool resultado) ? resultado : emCasoErro;

        /// <summary>Converte texto para bool nulável — nulo quando não reconhecido (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <returns>Valor booleano reconhecido, ou nulo.</returns>
        public static bool? ParaBoolOuNulo(this string? texto)
            => TentarBool(texto, out bool resultado) ? resultado : null;

        /// <summary>Formata um bool no par de palavras escolhido (padrão: Sim/Não).</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="formato">Par de palavras a usar.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTexto(this bool valor, FormatoBooleano formato = FormatoBooleano.SimNao)
        {
            switch (formato)
            {
                case FormatoBooleano.SimNao:
                    return valor ? "Sim" : "Não";
                case FormatoBooleano.AtivoInativo:
                    return valor ? "Ativo" : "Inativo";
                case FormatoBooleano.VerdadeiroFalso:
                    return valor ? "Verdadeiro" : "Falso";
                case FormatoBooleano.SN:
                    return valor ? "S" : "N";
                case FormatoBooleano.HabilitadoDesabilitado:
                    return valor ? "Habilitado" : "Desabilitado";
                default:
                    return valor ? "Sim" : "Não";
            }
        }

        /// <summary>Formata um bool nulável no par de palavras escolhido.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="formato">Par de palavras a usar.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this bool? valor, FormatoBooleano formato = FormatoBooleano.SimNao, string emCasoNulo = "")
            => valor.HasValue ? valor.Value.ParaTexto(formato) : emCasoNulo;

        // #endregion

        // #region Numérico  ->  destino sugerido: Conversao/Numerico/

        /// <summary>
        /// Tenta interpretar o texto como número na cultura efetiva; se falhar, tenta de novo com o
        /// separador decimal "trocado" (ex.: texto colado de planilha americana num site pt-BR).
        /// </summary>
        /// <typeparam name="T">Tipo numérico de destino.</typeparam>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <param name="resultado">Valor convertido, quando bem-sucedido.</param>
        /// <returns>Verdadeiro se a conversão deu certo.</returns>
        private static bool TentarNumero<T>(string? texto, CultureInfo? cultura, out T resultado) where T : struct, INumberBase<T>
        {
            resultado = default;

            if (EhNuloOuVazio(texto))
                return false;

            string t = texto!.Trim();
            CultureInfo principal = Cultura(cultura);

            if (T.TryParse(t, NumberStyles.Number, principal, out resultado))
                return true;

            CultureInfo alternativa = principal.Name == "pt-BR"
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo("pt-BR");

            return T.TryParse(t, NumberStyles.Number, alternativa, out resultado);
        }

        /// <summary>Converte texto para int. Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido.</returns>
        /// <exception cref="ConversaoException">Quando o texto não é um número válido.</exception>
        public static int ParaInt(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out int resultado) ? resultado : throw new ConversaoException(texto, typeof(int));

        /// <summary>Converte texto para int, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é um número válido.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou <paramref name="emCasoErro"/>.</returns>
        public static int ParaInt(this string? texto, int emCasoErro, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out int resultado) ? resultado : emCasoErro;

        /// <summary>Converte texto para int nulável — nulo quando inválido (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou nulo.</returns>
        public static int? ParaIntOuNulo(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out int resultado) ? resultado : null;

        /// <summary>Converte texto para long. Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido.</returns>
        /// <exception cref="ConversaoException">Quando o texto não é um número válido.</exception>
        public static long ParaLong(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out long resultado) ? resultado : throw new ConversaoException(texto, typeof(long));

        /// <summary>Converte texto para long, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é um número válido.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou <paramref name="emCasoErro"/>.</returns>
        public static long ParaLong(this string? texto, long emCasoErro, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out long resultado) ? resultado : emCasoErro;

        /// <summary>Converte texto para long nulável — nulo quando inválido (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou nulo.</returns>
        public static long? ParaLongOuNulo(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out long resultado) ? resultado : null;

        /// <summary>Converte texto (vírgula ou ponto) para decimal. Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido.</returns>
        /// <exception cref="ConversaoException">Quando o texto não é um número válido.</exception>
        public static decimal ParaDecimal(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out decimal resultado) ? resultado : throw new ConversaoException(texto, typeof(decimal));

        /// <summary>Converte texto para decimal, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é um número válido.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou <paramref name="emCasoErro"/>.</returns>
        public static decimal ParaDecimal(this string? texto, decimal emCasoErro, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out decimal resultado) ? resultado : emCasoErro;

        /// <summary>Converte texto para decimal nulável — nulo quando inválido (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou nulo.</returns>
        public static decimal? ParaDecimalOuNulo(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out decimal resultado) ? resultado : null;

        /// <summary>Converte texto (vírgula ou ponto) para double. Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido.</returns>
        /// <exception cref="ConversaoException">Quando o texto não é um número válido.</exception>
        public static double ParaDouble(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out double resultado) ? resultado : throw new ConversaoException(texto, typeof(double));

        /// <summary>Converte texto para double, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é um número válido.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou <paramref name="emCasoErro"/>.</returns>
        public static double ParaDouble(this string? texto, double emCasoErro, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out double resultado) ? resultado : emCasoErro;

        /// <summary>Converte texto para double nulável — nulo quando inválido (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou nulo.</returns>
        public static double? ParaDoubleOuNulo(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out double resultado) ? resultado : null;

        /// <summary>Converte texto (vírgula ou ponto) para float. Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido.</returns>
        /// <exception cref="ConversaoException">Quando o texto não é um número válido.</exception>
        public static float ParaFloat(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out float resultado) ? resultado : throw new ConversaoException(texto, typeof(float));

        /// <summary>Converte texto para float, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é um número válido.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou <paramref name="emCasoErro"/>.</returns>
        public static float ParaFloat(this string? texto, float emCasoErro, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out float resultado) ? resultado : emCasoErro;

        /// <summary>Converte texto para float nulável — nulo quando inválido (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Valor convertido, ou nulo.</returns>
        public static float? ParaFloatOuNulo(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(texto, cultura, out float resultado) ? resultado : null;

        /// <summary>Formata um int como texto na cultura efetiva (sem separador de milhar).</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTexto(this int valor, CultureInfo? cultura = null) => valor.ToString(Cultura(cultura));

        /// <summary>Formata um int nulável como texto.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this int? valor, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTexto(cultura) : emCasoNulo;

        /// <summary>Formata um int com separador de milhar (ex.: "1.234").</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTextoComMilhar(this int valor, CultureInfo? cultura = null) => valor.ToString("N0", Cultura(cultura));

        /// <summary>Formata um int nulável com separador de milhar.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTextoComMilhar(this int? valor, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTextoComMilhar(cultura) : emCasoNulo;

        /// <summary>Formata um long como texto na cultura efetiva (sem separador de milhar).</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTexto(this long valor, CultureInfo? cultura = null) => valor.ToString(Cultura(cultura));

        /// <summary>Formata um long nulável como texto.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this long? valor, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTexto(cultura) : emCasoNulo;

        /// <summary>Formata um long com separador de milhar.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTextoComMilhar(this long valor, CultureInfo? cultura = null) => valor.ToString("N0", Cultura(cultura));

        /// <summary>Formata um long nulável com separador de milhar.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTextoComMilhar(this long? valor, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTextoComMilhar(cultura) : emCasoNulo;

        /// <summary>Formata um decimal como texto (ex.: "10,25"), sem separador de milhar.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casasDecimais">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTexto(this decimal valor, int? casasDecimais = null, CultureInfo? cultura = null)
            => valor.ToString("F" + Casas(casasDecimais), Cultura(cultura));

        /// <summary>Formata um decimal nulável como texto.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casasDecimais">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this decimal? valor, int? casasDecimais = null, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTexto(casasDecimais, cultura) : emCasoNulo;

        /// <summary>Formata um decimal com separador de milhar (ex.: "1.234,56").</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casasDecimais">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTextoComMilhar(this decimal valor, int? casasDecimais = null, CultureInfo? cultura = null)
            => valor.ToString("N" + Casas(casasDecimais), Cultura(cultura));

        /// <summary>Formata um decimal nulável com separador de milhar.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casasDecimais">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTextoComMilhar(this decimal? valor, int? casasDecimais = null, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTextoComMilhar(casasDecimais, cultura) : emCasoNulo;

        /// <summary>Formata um double como texto, sem separador de milhar.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casasDecimais">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTexto(this double valor, int? casasDecimais = null, CultureInfo? cultura = null)
            => valor.ToString("F" + Casas(casasDecimais), Cultura(cultura));

        /// <summary>Formata um double nulável como texto.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casasDecimais">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this double? valor, int? casasDecimais = null, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTexto(casasDecimais, cultura) : emCasoNulo;

        /// <summary>Formata um double com separador de milhar.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casasDecimais">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTextoComMilhar(this double valor, int? casasDecimais = null, CultureInfo? cultura = null)
            => valor.ToString("N" + Casas(casasDecimais), Cultura(cultura));

        /// <summary>Formata um double nulável com separador de milhar.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casasDecimais">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTextoComMilhar(this double? valor, int? casasDecimais = null, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTextoComMilhar(casasDecimais, cultura) : emCasoNulo;

        /// <summary>Remove o "%" e espaços nas pontas, deixando só o número, para leitura de percentual.</summary>
        /// <param name="texto">Texto digitado (ex.: "12,5%").</param>
        /// <returns>Texto sem o símbolo de percentual.</returns>
        private static string LimparPorcentagem(string? texto) => (texto ?? string.Empty).Trim().TrimEnd('%').Trim();

        /// <summary>
        /// Formata um decimal como percentual (ex.: "12,5%"). Por padrão multiplica por 100
        /// (assume que 0,125 representa 12,5%); use <paramref name="valorJaEstaEmPercentual"/>
        /// quando o valor já vier como 12.5.
        /// </summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casas">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="valorJaEstaEmPercentual">Verdadeiro quando o valor já está multiplicado por 100.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado com "%".</returns>
        public static string ParaPorcentagem(this decimal valor, int? casas = null, bool valorJaEstaEmPercentual = false, CultureInfo? cultura = null)
        {
            decimal v = valorJaEstaEmPercentual ? valor : valor * 100m;
            return v.ToString("F" + Casas(casas), Cultura(cultura)) + "%";
        }

        /// <summary>Formata um decimal nulável como percentual.</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casas">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="valorJaEstaEmPercentual">Verdadeiro quando o valor já está multiplicado por 100.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaPorcentagem(this decimal? valor, int? casas = null, bool valorJaEstaEmPercentual = false, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaPorcentagem(casas, valorJaEstaEmPercentual, cultura) : emCasoNulo;

        /// <summary>Formata um double como percentual (ex.: "12,5%").</summary>
        /// <param name="valor">Valor a formatar.</param>
        /// <param name="casas">Casas decimais; se omitido, usa a configuração (padrão 2).</param>
        /// <param name="valorJaEstaEmPercentual">Verdadeiro quando o valor já está multiplicado por 100.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado com "%".</returns>
        public static string ParaPorcentagem(this double valor, int? casas = null, bool valorJaEstaEmPercentual = false, CultureInfo? cultura = null)
            => ((decimal)valor).ParaPorcentagem(casas, valorJaEstaEmPercentual, cultura);

        /// <summary>Lê um texto de percentual (ex.: "12,5%" ou "12,5") e devolve a fração decimal (0,125). Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Fração decimal (valor / 100).</returns>
        /// <exception cref="ConversaoException">Quando o texto não é um número válido.</exception>
        public static decimal ParaDecimalDePorcentagem(this string? texto, CultureInfo? cultura = null)
            => LimparPorcentagem(texto).ParaDecimal(cultura) / 100m;

        /// <summary>Lê um texto de percentual, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Fração devolvida quando o texto não é um número válido.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Fração decimal (valor / 100), ou <paramref name="emCasoErro"/>.</returns>
        public static decimal ParaDecimalDePorcentagem(this string? texto, decimal emCasoErro, CultureInfo? cultura = null)
            => TentarNumero(LimparPorcentagem(texto), cultura, out decimal resultado) ? resultado / 100m : emCasoErro;

        /// <summary>Lê um texto de percentual, devolvendo nulo quando inválido (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Fração decimal (valor / 100), ou nulo.</returns>
        public static decimal? ParaDecimalDePorcentagemOuNulo(this string? texto, CultureInfo? cultura = null)
            => TentarNumero(LimparPorcentagem(texto), cultura, out decimal resultado) ? resultado / 100m : null;

        // #endregion

        // #region Data e hora  ->  destino sugerido: Conversao/DataHora/

        /// <summary>Estilo de exibição da parte de DATA. Combine livremente com <see cref="EstiloHora"/>.</summary>
        public enum EstiloData
        {
            /// <summary>Não exibe a data (usado para mostrar só a hora).</summary>
            Nenhuma,

            /// <summary>"15/07/2026".</summary>
            Curta,

            /// <summary>"15 de julho de 2026".</summary>
            Completa,

            /// <summary>"qua, 15/07/2026".</summary>
            ComDiaSemanaAbreviado,

            /// <summary>"quarta-feira, 15 de julho de 2026".</summary>
            ComDiaSemanaCompleto
        }

        /// <summary>Estilo de exibição da parte de HORA. Combine livremente com <see cref="EstiloData"/>.</summary>
        public enum EstiloHora
        {
            /// <summary>Não exibe a hora (usado para mostrar só a data).</summary>
            Nenhuma,

            /// <summary>"14:30".</summary>
            Curta,

            /// <summary>"14:30:45".</summary>
            ComSegundos
        }

        /// <summary>Formatos aceitos ao ler data/hora em pt-BR, na ordem em que são tentados.</summary>
        private static readonly string[] FormatosDataHoraPtBr =
        {
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy",
            "d/M/yyyy HH:mm:ss",
            "d/M/yyyy HH:mm",
            "d/M/yyyy",
            "HH:mm:ss",
            "HH:mm"
        };

        /// <summary>Formata a parte de data conforme o estilo pedido.</summary>
        /// <param name="valor">Data/hora de origem.</param>
        /// <param name="estilo">Estilo escolhido.</param>
        /// <param name="cultura">Cultura efetiva já resolvida.</param>
        /// <returns>Texto da parte de data, ou vazio quando o estilo é <see cref="EstiloData.Nenhuma"/>.</returns>
        private static string FormatarParteData(DateTime valor, EstiloData estilo, CultureInfo cultura)
        {
            switch (estilo)
            {
                case EstiloData.Nenhuma:
                    return string.Empty;
                case EstiloData.Curta:
                    return valor.ToString("dd/MM/yyyy", cultura);
                case EstiloData.Completa:
                    return valor.ToString("d 'de' MMMM 'de' yyyy", cultura);
                case EstiloData.ComDiaSemanaAbreviado:
                    return valor.ToString("ddd, dd/MM/yyyy", cultura);
                case EstiloData.ComDiaSemanaCompleto:
                    return valor.ToString("dddd, d 'de' MMMM 'de' yyyy", cultura);
                default:
                    return valor.ToString("dd/MM/yyyy", cultura);
            }
        }

        /// <summary>Formata a parte de hora conforme o estilo pedido.</summary>
        /// <param name="valor">Data/hora de origem.</param>
        /// <param name="estilo">Estilo escolhido.</param>
        /// <param name="cultura">Cultura efetiva já resolvida.</param>
        /// <returns>Texto da parte de hora, ou vazio quando o estilo é <see cref="EstiloHora.Nenhuma"/>.</returns>
        private static string FormatarParteHora(DateTime valor, EstiloHora estilo, CultureInfo cultura)
        {
            switch (estilo)
            {
                case EstiloHora.Nenhuma:
                    return string.Empty;
                case EstiloHora.Curta:
                    return valor.ToString("HH:mm", cultura);
                case EstiloHora.ComSegundos:
                    return valor.ToString("HH:mm:ss", cultura);
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Formata data e/ou hora combinando livremente <see cref="EstiloData"/> e
        /// <see cref="EstiloHora"/> (ex.: <c>data.ParaTexto(EstiloData.Completa, EstiloHora.Curta)</c>).
        /// </summary>
        /// <param name="valor">Data/hora a formatar.</param>
        /// <param name="estiloData">Estilo da parte de data. Padrão: <see cref="EstiloData.Curta"/>.</param>
        /// <param name="estiloHora">Estilo da parte de hora. Padrão: <see cref="EstiloHora.Nenhuma"/>.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTexto(this DateTime valor, EstiloData estiloData = EstiloData.Curta, EstiloHora estiloHora = EstiloHora.Nenhuma, CultureInfo? cultura = null)
        {
            CultureInfo c = Cultura(cultura);
            string parteData = FormatarParteData(valor, estiloData, c);
            string parteHora = FormatarParteHora(valor, estiloHora, c);

            if (parteData.Length == 0)
                return parteHora;

            if (parteHora.Length == 0)
                return parteData;

            bool temDiaDaSemana = estiloData == EstiloData.ComDiaSemanaAbreviado || estiloData == EstiloData.ComDiaSemanaCompleto;
            return temDiaDaSemana ? $"{parteData} às {parteHora}" : $"{parteData} {parteHora}";
        }

        /// <summary>Atalho para formatar só a hora, com a data no estilo curto padrão junto (ex.: <c>data.ParaTexto(EstiloHora.Curta)</c>).</summary>
        /// <param name="valor">Data/hora a formatar.</param>
        /// <param name="estiloHora">Estilo da parte de hora.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado (data curta + hora).</returns>
        public static string ParaTexto(this DateTime valor, EstiloHora estiloHora, CultureInfo? cultura = null)
            => valor.ParaTexto(EstiloData.Curta, estiloHora, cultura);

        /// <summary>Formata com um formato .NET cru, para os casos fora dos estilos padrão (ex.: ISO para API externa).</summary>
        /// <param name="valor">Data/hora a formatar.</param>
        /// <param name="formato">Formato .NET (ex.: "yyyy-MM-dd").</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado.</returns>
        public static string ParaTexto(this DateTime valor, string formato, CultureInfo? cultura = null) => valor.ToString(formato, Cultura(cultura));

        /// <summary>Formata uma data/hora nulável combinando estilos.</summary>
        /// <param name="valor">Data/hora a formatar.</param>
        /// <param name="estiloData">Estilo da parte de data.</param>
        /// <param name="estiloHora">Estilo da parte de hora.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this DateTime? valor, EstiloData estiloData = EstiloData.Curta, EstiloHora estiloHora = EstiloHora.Nenhuma, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTexto(estiloData, estiloHora, cultura) : emCasoNulo;

        /// <summary>Formata uma data/hora nulável mostrando só a hora (com data curta implícita).</summary>
        /// <param name="valor">Data/hora a formatar.</param>
        /// <param name="estiloHora">Estilo da parte de hora.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this DateTime? valor, EstiloHora estiloHora, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTexto(estiloHora, cultura) : emCasoNulo;

        /// <summary>Formata uma data/hora nulável com um formato .NET cru.</summary>
        /// <param name="valor">Data/hora a formatar.</param>
        /// <param name="formato">Formato .NET (ex.: "yyyy-MM-dd").</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Texto formatado, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this DateTime? valor, string formato, string emCasoNulo = "", CultureInfo? cultura = null)
            => valor.HasValue ? valor.Value.ParaTexto(formato, cultura) : emCasoNulo;

        /// <summary>Tenta ler o texto como data/hora, em pt-BR (cascata dd/MM/yyyy) ou no formato informado.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <param name="resultado">Data convertida, quando bem-sucedida.</param>
        /// <returns>Verdadeiro se a conversão deu certo.</returns>
        private static bool TentarData(string? texto, string? formato, CultureInfo? cultura, out DateTime resultado)
        {
            resultado = default;

            if (EhNuloOuVazio(texto))
                return false;

            CultureInfo c = Cultura(cultura);
            string t = texto!.Trim();

            if (!string.IsNullOrEmpty(formato))
                return DateTime.TryParseExact(t, formato, c, DateTimeStyles.None, out resultado);

            return DateTime.TryParseExact(t, FormatosDataHoraPtBr, c, DateTimeStyles.None, out resultado);
        }

        /// <summary>Converte texto para data/hora. Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data/hora convertida.</returns>
        /// <exception cref="ConversaoException">Quando o texto não é uma data/hora válida.</exception>
        public static DateTime ParaDataHora(this string? texto, string? formato = null, CultureInfo? cultura = null)
            => TentarData(texto, formato, cultura, out DateTime resultado) ? resultado : throw new ConversaoException(texto, typeof(DateTime));

        /// <summary>Converte texto para data/hora, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é válido.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data/hora convertida, ou <paramref name="emCasoErro"/>.</returns>
        public static DateTime ParaDataHora(this string? texto, DateTime emCasoErro, string? formato = null, CultureInfo? cultura = null)
            => TentarData(texto, formato, cultura, out DateTime resultado) ? resultado : emCasoErro;

        /// <summary>Converte texto para data/hora nulável — nulo quando inválido (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data/hora convertida, ou nulo.</returns>
        public static DateTime? ParaDataHoraOuNulo(this string? texto, string? formato = null, CultureInfo? cultura = null)
            => TentarData(texto, formato, cultura, out DateTime resultado) ? resultado : null;

        /// <summary>Alias semântico de <see cref="ParaDataHora(string?, string?, CultureInfo?)"/>, para quando o campo é só de data.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data convertida.</returns>
        public static DateTime ParaData(this string? texto, string? formato = null, CultureInfo? cultura = null) => texto.ParaDataHora(formato, cultura);

        /// <summary>Alias semântico com fallback, para quando o campo é só de data.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é válido.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data convertida, ou <paramref name="emCasoErro"/>.</returns>
        public static DateTime ParaData(this string? texto, DateTime emCasoErro, string? formato = null, CultureInfo? cultura = null) => texto.ParaDataHora(emCasoErro, formato, cultura);

        /// <summary>Alias semântico nulável, para quando o campo é só de data.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data convertida, ou nulo.</returns>
        public static DateTime? ParaDataOuNulo(this string? texto, string? formato = null, CultureInfo? cultura = null) => texto.ParaDataHoraOuNulo(formato, cultura);

        /// <summary>Alias semântico de <see cref="ParaDataHora(string?, string?, CultureInfo?)"/>, para quando o campo é só de hora.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data/hora convertida (a parte de data recebe a data de hoje).</returns>
        public static DateTime ParaHora(this string? texto, string? formato = null, CultureInfo? cultura = null) => texto.ParaDataHora(formato, cultura);

        /// <summary>Alias semântico com fallback, para quando o campo é só de hora.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto não é válido.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data/hora convertida, ou <paramref name="emCasoErro"/>.</returns>
        public static DateTime ParaHora(this string? texto, DateTime emCasoErro, string? formato = null, CultureInfo? cultura = null) => texto.ParaDataHora(emCasoErro, formato, cultura);

        /// <summary>Alias semântico nulável, para quando o campo é só de hora.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="formato">Formato .NET exato; se omitido, tenta a cascata pt-BR.</param>
        /// <param name="cultura">Cultura explícita para esta chamada, se houver.</param>
        /// <returns>Data/hora convertida, ou nulo.</returns>
        public static DateTime? ParaHoraOuNulo(this string? texto, string? formato = null, CultureInfo? cultura = null) => texto.ParaDataHoraOuNulo(formato, cultura);

        // #endregion

        // #region Char  ->  destino sugerido: Conversao/Texto/

        /// <summary>Converte texto para char (primeiro caractere, ignorando espaços nas pontas). Lança em caso de erro.</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <returns>Primeiro caractere.</returns>
        /// <exception cref="ConversaoException">Quando o texto é nulo ou vazio.</exception>
        public static char ParaChar(this string? texto)
            => !EhNuloOuVazio(texto) ? texto!.Trim()[0] : throw new ConversaoException(texto, typeof(char));

        /// <summary>Converte texto para char, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <param name="emCasoErro">Valor devolvido quando o texto é nulo ou vazio.</param>
        /// <returns>Primeiro caractere, ou <paramref name="emCasoErro"/>.</returns>
        public static char ParaChar(this string? texto, char emCasoErro)
            => !EhNuloOuVazio(texto) ? texto!.Trim()[0] : emCasoErro;

        /// <summary>Converte texto para char nulável — nulo quando vazio (nunca lança).</summary>
        /// <param name="texto">Texto digitado.</param>
        /// <returns>Primeiro caractere, ou nulo.</returns>
        public static char? ParaCharOuNulo(this string? texto)
            => !EhNuloOuVazio(texto) ? texto!.Trim()[0] : null;

        /// <summary>Converte um char para texto.</summary>
        /// <param name="valor">Caractere a converter.</param>
        /// <returns>Texto com o caractere.</returns>
        public static string ParaTexto(this char valor) => valor.ToString();

        /// <summary>Converte um char nulável para texto.</summary>
        /// <param name="valor">Caractere a converter.</param>
        /// <param name="emCasoNulo">Texto devolvido quando o valor é nulo.</param>
        /// <returns>Texto com o caractere, ou <paramref name="emCasoNulo"/>.</returns>
        public static string ParaTexto(this char? valor, string emCasoNulo = "") => valor.HasValue ? valor.Value.ToString() : emCasoNulo;

        // #endregion

        // #region Enum e dropdown  ->  destino sugerido: Conversao/Enum/

        /// <summary>Devolve o valor numérico subjacente de um membro de enum.</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="valor">Membro do enum.</param>
        /// <returns>Valor numérico.</returns>
        public static int ParaInt<TEnum>(this TEnum valor) where TEnum : struct, Enum => Convert.ToInt32(valor);

        /// <summary>Devolve o nome do membro do enum (ex.: "Macarrao").</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="valor">Membro do enum.</param>
        /// <returns>Nome do membro.</returns>
        public static string ParaTexto<TEnum>(this TEnum valor) where TEnum : struct, Enum => valor.ToString();

        /// <summary>
        /// Devolve o nome amigável do membro: procura primeiro <see cref="DisplayAttribute.Name"/>,
        /// depois <see cref="DescriptionAttribute"/>, e cai no nome do membro se nenhum existir.
        /// </summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="valor">Membro do enum.</param>
        /// <returns>Nome amigável.</returns>
        public static string ParaDescricao<TEnum>(this TEnum valor) where TEnum : struct, Enum
        {
            FieldInfo? membro = typeof(TEnum).GetField(valor.ToString());

            if (membro is not null)
            {
                DisplayAttribute? display = membro.GetCustomAttribute<DisplayAttribute>();

                if (!string.IsNullOrEmpty(display?.Name))
                    return display!.Name!;

                DescriptionAttribute? descricao = membro.GetCustomAttribute<DescriptionAttribute>();

                if (descricao is not null)
                    return descricao.Description;
            }

            return valor.ToString();
        }

        /// <summary>Converte um valor numérico para o membro correspondente do enum. Lança em caso de erro.</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="valor">Valor numérico.</param>
        /// <returns>Membro correspondente.</returns>
        /// <exception cref="ConversaoException">Quando o valor não corresponde a nenhum membro definido.</exception>
        public static TEnum ParaEnum<TEnum>(this int valor) where TEnum : struct, Enum
            => Enum.IsDefined(typeof(TEnum), valor) ? (TEnum)(object)valor : throw new ConversaoException(valor.ToString(CultureInfo.InvariantCulture), typeof(TEnum));

        /// <summary>Converte um valor numérico para enum, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="valor">Valor numérico.</param>
        /// <param name="emCasoErro">Membro devolvido quando o valor não é definido no enum.</param>
        /// <returns>Membro correspondente, ou <paramref name="emCasoErro"/>.</returns>
        public static TEnum ParaEnum<TEnum>(this int valor, TEnum emCasoErro) where TEnum : struct, Enum
            => Enum.IsDefined(typeof(TEnum), valor) ? (TEnum)(object)valor : emCasoErro;

        /// <summary>Converte um valor numérico para enum nulável — nulo quando não definido (nunca lança).</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="valor">Valor numérico.</param>
        /// <returns>Membro correspondente, ou nulo.</returns>
        public static TEnum? ParaEnumOuNulo<TEnum>(this int valor) where TEnum : struct, Enum
            => Enum.IsDefined(typeof(TEnum), valor) ? (TEnum)(object)valor : null;

        /// <summary>Converte texto (nome do membro) para enum. Lança em caso de erro.</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="texto">Nome do membro (não sensível a maiúsculas).</param>
        /// <returns>Membro correspondente.</returns>
        /// <exception cref="ConversaoException">Quando o texto não bate com nenhum membro.</exception>
        public static TEnum ParaEnum<TEnum>(this string? texto) where TEnum : struct, Enum
            => !EhNuloOuVazio(texto) && Enum.TryParse(texto, true, out TEnum resultado) ? resultado : throw new ConversaoException(texto, typeof(TEnum));

        /// <summary>Converte texto para enum, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="texto">Nome do membro (não sensível a maiúsculas).</param>
        /// <param name="emCasoErro">Membro devolvido quando o texto não bate com nenhum membro.</param>
        /// <returns>Membro correspondente, ou <paramref name="emCasoErro"/>.</returns>
        public static TEnum ParaEnum<TEnum>(this string? texto, TEnum emCasoErro) where TEnum : struct, Enum
            => !EhNuloOuVazio(texto) && Enum.TryParse(texto, true, out TEnum resultado) ? resultado : emCasoErro;

        /// <summary>Converte texto para enum nulável — nulo quando não reconhecido (nunca lança).</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="texto">Nome do membro (não sensível a maiúsculas).</param>
        /// <returns>Membro correspondente, ou nulo.</returns>
        public static TEnum? ParaEnumOuNulo<TEnum>(this string? texto) where TEnum : struct, Enum
            => !EhNuloOuVazio(texto) && Enum.TryParse(texto, true, out TEnum resultado) ? resultado : null;

        /// <summary>Procura o membro do enum cuja descrição amigável (Display ou Description) bate com o texto.</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="texto">Descrição amigável a procurar.</param>
        /// <param name="resultado">Membro encontrado, quando a busca dá certo.</param>
        /// <returns>Verdadeiro se algum membro bateu.</returns>
        private static bool TentarPelaDescricao<TEnum>(string? texto, out TEnum resultado) where TEnum : struct, Enum
        {
            resultado = default;

            if (EhNuloOuVazio(texto))
                return false;

            string t = texto!.Trim();

            foreach (TEnum valor in Enum.GetValues<TEnum>())
            {
                if (string.Equals(valor.ParaDescricao(), t, StringComparison.OrdinalIgnoreCase))
                {
                    resultado = valor;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Converte a descrição amigável (Display ou Description) de volta para o membro do enum. Lança em caso de erro.</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="texto">Descrição amigável (ex.: "Macarrão").</param>
        /// <returns>Membro correspondente.</returns>
        /// <exception cref="ConversaoException">Quando nenhum membro tem essa descrição.</exception>
        public static TEnum ParaEnumPelaDescricao<TEnum>(this string? texto) where TEnum : struct, Enum
            => TentarPelaDescricao(texto, out TEnum resultado) ? resultado : throw new ConversaoException(texto, typeof(TEnum));

        /// <summary>Converte a descrição amigável para enum, devolvendo um valor padrão em caso de erro (nunca lança).</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="texto">Descrição amigável (ex.: "Macarrão").</param>
        /// <param name="emCasoErro">Membro devolvido quando nenhum bate com a descrição.</param>
        /// <returns>Membro correspondente, ou <paramref name="emCasoErro"/>.</returns>
        public static TEnum ParaEnumPelaDescricao<TEnum>(this string? texto, TEnum emCasoErro) where TEnum : struct, Enum
            => TentarPelaDescricao(texto, out TEnum resultado) ? resultado : emCasoErro;

        /// <summary>Converte a descrição amigável para enum nulável — nulo quando não encontrado (nunca lança).</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="texto">Descrição amigável (ex.: "Macarrão").</param>
        /// <returns>Membro correspondente, ou nulo.</returns>
        public static TEnum? ParaEnumPelaDescricaoOuNulo<TEnum>(this string? texto) where TEnum : struct, Enum
            => TentarPelaDescricao(texto, out TEnum resultado) ? resultado : null;

        /// <summary>Lista todos os membros do enum, prontos para popular tela ou relatório.</summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <returns>Cada membro com seu valor numérico e descrição amigável.</returns>
        public static IReadOnlyList<(TEnum Valor, int Numero, string Descricao)> ListarOpcoes<TEnum>() where TEnum : struct, Enum
        {
            List<(TEnum, int, string)> lista = new();

            foreach (TEnum valor in Enum.GetValues<TEnum>())
                lista.Add((valor, valor.ParaInt(), valor.ParaDescricao()));

            return lista;
        }

        /// <summary>Uma opção pronta para um dropdown/select (ex.: o BBIDropdown da BBiCore).</summary>
        /// <typeparam name="TValue">Tipo do valor de cada opção.</typeparam>
        public class OpcaoDropdown<TValue>
        {
            /// <summary>Valor da opção (o que vai no atributo/binding do dropdown).</summary>
            public TValue Valor { get; set; } = default!;

            /// <summary>Texto exibido para o usuário.</summary>
            public string Texto { get; set; } = string.Empty;

            /// <summary>Verdadeiro quando esta é a opção que deve iniciar selecionada.</summary>
            public bool Selecionado { get; set; }
        }

        /// <summary>
        /// Converte qualquer lista de objetos para o formato de opções de dropdown, marcando a
        /// selecionada e (opcionalmente) inserindo uma opção "Escolha..." no topo. Lista nula vira
        /// lista de opções vazia — nunca lança.
        /// </summary>
        /// <typeparam name="TOrigem">Tipo dos objetos de origem.</typeparam>
        /// <typeparam name="TValue">Tipo do valor de cada opção.</typeparam>
        /// <param name="origem">Lista de origem (pode ser nula).</param>
        /// <param name="campoValor">Função que extrai o valor de cada item.</param>
        /// <param name="campoTexto">Função que extrai o texto exibido de cada item.</param>
        /// <param name="valorSelecionado">Valor que deve iniciar marcado como selecionado, se houver.</param>
        /// <param name="incluirOpcaoEscolha">Se verdadeiro, insere uma opção vazia no topo da lista.</param>
        /// <param name="textoOpcaoEscolha">Texto da opção vazia, quando incluída.</param>
        /// <returns>Lista de opções pronta para o dropdown.</returns>
        public static List<OpcaoDropdown<TValue>> ParaOpcoesDropdown<TOrigem, TValue>(
            this IEnumerable<TOrigem>? origem,
            Func<TOrigem, TValue> campoValor,
            Func<TOrigem, string> campoTexto,
            TValue? valorSelecionado = default,
            bool incluirOpcaoEscolha = false,
            string textoOpcaoEscolha = "Escolha...")
        {
            List<OpcaoDropdown<TValue>> opcoes = new();

            if (incluirOpcaoEscolha)
            {
                opcoes.Add(new OpcaoDropdown<TValue>
                {
                    Valor = default!,
                    Texto = textoOpcaoEscolha,
                    Selecionado = valorSelecionado is null
                });
            }

            if (origem is not null)
            {
                foreach (TOrigem item in origem)
                {
                    TValue valor = campoValor(item);

                    opcoes.Add(new OpcaoDropdown<TValue>
                    {
                        Valor = valor,
                        Texto = campoTexto(item),
                        Selecionado = valorSelecionado is not null && EqualityComparer<TValue>.Default.Equals(valor, valorSelecionado)
                    });
                }
            }

            return opcoes;
        }

        /// <summary>
        /// Converte todos os membros de um enum para o formato de opções de dropdown, usando
        /// <see cref="ParaDescricao{TEnum}"/> como texto de cada opção.
        /// </summary>
        /// <typeparam name="TEnum">Tipo do enum.</typeparam>
        /// <param name="valorSelecionado">Membro que deve iniciar selecionado, se houver.</param>
        /// <param name="incluirOpcaoEscolha">Se verdadeiro, insere uma opção vazia no topo da lista.</param>
        /// <param name="textoOpcaoEscolha">Texto da opção vazia, quando incluída.</param>
        /// <returns>Lista de opções pronta para o dropdown.</returns>
        public static List<OpcaoDropdown<TEnum>> ParaOpcoesDropdown<TEnum>(
            TEnum? valorSelecionado = null,
            bool incluirOpcaoEscolha = false,
            string textoOpcaoEscolha = "Escolha...") where TEnum : struct, Enum
        {
            List<OpcaoDropdown<TEnum>> opcoes = new();

            if (incluirOpcaoEscolha)
            {
                opcoes.Add(new OpcaoDropdown<TEnum>
                {
                    Valor = default,
                    Texto = textoOpcaoEscolha,
                    Selecionado = !valorSelecionado.HasValue
                });
            }

            foreach (TEnum valor in Enum.GetValues<TEnum>())
            {
                opcoes.Add(new OpcaoDropdown<TEnum>
                {
                    Valor = valor,
                    Texto = valor.ParaDescricao(),
                    Selecionado = valorSelecionado.HasValue && EqualityComparer<TEnum>.Default.Equals(valor, valorSelecionado.Value)
                });
            }

            return opcoes;
        }

        // #endregion
    }
}
