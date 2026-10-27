using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Buteco.Api.Identity.Storage;

/// <summary>Um ciclo da limpeza, chamado pelo <see cref="IdentityTokenPruneService"/> e direto pelos testes.</summary>
public interface IIdentityTokenPruner
{
    Task<IdentityTokenPruneResult> RunCycleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Remove as linhas mortas de <c>openiddict_tokens</c> e depois as de
/// <c>openiddict_authorizations</c> (design.md da change limpeza-tokens-openiddict, D3 e
/// D5), pelo critério do próprio OpenIddict (<c>PruneAsync</c> dos dois managers).
///
/// <list type="bullet">
/// <item><b>Uma instância por ciclo:</b> <c>pg_try_advisory_xact_lock</c> na chave
/// <see cref="IdentityTokenPrunePolicy.AdvisoryLockKey"/>. Quem não consegue o lock não
/// espera: a outra instância está removendo as mesmas linhas;</item>
/// <item><b>o lock fica noutra conexão</b>, numa transação que não escreve nada. Dentro
/// dela, os lotes de 1.000 do <c>PruneAsync</c> virariam uma transação só, do tamanho da
/// limpeza inteira. O rollback no fim solta o lock; se o processo morre, a conexão fecha
/// e o Postgres solta;</item>
/// <item><b>tokens antes de autorizações:</b> a autorização só sai sem token;</item>
/// <item><b>o limite sai do <see cref="TimeProvider"/></b>; o "vencido" do critério dos
/// tokens é o relógio do Postgres, dentro do <c>DELETE</c> (D3, "Dois relógios").</item>
/// </list>
/// </summary>
public sealed class IdentityTokenPruner(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : IIdentityTokenPruner
{
    public async Task<IdentityTokenPruneResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        await using var lockScope = scopeFactory.CreateAsyncScope();
        var lockContext = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var lockTransaction = await lockContext.Database.BeginTransactionAsync(cancellationToken);

        // Forma bigint, espaço de chave separado do (int, int) do apps/workers (D5).
        var acquired = await lockContext.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({IdentityTokenPrunePolicy.AdvisoryLockKey}) AS \"Value\"")
            .SingleAsync(cancellationToken);

        if (!acquired)
        {
            return IdentityTokenPruneResult.Skipped;
        }

        try
        {
            var threshold = timeProvider.GetUtcNow() - IdentityTokenPrunePolicy.MinimumAge;

            await using var pruneScope = scopeFactory.CreateAsyncScope();
            var tokens = await pruneScope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>()
                .PruneAsync(threshold, cancellationToken);
            var authorizations = await pruneScope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>()
                .PruneAsync(threshold, cancellationToken);

            return IdentityTokenPruneResult.Pruned(tokens, authorizations);
        }
        finally
        {
            await lockTransaction.RollbackAsync(CancellationToken.None);
        }
    }
}

/// <summary>O que um ciclo fez: pulou por não ter o lock, ou removeu tantas linhas de cada tabela.</summary>
public sealed record IdentityTokenPruneResult(bool WasSkipped, long Tokens, long Authorizations)
{
    public static readonly IdentityTokenPruneResult Skipped = new(WasSkipped: true, Tokens: 0, Authorizations: 0);

    public static IdentityTokenPruneResult Pruned(long tokens, long authorizations) =>
        new(WasSkipped: false, tokens, authorizations);
}
