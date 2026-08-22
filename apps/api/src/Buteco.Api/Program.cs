using global::A2A.AspNetCore;
using Buteco.Api.A2A;
using Buteco.Api.Agents.Endpoints;
using Buteco.Api.AgentDelegations.Endpoints;
using Buteco.Api.AgentMcpBindings.Endpoints;
using Buteco.Api.Auth;
using Buteco.Api.Auth.Endpoints;
using Buteco.Api.Infrastructure;
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
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
builder.Services.AddSingleton<ProviderCatalogService>();
builder.Services.AddSingleton<IMcpCredentialCipher, AesGcmMcpCredentialCipher>();
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

app.UseHttpsRedirection();
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
