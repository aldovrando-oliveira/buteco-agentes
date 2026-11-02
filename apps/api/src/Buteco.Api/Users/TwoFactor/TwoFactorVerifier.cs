using System.Security.Cryptography;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.TwoFactor;

public enum TwoFactorCheck
{
    Accepted,
    Rejected,

    /// <summary>O segredo gravado não decifra com a chave configurada (D5).</summary>
    SecretUnreadable,
}

/// <summary>
/// Confere o código de quem tem a verificação ligada (design.md da change
/// verificacao-2-etapas-totp, D3, D5 e D6). A forma decide: 6 dígitos é o código do app, 16
/// caracteres de Crockford é um código de recuperação.
///
/// <list type="bullet">
/// <item><b>Uso único atômico.</b> Aceitar o código do app é gravar o passo por
/// <c>UPDATE … WHERE two_factor_last_step IS NULL OR two_factor_last_step &lt; @passo</c>, e
/// aceitar um de recuperação é marcar <c>used_at</c> por <c>UPDATE … WHERE used_at IS NULL</c>.
/// Uma linha afetada é aceite; duas submissões simultâneas aceitam uma só (tasks.md, 2.3).</item>
/// <item><b>Chave trocada.</b> A decifração fica dentro do <c>try</c> (convenção 4): o código do
/// app vira <see cref="TwoFactorCheck.SecretUnreadable"/>, com log de erro sem o segredo, e os
/// códigos de recuperação, que são hash, continuam valendo.</item>
/// </list>
/// </summary>
public sealed class TwoFactorVerifier(
    AppDbContext dbContext,
    TwoFactorSecretCipher cipher,
    TimeProvider timeProvider,
    ILogger<TwoFactorVerifier> logger)
{
    public static readonly EventId SecretUnreadableEvent = new(1194, "TwoFactorSecretUnreadable");

    public async Task<TwoFactorCheck> VerifyAsync(User user, string? input, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        if (Totp.Normalize(input) is { } code)
        {
            if (user.TwoFactorSecret is not { } stored)
            {
                return TwoFactorCheck.Rejected;
            }

            long? step;
            try
            {
                step = Totp.Match(cipher.Unprotect(user.Id, stored), code, now);
            }
            catch (CryptographicException exception)
            {
                logger.LogError(SecretUnreadableEvent, exception,
                    "O segredo do TOTP do usuário {UserId} não decifra com Identity:TwoFactorEncryptionKey. A chave configurada não é a que cifrou; os códigos de recuperação continuam valendo, e a redefinição pelo admin apaga o segredo.",
                    user.Id);
                return TwoFactorCheck.SecretUnreadable;
            }

            if (step is null)
            {
                return TwoFactorCheck.Rejected;
            }

            var accepted = await dbContext.Users
                .Where(candidate => candidate.Id == user.Id &&
                                    (candidate.TwoFactorLastStep == null || candidate.TwoFactorLastStep < step))
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.TwoFactorLastStep, step), cancellationToken);
            return accepted == 1 ? TwoFactorCheck.Accepted : TwoFactorCheck.Rejected;
        }

        if (RecoveryCode.Normalize(input) is { } recoveryCode)
        {
            var hash = RecoveryCode.Hash(recoveryCode);
            var used = await dbContext.UserRecoveryCodes
                .Where(candidate => candidate.UserId == user.Id && candidate.CodeHash == hash && candidate.UsedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.UsedAt, now), cancellationToken);
            return used == 1 ? TwoFactorCheck.Accepted : TwoFactorCheck.Rejected;
        }

        return TwoFactorCheck.Rejected;
    }

    /// <summary>O passo do código para o segredo do cadastro pendente, ou nulo (D8). Sem gravar nada.</summary>
    public long? MatchPending(User user, string? input)
    {
        if (Totp.Normalize(input) is not { } code || user.TwoFactorPendingSecret is not { } stored)
        {
            return null;
        }

        return Totp.Match(cipher.Unprotect(user.Id, stored), code, timeProvider.GetUtcNow());
    }

    /// <summary>Apaga os códigos de recuperação do usuário e grava 10 novos; devolve os códigos em texto, uma vez.</summary>
    public async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await dbContext.UserRecoveryCodes.Where(code => code.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        var codes = RecoveryCode.GenerateSet();
        var now = timeProvider.GetUtcNow();
        dbContext.UserRecoveryCodes.AddRange(codes.Select(code => UserRecoveryCode.Create(userId, code, now)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return codes;
    }
}
