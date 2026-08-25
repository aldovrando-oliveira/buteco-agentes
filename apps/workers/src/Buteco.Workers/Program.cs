using Buteco.Workers.Agents;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Mcp.Security;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Buteco.Workers.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<ChatClientOptions>(builder.Configuration.GetSection(ChatClientOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<McpCryptoOptions>(builder.Configuration.GetSection(McpCryptoOptions.SectionName));

// Não vinculado a nenhuma seção de configuração de propósito — defaults
// são, na prática, constantes de produto (ver Options/AgentDelegationToolOptions.cs).
builder.Services.Configure<AgentDelegationToolOptions>(_ => { });

// Único ponto de acesso a relógio/fuso local em todo apps/workers (design.md
// da change apps-workers-contexto-temporal, Decisão 6) — nenhum código,
// novo ou pré-existente, deve chamar DateTimeOffset.Now/TimeZoneInfo.Local
// diretamente. Testes substituem por FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IChatClientResolver, ChatClientResolver>();

builder.Services.AddSingleton<IMcpCredentialCipher, AesGcmMcpCredentialCipher>();

// PooledConnectionLifetime explícito (default do SocketsHttpHandler é
// infinito) — sem isso, conexões deste client de longa duração (reusado
// entre execuções via IHttpClientFactory) podem ficar presas a um
// McpServer atrás de proxy/CDN (ex.: Cloudflare) que derruba conexões
// ociosas do lado dele sem avisar; a próxima tentativa de reuso trava até
// estourar o timeout de inicialização do MCP em vez de abrir conexão nova.
builder.Services.AddHttpClient(McpTransportFactory.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromSeconds(30),
    });
builder.Services.AddSingleton<McpTransportFactory>();
builder.Services.AddSingleton<IMcpToolSetResolver, McpToolSetResolver>();

builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
builder.Services.AddSingleton<IAgentDelegationToolSetResolver, AgentDelegationToolSetResolver>();

// Timeout curto e fixo (design.md, Decision 3) — primeiro HttpClient nomeado
// do projeto com Timeout customizado, aditivo, sem afetar o cliente MCP.
builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<PushNotificationSender>();

// Singleton porque é injetado no TaskJobConsumer (BackgroundService, singleton) —
// AgentExecutionService não segura estado escopado diretamente, abre um
// IServiceScope novo por execução via IServiceScopeFactory quando precisa do
// AppDbContext. IChatClientResolver constrói o IChatClient por chamada (sem
// cache entre execuções), então ser singleton aqui não implica reaproveitar
// nenhuma instância de IChatClient — ver design.md, Decision 7.
builder.Services.AddSingleton<AgentExecutionService>();
builder.Services.AddHostedService<TaskJobConsumer>();

var host = builder.Build();

host.ValidateTimeZoneConfiguration();

host.Run();
