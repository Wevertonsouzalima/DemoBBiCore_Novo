# BBiCore — versão consolidada (para cópia manual)

Mesma biblioteca de importação de antes, mas com o código **agrupado em poucos
arquivos grandes** para facilitar a recriação manual no ambiente do trabalho.
Veja `COMO_COPIAR.md` para a ordem de cópia.

Consolidação aplicada:
- `BBiCore/BBiCore.cs` reúne todo o código C# da RCL (núcleo + leitores + tipos
  de apoio do componente). O componente Blazor fica no `.razor` (não se funde com `.cs`).
- `AnimesApp/AnimesApp.Modelos.cs` reúne modelos e mapas do app de teste.

Nada muda em comportamento: juntar tipos num arquivo é idêntico a tê-los
separados, para o compilador. Cada tipo está isolado em uma `#region` nomeada
pelo destino sugerido, e os arquivos trazem instruções no topo para a futura
separação no padrão do time.

## Conteúdo da biblioteca

- **Núcleo** (`BBiCore.Importacao`): motor `ImportadorTabular`, `MapaImportacao<T>`
  fluente, `OpcoesImportacao` (âncoras `LinhaCabecalho`/`LinhaInicioDados`/`ColunaInicio`,
  `LinhasEmBranco`, `Modo`, `CulturaGlobal`), tipos de resultado, contratos
  `ILeitorTabular`/`IAcessorLinha`, conversor, e os leitores `LeitorCsv`,
  `LeitorExcel` e `LeitorDespivotado`.
- **Componente** (`BBiCore.Componentes`): `BBiImportadorArquivo` (disparo
  automático/manual, `ImportarAsync()`/`LimparSelecao()`), `ModoDisparo`, `ArquivoDepositado`.

## Rodar o app de teste

```bash
dotnet restore
dotnet run --project AnimesApp
```
Acesse `http://localhost:5090`. Páginas: `/` (animes) e `/cruzado` (despivot).

## Documentação

Toda a superfície pública (e boa parte da privada) tem `<summary>`. O
`GenerateDocumentationFile` está ligado; `CS1591` é suprimido apenas para os
tipos gerados pelo Razor.
