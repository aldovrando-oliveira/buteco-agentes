using Buteco.Tests.Shared;

namespace Buteco.Api.Tests;

/// <summary>
/// O leitor do nginx.conf (<see cref="NginxRouting"/>, em tests/Shared, change nginx-prefixo-applications, D3), sobre
/// textos montados aqui. Só neste arquivo o nginx.conf é forjado, porque o caso é o do leitor; a cobertura de
/// verdade lê o arquivo do repositório (<see cref="NginxRouteCoverageTests"/>). Mora no projeto do apps/api só para
/// não rodar três vezes.
/// </summary>
public class NginxRoutingReaderTests
{
    private const string ApiBlock = """
        location ~ ^/(agents|users)(/|$) {
            if ($http_sec_fetch_mode = navigate) {
                rewrite ^ /index.html last;
            }
            set $api_upstream api:8080;
            proxy_pass http://$api_upstream;
        }
        """;

    [Fact]
    public void ARegexBlock_WithTheNestedIf_KeepsPrefixesUpstreamAndDeviation()
    {
        var location = Assert.Single(NginxRouting.Parse(ApiBlock));

        Assert.Equal("~", location.Modifier);
        Assert.Equal(["agents", "users"], location.Prefixes);
        Assert.Equal("api", location.Upstream);
        Assert.True(location.HasNavigationDeviation);
    }

    [Fact]
    public void ABraceInsideAComment_DoesNotEndTheBlock()
    {
        var text = """
            location ~ ^/(agents)(/|$) {
                # um comentário com { e com } no meio
                set $api_upstream api:8080;
                proxy_pass http://$api_upstream;
            }
            location / {
                try_files $uri /index.html;
            }
            """;

        var locations = NginxRouting.Parse(text);

        Assert.Equal(2, locations.Count);
        Assert.Equal("api", locations[0].Upstream);
        Assert.False(locations[0].HasNavigationDeviation);
        Assert.Null(locations[1].Upstream);
    }

    [Fact]
    public void AnExactBlock_HasNoPrefixes_AndKeepsItsPattern()
    {
        var text = """
            location = /.well-known/jwks {
                set $api_upstream api:8080;
                proxy_pass http://$api_upstream;
            }
            """;

        var location = Assert.Single(NginxRouting.Parse(text));

        Assert.Equal("=", location.Modifier);
        Assert.Equal("/.well-known/jwks", location.Pattern);
        Assert.Empty(location.Prefixes);
        Assert.Equal("api", location.Upstream);
    }

    [Fact]
    public void ABlockThatDoesNotForward_HasNoUpstream()
    {
        var text = """
            location = /index.html {
                add_header Cache-Control "no-store";
            }
            """;

        var location = Assert.Single(NginxRouting.Parse(text));

        Assert.Null(location.Upstream);
        Assert.False(location.HasNavigationDeviation);
    }

    [Theory]
    [InlineData("~ ^/(channels|contacts)/")]
    [InlineData("~ ^/channels(/|$)")]
    [InlineData("^~ /channels/")]
    [InlineData("/channels")]
    public void AForwardingBlockOutsideTheKnownShape_Throws_NamingThePattern(string head)
    {
        var text = $$"""
            location {{head}} {
                set $inbox_upstream inbox:8080;
                proxy_pass http://$inbox_upstream;
            }
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => NginxRouting.Parse(text));

        Assert.Contains(head.Split(' ')[^1], exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProxyPassWithoutTheVariableUpstream_Throws()
    {
        var text = """
            location ~ ^/(agents)(/|$) {
                proxy_pass http://api:8080;
            }
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => NginxRouting.Parse(text));

        Assert.Contains("^/(agents)(/|$)", exception.Message, StringComparison.Ordinal);
    }

    // ---- Os caminhos de navegação de topo (change nginx-retorno-federado, #285, design D3) ----

    private const string CallbackPath = "/auth/federated/callback";

    private const string AuthBlock = """
        location ~ ^/(auth)(/|$) {
            if ($http_sec_fetch_mode = navigate) {
                rewrite ^ /index.html last;
            }
            set $api_upstream api:8080;
            proxy_pass http://$api_upstream;
        }
        """;

    private static string ExactCallback(string body) => $$"""
        location = {{CallbackPath}} {
        {{body}}
        }
        {{AuthBlock}}
        """;

    [Fact]
    public void ANavigationPath_WithItsExactBlockToTheApp_WithoutTheDeviation_HasNoProblem()
    {
        var locations = NginxRouting.Parse(ExactCallback("""
                set $api_upstream api:8080;
                proxy_pass http://$api_upstream;
            """));

        Assert.Empty(NginxRouting.FindNavigationProblems([CallbackPath], locations, "api"));
    }

    [Fact]
    public void ANavigationPath_OnlyUnderTheRegexBlockWithTheDeviation_IsNamed()
    {
        var problem = Assert.Single(NginxRouting.FindNavigationProblems([CallbackPath], NginxRouting.Parse(AuthBlock), "api"));

        Assert.Contains($"'{CallbackPath}'", problem, StringComparison.Ordinal);
        Assert.Contains("não tem 'location =", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ANavigationPath_WhoseExactBlockHasTheDeviation_IsNamed()
    {
        var locations = NginxRouting.Parse(ExactCallback("""
                if ($http_sec_fetch_mode = navigate) {
                    rewrite ^ /index.html last;
                }
                set $api_upstream api:8080;
                proxy_pass http://$api_upstream;
            """));

        var problem = Assert.Single(NginxRouting.FindNavigationProblems([CallbackPath], locations, "api"));

        Assert.Contains($"'{CallbackPath}'", problem, StringComparison.Ordinal);
        Assert.Contains("desvio de navegação", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ANavigationPath_WhoseExactBlockGoesToAnotherUpstream_IsNamed()
    {
        var locations = NginxRouting.Parse(ExactCallback("""
                set $inbox_upstream inbox:8080;
                proxy_pass http://$inbox_upstream;
            """));

        var problem = Assert.Single(NginxRouting.FindNavigationProblems([CallbackPath], locations, "api"));

        Assert.Contains($"'{CallbackPath}'", problem, StringComparison.Ordinal);
        Assert.Contains("encaminha para 'inbox'", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRepositoryConfig_IsFound()
    {
        Assert.EndsWith(NginxRouting.RelativePath, NginxRouting.FindConfig().Replace('\\', '/'), StringComparison.Ordinal);
    }
}
