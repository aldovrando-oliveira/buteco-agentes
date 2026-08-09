using Buteco.Inbox.Agents;
using Buteco.Inbox.Channels.Endpoints;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);
// Scoped, não o default do pacote (Singleton) — os handlers dependem de
// AppDbContext, que é Scoped; Singleton falharia a validação de DI no
// Build() (não é possível consumir um serviço Scoped a partir de um Singleton).
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

builder.Services.Configure<InboxCryptoOptions>(builder.Configuration.GetSection(InboxCryptoOptions.SectionName));
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.AddSingleton<IChannelCredentialCipher, AesGcmChannelCredentialCipher>();

var apiBaseUrl = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()?.BaseUrl
    ?? throw new InvalidOperationException("Api:BaseUrl não configurado.");

// Timeout curto e fixo (design.md, Decision 4) — mesmo padrão de
// PushNotificationSender em apps/workers.
builder.Services.AddHttpClient(AgentReferenceValidator.HttpClientName, client => client.BaseAddress = new Uri(apiBaseUrl))
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IAgentReferenceValidator, AgentReferenceValidator>();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapChannelEndpoints();

app.Run();

public partial class Program;
