using System.Net;
using System.Text.Json;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Connectors.Tests;

// connector-plugin, "Registro incompleto derruba o boot" e "Conector falso só na
// composição de teste".
public class ConnectorRegistrationTests
{
    [Fact]
    public void SemFonteDeConteudo_ReprovaNomeandoChaveEContrato()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFolderNavigator>("google-drive", new FakeConnector());
        services.AddSingleton(new ConnectorAccount("google-drive", "conta@conectores.test"));

        var exception = Assert.Throws<InvalidOperationException>(services.ValidateConnectorRegistrations);

        Assert.Contains("'google-drive'", exception.Message);
        Assert.Contains(nameof(IFolderContentSource), exception.Message);
    }

    [Fact]
    public void SemNavegador_ReprovaNomeandoChaveEContrato()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFolderContentSource>("google-drive", new FakeConnector());
        services.AddSingleton(new ConnectorAccount("google-drive", "conta@conectores.test"));

        var exception = Assert.Throws<InvalidOperationException>(services.ValidateConnectorRegistrations);

        Assert.Contains("'google-drive'", exception.Message);
        Assert.Contains(nameof(IFolderNavigator), exception.Message);
    }

    [Fact]
    public void SemConta_ReprovaNomeandoChaveEConta()
    {
        var services = new ServiceCollection();
        var fake = new FakeConnector();
        services.AddKeyedSingleton<IFolderNavigator>("google-drive", fake);
        services.AddKeyedSingleton<IFolderContentSource>("google-drive", fake);

        var exception = Assert.Throws<InvalidOperationException>(services.ValidateConnectorRegistrations);

        Assert.Contains("'google-drive'", exception.Message);
        Assert.Contains(nameof(ConnectorAccount), exception.Message);
    }

    [Fact]
    public void ContaDuplicadaParaAMesmaChave_Reprova()
    {
        var services = new ServiceCollection();
        services.AddConnector("google-drive", "a@conectores.test", _ => new FakeConnector(), _ => new FakeConnector());
        services.AddSingleton(new ConnectorAccount("google-drive", "b@conectores.test"));

        var exception = Assert.Throws<InvalidOperationException>(services.ValidateConnectorRegistrations);

        Assert.Contains("'google-drive'", exception.Message);
    }

    [Fact]
    public void RegistroCompleto_Passa()
    {
        var services = new ServiceCollection();
        services.AddConnector("google-drive", "conta@conectores.test", _ => new FakeConnector(), _ => new FakeConnector());

        Assert.Null(Record.Exception(services.ValidateConnectorRegistrations));
    }

    [Fact]
    public void NenhumConector_Passa()
    {
        Assert.Null(Record.Exception(new ServiceCollection().ValidateConnectorRegistrations));
    }

    // Convenção 8, forma IServiceCollection: os testes acima verificam a extensão; este
    // verifica a coleção REAL do Program.cs, capturada depois de todos os registros.
    [Fact]
    public async Task ComposicaoReal_SemCredencial_PassaESemChaves()
    {
        await using var factory = new ConnectorsFactory(withFakeConnector: false);
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        Assert.Null(Record.Exception(factory.CapturedServices!.ValidateConnectorRegistrations));
        Assert.Empty(factory.CapturedServices!.Where(d => d.ServiceType == typeof(ConnectorAccount)));

        var response = await client.GetAsync("/connectors/providers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ComposicaoReal_ComCredencial_TemOsTresRegistrosDoGoogle()
    {
        var key = TestServiceAccountKey.Create();
        await using var factory = new ConnectorsFactory(withFakeConnector: false, googleKeyBase64: key.Base64);
        _ = factory.CreateClient();
        var services = factory.CapturedServices!;

        Assert.Null(Record.Exception(services.ValidateConnectorRegistrations));
        Assert.Contains(services, d => d.ServiceType == typeof(IFolderNavigator) && Equals(d.ServiceKey, "google-drive"));
        Assert.Contains(services, d => d.ServiceType == typeof(IFolderContentSource) && Equals(d.ServiceKey, "google-drive"));
        Assert.Contains(services, d => d.ServiceType == typeof(ConnectorAccount) && ((ConnectorAccount)d.ImplementationInstance!).Key == "google-drive");
    }

    [Fact]
    public async Task ProducaoNaoListaOConectorFalso()
    {
        var key = TestServiceAccountKey.Create();
        await using var factory = new ConnectorsFactory(withFakeConnector: false, googleKeyBase64: key.Base64);
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var keys = JsonDocument.Parse(await client.GetStringAsync("/connectors/providers")).RootElement
            .EnumerateArray().Select(provider => provider.GetProperty("key").GetString()).ToList();

        Assert.Equal(["google-drive"], keys);
        Assert.DoesNotContain(FakeConnector.Key, keys);
    }
}
