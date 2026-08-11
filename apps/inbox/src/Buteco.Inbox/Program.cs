using Buteco.Inbox.Agents;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Testing;
using Buteco.Inbox.Channels.Adapters.Waha;
using Buteco.Inbox.Channels.Endpoints;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Channels.Webhooks.Endpoints;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Contacts.Endpoints;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);
// Scoped, não o default do pacote (Singleton) — os handlers dependem de
// AppDbContext, que é Scoped; Singleton falharia a validação de DI no
// Build() (não é possível consumir um serviço Scoped a partir de um Singleton).
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

builder.Services.Configure<InboxCryptoOptions>(builder.Configuration.GetSection(InboxCryptoOptions.SectionName));
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<PublicUrlOptions>(builder.Configuration.GetSection(PublicUrlOptions.SectionName));
builder.Services.Configure<DebounceOptions>(builder.Configuration.GetSection(DebounceOptions.SectionName));
// Nome totalmente qualificado — colide com Microsoft.AspNetCore.Builder.SessionOptions
// (middleware de sessão HTTP do ASP.NET Core, não usado aqui), trazido por
// implicit usings do Sdk.Web.
builder.Services.Configure<Buteco.Inbox.Contacts.SessionOptions>(
    builder.Configuration.GetSection(Buteco.Inbox.Contacts.SessionOptions.SectionName));
builder.Services.AddSingleton<IChannelCredentialCipher, AesGcmChannelCredentialCipher>();
// Scoped, não Singleton — depende de AppDbContext, que é Scoped (mesmo
// motivo de ServiceLifetime.Scoped no AddMediator acima).
builder.Services.AddScoped<IContactSessionResolver, ContactSessionResolver>();

builder.Services.AddSingleton<IChannelAdapterRegistry, ChannelAdapterRegistry>();
// Adapter de teste desta fatia (design.md, Decision 6) — nenhum WAHA/Telegram
// real ainda. Cada módulo de adapter registra os dois serviços sob a
// mesma chave (design.md, Decision 5).
builder.Services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("test-channel");
builder.Services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("test-channel");
// Terceiro contrato (inbox-adapter-waha, design.md, Decision 2) — sem ele,
// "test-channel" ficaria incompleto e ValidateChannelAdapterRegistrations
// abaixo derrubaria o processo no startup.
builder.Services.AddKeyedSingleton<IInboundWebhookHandler, TestInboundWebhookHandler>("test-channel");
// Primeiro adapter real (inbox-adapter-waha, design.md, Decisions 4/6/7).
// WahaOutboundMessageSender usa IHttpClientFactory.CreateClient() anônimo
// (sem nome) — já disponível pelos AddHttpClient(...) nomeados abaixo, que
// registram os serviços core de IHttpClientFactory como efeito colateral
// (design.md, Decision 7: BaseAddress é por credencial/canal, não fixa por
// adapter, então não há um client nomeado dedicado a registrar aqui).
builder.Services.AddKeyedSingleton<IChannelConfigValidator, WahaChannelConfigValidator>("waha");
builder.Services.AddKeyedSingleton<IOutboundMessageSender, WahaOutboundMessageSender>("waha");
builder.Services.AddKeyedSingleton<IInboundWebhookHandler, WahaInboundWebhookHandler>("waha");
// Falha o startup se algum ChannelType não tiver os três serviços
// registrados (design.md, Decision 2/Decision 7 de inbox-adapter-contrato-catalogo)
// — precisa rodar depois de todos os AddKeyedSingleton acima.
builder.Services.ValidateChannelAdapterRegistrations();

var apiBaseUrl = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()?.BaseUrl
    ?? throw new InvalidOperationException("Api:BaseUrl não configurado.");

// Timeout curto e fixo (design.md, Decision 4) — mesmo padrão de
// PushNotificationSender em apps/workers.
builder.Services.AddHttpClient(AgentReferenceValidator.HttpClientName, client => client.BaseAddress = new Uri(apiBaseUrl))
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IAgentReferenceValidator, AgentReferenceValidator>();

// Scoped, não Singleton — depende de AppDbContext/IContactSessionResolver,
// que são Scoped (mesmo motivo dos registros acima).
builder.Services.AddScoped<IInboundMessageOrchestrator, InboundMessageOrchestrator>();

// Sem BaseAddress fixo — A2AClientFactory monta a Uri completa por
// AgentId (design.md, Decisão 3). Timeout curto e fixo, mesmo padrão de
// AgentReferenceValidator.
builder.Services.AddHttpClient(A2AClientFactory.HttpClientName)
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IA2AClientFactory, A2AClientFactory>();

builder.Services.AddHostedService<DebounceSweepService>();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapChannelEndpoints();
app.MapContactEndpoints();
app.MapPushNotificationEndpoints();
app.MapWebhookEndpoints();

app.Run();

public partial class Program;
