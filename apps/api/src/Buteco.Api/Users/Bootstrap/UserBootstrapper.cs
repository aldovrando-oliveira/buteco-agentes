using System.Net.Mail;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Passwords;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Users.Bootstrap;

/// <summary>Um passo do aquecimento (design.md D3.13); trocável por DI para o teste de falha.</summary>
public interface IUserBootstrapper
{
    Task<UserBootstrapOutcome> RunAsync(CancellationToken cancellationToken);
}

public enum UserBootstrapOutcome
{
    Created,

    /// <summary>Já havia usuário: nada muda, nem senha nem e-mail.</summary>
    Ignored,

    /// <summary>Não há usuário, e falta configuração: só o aviso.</summary>
    MissingConfiguration,
}

/// <summary>
/// Cria o primeiro Admin (design.md da change usuarios-locais-login-emissor, D2), em
/// segundo plano depois do start e nunca no boot:
/// <list type="bullet">
/// <item><b>só sem nenhum usuário</b>, e nunca sobrescreve: reaplicar a configuração a cada
/// boot seria uma porta dos fundos permanente para quem tem o <c>.env</c>;</item>
/// <item><b>o hash inicial é o PBKDF2 de <c>Auth:OperatorPasswordHash</c></b>, marcado como
/// legado; o primeiro login refaz em Argon2id;</item>
/// <item><b>duas instâncias criam um só:</b> <c>pg_advisory_xact_lock</c> em
/// <see cref="AdvisoryLockKey"/> na transação da criação, e o índice único por baixo.</item>
/// </list>
/// </summary>
public sealed class UserBootstrapper(
    AppDbContext dbContext,
    IOptions<UserBootstrapOptions> options,
    TimeProvider timeProvider,
    ILogger<UserBootstrapper> logger) : IUserBootstrapper
{
    public static readonly EventId CreatedEvent = new(1188, "UserBootstrapCreated");
    public static readonly EventId IgnoredEvent = new(1189, "UserBootstrapIgnored");
    public static readonly EventId MissingConfigurationEvent = new(1190, "UserBootstrapMissingConfiguration");

    /// <summary>
    /// Chave do <c>pg_advisory_xact_lock</c> da criação. São os bytes ASCII de
    /// <c>BUTECOU1</c> lidos como inteiro big-endian, na forma de uma chave <c>bigint</c>,
    /// pelo mesmo motivo da <c>IdentityKeyRotationPolicy.AdvisoryLockKey</c>
    /// (<c>BUTECOK1</c>): o espaço de 64 bits não se sobrepõe ao de dois <c>int4</c> do
    /// <c>apps/workers</c>.
    /// </summary>
    public const long AdvisoryLockKey = 4779819235982398769;

    public async Task<UserBootstrapOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var email = options.Value.BootstrapAdminEmail.Trim();
        var hash = options.Value.OperatorPasswordHash;
        var configured = MailAddress.TryCreate(email, out var parsed) && parsed.Address == email &&
            BootstrapLegacyPasswordVerifier.IsWellFormed(hash);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(options.Value.BootstrapAdminEmail))
            {
                logger.LogInformation(IgnoredEvent, "Bootstrap do primeiro Admin ignorado: já existem usuários.");
            }

            return UserBootstrapOutcome.Ignored;
        }

        if (!configured)
        {
            logger.LogWarning(
                MissingConfigurationEvent,
                "Nenhum usuário; defina Auth:BootstrapAdminEmail e Auth:OperatorPasswordHash para criar o primeiro Admin.");
            return UserBootstrapOutcome.MissingConfiguration;
        }

        var user = User.CreateBootstrap(email, hash, timeProvider.GetUtcNow());
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(CreatedEvent, "Primeiro Admin criado pelo bootstrap, usuário {UserId}.", user.Id);
        return UserBootstrapOutcome.Created;
    }
}
