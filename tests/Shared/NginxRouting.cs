using System.Text.RegularExpressions;

namespace Buteco.Tests.Shared;

// O leitor do apps/frontend/deploy/nginx.conf e a comparação dos blocos dele com as rotas de um app
// (change nginx-prefixo-applications, #268, design D2 a D4).
//
// Mora em tests/Shared/ e entra nos projetos de teste do apps/api, do apps/inbox e do apps/connectors por
// <Compile Include … Link>, como o tests/ApiConnectorsRoundTrip.Tests já faz com arquivos de teste do
// apps/connectors. Três cópias derivariam (uma correção do leitor numa delas deixaria as outras duas lendo o
// mesmo arquivo de outro jeito), e libs/ é para código de produção. Este arquivo não referencia nenhum app:
// recebe os padrões de rota como texto, e cada teste passa os do próprio host construído.
//
// O leitor entende só a forma de hoje do nginx.conf, e falha alto fora dela: um bloco que encaminha com outra
// forma faz o Parse lançar, nomeando o padrão, em vez de virar um bloco de zero prefixos que ninguém confere.
public static class NginxRouting
{
    public const string RelativePath = "apps/frontend/deploy/nginx.conf";

    // Os três upstreams do stack (docker-compose.prod.yml). Bloco que encaminha para outro host reprova em todos
    // os três testes: é como nenhum prefixo do arquivo fica sem dono conferido.
    public static readonly IReadOnlySet<string> KnownUpstreams = new HashSet<string>(StringComparer.Ordinal)
    {
        "api",
        "inbox",
        "connectors",
    };

    public sealed record Location(string Modifier, string Pattern, IReadOnlyList<string> Prefixes, string? Upstream, bool HasNavigationDeviation)
    {
        public string Display => Modifier.Length == 0 ? $"location {Pattern}" : $"location {Modifier} {Pattern}";
    }

    private static readonly Regex LocationHead = new(@"location\s+(?:(=|~\*|\^~|~)\s+)?(\S+)\s*\{", RegexOptions.CultureInvariant);
    private static readonly Regex PrefixPattern = new(@"^\^/\(([a-z0-9-]+(?:\|[a-z0-9-]+)*)\)\(/\|\$\)$", RegexOptions.CultureInvariant);
    private static readonly Regex UpstreamSet = new(@"set\s+\$([a-z]+)_upstream\s+([A-Za-z0-9.-]+):(\d+)\s*;", RegexOptions.CultureInvariant);
    private static readonly Regex ProxyPass = new(@"proxy_pass\s+http://\$([a-z]+)_upstream\s*;", RegexOptions.CultureInvariant);
    private static readonly Regex NavigationDeviation = new(
        @"if\s*\(\s*\$http_sec_fetch_mode\s*=\s*navigate\s*\)\s*\{\s*rewrite\s+\^\s+/index\.html\s+last\s*;\s*\}",
        RegexOptions.CultureInvariant);

    /// <summary>Sobe de <see cref="AppContext.BaseDirectory"/> até a pasta que tem o nginx.conf. Não achar é falha, nunca "pulado".</summary>
    public static string FindConfig()
    {
        var start = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, RelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"{RelativePath} não encontrado subindo de '{start}'.");
    }

    public static IReadOnlyList<Location> ParseFile() => Parse(File.ReadAllText(FindConfig()));

    public static IReadOnlyList<Location> Parse(string text)
    {
        var withoutComments = string.Join('\n', text.Split('\n').Select(line =>
        {
            var hash = line.IndexOf('#');
            return hash < 0 ? line : line[..hash];
        }));

        var locations = new List<Location>();
        foreach (Match head in LocationHead.Matches(withoutComments))
        {
            var modifier = head.Groups[1].Value;
            var pattern = head.Groups[2].Value;
            var body = BlockBody(withoutComments, head.Index + head.Length - 1, pattern);

            string? upstream = null;
            if (ProxyPass.Match(body) is { Success: true } pass)
            {
                var set = UpstreamSet.Matches(body).FirstOrDefault(m => m.Groups[1].Value == pass.Groups[1].Value)
                    ?? throw new InvalidOperationException(
                        $"bloco '{pattern}': proxy_pass por ${pass.Groups[1].Value}_upstream sem o set correspondente.");
                upstream = set.Groups[2].Value;
            }
            else if (body.Contains("proxy_pass", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"bloco '{pattern}': proxy_pass fora da forma 'http://$<app>_upstream' que o leitor entende.");
            }

            IReadOnlyList<string> prefixes = [];
            if (upstream is not null)
            {
                switch (modifier)
                {
                    case "~":
                        var prefixMatch = PrefixPattern.Match(pattern);
                        if (!prefixMatch.Success)
                        {
                            throw new InvalidOperationException(
                                $"bloco de regex '{pattern}' encaminha para '{upstream}' fora da forma ^/(a|b|…)(/|$) que o leitor entende.");
                        }

                        prefixes = prefixMatch.Groups[1].Value.Split('|');
                        break;
                    case "=":
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"bloco '{pattern}' (modificador '{modifier}') encaminha para '{upstream}': só '~ ^/(…)(/|$)' e '=' são entendidos.");
                }
            }

            locations.Add(new Location(modifier, pattern, prefixes, upstream, NavigationDeviation.IsMatch(body)));
        }

        return locations;
    }

    // O corpo do bloco, da chave que abre até a que fecha, contando as chaves: o if do desvio tem as dele.
    private static string BlockBody(string text, int openBrace, string pattern)
    {
        var depth = 0;
        for (var i = openBrace; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[(openBrace + 1)..i];
                    }

                    break;
            }
        }

        throw new InvalidOperationException($"bloco '{pattern}' sem a chave que o fecha.");
    }

    /// <summary>
    /// As divergências entre as rotas de um app e os blocos do nginx que encaminham para ele, nos dois sentidos
    /// (spec server-deployment, "Nginx do stack roteia por prefixo"). Lista vazia é cobertura completa.
    /// </summary>
    /// <param name="routePatterns">O RawText de cada RouteEndpoint do host construído do app.</param>
    /// <param name="upstream">O host do upstream do app no nginx: api, inbox ou connectors.</param>
    /// <param name="servedWithoutBlock">Primeiros segmentos servidos que de propósito não têm bloco, com o motivo.</param>
    /// <param name="exactBlocks">Padrões dos blocos exatos (location =) que encaminham para o app, com o motivo.</param>
    /// <param name="requiredUris">URIs que têm de chegar ao app sem rota mapeada (os endpoints do OpenIddict).</param>
    public static IReadOnlyList<string> FindProblems(
        IEnumerable<string> routePatterns,
        IReadOnlyList<Location> locations,
        string upstream,
        IReadOnlyDictionary<string, string> servedWithoutBlock,
        IReadOnlyDictionary<string, string> exactBlocks,
        IEnumerable<string>? requiredUris = null)
    {
        var problems = new List<string>();
        var app = $"apps/{upstream}";

        var served = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pattern in routePatterns)
        {
            var segment = pattern.TrimStart('/').Split('/')[0];
            if (segment.Length == 0 || segment.Contains('{') || segment.Contains('*'))
            {
                problems.Add($"rota '{pattern}' do {app} sem primeiro segmento literal: o nginx não tem como rotear por prefixo.");
                continue;
            }

            served.Add(segment);
        }

        foreach (var location in locations.Where(l => l.Upstream is not null && !KnownUpstreams.Contains(l.Upstream)))
        {
            problems.Add($"{location.Display} encaminha para o upstream desconhecido '{location.Upstream}'.");
        }

        foreach (var repeated in locations.SelectMany(l => l.Prefixes).GroupBy(p => p, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"prefixo '{repeated.Key}' repetido em mais de um bloco de regex: o nginx usa a primeira regex que casa.");
        }

        var regexBlocks = locations.Where(l => l.Upstream == upstream && l.Modifier == "~").ToList();
        if (regexBlocks.Count == 0)
        {
            problems.Add($"nenhum bloco de regex encaminha para '{upstream}'.");
        }

        var blockPrefixes = new SortedSet<string>(regexBlocks.SelectMany(l => l.Prefixes), StringComparer.Ordinal);

        foreach (var segment in served.Where(s => !servedWithoutBlock.ContainsKey(s) && !blockPrefixes.Contains(s)))
        {
            problems.Add($"prefixo servido pelo {app} sem bloco no nginx: '{segment}' (cai no fallback do SPA, 200 com HTML).");
        }

        foreach (var prefix in blockPrefixes.Where(p => !served.Contains(p)))
        {
            problems.Add($"prefixo do nginx que o {app} não serve: '{prefix}'.");
        }

        foreach (var exception in servedWithoutBlock.Keys)
        {
            if (!served.Contains(exception))
            {
                problems.Add($"exceção '{exception}' (servida, sem bloco) que o {app} não serve mais.");
            }

            if (locations.Any(l => l.Prefixes.Contains(exception)))
            {
                problems.Add($"exceção '{exception}' (servida, sem bloco) tem bloco no nginx: está nos dois lados.");
            }
        }

        foreach (var block in regexBlocks.Where(l => !l.HasNavigationDeviation))
        {
            problems.Add($"{block.Display} encaminha para '{upstream}' sem o desvio de navegação (Sec-Fetch-Mode: navigate).");
        }

        var exactOfApp = locations.Where(l => l.Upstream == upstream && l.Modifier == "=").ToList();
        foreach (var block in exactOfApp.Where(l => !exactBlocks.ContainsKey(l.Pattern)))
        {
            problems.Add($"{block.Display} encaminha para '{upstream}' e não está entre as exceções nomeadas.");
        }

        foreach (var exception in exactBlocks.Keys)
        {
            var block = exactOfApp.FirstOrDefault(l => l.Pattern == exception);
            if (block is null)
            {
                problems.Add($"exceção 'location = {exception}' sem o bloco exato correspondente no nginx.");
            }
            else if (block.HasNavigationDeviation)
            {
                problems.Add($"exceção 'location = {exception}' tem o desvio de navegação, e os blocos exatos não o levam.");
            }
        }

        foreach (var uri in requiredUris ?? [])
        {
            var segment = uri.TrimStart('/').Split('/')[0];
            if (!exactOfApp.Any(l => l.Pattern == uri) && !blockPrefixes.Contains(segment))
            {
                problems.Add($"'{uri}' não chega ao {app}: nem bloco exato, nem prefixo '{segment}' num bloco de regex dele.");
            }
        }

        return problems;
    }
}
