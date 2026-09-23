using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Buteco.Api.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Support;

// Config/credenciais compartilhadas por todas as fixtures de teste desta
// classe de testes (WebApplicationFactory<Program> independentes, uma
// por cenário — mesmo padrão já existente antes desta change). Não é
// libs/ nem produção: só evita repetir a mesma geração de hash Pbkdf2
// (design.md, Decision 5 — geração real é offline, fora do código de
// produção) em cada uma das seis fixtures.
public static class TestAuthentication
{
    public const string KnownOperatorUsername = "operator";
    public const string KnownOperatorPassword = "correct-horse-battery-staple";

    // Mesmo valor usado por InboxFactoryFixture (apps/inbox) — prova que
    // um token emitido por apps/api é aceito por apps/inbox sem
    // coordenação em runtime, só pela chave compartilhada (design.md,
    // Decision 1, Risco 1).
    public const string TokenSigningKey = "test-signing-key-shared-between-api-and-inbox-fixtures";

    public static Dictionary<string, string?> ConfigOverrides { get; } = new()
    {
        ["Auth:TokenSigningKey"] = TokenSigningKey,
        ["Auth:OperatorUsername"] = KnownOperatorUsername,
        ["Auth:OperatorPasswordHash"] = ComputeKnownOperatorPasswordHash(),

        // TZ é PRÉ-REQUISITO DE BOOT desde a change rotas-de-agregacao-sistema:
        // ValidateTimeZoneConfiguration compara o valor declarado com o fuso que
        // o processo resolve, e reprova a subida se divergirem. Sem esta linha,
        // as SEIS fixtures que sobem o host real deixariam de subir — a variável
        // TZ não está definida no ambiente de quem roda a suíte.
        //
        // O valor é o fuso DA MÁQUINA, não uma constante: assim a precondição
        // vale em qualquer máquina e em CI, sem tornar a suíte dependente de o
        // desenvolvedor estar em America/Sao_Paulo. A checagem em si é
        // exercitada por TimeZoneStartupValidationTests, que fixa os dois lados.
        ["TZ"] = TimeZoneInfo.Local.Id,
    };

    // Anexado por padrão em ConfigureClient de cada fixture — os testes
    // de negócio (anteriores a esta change) não deveriam precisar saber
    // sobre token nenhum. Testes que precisam de um cenário sem token ou
    // com token inválido sobrescrevem o header explicitamente antes da
    // chamada específica.
    public static void AttachOperatorToken(HttpClient client, IServiceProvider services)
    {
        var tokenService = services.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.Issue("operator", TimeSpan.FromMinutes(30));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static string ComputeKnownOperatorPasswordHash()
    {
        const int iterations = 100_000;
        var salt = "fixed-test-salt-not-for-production"u8.ToArray();
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(KnownOperatorPassword),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
}
