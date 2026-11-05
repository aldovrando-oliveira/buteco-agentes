using Buteco.Connectors.Auth;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Connectors.GoogleDrive;
using Buteco.Connectors.Endpoints;
using Buteco.Connectors.Options;
using Buteco.Connectors.Sync;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

// Ciclo de sincronização (design.md da change ciclo-de-sincronizacao). Api:BaseUrl é
// opcional (D2): sem ele o agendamento não é registrado e o resto do app não muda.
// Presente e inválido derruba o boot, na checagem depois do Build().
builder.Services.AddKnowledgeSync(builder.Configuration);

// Provedores. Cada um registra os dois contratos keyed e a conta, e só quando a
// credencial está presente (design.md, D3): sem credencial, o provedor não existe
// para este processo e as rotas respondem provider-not-configured.
builder.Services.AddGoogleDriveConnector(builder.Configuration);

// Falha o boot se alguma chave tiver um dos três registros sem os outros dois
// (convenção 8, forma IServiceCollection). Precisa rodar depois de todos os
// registros de conector acima.
builder.Services.ValidateConnectorRegistrations();

// O access token do emissor do apps/api, validado pelo JWKS, num esquema só
// (remover-hmac-token-servico, D1). Nenhuma requisição espera o apps/api: as chaves vêm
// em segundo plano (validacao-jwks-inbox-connectors, D4).
builder.Services.AddIssuerTokenAuthentication(builder.Configuration);

// Autenticação MAIS a tabela de subjects (design.md, D1). Só RequireAuthenticatedUser()
// é o defeito da #116: qualquer subject validamente assinado teria acesso a tudo.
builder.Services.AddSingleton<IAuthorizationHandler, ConnectorsSubjectAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new ConnectorsSubjectRequirement())
        .Build());

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.WithOrigins(corsOptions.AllowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health")
    .AllowAnonymous()
    .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.HealthProbe));
app.MapConnectorEndpoints();
app.MapSyncEndpoints();

// Convenção 8, as duas sobre o host construído e depois de todos os Map*: rota
// anônima sem classificação, e tabela de subjects divergente das rotas mapeadas
// (nos dois sentidos, design.md, D1).
app.ValidateRouteAuthenticationClassification("/health");
app.ValidateConnectorsSubjectRoutes(ConnectorsSubjectAuthorizationHandler.SubjectRoutes);

// Convenção 8, sobre o host construído: Api:BaseUrl presente e inválido derruba o boot;
// ausente sobe com aviso. O guarda da composição real está em ApiConfigurationValidationTests.
app.ValidateApiConfiguration();

// O segredo do apps/connectors no emissor (servicos-token-emissor, D5; convenção 8): com
// Api:BaseUrl, presente, base64 e 32 bytes, sem ecoar o valor.
app.ValidateServiceClientConfiguration();

// Convenção 8 (validacao-jwks-inbox-connectors, D7): issuer do emissor presente e válido,
// e Api:BaseUrl presente; e a tabela de clientes apontando só para subjects de
// SubjectRoutes. Sem Identity:Issuer, o boot falha (remover-hmac-token-servico, D2).
app.ValidateIssuerTokenConfiguration();
app.ValidateIssuerClientSubjects();

app.Run();

public partial class Program;
