// =============================================================================
//  TransporteExchange.cs  —  Entrega via Exchange (EWS) (BBiCore.Email)
// -----------------------------------------------------------------------------
//  ARQUIVO ISOLADO DE PROPÓSITO: é o único que depende do pacote da EWS.
//  Se o seu feed NuGet não tiver o pacote, remova ESTE arquivo e a linha
//  correspondente do .csproj — o resto da biblioteca (inclusive o transporte
//  SMTP e o simulado) continua compilando.
//
//  PACOTE (.NET 9):
//      <PackageReference Include="Microsoft.Exchange.WebServices.NETStandard" Version="2.0.1" />
//  O pacote OFICIAL "Microsoft.Exchange.WebServices" 2.2 é .NET Framework e NÃO
//  roda em .NET 9. O acima expõe a MESMA API (ExchangeService, WebCredentials,
//  EmailMessage), então o código é o mesmo do legado — muda só a referência.
//
//  Este caminho serve para Exchange ON-PREMISES com autenticação básica/NTLM
//  (o caso corporativo típico). Não exige Entra ID nem registro de aplicação.
// =============================================================================

using System.Net;
using Microsoft.Exchange.WebServices.Data;

namespace BBiCore.Email
{
    /// <summary>
    /// Entrega e-mails pelo Exchange corporativo, via EWS. Também sabe salvar a mensagem na pasta
    /// Rascunhos da caixa postal (implementa <see cref="ITransporteRascunho"/>) — o e-mail aparece no
    /// Outlook da conta, pronto para revisar e disparar manualmente.
    /// </summary>
    public sealed class TransporteExchange : ITransporteEmail, ITransporteRascunho
    {
        /// <summary>Configurações do sistema.</summary>
        private readonly OpcoesEmail _opcoes;

        /// <summary>Cadastro do sistema: é dele que saem as credenciais (a aplicação não as fornece).</summary>
        private readonly CadastroSistema _cadastro;

        /// <summary>Cria o transporte do Exchange.</summary>
        /// <param name="opcoes">Configurações do sistema (URL do EWS, versão, remetente…).</param>
        /// <param name="cadastro">Acesso ao cadastro do sistema (fonte das credenciais).</param>
        public TransporteExchange(OpcoesEmail opcoes, CadastroSistema cadastro)
        {
            _opcoes = opcoes ?? throw new ArgumentNullException(nameof(opcoes));
            _cadastro = cadastro ?? throw new ArgumentNullException(nameof(cadastro));
        }

        /// <inheritdoc/>
        public async Task EntregarAsync(MensagemEmail mensagem, CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(mensagem);

            CredenciaisEmail credenciais = await ObterCredenciaisAsync(cancelamento);
            ExchangeService servico = ConfigurarServico(credenciais);

            string remetente = string.IsNullOrWhiteSpace(credenciais.EnderecoRemetente)
                ? mensagem.Remetente
                : credenciais.EnderecoRemetente;

            EmailMessage email = MontarMensagem(servico, mensagem, remetente);

            // A EWS Managed API é síncrona: sai do thread do circuito para não bloquear a UI.
            await Task.Run(
                () =>
                {
                    switch (_opcoes.SalvarCopiaEmEnviados)
                    {
                        case true:
                            email.SendAndSaveCopy();
                            break;
                        default:
                            email.Send();
                            break;
                    }
                },
                cancelamento);
        }

        /// <inheritdoc/>
        public async Task<ResultadoEnvio> TestarConexaoAsync(CancellationToken cancelamento = default)
        {
            try
            {
                CredenciaisEmail credenciais = await ObterCredenciaisAsync(cancelamento);
                ExchangeService servico = ConfigurarServico(credenciais);

                // Uma leitura mínima basta para provar que a URL responde e a credencial autentica.
                await Task.Run(
                    () => Folder.Bind(servico, WellKnownFolderName.Inbox),
                    cancelamento);

                return new ResultadoEnvio(true, "Conexão com o Exchange bem-sucedida.");
            }
            catch (Exception ex)
            {
                return new ResultadoEnvio(false, $"Falha na conexão: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task SalvarRascunhoAsync(MensagemEmail mensagem, CancellationToken cancelamento = default)
        {
            ArgumentNullException.ThrowIfNull(mensagem);

            CredenciaisEmail credenciais = await ObterCredenciaisAsync(cancelamento);
            ExchangeService servico = ConfigurarServico(credenciais);

            string remetente = string.IsNullOrWhiteSpace(credenciais.EnderecoRemetente)
                ? mensagem.Remetente
                : credenciais.EnderecoRemetente;

            EmailMessage email = MontarMensagem(servico, mensagem, remetente);

            // Save (em vez de Send) deposita a mensagem na pasta Rascunhos: nada é transmitido.
            await Task.Run(() => email.Save(WellKnownFolderName.Drafts), cancelamento);
        }

        /// <summary>Obtém as credenciais do cadastro do sistema.</summary>
        /// <param name="cancelamento">Token de cancelamento.</param>
        /// <returns>Credenciais prontas.</returns>
        private async Task<CredenciaisEmail> ObterCredenciaisAsync(CancellationToken cancelamento)
            => await _cadastro.ObterCredenciaisAsync(cancelamento);

        /// <summary>Configura o serviço EWS (canal seguro, endereço e credenciais).</summary>
        /// <param name="credenciais">Credenciais de autenticação.</param>
        /// <returns>Serviço pronto para enviar.</returns>
        private ExchangeService ConfigurarServico(CredenciaisEmail credenciais)
        {
            if (string.IsNullOrWhiteSpace(_opcoes.UrlEws))
                throw new InvalidOperationException(
                    "OpcoesEmail.UrlEws não configurada — informe o endereço do serviço EWS do Exchange.");

            // Servidores antigos costumam exigir TLS 1.2 explicitamente.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            if (!_opcoes.ValidarCertificadoServidor)
                ServicePointManager.ServerCertificateValidationCallback =
                    (remetente, certificado, cadeia, erros) => true;

            ExchangeService servico = new(ResolverVersao(_opcoes.VersaoExchange))
            {
                Credentials = new WebCredentials(credenciais.Usuario, credenciais.Senha),
                Timeout = _opcoes.TimeoutSegundos * 1000,
                Url = new Uri(_opcoes.UrlEws)
            };

            return servico;
        }

        /// <summary>Monta a mensagem do Exchange a partir da mensagem pronta.</summary>
        /// <param name="servico">Serviço EWS configurado.</param>
        /// <param name="mensagem">Mensagem pronta.</param>
        /// <param name="remetente">Endereço do remetente.</param>
        /// <returns>Mensagem pronta para envio.</returns>
        private static EmailMessage MontarMensagem(ExchangeService servico, MensagemEmail mensagem, string remetente)
        {
            EmailMessage email = new(servico)
            {
                Subject = mensagem.Assunto,
                Body = new MessageBody(BodyType.HTML, mensagem.CorpoHtml),
                Sensitivity = MapearSensibilidade(mensagem.Classificacao)
            };

            if (!string.IsNullOrWhiteSpace(remetente))
                email.From = new EmailAddress(remetente);

            foreach (string destino in mensagem.Destinatarios)
                email.ToRecipients.Add(destino);

            foreach (string cc in mensagem.Cc)
                email.CcRecipients.Add(cc);

            foreach (string cco in mensagem.Cco)
                email.BccRecipients.Add(cco);

            foreach (AnexoMensagem anexo in mensagem.Anexos)
            {
                FileAttachment arquivo = email.Attachments.AddFileAttachment(anexo.NomeArquivo, anexo.Conteudo);

                if (!string.IsNullOrWhiteSpace(anexo.ContentType))
                    arquivo.ContentType = anexo.ContentType;

                // Imagem embutida: o corpo a referencia por cid:{ContentId}.
                if (anexo.EhInline)
                {
                    arquivo.IsInline = true;
                    arquivo.ContentId = anexo.ContentId;
                }
            }

            return email;
        }

        /// <summary>Converte o nome configurado da versão do Exchange no valor da API.</summary>
        /// <param name="versao">Nome da versão (ex.: "Exchange2010_SP2").</param>
        /// <returns>Versão correspondente; Exchange2010_SP2 quando não reconhecida.</returns>
        private static ExchangeVersion ResolverVersao(string versao)
        {
            switch (versao)
            {
                case "Exchange2007_SP1":
                    return ExchangeVersion.Exchange2007_SP1;
                case "Exchange2010":
                    return ExchangeVersion.Exchange2010;
                case "Exchange2010_SP1":
                    return ExchangeVersion.Exchange2010_SP1;
                case "Exchange2010_SP2":
                    return ExchangeVersion.Exchange2010_SP2;
                case "Exchange2013":
                    return ExchangeVersion.Exchange2013;
                case "Exchange2013_SP1":
                    return ExchangeVersion.Exchange2013_SP1;
                default:
                    return ExchangeVersion.Exchange2010_SP2;
            }
        }

        /// <summary>Mapeia a classificação do template para a sensibilidade do Exchange.</summary>
        /// <param name="classificacao">Classificação do e-mail.</param>
        /// <returns>Sensibilidade correspondente.</returns>
        private static Sensitivity MapearSensibilidade(ClassificacaoEmail classificacao)
        {
            switch (classificacao)
            {
                case ClassificacaoEmail.Confidencial:
                    return Sensitivity.Confidential;
                case ClassificacaoEmail.Interno:
                    return Sensitivity.Private;
                case ClassificacaoEmail.Publico:
                default:
                    return Sensitivity.Normal;
            }
        }
    }
}
