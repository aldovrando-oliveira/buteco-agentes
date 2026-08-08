using Buteco.Workers.Agents;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Mcp.Security;
using Buteco.Workers.Messaging;
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

builder.Services.AddSingleton<IChatClientResolver, ChatClientResolver>();

builder.Services.AddSingleton<IMcpCredentialCipher, AesGcmMcpCredentialCipher>();
builder.Services.AddHttpClient(McpTransportFactory.HttpClientName);
builder.Services.AddSingleton<McpTransportFactory>();
builder.Services.AddSingleton<IMcpToolSetResolver, McpToolSetResolver>();

builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
builder.Services.AddSingleton<IAgentDelegationToolSetResolver, AgentDelegationToolSetResolver>();

// Singleton porque é injetado no TaskJobConsumer (BackgroundService, singleton) —
// AgentExecutionService não segura estado escopado diretamente, abre um
// IServiceScope novo por execução via IServiceScopeFactory quando precisa do
// AppDbContext. IChatClientResolver constrói o IChatClient por chamada (sem
// cache entre execuções), então ser singleton aqui não implica reaproveitar
// nenhuma instância de IChatClient — ver design.md, Decision 7.
builder.Services.AddSingleton<AgentExecutionService>();
builder.Services.AddHostedService<TaskJobConsumer>();

var host = builder.Build();
host.Run();
