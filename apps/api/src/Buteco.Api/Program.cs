using global::A2A.AspNetCore;
using Buteco.Api.A2A;
using Buteco.Api.Agents.Endpoints;
using Buteco.Api.AgentDelegations.Endpoints;
using Buteco.Api.AgentKnowledgeBindings.Endpoints;
using Buteco.Api.AgentMcpBindings.Endpoints;
using Buteco.Api.Auth;
using Buteco.Api.Auth.Endpoints;
using Buteco.Api.Infrastructure;
using Buteco.Api.Insights.Endpoints;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeBases.Endpoints;
using Buteco.Api.KnowledgeDocuments.Endpoints;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeFragments.Endpoints;
using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Endpoints;
using Buteco.Api.McpServers.Security;
using Buteco.Api.Messaging;
using Buteco.Api.Options;
using Buteco.Api.Providers;
using Buteco.Api.Providers.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);
// Scoped, não o default do pacote (Singleton) — os handlers dependem de
// AppDbContext, que é Scoped; Singleton falharia a validação de DI no
// Build() (não é possível consumir um serviço Scoped a partir de um Singleton).
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<McpCryptoOptions>(builder.Configuration.GetSection(McpCryptoOptions.SectionName));
builder.Services.Configure<PublicUrlOptions>(builder.Configuration.GetSection(PublicUrlOptions.SectionName));
builder.Services.Configure<TokenSigningOptions>(builder.Configuration.GetSection(TokenSigningOptions.SectionName));
builder.Services.Configure<OperatorCredentialOptions>(builder.Configuration.GetSection(OperatorCredentialOptions.SectionName));

// Configuração da agregação de métricas. O fuso NÃO vem da seção: vem de TZ, a
// mesma variável entregue a apps/workers com :? no compose (design.md, D1). É o
// que faz os dois processos concordarem por construção — sem isso, nada impede o
// balde de sair em UTC enquanto o worker renderiza em America/Sao_Paulo, e 27,7%
// da série muda de barra em silêncio (medido em 23/09/2026, 260 linhas).
builder.Services.Configure<MetricsOptions>(options =>
{
    builder.Configuration.GetSection(MetricsOptions.SectionName).Bind(options);
    options.TimeZone = builder.Configuration["TZ"];
});

// PRIMEIRO TimeProvider de apps/api. Precedente: apps/workers/Program.cs:38.
// Registrado, e não TimeProvider.System resolvido direto no ponto de uso, por
// duas razões: a checagem de fuso valida a instância que a aplicação de fato
// usa, e a janela relativa da agregação fica verificável com FakeTimeProvider —
// resolver o "agora" com now() no SQL a tornaria intestável de forma
// determinística (design.md, D3).
//
// NÃO converte o DateTimeOffset.UtcNow que já existe nas entidades: é trabalho
// de outra change, e misturá-lo aqui tornaria este diff ilegível.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
builder.Services.AddSingleton<IKnowledgeIndexingJobPublisher, RabbitMqKnowledgeIndexingJobPublisher>();
builder.Services.AddSingleton<ProviderCatalogService>();
builder.Services.AddSingleton<IMcpCredentialCipher, AesGcmMcpCredentialCipher>();

// Extratores de conteúdo por SourceType, via DI keyed — mesmo idioma dos
// adapters de canal de apps/inbox. A completude do registro é garantida por
// ValidateKnowledgeExtractorRegistrations logo abaixo, nos dois sentidos.
builder.Services.AddKeyedSingleton<IKnowledgeSourceExtractor, MarkdownSourceExtractor>(KnowledgeSourceTypes.Markdown);
builder.Services.AddScoped<KnowledgeContentProcessor>();
builder.Services.ValidateKnowledgeExtractorRegistrations();
builder.Services.AddHttpClient(McpConnectionTester.HttpClientName);
builder.Services.AddSingleton<IMcpConnectionTester, McpConnectionTester>();
builder.Services.AddSingleton<AgentA2AServerRegistry>();
builder.Services.AddSingleton<IAgentA2AServerRegistry>(sp => sp.GetRequiredService<AgentA2AServerRegistry>());
builder.Services.AddSingleton<RoutingA2ARequestHandler>();

// Esquema único de autenticação (design.md, Decision 1/3) — aceita
// token de operador e token de serviço de apps/inbox, ambos validados
// pela mesma assinatura via ITokenService.
builder.Services
    .AddAuthentication(OperatorTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, OperatorTokenAuthenticationHandler>(
        OperatorTokenAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddSingleton<IAuthorizationHandler, ServiceScopeAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new ServiceScopeRequirement())
        .Build());

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(corsOptions.AllowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Convenção 8, e a razão de ser no boot está no XML doc da extensão: um nome de
// fuso digitado errado não falha aqui por si — ele chega intacto ao AT TIME ZONE
// da agregação e vira 500 no painel, numa requisição de operador.
//
// Diferente de apps/workers, ESTE registro É provado por teste: a suíte de
// apps/api monta o host pela WebApplicationFactory, que roda a composição real
// do Program.cs. Remover esta linha reprova TimeZoneStartupValidationTests
// .RealComposition_WithDivergentTimeZone_FailsToStart.
app.ValidateTimeZoneConfiguration();

// Sem UseHttpsRedirection: no compose de servidor (containerizacao-stack-servidor),
// apps/api só recebe tráfego HTTP puro do nginx interno do stack — TLS termina
// fora do stack (nginx/Cloudflare já existentes), sem ForwardedHeaders
// configurado. Redirecionar aqui geraria 307 permanente em todo request via
// proxy. Alinha com apps/inbox, que nunca chamou este middleware.
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health")
    .AllowAnonymous()
    .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.HealthProbe));
app.MapAuthEndpoints();
app.MapAgentEndpoints();
app.MapProviderEndpoints();
app.MapMcpServerEndpoints();
app.MapAgentMcpBindingEndpoints();
app.MapAgentDelegationEndpoints();
app.MapAgentKnowledgeBindingEndpoints();
app.MapKnowledgeBaseEndpoints();
app.MapKnowledgeDocumentEndpoints();
app.MapKnowledgeIndexEndpoints();
app.MapInsightsEndpoints();
app.MapA2A(app.Services.GetRequiredService<RoutingA2ARequestHandler>(), "/agents/{id}/a2a");
app.MapAgentCardEndpoint();

// Falha o startup se alguma rota não estiver classificada como
// autenticada (padrão) ou anônima com motivo documentado (design.md,
// Decision 4) — mesmo padrão de ValidateChannelAdapterRegistrations
// (apps/inbox). Precisa rodar depois de todos os Map* acima.
app.ValidateRouteAuthenticationClassification(
    "/health",
    "/auth/login",
    "/agents/{id:guid}/.well-known/agent-card.json");

app.Run();

public partial class Program;
