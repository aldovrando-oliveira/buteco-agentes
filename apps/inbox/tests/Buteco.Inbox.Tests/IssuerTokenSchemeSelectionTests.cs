using System.Net;
using Buteco.Inbox.Tests.Support;

namespace Buteco.Inbox.Tests;

/// <summary>
/// O que sobrou da escolha de esquema pela forma do token (design.md da change
/// validacao-jwks-inbox-connectors, D2), que saiu com o esquema antigo na change
/// remover-hmac-token-servico: um token inválido não muda a resposta de uma rota anônima.
/// O nome da classe fica, para o caso manter o nome. Mesmo cenário do <c>apps/connectors</c>.
/// </summary>
public class IssuerTokenSchemeSelectionTests(PostgresOnlyFixture postgres) : IClassFixture<PostgresOnlyFixture>
{
    [Fact]
    public async Task AnonymousRoute_WithInvalidJwt_StillAnswersByTheRoute()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        await factory.WaitForKeysAsync();

        var response = await factory.CreateClientWithToken("a.b.c").GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
