using Buteco.Connectors.Connectors.GoogleDrive;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

// google-drive-connector, "Credencial da service account por variável de ambiente".
public class GoogleServiceAccountKeyTests
{
    [Fact]
    public void ChaveValida_ExpoeOEmail()
    {
        var key = TestServiceAccountKey.Create();

        var parsed = GoogleServiceAccountKey.Parse(key.Base64);

        Assert.Equal(TestServiceAccountKey.ClientEmail, parsed.ClientEmail);
    }

    [Fact]
    public void ChaveValidaComQuebraDeLinhaNoBase64_EAceita()
    {
        var key = TestServiceAccountKey.Create();
        var wrapped = string.Join("\n", key.Base64.Chunk(76).Select(chunk => new string(chunk)));

        Assert.Equal(TestServiceAccountKey.ClientEmail, GoogleServiceAccountKey.Parse(wrapped).ClientEmail);
    }

    public static TheoryData<string, string> Invalidas()
    {
        var valid = TestServiceAccountKey.Create();
        return new TheoryData<string, string>
        {
            { "%%%isto-nao-e-base64%%%", "base64" },
            { TestServiceAccountKey.ToBase64("{ isto não é json"), "JSON" },
            { TestServiceAccountKey.ToBase64("[1,2]"), "JSON" },
            { TestServiceAccountKey.Create(f => f["type"] = "authorized_user").Base64, "type" },
            { TestServiceAccountKey.Create(f => f.Remove("client_email")).Base64, "client_email" },
            { TestServiceAccountKey.Create(f => f["client_email"] = "").Base64, "client_email" },
            { TestServiceAccountKey.Create(f => f.Remove("private_key")).Base64, "private_key" },
            { TestServiceAccountKey.Create(f => f["private_key"] = "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n").Base64, "private_key" },
        };
    }

    [Theory]
    [MemberData(nameof(Invalidas))]
    public void ChaveInvalida_FalhaNomeandoAVerificacaoSemOValor(string base64, string expectedMention)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GoogleServiceAccountKey.Parse(base64));

        Assert.Contains(expectedMention, exception.Message);
        Assert.DoesNotContain(base64, exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ToStringNaoExpoeNenhumCampo()
    {
        var key = TestServiceAccountKey.Create();

        var text = GoogleServiceAccountKey.Parse(key.Base64).ToString();

        Assert.DoesNotContain(key.PemBodyFragment, text);
        Assert.DoesNotContain(key.PrivateKeyId, text);
        Assert.DoesNotContain(TestServiceAccountKey.ClientEmail, text);
    }
}

// O mesmo requisito, pelo boot da aplicação.
public class GoogleDriveBootTests
{
    [Fact]
    public void BootComJsonSemChavePrivada_FalhaCitandoPrivateKeySemOValor()
    {
        var key = TestServiceAccountKey.Create(f => f.Remove("private_key"));
        using var factory = new ConnectorsFactory(withFakeConnector: false, googleKeyBase64: key.Base64);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("private_key", RouteAuthenticationTests.Flatten(exception));
        Assert.DoesNotContain(key.Base64, exception.ToString());
    }
}
