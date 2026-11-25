using Buteco.Api.Infrastructure;
using Buteco.Api.Providers.Entities;
using Buteco.Api.Providers.Responses;
using Buteco.Api.Providers.Vendors;
using Buteco.Api.Users;
using Buteco.ProviderCatalog;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.Providers.Endpoints;

/// <summary>
/// A curadoria de modelos por provedor (design.md da change modelos-curados-por-provedor, D6;
/// spec <c>llm-model-curation</c>). Só o Admin, pela <c>RoleRoutes</c> e pelo papel
/// <b>gravado</b> (<see cref="CurrentUser.FindAdminAsync"/>), como <c>/applications</c>: retirar
/// um modelo faz agentes de toda a instalação recusarem tarefas.
///
/// <list type="bullet">
/// <item><c>GET /providers/models</c>: a lista curada dos três provedores, com a contagem de
/// agentes; nunca consulta fornecedor;</item>
/// <item><c>GET /providers/{provider}/vendor-models</c>: as opções do fornecedor, na hora;</item>
/// <item><c>POST /providers/{provider}/models</c>: habilita só o que o fornecedor listou;</item>
/// <item><c>DELETE /providers/{provider}/models?model=</c>: retira, sem consultar fornecedor e
/// com ou sem a chave. O id vai na query porque id de gateway compatível pode ter <c>/</c>.</item>
/// </list>
///
/// <para>
/// Sem senha de quem chama (decisão do dono de 19/11/2026): curar modelo não toca credencial e
/// se desfaz habilitando de novo; a confirmação de retirar é na linha, com a contagem.
/// </para>
/// </summary>
public static class ProviderCurationEndpoints
{
    public const string ModelsPath = "/providers/models";
    public const string VendorModelsPath = "/providers/{provider}/vendor-models";
    public const string ProviderModelsPath = "/providers/{provider}/models";

    public static IEndpointRouteBuilder MapProviderCurationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ModelsPath, ListCuratedAsync);
        app.MapGet(VendorModelsPath, ListVendorModelsAsync);
        app.MapPost(ProviderModelsPath, EnableAsync);
        app.MapDelete(ProviderModelsPath, RetireAsync);

        return app;
    }

    private static async Task<IResult> ListCuratedAsync(
        HttpContext httpContext,
        AppDbContext dbContext,
        ProviderCatalogService catalog,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        var enabled = await catalog.GetEnabledModelsAsync(cancellationToken);

        var counts = await dbContext.Agents
            .AsNoTracking()
            .Where(agent => agent.Provider != null && agent.Model != null)
            .GroupBy(agent => new { agent.Provider, agent.Model })
            .Select(group => new { group.Key.Provider, group.Key.Model, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var countByPair = counts.ToDictionary(row => (row.Provider!, row.Model!), row => row.Count);

        var response = LlmProviders.All
            .Select(provider => new CuratedProviderResponse(
                provider.Id,
                catalog.IsProviderConfigured(provider.Id),
                ProviderCatalogService.ModelsOf(enabled, provider.Id)
                    .Select(model => new CuratedModelResponse(model, countByPair.GetValueOrDefault((provider.Id, model))))
                    .ToList()))
            .ToList();

        return TypedResults.Ok(response);
    }

    private static async Task<IResult> ListVendorModelsAsync(
        string provider,
        HttpContext httpContext,
        AppDbContext dbContext,
        ProviderCatalogService catalog,
        VendorModelCatalog vendors,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (ProviderRefusal(provider, catalog) is { } refused)
        {
            return refused;
        }

        var listing = await vendors.ListAsync(provider, cancellationToken);
        if (listing.Failure is { } failure)
        {
            return ProviderCurationErrors.VendorFailure(failure);
        }

        var enabled = (await catalog.GetEnabledModelsAsync(cancellationToken))
            .Where(pair => pair.Provider == provider)
            .Select(pair => pair.Model)
            .ToHashSet(StringComparer.Ordinal);

        return TypedResults.Ok(new VendorModelsResponse(
            provider,
            listing.Filter,
            listing.Models.Select(model => new VendorModelResponse(model.Id, model.DisplayName, enabled.Contains(model.Id))).ToList()));
    }

    /// <summary>A ordem das conferências é a da D6 e do requisito "Habilitar só o que o fornecedor listou".</summary>
    private static async Task<IResult> EnableAsync(
        string provider,
        EnableModelRequest? body,
        HttpContext httpContext,
        AppDbContext dbContext,
        ProviderCatalogService catalog,
        VendorModelCatalog vendors,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        // 1.
        var model = body?.Model?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            return ProviderCurationErrors.ModelRequired();
        }

        // 2.
        if (!IsKnownProvider(provider))
        {
            return ProviderCurationErrors.ProviderNotFound();
        }

        // 3. O estado pedido já existe: nada a gravar, e a resposta só afirma a lista curada,
        // que não depende da chave.
        if (await IsEnabledAsync(dbContext, provider, model, cancellationToken))
        {
            return TypedResults.Ok(new EnabledModelResponse(provider, model));
        }

        // 4.
        if (!catalog.IsProviderConfigured(provider))
        {
            return ProviderCurationErrors.ProviderNotConfigured();
        }

        // 5.
        var listing = await vendors.ListAsync(provider, cancellationToken);
        if (listing.Failure is { } failure)
        {
            return ProviderCurationErrors.VendorFailure(failure);
        }

        // 6. O servidor impõe "sem digitação livre": só o que o fornecedor listou agora.
        if (!listing.Models.Any(listed => string.Equals(listed.Id, model, StringComparison.Ordinal)))
        {
            return ProviderCurationErrors.ModelNotListed();
        }

        // 7.
        dbContext.EnabledLlmModels.Add(new EnabledLlmModel(provider, model));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPrimaryKeyViolation(exception))
        {
            // Corrida de dois POST do mesmo par (D6, R13): o outro gravou entre o passo 3 e
            // este. O estado pedido é o estado que existe: 200, como o passo 3, e nunca 500.
            return TypedResults.Ok(new EnabledModelResponse(provider, model));
        }

        return TypedResults.Created(ModelsPath, new EnabledModelResponse(provider, model));
    }

    private static async Task<IResult> RetireAsync(
        string provider,
        string? model,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        var trimmed = model?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return ProviderCurationErrors.ModelRequired();
        }

        if (!IsKnownProvider(provider))
        {
            return ProviderCurationErrors.ProviderNotFound();
        }

        var removed = await dbContext.EnabledLlmModels
            .Where(enabled => enabled.Provider == provider && enabled.Model == trimmed)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == 0 ? ProviderCurationErrors.ModelNotEnabled() : TypedResults.NoContent();
    }

    private static IResult? ProviderRefusal(string provider, ProviderCatalogService catalog)
    {
        if (!IsKnownProvider(provider))
        {
            return ProviderCurationErrors.ProviderNotFound();
        }

        return catalog.IsProviderConfigured(provider) ? null : ProviderCurationErrors.ProviderNotConfigured();
    }

    private static bool IsKnownProvider(string provider) => LlmProviders.All.Any(definition => definition.Id == provider);

    private static Task<bool> IsEnabledAsync(AppDbContext dbContext, string provider, string model, CancellationToken cancellationToken) =>
        dbContext.EnabledLlmModels.AsNoTracking().AnyAsync(enabled => enabled.Provider == provider && enabled.Model == model, cancellationToken);

    private static bool IsPrimaryKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        postgres.ConstraintName == AppDbContext.EnabledLlmModelsPrimaryKeyName;
}
