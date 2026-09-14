// =============================================================================
//  Program.cs  —  Host Blazor Server do app de demonstração (DemoBBiCore)
//  Registra os componentes interativos, as configurações de e-mail do sistema
//  e o enviador. Harness para exercitar a RCL BBiCore; não faz parte da lib.
// =============================================================================

using BBiCore.Email;
using BBiCore.Templates;
using DemoBBiCore;
using DemoBBiCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// -----------------------------------------------------------------------------
// Configurações de e-mail: cada sistema tem o SEU grupo (remetente, usuário
// sistêmico, senha, servidor). Aqui vêm do appsettings ("Email"); em produção,
// a senha deve vir de um cofre/segredo, não do arquivo.
// -----------------------------------------------------------------------------
OpcoesEmail opcoesEmail = builder.Configuration.GetSection("Email").Get<OpcoesEmail>() ?? new OpcoesEmail();

// Caminhos relativos do appsettings resolvidos a partir da raiz do app.
if (!string.IsNullOrWhiteSpace(opcoesEmail.PastaBaseAnexos) && !Path.IsPathRooted(opcoesEmail.PastaBaseAnexos))
    opcoesEmail.PastaBaseAnexos = Path.Combine(builder.Environment.ContentRootPath, opcoesEmail.PastaBaseAnexos);

// Pasta onde o enviador simulado grava os .eml (para inspeção nesta demo).
opcoesEmail.PastaSimulacao = Path.Combine(builder.Environment.ContentRootPath, "emails-simulados");

builder.Services.AddSingleton(opcoesEmail);

// -----------------------------------------------------------------------------
// ENVIO DE E-MAIL — arquitetura por composição:
//   IServicoEmail (o que o dev usa)  ->  ITransporteEmail (quem entrega)
//
// Escolha UM transporte. Trocar é trocar esta linha; nada mais muda.
// -----------------------------------------------------------------------------

// Desenvolvimento: grava o .eml em disco, sem servidor nenhum.
builder.Services.AddScoped<ITransporteEmail, TransporteSimulado>();

// Produção — SMTP (MailKit). Precisa de um relay SMTP que aceite a conta do sistema:
//   builder.Services.AddScoped<ITransporteEmail, TransporteSmtp>();
//
// Produção — Exchange (EWS). Usa a URL do EWS e as credenciais do sistema:
//   builder.Services.AddScoped<ITransporteEmail, TransporteExchange>();

// A fachada que o dev injeta para enviar (avulso, por template salvo, ou template em mãos).
builder.Services.AddScoped<IServicoEmail>(sp => new ServicoEmail(
    sp.GetRequiredService<ITransporteEmail>(),
    sp.GetRequiredService<OpcoesEmail>(),
    sp.GetRequiredService<MotorTemplate>(),
    sp.GetService<IRepositorioTemplateEmail>(),
    sp.GetService<ServicoAnexos>()));

// Motor de marcadores usado no envio POR TEMPLATE (o avulso não usa: lá o texto é literal).
builder.Services.AddSingleton(new MotorTemplate(new Dictionary<string, string>
{
    ["NomeSistema"] = "Sistema de Pedidos",
    ["Area"] = "Central de Atendimento"
}));

// CREDENCIAIS — a aplicação NÃO fornece credencial. Ela apenas se identifica
// (OpcoesEmail.NomeSistema, no appsettings) e a biblioteca busca o cadastro do app
// no banco centralizador (ver BBiCore/Email/CadastroSistema.cs).
builder.Services.AddScoped<CadastroSistema>();

// ACERVO DE ANEXOS — em memória nesta demo. Em produção, implemente IRepositorioAnexos
// contra as suas tabelas (script em BBiCore/Email/BBiCore-Email.sql).
builder.Services.AddSingleton<IRepositorioAnexos, RepositorioAnexosMemoria>();
builder.Services.AddScoped<ServicoAnexos>();

// LOG — todo envio é registrado pela própria biblioteca (sucesso e falha), sem
// que a aplicação precise fazer nada (ver BBiCore/Email/LogEmail.cs).

builder.Services.AddSingleton(new MotorTemplate(new Dictionary<string, string>
{
    ["NomeSistema"] = "Sistema de Pedidos",
    ["Area"] = "Central de Atendimento"
}));

// CREDENCIAIS — quando vierem do cadastro do aplicativo (com a senha criptografada),

// Persistência de templates. Nesta demo, em memória (some ao reiniciar o app).
// Em produção, implemente IRepositorioTemplateEmail com EF Core na tabela do sistema.
builder.Services.AddSingleton<IRepositorioTemplateEmail, RepositorioTemplateEmailMemoria>();

var app = builder.Build();

// Semeia o acervo de anexos com exemplos (só na demonstração).
await SemeadorAcervo.SemearAsync(app.Services.GetRequiredService<IRepositorioAnexos>());

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
