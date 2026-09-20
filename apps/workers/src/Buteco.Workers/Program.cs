using Buteco.Workers.Agents;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Diagnostics;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Mcp.Security;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Buteco.Workers.Knowledge.Chunking;
using Buteco.Workers.Knowledge.Embedding;
using Buteco.Workers.Knowledge.Execution;
using Buteco.Workers.Knowledge.Indexing;
using Buteco.Workers.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<ChatClientOptions>(builder.Configuration.GetSection(ChatClientOptions.SectionName));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<McpCryptoOptions>(builder.Configuration.GetSection(McpCryptoOptions.SectionName));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection(EmbeddingOptions.SectionName));

// Não vinculado a nenhuma seção de configuração de propósito — defaults
// são, na prática, constantes de produto (ver Options/AgentDelegationToolOptions.cs).
builder.Services.Configure<AgentDelegationToolOptions>(_ => { });

// Mesmo motivo do de cima, e a janela da varredura NÃO mora aqui: é lida em
// runtime do Timeout acima (ver Options/TaskDiagnosticsOptions.cs).
builder.Services.Configure<TaskDiagnosticsOptions>(_ => { });

// Único ponto de acesso a relógio/fuso local em todo apps/workers (design.md
// da change apps-workers-contexto-temporal, Decisão 6) — nenhum código,
// novo ou pré-existente, deve chamar DateTimeOffset.Now/TimeZoneInfo.Local
// diretamente. Testes substituem por FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IChatClientResolver, ChatClientResolver>();
builder.Services.AddSingleton<IEmbeddingGeneratorResolver, EmbeddingGeneratorResolver>();
builder.Services.AddSingleton<IKnowledgeChunker, KnowledgeChunker>();
builder.Services.AddSingleton<IKnowledgeIndexingJobPublisher, RabbitMqKnowledgeIndexingJobPublisher>();
builder.Services.AddSingleton<KnowledgeIndexingService>();

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
builder.Services.AddSingleton<IKnowledgeToolSetResolver, KnowledgeToolSetResolver>();

// Dedupe global do espaço de nome de tool, aplicado no ponto que une os dois
// conjuntos acima (change dedupe-global-nome-de-tool, Decisão 1) — sem estado,
// singleton como os resolvedores.
builder.Services.AddSingleton<ToolNameDeduplicator>();

// Timeout curto e fixo (design.md, Decision 3) — primeiro HttpClient nomeado
// do projeto com Timeout customizado, aditivo, sem afetar o cliente MCP.
builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<PushNotificationSender>();

// Singleton porque é injetado no TaskJobConsumer (BackgroundService, singleton) —
// AgentExecutionService não segura estado escopado diretamente, abre um
// IServiceScope novo por execução via IServiceScopeFactory quando precisa do
// AppDbContext. IChatClientResolver mantém UMA instância de IChatClient viva por
// par (provider, model) pelo resto da vida do processo, então ser singleton aqui
// é o que sustenta esse cache — ver design.md da change
// fix-vazamento-httpclient-chat. O resolver injeta só IOptions<T>, também
// singleton: não há dependência escopada capturada por singleton.
builder.Services.AddSingleton<AgentExecutionService>();
builder.Services.AddHostedService<TaskJobConsumer>();
builder.Services.AddHostedService<KnowledgeIndexingConsumer>();

// PRIMEIRO hosted service periódico deste app (os dois acima são consumidores
// RabbitMQ) — ver Diagnostics/NonTerminalTaskDetectorService.cs para o que isso
// implica na leitura da série com N instâncias. Sem stateful: o detector é
// singleton e abre escopo por ciclo.
//
// NENHUM TESTE DESTA SUÍTE PROVA ESTE REGISTRO: os testes de apps/workers montam
// o host à mão (BuildHost por classe), não pelo Program.cs — diferente de
// apps/api, onde a WebApplicationFactory roda a composição real. Remover esta
// linha deixa NonTerminalTaskDetectorTests verde e a produção sem varredura
// (design.md, D6). É conferência manual de escopo, não cobertura de teste.
builder.Services.AddSingleton<NonTerminalTaskDetector>();
builder.Services.AddHostedService<NonTerminalTaskDetectorService>();

var host = builder.Build();

host.ValidateTimeZoneConfiguration();

// Quinto caso da convenção 8, e o PRIMEIRO desta base que faz I/O no boot —
// ver o XML doc do método para o que sustenta o custo (o compose garante
// Postgres saudável e migrado antes deste processo subir) e para a mudança de
// comportamento em desenvolvimento fora do compose.
host.ValidateEmbeddingIndexConsistency();

host.Run();
