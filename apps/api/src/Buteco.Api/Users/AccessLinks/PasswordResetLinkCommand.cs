using Buteco.Api.Identity;
using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// O caminho de emergência da senha (design.md da change comando-password-reset-link, D2 a D5):
/// <c>password-reset-link --email &lt;e-mail&gt;</c>, lido dos argumentos do processo pelo
/// <c>Program.cs</c> antes de montar o host web, como o <c>two-factor-reset</c> da #220. Para o
/// último Admin que perdeu a senha, que não tem a quem pedir o link. Em produção:
/// <c>docker compose -f docker-compose.prod.yml --env-file .env.prod run --rm --no-deps api password-reset-link --email &lt;e-mail&gt;</c>.
///
/// <list type="bullet">
/// <item><b>Só quem tem o servidor:</b> não há rota HTTP, e o processo do comando não sobe
/// Kestrel, rotas nem serviços em segundo plano;</item>
/// <item><b>só Admin ativo</b> (Q2): quem não é Admin sempre tem um Admin ativo a quem pedir (a regra
/// do último Admin, change papeis-no-token, D6). Diferente do <c>two-factor-reset</c>, que aceita
/// qualquer papel: cada comando sozinho não dá acesso, mas os dois juntos fazem do link uma
/// credencial completa por 24 h (D4);</item>
/// <item><b>o efeito é o da rota do admin</b>, pela mesma escrita (<see cref="PasswordResetLink"/>);</item>
/// <item><b>a base do painel antes do banco</b> (D2): sem ela, <see cref="NoPanelBase"/> sai antes de
/// montar o host, e o link anterior continua valendo;</item>
/// <item><b>o link sai uma vez, sozinho numa linha do stdout</b>; a linha <c>Warning</c> com
/// <see cref="Event"/> vai ao stderr e nunca leva o token, o link, o hash nem o carimbo (D3). As duas
/// passam pelo driver de log do Docker: a proteção é o link morrer ao ser usado;</item>
/// <item><b>saídas:</b> <see cref="Issued"/>, <see cref="NoUser"/>, <see cref="Deactivated"/>,
/// <see cref="Invited"/>, <see cref="NotAdmin"/>, <see cref="Usage"/>, <see cref="Failure"/> e
/// <see cref="NoPanelBase"/>. O <see cref="NoUser"/> só sai de uma consulta que respondeu: uma exceção
/// é sempre <see cref="Failure"/>;</item>
/// <item><b>não aplica migrations:</b> quem migra é o serviço <c>migrator</c> do compose.</item>
/// </list>
/// </summary>
public static class PasswordResetLinkCommand
{
    public const string Name = "password-reset-link";

    public const int Issued = 0;
    public const int NoUser = 1;
    public const int Deactivated = 2;
    public const int Invited = 3;
    public const int NotAdmin = 4;
    public const int Usage = 64;
    public const int Failure = 70;

    /// <summary><c>EX_CONFIG</c> do <c>sysexits.h</c>, na família do <see cref="Usage"/> e do <see cref="Failure"/>.</summary>
    public const int NoPanelBase = 78;

    public static readonly EventId Event = new(1197, "PasswordResetLinkEmergency");

    /// <summary>A categoria do único log do processo do comando.</summary>
    public const string LogCategory = "Buteco.Api.PasswordResetLinkCommand";

    public static bool Matches(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(string[] args)
    {
        // O registro vai inteiro ao stderr: o stdout leva só o link (D3).
        using var loggerFactory = LoggerFactory.Create(logging => logging
            .AddSimpleConsole(console => console.SingleLine = true)
            .AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace)
            .SetMinimumLevel(LogLevel.Warning));
        var logger = loggerFactory.CreateLogger(LogCategory);

        if (args is not [Name, "--email", var email] || string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(Event, "password-reset-link: uso errado. Use: password-reset-link --email <e-mail>. Resultado {Resultado}.", "uso");
            return Usage;
        }

        try
        {
            var builder = Host.CreateApplicationBuilder();

            // A base pela mesma regra da rota (PanelBaseUrl), lida só da configuração, antes de montar
            // o host e de abrir conexão: sem ela, nada é escrito. Só os nomes das chaves saem.
            if (PanelBase(builder.Configuration) is not { } panelBase)
            {
                logger.LogWarning(Event,
                    "password-reset-link: a url do painel não está configurada (PublicUrl:BaseUrl ausente ou inválida, e Identity:PanelBaseUrl vazia); nada mudou. Resultado {Resultado}.",
                    "sem-url");
                return NoPanelBase;
            }

            // Só o log acima sai do processo: o do EF e o do host ficam de fora, para nenhuma
            // linha levar comando SQL ou endereço do banco.
            builder.Logging.ClearProviders();
            builder.Services.AddInfrastructure(builder.Configuration);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddScoped<PasswordResetLink>();
            using var host = builder.Build();

            await using var scope = host.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var normalizedEmail = EmailNormalization.Normalize(email);
            var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalizedEmail);

            if (user is null)
            {
                logger.LogWarning(Event, "password-reset-link: nenhum usuário com o e-mail {Email}. Resultado {Resultado}.", email, "sem-usuario");
                return NoUser;
            }

            switch (user.Status)
            {
                case UserStatus.Deactivated:
                    return LogDeactivated(logger, user.Id, email);
                case UserStatus.Invited:
                    logger.LogWarning(Event,
                        "password-reset-link: o usuário {UsuarioId} ({Email}) ainda não aceitou o convite; gere um novo link do convite em Usuários. Nada mudou. Resultado {Resultado}.",
                        user.Id, email, "convidado");
                    return Invited;
            }

            if (user.Role != UserRole.Admin)
            {
                logger.LogWarning(Event,
                    "password-reset-link: o usuário {UsuarioId} ({Email}) tem o papel {Papel}; o comando atende só Admin. Peça o link a um Admin em Editar usuário. Nada mudou. Resultado {Resultado}.",
                    user.Id, email, user.Role, "nao-admin");
                return NotAdmin;
            }

            if (await scope.ServiceProvider.GetRequiredService<PasswordResetLink>().IssueAsync(user.Id, panelBase, CancellationToken.None) is not { } issued)
            {
                // Desativado entre a leitura e a escrita.
                return LogDeactivated(logger, user.Id, email);
            }

            await Console.Out.WriteLineAsync(issued.Link);
            await Console.Out.FlushAsync();
            logger.LogWarning(Event,
                "password-reset-link: link de redefinição gerado para o usuário {UsuarioId} ({Email}, {Papel}), válido até {ExpiraEm:O}; o anterior deixou de valer. A senha atual vale até a nova ser criada. Resultado {Resultado}.",
                user.Id, email, user.Role, issued.ExpiresAt, "gerado");
            return Issued;
        }
        catch (Exception exception)
        {
            // Só o tipo: a mensagem de uma falha de conexão pode trazer o endereço do banco.
            logger.LogWarning(Event, "password-reset-link: falha ({Tipo}, causa {Causa}); nada mudou. Resultado {Resultado}.",
                exception.GetType().FullName, exception.GetBaseException().GetType().FullName, "falha");
            return Failure;
        }
    }

    private static int LogDeactivated(ILogger logger, Guid userId, string email)
    {
        logger.LogWarning(Event, "password-reset-link: o usuário {UsuarioId} ({Email}) está desativado; nada mudou. Resultado {Resultado}.",
            userId, email, "desativado");
        return Deactivated;
    }

    /// <summary>
    /// A base do painel pela regra da rota (<see cref="PanelBaseUrl"/>): <c>Identity:PanelBaseUrl</c>
    /// absoluta, senão o issuer de <c>PublicUrl:BaseUrl</c>; <c>null</c> sem nenhuma das duas.
    /// </summary>
    private static Uri? PanelBase(IConfiguration configuration)
    {
        var options = configuration.GetSection(IdentityIssuerOptions.SectionName).Get<IdentityIssuerOptions>() ?? new IdentityIssuerOptions();
        var baseUrl = configuration[$"{PublicUrlOptions.SectionName}:{nameof(PublicUrlOptions.BaseUrl)}"];
        var issuer = !string.IsNullOrWhiteSpace(baseUrl) && IdentityIssuerState.TryParseIssuer(baseUrl, out var parsed) ? parsed : null;
        if (issuer is null && !Uri.TryCreate(options.PanelBaseUrl, UriKind.Absolute, out _))
        {
            return null;
        }

        return PanelBaseUrl.Resolve(options, new IdentityIssuerState(issuer));
    }
}
