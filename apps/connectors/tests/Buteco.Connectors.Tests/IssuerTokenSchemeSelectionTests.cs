using System.Net;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

/// <summary>
/// O que sobrou da escolha de esquema pela forma do token (design.md da change
/// validacao-jwks-inbox-connectors, D2), que saiu com o esquema antigo na change
/// remover-hmac-token-servico: um token inválido não muda a resposta de uma rota anônima.
/// O nome da classe fica, para o caso manter o nome. Mesmo cenário do <c>apps/inbox</c>.
/// </summary>
public class IssuerTokenSchemeSelectionTests
{
    [Fact]
    public async Task AnonymousRoute_WithInvalidJwt_StillAnswersByTheRoute()
    {
        await using var factory = new ConnectorsFactory(issuer: new TestIssuer());
        await factory.WaitForIssuerKeysAsync();

        var response = await factory.CreateClientWithToken("a.b.c").GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
