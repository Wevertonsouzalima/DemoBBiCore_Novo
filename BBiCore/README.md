# BBiCore — biblioteca de componentes (RCL, .NET 9)


## Organização (uma pasta por módulo, autocontida)

Cada módulo fica numa pasta com TUDO junto — componentes, CSS, JS colocado e
classes — para copiar módulo a módulo sem caçar arquivos soltos:

```
Templates/    BBiCampoTemplate.razor (+.css+.js)   MotorTemplate.cs   <-- marcadores {{campo}}
Importacao/   BBiImportadorArquivo.razor (+.css)   Importacao.cs      (motor + despivot)
Email/        BBiEmail / BBiComporEmail (+.css)    Email.cs (+ TransporteExchange.cs à parte — ver "Pacotes" abaixo)
Exportacao/   BBiExportador (+.css+.js)            Exportacao.cs
Mascaras/     BBiCampoMascarado/Moeda/Data (+.css) BBiCampoMascarado.razor.js  Mascaras.cs
SeletorData/  BBISeletorData (+.css+.js)           ModoSeletorData.cs (drill-down dias->meses->anos)
Conversoes/   Conversoes.cs                        (bool/numérico/data/char/enum <-> texto, sem UI)
_Imports.razor   BBiCore.csproj
```

### Módulo SeletorData (`BBISeletorData`)

Seletor de data/hora/período com calendário próprio — sem `<input type="date">` nativo.
Drill-down estilo Material (dias → meses → anos, clicando no título). No desktop abre
como popover ancorado, com os meses vizinhos em colunas verticais laterais (a própria
navegação); no celular vira modal centralizado com setas no cabeçalho + swipe na grade.
Navegação de mês/ano/década roda inteiramente em C#; o JS colocado só posiciona o
popover, detecta celular, captura o swipe e aplica a máscara de digitação.

Parâmetros: `Modo` (`Data`/`DataHora`/`Periodo`), `@bind-Valor` (Data/DataHora) ou
`@bind-ValorInicio`/`@bind-ValorFim` (Periodo), `Rotulo`, `Placeholder`, `DataMinima`,
`DataMaxima`, `PermitirDigitacao`, `MostrarBotaoHoje`, `PrimeiroDiaSemana`,
`LarguraQuebraCelular` (padrão 768), `Desabilitado`.

### Módulo Conversoes (`ConversaoExtensions`)

Conversão genérica de tipo, nos dois sentidos, sempre em extension method
(`texto.ParaInt()`, `valor.ParaTexto()`) e sempre com três variantes por conversão de
texto: lança `ConversaoException` (`.ParaInt()`), devolve um fallback
(`.ParaInt(emCasoErro: 0)`) ou devolve nulo (`.ParaIntOuNulo()`) — nunca deixa escapar
uma exceção "crua" do .NET. Cobre bool (Sim/Não, Ativo/Inativo, Verdadeiro/Falso, S/N,
Habilitado/Desabilitado), numérico (int/long/decimal/double/float, com milhar e
percentual), data/hora (estilos combináveis `EstiloData` × `EstiloHora`, sempre pt-BR
por padrão), char e enum (valor numérico, nome, descrição via `[Display]`/`[Description]`,
e conversão para lista de opções de dropdown). Cultura e casas decimais seguem a
prioridade: parâmetro explícito na chamada &gt; appsettings do site
(`ConversaoConfiguracao.Inicializar(builder.Configuration)`, seção `BBiCore:Conversao`)
&gt; pt-BR / 2 casas. Não substitui `Mascaras.cs` — CPF/CNPJ/telefone/CEP continuam lá;
aqui é conversão de tipo, não documento.

### Módulo Template (reutilizável)

O mecanismo de marcadores `{{campo}}` é um módulo próprio (`BBiCore.Templates`),
independente de e-mail — serve a documentos, mensagens, títulos de relatório etc.

- `MotorTemplate` — resolução multi-fonte (objeto -> variáveis do sistema ->
  computados), listagem de campos e validação. Classe pura, usável sem Blazor.
- `BBiCampoTemplate` — campo (input ou textarea, via `Multilinha`) com menu de
  inserção de campos com busca e validação dos marcadores não encontrados.
  Parâmetros: `Motor`, `TipoDados`, `Fontes`, `@bind-Valor`, `Multilinha`,
  `Rotulo`, `TextoDica`, `MostrarFaltantes`, `FaltantesChanged`.

O módulo de e-mail **consome** essa peça: o corpo (BBiComporEmail) e os campos do
envelope — assunto, Para, Cc, Cco (BBiEmail) — são `BBiCampoTemplate`. O que é
específico de e-mail (modo normal com imagens, HTML Outlook-safe, preview, envio,
anexos, validação de endereços) continua no módulo Email.

O JS é *colocado* (`{Componente}.razor.js`, ao lado do `.razor`) e servido em
`_content/BBiCore/{pasta}/{Componente}.razor.js`. Isso mantém o JS na pasta do
módulo (bom para a cópia) e continua funcionando por referência de projeto ou NuGet.

Biblioteca única com três áreas, consolidada no menor número de arquivos possível
para facilitar a cópia manual e a divisão posterior no padrão do time.

## Conteúdo

Todo o **código C#** está em `BBiCore.cs` (um arquivo), dividido em três namespaces:

- **`BBiCore.Importacao`** — importador tabular (CSV/Excel) e despivotador:
  motor `ImportadorTabular`, `MapaImportacao<T>`, `OpcoesImportacao` (âncoras de
  início, tratamento de brancos), leitores `LeitorCsv`/`LeitorExcel`/`LeitorDespivotado`,
  tipos de resultado e contratos.
- **`BBiCore.Componentes`** — tipos de apoio dos componentes (`ModoDisparo`,
  `ArquivoDepositado`).
- **`BBiCore.Email`** — motor de template multi-fonte (`MotorTemplate`),
  contrato (`ITemplateEmail`/`IAnexoTemplate` + enums + DTOs), e envio
  (`IEnviadorEmail`, `EmailResolvido`, `ResolvedorEmail`, stub `EnviadorEmailNaoImplementado`).

- **`BBiCore.Exportacao`** — exporta listas para CSV (RFC 4180) e Excel (.xlsx),
  com descoberta de colunas por reflexão e seleção na UI (`BBiExportador`).
- **`BBiCore.Mascaras`** — máscaras/validações brasileiras (CPF, CNPJ, telefone, CEP,
  moeda, data) como utilitários puros + campos (`BBiCampoMascarado`, `BBiCampoMoeda`,
  `BBiCampoData`): exibem em BR, entregam valor nativo/cru (sem conflito ao salvar).

Os **componentes visuais** ficam nos `.razor` (Razor não se funde com `.cs`):

- `Componentes/BBiImportadorArquivo.razor` — importação/despivot de arquivos.
- `Componentes/BBiComporEmail.razor` — composição do corpo do e-mail (modo normal/avançado).
- `Componentes/BBiEmail.razor` — componente completo de e-mail (envelope + corpo + anexos + envio).


## Configuração no sistema consumidor

O envio de e-mail é centralizado. Cada sistema tem o SEU grupo de configurações
(`OpcoesEmail`: remetente, usuário sistêmico, senha, servidor) e registra UMA
implementação de `IEnviadorEmail`:

```csharp
// Configurações do sistema (idealmente vindas do appsettings; senha de um cofre).
OpcoesEmail opcoes = builder.Configuration.GetSection("Email").Get<OpcoesEmail>()!;
builder.Services.AddSingleton(opcoes);

// Produção:
builder.Services.AddScoped<IEnviadorEmail, EnviadorEmailExchange>();
// Testes (grava .eml em disco, sem servidor):
// builder.Services.AddScoped<IEnviadorEmail, EnviadorEmailSimulado>();
```

O envio monta o MIME (corpo, imagens inline por cid:, anexos das formas 1 e 3),
aplica a política de falha de anexo, sanitiza caminhos (path traversal) e trata a
exclusão pós-anexo. Usa `SmtpClient` nativo; para o caminho mais moderno, troque a
entrega por MailKit/Graph implementando a mesma `IEnviadorEmail`.

A persistência de templates é do consumidor: implemente seu repositório contra a
tabela `Email.Templates` (local por sistema); o componente apenas dispara `OnSalvar`.

## Divisão futura

Cada tipo em `BBiCore.cs` está isolado em uma `#region` nomeada pelo destino
sugerido (Models/, Services/, Interfaces/, Readers/, Mapping/…). As áreas
Importacao e Email são independentes e podem virar projetos/pastas separados.
Ao dividir, preserve os namespaces e a API pública.

## Observação

Este pacote não foi compilado no ambiente de geração (sem SDK .NET / NuGet).
Balanceamentos e lógica foram conferidos; o primeiro `dotnet build`/`run` no seu
ambiente é o teste real, e os `.razor` (JS interop + iframe) são a parte mais
sujeita a ajuste fino.

## Envio de e-mail (composição)

```
IServicoEmail  ── o que o dev injeta
      |
      +── ServicoEmail ── comum a todo envio: resolve template, materializa anexos,
      |                    aplica redirecionamento de teste, repete em falha
      |
      +── ITransporteEmail ── só entrega:
               TransporteSmtp (MailKit) · TransporteExchange (EWS) · TransporteSimulado (.eml)
```

Trocar de transporte é trocar **uma linha** no registro:

```csharp
builder.Services.AddScoped<ITransporteEmail, TransporteSimulado>();   // dev
// builder.Services.AddScoped<ITransporteEmail, TransporteSmtp>();    // relay SMTP
// builder.Services.AddScoped<ITransporteEmail, TransporteExchange>();// Exchange (EWS)

builder.Services.AddScoped<IServicoEmail>(sp => new ServicoEmail(
    sp.GetRequiredService<ITransporteEmail>(),
    sp.GetRequiredService<OpcoesEmail>(),
    sp.GetRequiredService<MotorTemplate>(),
    sp.GetService<IRepositorioTemplateEmail>()));
```

### Três formas de enviar

```csharp
// 1) AVULSO — texto LITERAL: um {{campo}} aqui NÃO é resolvido, chega assim ao destinatário.
EmailAvulso email = new() { Assunto = "Aviso" };
email.Para.Add("fulano@empresa.com");
email.DefinirCorpoTexto("Olá,\n\nSegue o relatório.");
email.Anexos.Add(AnexoAvulso.DeBytes("relatorio.csv", bytes, "text/csv"));
await servico.EnviarAsync(email);

// 2) POR TEMPLATE SALVO — aqui os marcadores SÃO resolvidos com o objeto de dados.
await servico.EnviarPorTemplateAsync("aviso-pedido", pedido);

// 3) TEMPLATE EM MÃOS — é o caminho que o componente usa.
await servico.EnviarTemplateAsync(template, pedido);
```

**Regra:** `{{marcador}}` só tem significado no envio **por template**. No avulso, o texto é literal.

### Credenciais e pacotes

As credenciais vêm do cadastro do aplicativo: implemente `IProvedorCredenciaisEmail` no SEU
projeto — a **descriptografia da senha acontece do seu lado**, e a biblioteca recebe a credencial
pronta (nunca conhece o seu banco nem o seu algoritmo).

| Pacote | Usado por | Observação |
|---|---|---|
| `MailKit` | `TransporteSmtp` | caminho SMTP moderno (o `SmtpClient` da BCL está obsoleto) |
| `Microsoft.Exchange.WebServices.NETStandard` | `TransporteExchange` | **desligado por padrão** — ver abaixo |

Cada transporte está num arquivo próprio: se um pacote não existir no seu feed, remova o arquivo e
a linha do `.csproj` — os outros continuam funcionando.

### Trava de homologação

Preencha `OpcoesEmail.RedirecionarPara`: todos os e-mails passam a ir só para esses endereços
(Cc/Cco descartados, assunto prefixado com `[TESTE]`).

## CNPJ alfanumérico

O módulo `Mascaras` já trata o **CNPJ alfanumérico** (IN RFB 2.229/2024, a partir de julho/2026):
12 caracteres alfanuméricos + 2 dígitos verificadores numéricos, com o DV calculado pelo módulo 11
sobre os valores ASCII − 48. É **retrocompatível**: CNPJ numérico continua válido pela mesma regra.

- `Cpf` — continua **somente numérico** (regra própria, separada).
- `Cnpj` — **alfanumérico**: `Formatar`, `Limpar`, `EhValido`, `EhAlfanumerico`, `CalcularVerificadores`.
- `Documento` — aceita os dois: se houver letra, só pode ser CNPJ; senão decide pelo tamanho.

**Atenção no banco:** guarde CNPJ como **texto** (`VARCHAR(14)`). Coluna numérica não comporta o
formato novo, e um `CHECK` do tipo `^\d{14}$` passa a barrar CNPJs válidos.

## Rascunho

Duas coisas distintas, ambas cobertas:

**1. Situação do template** — `ITemplateEmail.Situacao` (`Rascunho` | `Publicado`). No componente há
os botões **Salvar rascunho** e **Publicar**. O rascunho pode ser salvo **incompleto** (sem
destinatário, com marcador faltando, corpo vazio); ao publicar, as validações de envio são exigidas.

**2. Gerar o .eml em vez de enviar** — o serviço monta a MESMA mensagem que seria transmitida e a
entrega como arquivo. O .eml sai **sempre resolvido** (sem marcadores): é exatamente o e-mail que
seria enviado, pronto para abrir no Outlook e disparar.

```csharp
OpcoesRascunho opcoes = new()
{
    // Combinável: uma, duas ou as três.
    Destino = DestinoRascunho.Bytes          // devolve os bytes (para baixar no navegador)
            | DestinoRascunho.Arquivo        // grava o .eml em disco
            | DestinoRascunho.CaixaPostal,   // deposita na pasta Rascunhos do Outlook (só Exchange)
    PastaArquivo = @"C:\rascunhos"
};

ResultadoRascunho r = await servico.GerarRascunhoTemplateAsync(template, pedido, opcoes);
// r.Conteudo (bytes) · r.CaminhoArquivo · r.SalvoNaCaixaPostal
```

No componente, o destino é o parâmetro `DestinosDoRascunho` (padrão: baixar pelo navegador).

`CaixaPostal` exige um transporte que implemente `ITransporteRascunho` — hoje, o `TransporteExchange`.
Pedir esse destino com outro transporte **não quebra** o restante: os demais destinos são produzidos
e o resultado traz a ressalva.

## Template: rascunho, publicado e marcador sem valor

**Template** é um e-mail salvo e reutilizável: monta-se uma vez (assunto, corpo, destinatários,
anexos) e os sistemas disparam N vezes, trocando só o que vem dos `{{marcadores}}`.
O `ITemplateEmail` é o **conjunto mínimo de colunas** que a tabela de cada sistema deve ter —
o sistema pode acrescentar as suas (`IdArea`, `IdUsuario`, `DataAlteracao`…), nunca ter menos.

### Situação (trava real, não rótulo)

| Situação | Salvar | Enviar |
|---|---|---|
| `Rascunho` | sem validação (pode estar incompleto) | **recusado** — "publique antes de enviar" |
| `Publicado` | exige assunto, destinatário, corpo | liberado para os sistemas |

O "enviar teste" do componente é a exceção consciente (`permitirRascunho: true`) — testar antes de
publicar é justamente o ponto. E a listagem filtra: `ListarNomesAsync(SituacaoTemplate.Publicado)`
para a tela de seleção não oferecer rascunhos.

### Marcador sem valor no envio (`AcaoNaFalhaDeMarcador`)

Mesmo modelo da política de anexo:

| Valor | Efeito |
|---|---|
| `FalharEnvio` *(padrão)* | aborta e lista os marcadores sem valor |
| `EnviarMesmoAssim` | envia **removendo os trechos** — o destinatário nunca vê um `{{campo}}` cru |

Vale para o assunto, o corpo, os endereços e os caminhos de anexo. Se a remoção zerar os
destinatários (o marcador *era* o endereço), o envio é abortado — não se manda e-mail órfão.
A mesma política se aplica ao `.eml` do rascunho, que por isso nunca sai com marcador cru.

## Exportação em PDF

O `BBiExportador` exporta para **CSV**, **Excel** e **PDF**. O PDF usa **PDFsharp/MigraDoc**
(licença **MIT** — uso comercial livre, sem limite de faturamento). O QuestPDF foi descartado
porque a licença gratuita só vale para empresas com receita abaixo de US$ 1M; o iText é AGPL.

Como a página é finita (ao contrário do CSV/Excel), o PDF tem decisões próprias:

| Item | Comportamento |
|---|---|
| Orientação | **automática**: paisagem acima de 6 colunas (ou fixe retrato/paisagem) |
| Fonte | encolhe conforme o nº de colunas, até um piso (padrão 6 pt) |
| Texto que não cabe | **quebra dentro da célula** (nunca corta o valor) |
| Cabeçalho | repetido em **todas** as páginas |
| Rodapé | "Página X de Y" + data de geração |
| Valores | padrão brasileiro; números alinhados à direita |

O componente mostra antes de gerar como a página vai sair (ex.: *"10 coluna(s) → paisagem, fonte 8,2 pt"*).

O gerador fica na seção "ExportadorPdf.cs" dentro de `Exportacao/Exportacao.cs` (arquivo único do
módulo — o comentário de seção no topo dela ainda marca a origem): se precisar trocar de biblioteca,
mexe-se só nessa seção e na linha do `.csproj`.

## Pacotes: dois pontos de atenção

**MailKit — versão mínima 4.16.0.** Versões anteriores têm a falha de *STARTTLS Response Injection*
(CVE-2026-41319): um atacante no meio do caminho consegue injetar respostas de protocolo antes do
TLS e forçar um downgrade da autenticação. Não use abaixo de 4.16.0.

**Transporte Exchange (EWS) — desligado por padrão.** O pacote da EWS para .NET não tem versão
estável (a mais recente é `2.0.0-beta3`), e restore de pré-lançamento costuma ser barrado em
ambiente corporativo. O pacote **oficial** da Microsoft (2.2) é .NET Framework e não roda em .NET 9.

Por isso o `.csproj` tem uma chave:

```xml
<PropertyGroup>
  <IncluirTransporteExchange>false</IncluirTransporteExchange>
</PropertyGroup>
```

Com ela em `false`, o `Email/TransporteExchange.cs` sai da compilação e o pacote não é baixado —
o envio funciona por **SMTP (MailKit)** e pelo **simulado**. Para usar o Exchange, mude para `true`
(e garanta que o seu feed aceita pré-lançamento). Nada mais muda no código.

> **Por isso `TransporteExchange.cs` continua num arquivo separado** dentro de `Email/`, mesmo com o
> resto do módulo consolidado em `Email.cs` — é o único jeito de manter essa exclusão condicional do
> `.csproj` funcionando (fundi-lo obrigaria o `Email.cs` inteiro a depender do pacote da EWS sempre).
