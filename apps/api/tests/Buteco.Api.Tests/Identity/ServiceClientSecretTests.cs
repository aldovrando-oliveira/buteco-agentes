using Buteco.Api.Identity.Clients;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O formato do segredo de um cliente de serviço (design.md da change
/// clientes-servico-emissor, D2): presente, em base64, com pelo menos 32 bytes, e nenhuma
/// mensagem com o valor.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com a mensagem de tamanho interpolando o valor
/// configurado, <see cref="AShortSecret_IsRefused_NamingTheSetting_WithoutTheValue"/> reprova
/// (registro no tasks.md, 2.3).
/// </para>
/// </summary>
public class ServiceClientSecretTests
{
    private const string Name = ServiceClientSecret.InboxConfigurationName;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingSecret_IsRefused_NamingTheSetting(string? configured)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ServiceClientSecret.Decode(Name, configured));

        Assert.Contains($"{Name} não configurado", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecretOutsideBase64_IsRefused_NamingTheSetting_WithoutTheValue()
    {
        const string configured = "isto-nao-e-base64!!";

        var exception = Assert.Throws<InvalidOperationException>(() => ServiceClientSecret.Decode(Name, configured));

        Assert.Contains($"{Name} não é base64 válido", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(configured, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AShortSecret_IsRefused_NamingTheSetting_WithoutTheValue()
    {
        var configured = Convert.ToBase64String(new byte[16]);

        var exception = Assert.Throws<InvalidOperationException>(() => ServiceClientSecret.Decode(Name, configured));

        Assert.Contains($"{Name} tem 16 bytes; o mínimo é 32", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(configured, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecretOf32Bytes_IsDecoded()
    {
        var bytes = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

        Assert.Equal(bytes, ServiceClientSecret.Decode(Name, Convert.ToBase64String(bytes)));
    }
}
