using Buteco.Api.Infrastructure;
using Buteco.Api.Users.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// O caminho de emergência das 2 etapas (design.md da change verificacao-2-etapas-obrigatoria, D6):
/// <c>two-factor-reset --email &lt;e-mail&gt;</c>, lido dos argumentos do processo pelo
/// <c>Program.cs</c> antes de montar o host web. Para quem perdeu o celular e os códigos,
/// inclusive o último Admin. Em produção:
/// <c>docker compose -f docker-compose.prod.yml --env-file .env.prod run --rm --no-deps api two-factor-reset --email &lt;e-mail&gt;</c>.
///
/// <list type="bullet">
/// <item><b>Só quem tem o servidor:</b> não há rota HTTP, e o processo do comando não sobe
/// Kestrel, rotas nem serviços em segundo plano. Quem roda o comando já lê o <c>.env.prod</c>;</item>
/// <item><b>o efeito é o da rota do admin</b>, pela mesma implementação (<see cref="TwoFactorReset"/>);</item>
/// <item><b>saídas:</b> <see cref="ResetDone"/>, <see cref="NoUser"/>, <see cref="Deactivated"/>,
/// <see cref="NotEnabled"/>, <see cref="Usage"/> e <see cref="Failure"/>. O <see cref="NoUser"/> só
/// sai de uma consulta que respondeu: uma exceção é sempre <see cref="Failure"/>, nunca um código
/// de resultado (decisão do dono, 08/11/2026);</item>
/// <item><b>registro:</b> uma linha <c>Warning</c> com <see cref="Event"/>, o resultado, o e-mail
/// pedido, o id do usuário e as sessões encerradas. Nenhum segredo, código, hash ou stamp; na
/// falha, só o tipo do erro, nunca a mensagem, que pode trazer o endereço do banco;</item>
/// <item><b>não aplica migrations:</b> quem migra é o serviço <c>migrator</c> do compose.</item>
/// </list>
/// </summary>
public static class TwoFactorEmergencyCommand
{
    public const string Name = "two-factor-reset";

    public const int ResetDone = 0;
    public const int NoUser = 1;
    public const int Deactivated = 2;
    public const int NotEnabled = 3;
    public const int Usage = 64;
    public const int Failure = 70;

    public static readonly EventId Event = new(1196, "TwoFactorEmergencyReset");

    /// <summary>A categoria do único log do processo do comando.</summary>
    public const string LogCategory = "Buteco.Api.TwoFactorEmergencyCommand";

    public static bool Matches(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging
            .AddSimpleConsole(console => console.SingleLine = true)
            .SetMinimumLevel(LogLevel.Warning));
        var logger = loggerFactory.CreateLogger(LogCategory);

        if (args is not [Name, "--email", var email] || string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(Event, "two-factor-reset: uso errado. Use: two-factor-reset --email <e-mail>. Resultado {Resultado}.", "uso");
            return Usage;
        }

        try
        {
            var builder = Host.CreateApplicationBuilder();
            // Só o log acima sai do processo: o do EF e o do host ficam de fora, para nenhuma
            // linha levar comando SQL ou endereço do banco.
            builder.Logging.ClearProviders();
            builder.Services.AddInfrastructure(builder.Configuration);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddScoped<UserSessions>();
            builder.Services.AddScoped<TwoFactorReset>();
            using var host = builder.Build();

            await using var scope = host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var normalizedEmail = EmailNormalization.Normalize(email);
            var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail);

            if (user is null)
            {
                logger.LogWarning(Event, "two-factor-reset: nenhum usuário com o e-mail {Email}. Resultado {Resultado}.", email, "sem-usuario");
                return NoUser;
            }

            if (user.Status == UserStatus.Deactivated)
            {
                logger.LogWarning(Event, "two-factor-reset: o usuário {UsuarioId} ({Email}) está desativado; nada mudou. Resultado {Resultado}.",
                    user.Id, email, "desativado");
                return Deactivated;
            }

            if (!user.IsTwoFactorEnabled)
            {
                logger.LogWarning(Event, "two-factor-reset: o usuário {UsuarioId} ({Email}) não tem a verificação em 2 etapas; nada mudou. Resultado {Resultado}.",
                    user.Id, email, "sem-verificacao");
                return NotEnabled;
            }

            var ended = await scope.ServiceProvider.GetRequiredService<TwoFactorReset>().ResetAsync(user, CancellationToken.None);
            logger.LogWarning(Event,
                "two-factor-reset: verificação em 2 etapas redefinida para o usuário {UsuarioId} ({Email}); {Sessoes} sessões encerradas. O próximo login pede o cadastro. Resultado {Resultado}.",
                user.Id, email, ended, "redefinida");
            return ResetDone;
        }
        catch (Exception exception)
        {
            // Só o tipo: a mensagem de uma falha de conexão pode trazer o endereço do banco.
            logger.LogWarning(Event, "two-factor-reset: falha ({Tipo}, causa {Causa}); nada mudou. Resultado {Resultado}.",
                exception.GetType().FullName, exception.GetBaseException().GetType().FullName, "falha");
            return Failure;
        }
    }
}
