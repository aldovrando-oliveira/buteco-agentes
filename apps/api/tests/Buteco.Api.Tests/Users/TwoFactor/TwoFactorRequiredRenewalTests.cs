using System.Net;
using System.Text.Json;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// A renovação exige a verificação em 2 etapas (spec <c>issuer-user-login</c>, "Refresh rotativo e
/// deslizante de 14 dias; todo token novo relê o usuário"; design.md da change
/// verificacao-2-etapas-obrigatoria, D4). É o que encerra as sessões abertas antes da
/// obrigatoriedade.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.2: com a condição retirada
/// do <c>UserGrantTokenHandler</c>, <see cref="Renewal_WithoutTwoFactor_IsInvalidGrant_ThePanelEndsTheSession"/> reprova.
/// </para>
/// </summary>
public class TwoFactorRequiredRenewalTests(TwoFactorRequiredRenewalTests.Fixture factory) : IClassFixture<TwoFactorRequiredRenewalTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    // O contrato que o painel lê: apps/frontend/src/auth/renewal.ts trata o 400 com o corpo JSON
    // e error = invalid_grant como sessão encerrada, e o sessionFetch leva ao /entrar com o aviso
    // de sessão expirada. Mudar o status, o Content-Type ou o valor de error aqui faz o painel
    // repetir a renovação em vez de sair.
    [Fact]
    public async Task Renewal_WithoutTwoFactor_IsInvalidGrant_ThePanelEndsTheSession()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "renova-sem");
        // A sessão de antes da obrigatoriedade: de quem não tinha a verificação.
        await TwoFactorTestClient.SeedDisabledAsync(factory.Services, account.User.Id);

        var renewal = await IdentityIssuerUserFlow.RefreshAsync(TwoFactorAccount.Anonymous(factory), account.RefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, renewal.StatusCode);
        Assert.Equal("application/json", renewal.Content.Headers.ContentType?.MediaType);
        Assert.Equal("invalid_grant", JsonDocument.Parse(await renewal.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Renewal_WithTwoFactor_Renews()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "renova-com");

        var renewal = await IdentityIssuerUserFlow.RefreshAsync(TwoFactorAccount.Anonymous(factory), account.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, renewal.StatusCode);
    }
}
