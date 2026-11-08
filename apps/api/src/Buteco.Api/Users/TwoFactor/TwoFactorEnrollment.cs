using Buteco.Api.Infrastructure;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// O cadastro da verificação em 2 etapas (design.md da change verificacao-2-etapas-obrigatoria,
/// D1, D2 e D5), usado pelas rotas do cadastro no login. É o único caminho de cadastro desde a
/// #220: as rotas por access token de <c>/account/two-factor</c> saíram.
///
/// <list type="bullet">
/// <item><b>Prazo:</b> 10 minutos do primeiro <c>setup</c> do cookie. Um <c>setup</c> seguinte troca o
/// segredo e mantém o fim do prazo; o início gravado no banco é o fim menos 10 minutos, para o
/// pendente vencer no mesmo instante que o cookie;</item>
/// <item><b>Confirmação:</b> o segredo pendente vira o ativo, o pendente é apagado, o passo do
/// código é gravado como o último aceito e os 10 códigos de recuperação são gerados, numa
/// transação.</item>
/// </list>
/// </summary>
public sealed class TwoFactorEnrollment(
    AppDbContext dbContext,
    TwoFactorSecretCipher cipher,
    TwoFactorVerifier verifier,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public sealed record Started(string Segredo, string OtpauthUri, DateTimeOffset Deadline);

    /// <summary>
    /// Grava um segredo pendente novo. <paramref name="deadline"/> é o fim do prazo já gravado no
    /// cookie (um <c>setup</c> seguinte), ou nulo no primeiro, que abre o prazo agora.
    /// </summary>
    public async Task<Started> StartAsync(User user, DateTimeOffset? deadline, CancellationToken cancellationToken)
    {
        var end = deadline ?? timeProvider.GetUtcNow() + Lifetime;
        var secret = Totp.NewSecret();
        user.StartTwoFactorEnrollment(cipher.Protect(user.Id, secret), end - Lifetime);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new Started(Base32.Encode(secret), OtpAuthUri.Build(user.Email, secret), end);
    }

    /// <summary>O cadastro pendente existe e não venceu, pela regra do tempo e não por limpeza.</summary>
    public bool IsPendingLive(User user) =>
        user.TwoFactorPendingAt is { } startedAt && timeProvider.GetUtcNow() - startedAt <= Lifetime;

    /// <summary>O passo do código para o segredo pendente, ou nulo.</summary>
    public long? Match(User user, string? code) => verifier.MatchPending(user, code);

    /// <summary>Liga a verificação com o passo confirmado e devolve os 10 códigos de recuperação, uma vez.</summary>
    public async Task<IReadOnlyList<string>> CompleteAsync(User user, long confirmedStep, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        user.EnableTwoFactor(timeProvider.GetUtcNow(), confirmedStep);
        await dbContext.SaveChangesAsync(cancellationToken);
        var codes = await verifier.ReplaceRecoveryCodesAsync(user.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return codes;
    }
}
