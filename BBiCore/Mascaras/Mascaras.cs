// =============================================================================
//  Mascaras.cs  —  Máscaras e validações brasileiras (BBiCore.Mascaras)
// -----------------------------------------------------------------------------
//  Utilitários puros (sem UI) para formatar e validar CPF, CNPJ, telefone, CEP,
//  moeda e data no padrão brasileiro. A regra fica AQUI (reutilizável em model,
//  API, testes); os componentes são apenas a casca visual sobre estes.
//
//  Princípio: EXIBE em português (dd/MM/yyyy, vírgula decimal, máscara), mas o
//  valor de trabalho é o NATIVO/limpo — datas como DateTime (persistidas
//  yyyy-MM-dd), moeda como decimal (ponto), documentos como caracteres crus.
//
//  CNPJ ALFANUMÉRICO (IN RFB 2.229/2024, vigente a partir de julho/2026):
//  o CNPJ passa a ter 12 caracteres ALFANUMÉRICOS (A–Z e 0–9) + 2 dígitos
//  verificadores NUMÉRICOS. O DV segue o módulo 11, mas o valor de cada
//  caractere passa a ser (ASCII - 48): '0'..'9' -> 0..9 e 'A'..'Z' -> 17..42.
//  É retrocompatível por desenho: um CNPJ puramente numérico continua validando
//  exatamente como antes. Por isso o CNPJ tem regra PRÓPRIA, separada da do CPF
//  (que continua somente numérico).
//  ATENÇÃO NO BANCO: guarde CNPJ como texto (VARCHAR/CHAR 14). Coluna numérica
//  não comporta o formato novo.
//
//  >>> NOTA PARA REORGANIZAÇÃO: cada tipo em sua #region, nomeada pelo destino.
// =============================================================================

using System.Globalization;
using System.Text;

namespace BBiCore.Mascaras
{
    // #region Utilitário base  ->  destino sugerido: Helpers/

    /// <summary>Funções básicas compartilhadas pelas máscaras.</summary>
    public static class TextoMascara
    {
        /// <summary>Cultura brasileira usada em toda a formatação.</summary>
        public static readonly CultureInfo CulturaBr = CultureInfo.GetCultureInfo("pt-BR");

        /// <summary>Remove tudo o que não for dígito.</summary>
        /// <param name="texto">Texto de entrada.</param>
        /// <returns>Apenas os dígitos.</returns>
        public static string SomenteDigitos(string? texto)
        {
            if (string.IsNullOrEmpty(texto))
                return string.Empty;

            StringBuilder sb = new(texto.Length);

            foreach (char c in texto)
                if (char.IsDigit(c))
                    sb.Append(c);

            return sb.ToString();
        }

        /// <summary>Remove tudo o que não for letra ou dígito, deixando as letras em MAIÚSCULO (formato do CNPJ alfanumérico).</summary>
        /// <param name="texto">Texto de entrada.</param>
        /// <returns>Apenas letras (maiúsculas) e dígitos.</returns>
        public static string SomenteAlfanumerico(string? texto)
        {
            if (string.IsNullOrEmpty(texto))
                return string.Empty;

            StringBuilder sb = new(texto.Length);

            foreach (char c in texto)
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToUpperInvariant(c));

            return sb.ToString();
        }
    }

    // #endregion

    // #region CPF (numérico)  ->  destino sugerido: Validacao/

    /// <summary>Formatação e validação de CPF. Continua SOMENTE NUMÉRICO — a mudança alfanumérica é exclusiva do CNPJ.</summary>
    public static class Cpf
    {
        /// <summary>Quantidade de dígitos de um CPF.</summary>
        public const int Tamanho = 11;

        /// <summary>Aplica a máscara 000.000.000-00 sobre os dígitos disponíveis (parcial enquanto digita).</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>CPF formatado até onde há dígitos.</returns>
        public static string Formatar(string? valor)
        {
            string d = TextoMascara.SomenteDigitos(valor);

            if (d.Length > Tamanho)
                d = d[..Tamanho];

            StringBuilder sb = new();

            for (int i = 0; i < d.Length; i++)
            {
                if (i == 3 || i == 6)
                    sb.Append('.');
                else if (i == 9)
                    sb.Append('-');

                sb.Append(d[i]);
            }

            return sb.ToString();
        }

        /// <summary>Devolve o valor cru do CPF (só dígitos), pronto para persistir.</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Somente os dígitos.</returns>
        public static string Limpar(string? valor) => TextoMascara.SomenteDigitos(valor);

        /// <summary>Valida um CPF pelos dígitos verificadores (módulo 11).</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Verdadeiro se válido.</returns>
        public static bool EhValido(string? valor)
        {
            string d = TextoMascara.SomenteDigitos(valor);

            if (d.Length != Tamanho)
                return false;

            if (TodosIguais(d))
                return false;

            int dv1 = CalcularDigito(d, 9, 10);
            int dv2 = CalcularDigito(d, 10, 11);

            return dv1 == d[9] - '0' && dv2 == d[10] - '0';
        }

        /// <summary>Indica se todos os dígitos são iguais (ex.: 111.111.111-11), caso sempre inválido.</summary>
        /// <param name="digitos">Dígitos do CPF.</param>
        /// <returns>Verdadeiro quando são todos iguais.</returns>
        private static bool TodosIguais(string digitos)
        {
            for (int i = 1; i < digitos.Length; i++)
                if (digitos[i] != digitos[0])
                    return false;

            return true;
        }

        /// <summary>Calcula um dígito verificador do CPF (módulo 11, pesos decrescentes).</summary>
        /// <param name="digitos">Dígitos do CPF.</param>
        /// <param name="quantidade">Quantos dígitos considerar.</param>
        /// <param name="pesoInicial">Peso do primeiro dígito.</param>
        /// <returns>Dígito verificador (0–9).</returns>
        private static int CalcularDigito(string digitos, int quantidade, int pesoInicial)
        {
            int soma = 0;
            int peso = pesoInicial;

            for (int i = 0; i < quantidade; i++)
            {
                soma += (digitos[i] - '0') * peso;
                peso--;
            }

            int resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
    }

    // #endregion

    // #region CNPJ (ALFANUMÉRICO)  ->  destino sugerido: Validacao/

    /// <summary>
    /// Formatação e validação de CNPJ no formato ALFANUMÉRICO (IN RFB 2.229/2024): 12 caracteres
    /// alfanuméricos (A–Z, 0–9) + 2 dígitos verificadores numéricos. O CNPJ puramente numérico
    /// continua válido e passa pela MESMA regra — o cálculo é retrocompatível por desenho.
    /// </summary>
    public static class Cnpj
    {
        /// <summary>Quantidade total de caracteres de um CNPJ.</summary>
        public const int Tamanho = 14;

        /// <summary>Quantidade de caracteres da base (tudo antes dos dígitos verificadores).</summary>
        public const int TamanhoBase = 12;

        /// <summary>Pesos do primeiro dígito verificador (sobre os 12 caracteres da base).</summary>
        private static readonly int[] PesosDv1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        /// <summary>Pesos do segundo dígito verificador (sobre a base + o primeiro DV).</summary>
        private static readonly int[] PesosDv2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        /// <summary>Aplica a máscara 00.000.000/0000-00 sobre os caracteres disponíveis (aceita letras na base).</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>CNPJ formatado até onde há caracteres.</returns>
        public static string Formatar(string? valor)
        {
            string c = Limpar(valor);

            if (c.Length > Tamanho)
                c = c[..Tamanho];

            StringBuilder sb = new();

            for (int i = 0; i < c.Length; i++)
            {
                if (i == 2 || i == 5)
                    sb.Append('.');
                else if (i == 8)
                    sb.Append('/');
                else if (i == 12)
                    sb.Append('-');

                sb.Append(c[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Devolve o valor cru do CNPJ, pronto para persistir: alfanumérico em MAIÚSCULO, sem máscara.
        /// Guarde como TEXTO no banco — coluna numérica não comporta o formato alfanumérico.
        /// </summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Somente letras maiúsculas e dígitos.</returns>
        public static string Limpar(string? valor) => TextoMascara.SomenteAlfanumerico(valor);

        /// <summary>Valida um CNPJ (alfanumérico ou numérico) pelo módulo 11 com valores ASCII − 48.</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Verdadeiro se válido.</returns>
        public static bool EhValido(string? valor)
        {
            string c = Limpar(valor);

            if (c.Length != Tamanho)
                return false;

            if (TodosIguais(c))
                return false;

            string baseCnpj = c[..TamanhoBase];
            string verificadores = c[TamanhoBase..];

            // Os dois últimos caracteres são SEMPRE numéricos, mesmo no formato alfanumérico.
            if (!char.IsDigit(verificadores[0]) || !char.IsDigit(verificadores[1]))
                return false;

            string esperados = CalcularVerificadores(baseCnpj);
            return string.Equals(esperados, verificadores, StringComparison.Ordinal);
        }

        /// <summary>Indica se o CNPJ é do formato novo (tem ao menos uma letra).</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Verdadeiro quando há letra — ou seja, é alfanumérico.</returns>
        public static bool EhAlfanumerico(string? valor)
        {
            foreach (char c in Limpar(valor))
                if (char.IsLetter(c))
                    return true;

            return false;
        }

        /// <summary>Calcula os dois dígitos verificadores a partir dos 12 caracteres da base.</summary>
        /// <param name="baseCnpj">Base do CNPJ (12 caracteres alfanuméricos).</param>
        /// <returns>Os dois dígitos verificadores (ex.: "35").</returns>
        /// <exception cref="ArgumentException">Quando a base não tem 12 caracteres.</exception>
        public static string CalcularVerificadores(string baseCnpj)
        {
            string c = Limpar(baseCnpj);

            if (c.Length != TamanhoBase)
                throw new ArgumentException($"A base do CNPJ deve ter {TamanhoBase} caracteres.", nameof(baseCnpj));

            int dv1 = CalcularDigito(c, PesosDv1);
            int dv2 = CalcularDigito(c + dv1.ToString(CultureInfo.InvariantCulture), PesosDv2);

            return $"{dv1}{dv2}";
        }

        /// <summary>Valor do caractere no cálculo do DV: ASCII − 48 ('0'..'9' viram 0..9; 'A'..'Z' viram 17..42).</summary>
        /// <param name="caractere">Caractere já em maiúsculo.</param>
        /// <returns>Valor numérico usado no módulo 11.</returns>
        private static int ValorCaractere(char caractere) => caractere - 48;

        /// <summary>Calcula um dígito verificador aplicando os pesos e o módulo 11.</summary>
        /// <param name="caracteres">Caracteres considerados (base, ou base + primeiro DV).</param>
        /// <param name="pesos">Pesos correspondentes.</param>
        /// <returns>Dígito verificador (0–9).</returns>
        private static int CalcularDigito(string caracteres, int[] pesos)
        {
            int soma = 0;

            for (int i = 0; i < pesos.Length; i++)
                soma += ValorCaractere(caracteres[i]) * pesos[i];

            int resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        /// <summary>Indica se todos os caracteres são iguais (ex.: 00000000000000), caso sempre inválido.</summary>
        /// <param name="caracteres">Caracteres do CNPJ.</param>
        /// <returns>Verdadeiro quando são todos iguais.</returns>
        private static bool TodosIguais(string caracteres)
        {
            for (int i = 1; i < caracteres.Length; i++)
                if (caracteres[i] != caracteres[0])
                    return false;

            return true;
        }
    }

    // #endregion

    // #region Documento (CPF ou CNPJ)  ->  destino sugerido: Validacao/

    /// <summary>
    /// Campo que aceita CPF ou CNPJ. A escolha é automática: havendo letra, só pode ser CNPJ
    /// (alfanumérico); caso contrário decide pelo tamanho (até 11 dígitos = CPF, acima = CNPJ).
    /// </summary>
    public static class Documento
    {
        /// <summary>Formata como CPF ou CNPJ, conforme o conteúdo digitado.</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Documento formatado.</returns>
        public static string Formatar(string? valor)
        {
            switch (Detectar(valor))
            {
                case TipoDocumento.Cnpj:
                    return Cnpj.Formatar(valor);
                case TipoDocumento.Cpf:
                default:
                    return Cpf.Formatar(valor);
            }
        }

        /// <summary>Devolve o valor cru conforme o tipo detectado (CPF: dígitos; CNPJ: alfanumérico).</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Valor pronto para persistir.</returns>
        public static string Limpar(string? valor)
        {
            switch (Detectar(valor))
            {
                case TipoDocumento.Cnpj:
                    return Cnpj.Limpar(valor);
                case TipoDocumento.Cpf:
                default:
                    return Cpf.Limpar(valor);
            }
        }

        /// <summary>Valida como CPF (11 dígitos) ou CNPJ (14 caracteres), conforme o conteúdo.</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Verdadeiro se válido no formato detectado.</returns>
        public static bool EhValido(string? valor)
        {
            switch (Detectar(valor))
            {
                case TipoDocumento.Cnpj:
                    return Cnpj.EhValido(valor);
                case TipoDocumento.Cpf:
                default:
                    return Cpf.EhValido(valor);
            }
        }

        /// <summary>Descobre se o texto digitado é CPF ou CNPJ.</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Tipo detectado.</returns>
        public static TipoDocumento Detectar(string? valor)
        {
            string alfanumerico = TextoMascara.SomenteAlfanumerico(valor);

            foreach (char c in alfanumerico)
                if (char.IsLetter(c))
                    return TipoDocumento.Cnpj;

            return alfanumerico.Length > Cpf.Tamanho ? TipoDocumento.Cnpj : TipoDocumento.Cpf;
        }
    }

    /// <summary>Tipo de documento detectado num campo genérico.</summary>
    public enum TipoDocumento
    {
        /// <summary>Cadastro de Pessoa Física.</summary>
        Cpf,

        /// <summary>Cadastro Nacional da Pessoa Jurídica.</summary>
        Cnpj
    }

    // #endregion

    // #region Telefone e CEP  ->  destino sugerido: Formatacao/

    /// <summary>Formatação de telefone brasileiro (fixo e celular).</summary>
    public static class Telefone
    {
        /// <summary>Aplica (00) 0000-0000 ou (00) 00000-0000 conforme a quantidade de dígitos.</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Telefone formatado.</returns>
        public static string Formatar(string? valor)
        {
            string d = TextoMascara.SomenteDigitos(valor);

            if (d.Length > 11)
                d = d[..11];

            StringBuilder sb = new();
            bool celular = d.Length > 10;

            for (int i = 0; i < d.Length; i++)
            {
                if (i == 0)
                    sb.Append('(');
                else if (i == 2)
                    sb.Append(") ");
                else if ((celular && i == 7) || (!celular && i == 6))
                    sb.Append('-');

                sb.Append(d[i]);
            }

            return sb.ToString();
        }

        /// <summary>Devolve o valor cru do telefone (só dígitos).</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Somente os dígitos.</returns>
        public static string Limpar(string? valor) => TextoMascara.SomenteDigitos(valor);
    }

    /// <summary>Formatação de CEP.</summary>
    public static class Cep
    {
        /// <summary>Aplica 00000-000 sobre os dígitos.</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>CEP formatado.</returns>
        public static string Formatar(string? valor)
        {
            string d = TextoMascara.SomenteDigitos(valor);

            if (d.Length > 8)
                d = d[..8];

            if (d.Length <= 5)
                return d;

            return d[..5] + "-" + d[5..];
        }

        /// <summary>Devolve o valor cru do CEP (só dígitos).</summary>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Somente os dígitos.</returns>
        public static string Limpar(string? valor) => TextoMascara.SomenteDigitos(valor);
    }

    // #endregion

    // #region Moeda e data  ->  destino sugerido: Formatacao/

    /// <summary>Formatação/conversão de moeda no padrão brasileiro.</summary>
    public static class Moeda
    {
        /// <summary>Formata um valor decimal como moeda brasileira.</summary>
        /// <param name="valor">Valor.</param>
        /// <param name="comSimbolo">Se inclui "R$".</param>
        /// <returns>Texto formatado (ex.: "1.234,56" ou "R$ 1.234,56").</returns>
        public static string Formatar(decimal valor, bool comSimbolo = false)
            => valor.ToString(comSimbolo ? "C2" : "N2", TextoMascara.CulturaBr);

        /// <summary>Converte um texto (dígitos como centavos) em decimal. Ex.: "123456" → 1234,56.</summary>
        /// <param name="valor">Texto digitado.</param>
        /// <returns>Valor decimal (0 se vazio).</returns>
        public static decimal DeDigitosCentavos(string? valor)
        {
            string d = TextoMascara.SomenteDigitos(valor);

            if (d.Length == 0)
                return 0m;

            return long.Parse(d, CultureInfo.InvariantCulture) / 100m;
        }
    }

    /// <summary>Formatação/conversão de data no padrão brasileiro.</summary>
    public static class Data
    {
        /// <summary>Formata uma data como dd/MM/yyyy.</summary>
        /// <param name="valor">Data.</param>
        /// <returns>Texto formatado.</returns>
        public static string Formatar(DateTime valor)
            => valor.ToString("dd/MM/yyyy", TextoMascara.CulturaBr);

        /// <summary>Tenta converter um texto dd/MM/yyyy em <see cref="DateTime"/>.</summary>
        /// <param name="valor">Texto no formato brasileiro.</param>
        /// <param name="data">Data convertida, quando válida.</param>
        /// <returns>Verdadeiro se a conversão teve sucesso.</returns>
        public static bool TentarConverter(string? valor, out DateTime data)
            => DateTime.TryParseExact(
                valor,
                ["dd/MM/yyyy", "d/M/yyyy"],
                TextoMascara.CulturaBr,
                DateTimeStyles.None,
                out data);
    }

    // #endregion

    // #region Máscara unificada (usada pelos componentes)  ->  destino sugerido: Formatacao/

    /// <summary>Tipos de máscara aplicáveis a campos de texto.</summary>
    public enum TipoMascara
    {
        /// <summary>CPF ou CNPJ, detectado pelo conteúdo.</summary>
        Documento,

        /// <summary>Somente CPF (numérico).</summary>
        Cpf,

        /// <summary>Somente CNPJ (alfanumérico).</summary>
        Cnpj,

        /// <summary>Telefone fixo ou celular.</summary>
        Telefone,

        /// <summary>CEP.</summary>
        Cep
    }

    /// <summary>Ponto único de formatação, limpeza e validação por <see cref="TipoMascara"/>.</summary>
    public static class Mascara
    {
        /// <summary>Formata um texto conforme o tipo de máscara.</summary>
        /// <param name="tipo">Tipo de máscara.</param>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Texto formatado.</returns>
        public static string Formatar(TipoMascara tipo, string? valor)
        {
            switch (tipo)
            {
                case TipoMascara.Documento:
                    return Documento.Formatar(valor);
                case TipoMascara.Cpf:
                    return Cpf.Formatar(valor);
                case TipoMascara.Cnpj:
                    return Cnpj.Formatar(valor);
                case TipoMascara.Telefone:
                    return Telefone.Formatar(valor);
                case TipoMascara.Cep:
                    return Cep.Formatar(valor);
                default:
                    return valor ?? string.Empty;
            }
        }

        /// <summary>Devolve o valor cru (sem máscara), pronto para persistir. O CNPJ mantém as letras; os demais, só dígitos.</summary>
        /// <param name="tipo">Tipo de máscara.</param>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Valor cru.</returns>
        public static string Limpar(TipoMascara tipo, string? valor)
        {
            switch (tipo)
            {
                case TipoMascara.Documento:
                    return Documento.Limpar(valor);
                case TipoMascara.Cpf:
                    return Cpf.Limpar(valor);
                case TipoMascara.Cnpj:
                    return Cnpj.Limpar(valor);
                case TipoMascara.Telefone:
                    return Telefone.Limpar(valor);
                case TipoMascara.Cep:
                    return Cep.Limpar(valor);
                default:
                    return valor ?? string.Empty;
            }
        }

        /// <summary>Valida um texto conforme o tipo. Retorna nulo quando o tipo não tem validação (telefone/CEP).</summary>
        /// <param name="tipo">Tipo de máscara.</param>
        /// <param name="valor">Texto (com ou sem máscara).</param>
        /// <returns>Verdadeiro/falso para documentos; nulo quando não há regra de validação.</returns>
        public static bool? Validar(TipoMascara tipo, string? valor)
        {
            switch (tipo)
            {
                case TipoMascara.Documento:
                    return Documento.EhValido(valor);
                case TipoMascara.Cpf:
                    return Cpf.EhValido(valor);
                case TipoMascara.Cnpj:
                    return Cnpj.EhValido(valor);
                case TipoMascara.Telefone:
                case TipoMascara.Cep:
                default:
                    return null;
            }
        }

        /// <summary>Indica se o tipo aceita letras na digitação (hoje: CNPJ alfanumérico e o documento genérico).</summary>
        /// <param name="tipo">Tipo de máscara.</param>
        /// <returns>Verdadeiro quando letras são válidas.</returns>
        public static bool AceitaLetras(TipoMascara tipo)
        {
            switch (tipo)
            {
                case TipoMascara.Cnpj:
                case TipoMascara.Documento:
                    return true;
                case TipoMascara.Cpf:
                case TipoMascara.Telefone:
                case TipoMascara.Cep:
                default:
                    return false;
            }
        }

        /// <summary>Mensagem padrão de inválido para o tipo.</summary>
        /// <param name="tipo">Tipo de máscara.</param>
        /// <returns>Texto do erro exibido no campo.</returns>
        public static string MensagemInvalido(TipoMascara tipo)
        {
            switch (tipo)
            {
                case TipoMascara.Cpf:
                    return "CPF inválido.";
                case TipoMascara.Cnpj:
                    return "CNPJ inválido.";
                case TipoMascara.Documento:
                    return "Documento inválido.";
                default:
                    return "Valor inválido.";
            }
        }

        /// <summary>Chave da máscara usada pelo JS do lado do cliente.</summary>
        /// <param name="tipo">Tipo de máscara.</param>
        /// <returns>Identificador textual (ex.: "doc", "cnpj", "cep").</returns>
        internal static string ChaveJs(TipoMascara tipo)
        {
            switch (tipo)
            {
                case TipoMascara.Documento:
                    return "doc";
                case TipoMascara.Cpf:
                    return "cpf";
                case TipoMascara.Cnpj:
                    return "cnpj";
                case TipoMascara.Telefone:
                    return "tel";
                case TipoMascara.Cep:
                    return "cep";
                default:
                    return string.Empty;
            }
        }
    }

    // #endregion
}
