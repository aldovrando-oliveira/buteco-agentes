using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

/// <summary>
/// O conector falso com listagem e markdown configuráveis (connector-plugin, "Listagem
/// configurada pelo teste"; design.md da change ciclo-de-sincronizacao, D11).
/// </summary>
public class FakeConnectorTests
{
    [Fact]
    public async Task ListingAndMarkdown_AreWhatTheTestConfigured()
    {
        var fake = new FakeConnector
        {
            ListRoot = _ => new RootListing(
                [new RootFile("ref-a", "A.md", "v1", "text/markdown"), new RootFile("ref-b", "B", "v1", "application/vnd.google-apps.document")],
                [new IgnoredFile("ref-c", "Atalho", "shortcut-not-followed", null)]),
            Markdown = file => file.ExternalRef == "ref-b"
                ? throw new ConnectorFailure("download-blocked")
                : "# A\\n",
        };

        var listing = await fake.ListRootAsync("pasta", CancellationToken.None);
        var markdown = await fake.GetMarkdownAsync(listing.Files[0], CancellationToken.None);
        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => fake.GetMarkdownAsync(listing.Files[1], CancellationToken.None));

        Assert.Equal(["ref-a", "ref-b"], listing.Files.Select(file => file.ExternalRef));
        Assert.Equal("ref-c", Assert.Single(listing.Ignored).ExternalRef);
        Assert.Equal("# A\\n", markdown);
        Assert.Equal("download-blocked", failure.Code);
        Assert.Equal(1, fake.ListRootCalls);
        Assert.Equal(["ref-a", "ref-b"], fake.MarkdownRequests);
    }
}
