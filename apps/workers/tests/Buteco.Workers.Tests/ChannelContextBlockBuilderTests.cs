using Buteco.Workers.Agents;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre a change inbox-contexto-canal, Tarefa 2.4 — testes unitários puros
/// do construtor do bloco de contexto de canal, sem Testcontainers:
/// <see cref="ChannelContextBlockBuilder"/> é uma função pura (design.md,
/// D5).
/// </summary>
public class ChannelContextBlockBuilderTests
{
    private const string NotAUserMessageMarker =
        "[Contexto de canal — não é uma mensagem do usuário, não responda a ele diretamente]";

    [Fact]
    public void Build_BothFieldsPresent_ProducesFullBlockWithBothLines()
    {
        var block = ChannelContextBlockBuilder.Build("waha", "5511912345678@c.us");

        Assert.Contains(NotAUserMessageMarker, block);
        Assert.Contains("Canal de origem desta conversa: waha", block);
        Assert.Contains("Identificador do contato atribuído pelo canal: 5511912345678@c.us", block);
    }

    [Fact]
    public void Build_OnlyChannelTypePresent_ProducesPartialBlockWithoutMentioningContactExternalId()
    {
        var block = ChannelContextBlockBuilder.Build("telegram", contactExternalId: null);

        Assert.Contains("Canal de origem desta conversa: telegram", block);
        // Asserção negativa (design.md, D6, caso 2; convenção 13): nenhuma
        // menção ao campo ausente, nem "desconhecido" nem placeholder — não
        // basta afirmar o que está presente, é preciso afirmar a ausência
        // do que não está.
        Assert.DoesNotContain("Identificador do contato", block);
        Assert.DoesNotContain("desconhecido", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_OnlyContactExternalIdPresent_ProducesPartialBlockWithoutMentioningChannelType()
    {
        var block = ChannelContextBlockBuilder.Build(channelType: null, "123456789");

        Assert.Contains("Identificador do contato atribuído pelo canal: 123456789", block);
        // Espelho da asserção negativa acima, para o outro campo ausente.
        Assert.DoesNotContain("Canal de origem", block);
        Assert.DoesNotContain("desconhecido", block, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("", null)]
    public void Build_BothFieldsAbsentOrEmpty_ReturnsNull(string? channelType, string? contactExternalId)
    {
        var block = ChannelContextBlockBuilder.Build(channelType, contactExternalId);

        Assert.Null(block);
    }

    [Fact]
    public void Build_ContactExternalIdText_NeverNamesItAsPhone()
    {
        var block = ChannelContextBlockBuilder.Build("telegram", "123456789");

        Assert.DoesNotContain("telefone", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Telefone", block);
    }
}
