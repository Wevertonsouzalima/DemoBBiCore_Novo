# PROMPT — contexto da BBiCore para outra IA (Claude em outro contexto ou Copilot)

> Cole este arquivo junto com o(s) arquivo(s) de código quando pedir ajuda a uma IA
> que não participou da construção. Ele resume o projeto, as decisões fechadas e o
> padrão de código, para a IA continuar no mesmo trilho.

## O que é

`BBiCore` é uma biblioteca de componentes .NET 9 (RCL, `Microsoft.NET.Sdk.Razor`)
para projetos Blazor Server internos. Reúne três módulos independentes:

1. **Importação tabular** (`BBiCore.Importacao`): lê CSV/Excel e converte em objetos
   tipados via um mapa fluente. Motor puro (`ImportadorTabular`), leitores
   (`LeitorCsv`, `LeitorExcel`) e um **despivotador** (`LeitorDespivotado`, decorador
   de `ILeitorTabular` que transforma matriz larga em registros longos).
2. **Componentes** (`BBiCore.Componentes`): os componentes Blazor
   (`BBiImportadorArquivo`, `BBiComporEmail`, `BBiEmail`) e tipos de apoio.
3. **E-mail** (`BBiCore.Email`): motor de template com marcadores `{{Campo}}`,
   contrato de template/anexo, e envio centralizado.

## Padrão de código (SEGUIR SEMPRE)

- Idioma: **português do Brasil** (identificadores, mensagens e summaries). Nada de
  inglês fora de termos de programação consagrados.
- `switch`/`case` com `break` explícito. **Nunca** switch expressions (`=>`).
- **Summary XML em tudo**: tipos, propriedades, métodos (inclusive privados), campos.
- Arquivos consolidados: todo o código C# fica em `BBiCore.cs` (um arquivo, vários
  namespaces em bloco). Componentes visuais nos `.razor` (Razor não se funde com `.cs`).
- Cada tipo está isolado em uma `#region` nomeada pelo destino sugerido de uma futura
  divisão (Models/, Services/, Interfaces/, Readers/, Mapping/…).

## Decisões arquiteturais fechadas

- **Importação**: numeração de linha sempre FÍSICA (linhas puladas/brancas contam).
  Âncoras de início (`LinhaCabecalho`, `LinhaInicioDados`, `ColunaInicio`) em vez de
  "pular N". Despivot é peça opcional (decorador), não altera o motor.
- **E-mail — marcadores**: delimitador `{{ }}` duplo (nunca `{ }`, que colide com CSS).
  Objeto de dados **flat** (um nível, sem `{{Cliente.Nome}}`). Formato `.NET` via
  `{{Campo:formato}}`; cultura **pt-BR** fixa. Marcador ausente vira vazio e é reportado.
  Três fontes, nesta ordem: objeto → variáveis do sistema → computados (prefixo
  reservado `Sistema.`, ex.: `{{Sistema.Saudacao}}`).
- **E-mail — contrato**: `ITemplateEmail` representa o e-mail montado com marcadores
  CRUS (nada resolvido é persistido). Contrato por **interface**; o sistema implementa
  na sua entidade EF. `Email.Templates` é **local por sistema**. Anexos: forma 1
  (bytes no banco), forma 3 (caminho dinâmico com marcadores); forma 2 (callback) é
  parâmetro de envio, fora do contrato. Imagens de cabeçalho/rodapé do modo normal são
  anexos INLINE por `ContentId` (cid:bbi-cabecalho / cid:bbi-rodape).
- **E-mail — editor**: modo NORMAL (imagem + texto + imagem, gera HTML Outlook-safe em
  tabela) e AVANÇADO (HTML cru). Ambos em `<textarea>` (contentEditable foi descartado).
  Transição normal→avançado é só-ida, com "voltar" só enquanto o HTML está intacto.
  Preview é fiel (sem cor nos marcadores), exibido em MODAL estático. Menu "Adicionar
  campo" tem busca e é limitado por `FontesCampo` (endereços não oferecem automáticos);
  a máscara controla só a SUGESTÃO, não a validação.
- **E-mail — persistência x envio**: **salvar** é do consumidor (componente dispara
  `OnSalvar`, não fala com banco). **Enviar** é centralizado na DLL via `IEnviadorEmail`
  (o componente aciona o serviço; não abre conexão de rede). Cada sistema registra UMA
  implementação na DI. Destino final é **Outlook** (HTML tem que ser tabela + inline).
- Alvo é **.NET 9**, sem compatibilidade com .NET Framework.

## Estado atual / pendências

- **Envio implementado**: `EnviadorEmailBase` monta o MIME (corpo HTML, imagens inline
  por cid:, anexos das formas 1 e 3, sensibilidade, política de falha, sanitização contra
  path traversal, exclusão pós-anexo). `EnviadorEmailExchange` envia via SMTP com as
  credenciais de `OpcoesEmail`; `EnviadorEmailSimulado` grava o .eml em disco (testes).
  Cada sistema tem seu grupo em `OpcoesEmail` (remetente, usuário, senha, servidor).
  Observação: usa `System.Net.Mail.SmtpClient` (nativo, zero dependência). Se quiser o
  caminho mais moderno, dá para trocar a ENTREGA por MailKit ou Microsoft Graph
  implementando a mesma `IEnviadorEmail` — o resto não muda.
- **Persistência implementada**: contrato `IRepositorioTemplateEmail` (ListarNomes/Obter/
  Salvar/Excluir) + implementação de referência `RepositorioTemplateEmailMemoria` (thread-safe,
  cópia defensiva). Em produção, cada sistema implementa com EF Core na tabela local. Salvar
  continua sendo do consumidor: o componente dispara `OnSalvar`; a página chama o repositório.
- Não há mais pendências estruturais conhecidas — os três módulos estão completos.

## Tarefas comuns que você pode receber

- **"Separe no padrão do time"**: crie um arquivo por tipo (ou por camada) recortando
  cada `#region`, preservando namespaces e API pública. Não altere comportamento.
- **"Implemente o envio Exchange"**: implemente `IEnviadorEmail.EnviarAsync(EmailResolvido)`.
- **"Adicione um recurso"**: mantenha o padrão acima (pt-BR, switch/case, summaries).

## Aviso

O código foi gerado sem compilar (ambiente sem SDK .NET/NuGet). Ao receber, o primeiro
`dotnet build` é o teste real; os `.razor` (JS interop + iframe) são a parte mais
sujeita a ajuste fino.
