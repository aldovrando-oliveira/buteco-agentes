using Buteco.Connectors.Auth;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Connectors.GoogleDrive;
using Buteco.Connectors.Endpoints;
using Buteco.Connectors.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast: sem a chave não há como validar token nenhum.
var tokenSigningKey = builder.Configuration.GetSection(TokenSigningOptions.SectionName).Get<TokenSigningOptions>()?.TokenSigningKey;
if (string.IsNullOrWhiteSpace(tokenSigningKey))
{
    throw new InvalidOperationException("Auth:TokenSigningKey não configurado.");
}

builder.Services.AddHealthChecks();
builder.Services.Configure<TokenSigningOptions>(builder.Configuration.GetSection(TokenSigningOptions.SectionName));
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton(TimeProvider.System);

// Provedores. Cada um registra os dois contratos keyed e a conta, e só quando a
// credencial está presente (design.md, D3): sem credencial, o provedor não existe
// para este processo e as rotas respondem provider-not-configured.
builder.Services.AddGoogleDriveConnector(builder.Configuration);

// Falha o boot se alguma chave tiver um dos três registros sem os outros dois
// (convenção 8, forma IServiceCollection). Precisa rodar depois de todos os
// registros de conector acima.
builder.Services.ValidateConnectorRegistrations();

builder.Services
    .AddAuthentication(OperatorTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, OperatorTokenAuthenticationHandler>(
        OperatorTokenAuthenticationHandler.SchemeName, _ => { });

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

// Convenção 8, as duas sobre o host construído e depois de todos os Map*: rota
// anônima sem classificação, e tabela de subjects divergente das rotas mapeadas
// (nos dois sentidos, design.md, D1).
app.ValidateRouteAuthenticationClassification("/health");
app.ValidateConnectorsSubjectRoutes(ConnectorsSubjectAuthorizationHandler.SubjectRoutes);

app.Run();

public partial class Program;
