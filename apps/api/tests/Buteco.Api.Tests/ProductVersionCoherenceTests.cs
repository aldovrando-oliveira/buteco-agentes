using System.Text.Json;
using System.Xml.Linq;

namespace Buteco.Api.Tests;

/// <summary>
/// O produto é um entregável só e tem UMA versão, a mesma em todos os componentes (convenção 26 do
/// 01-ARQUITETURA_E_CONVENCOES.md, "Fechamento de versão"). Ela mora em dois arquivos: a <c>&lt;Version&gt;</c> do
/// <c>Directory.Build.props</c> da raiz, que vale para todos os projetos .NET, e o <c>apps/frontend/package.json</c>.
/// Este teste reprova se os dois divergirem, se algum <c>.csproj</c> definir versão própria por cima do props, ou se
/// algum <c>package.json</c> do repositório tiver outra versão. Lê os arquivos do repositório, como os testes do
/// nginx, e mora no projeto do apps/api só para não rodar três vezes. O AgentCard da A2A tem versão própria (a do
/// agente para o protocolo, não a de um componente) e fica de fora.
/// </summary>
public class ProductVersionCoherenceTests
{
    private const string PackageJsonPath = "apps/frontend/package.json";
    private const string BuildPropsPath = "Directory.Build.props";

    // As propriedades que dão versão a um assembly. Qualquer uma num .csproj fura a versão única do props.
    private static readonly string[] VersionProperties =
        ["Version", "VersionPrefix", "VersionSuffix", "AssemblyVersion", "FileVersion", "InformationalVersion"];

    // Fora da varredura: dependências e saídas de build, e as pastas ocultas (.git, e .claude, onde moram as
    // worktrees, que são cópias do repositório em outra branch).
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.Ordinal) { "node_modules", "bin", "obj" };

    [Fact]
    public void FrontendPackageVersion_EqualsDotnetBuildPropsVersion()
    {
        var root = FindRepositoryRoot();

        var packageVersion = ReadPackageJsonVersion(Path.Combine(root, PackageJsonPath));
        var propsVersion = ReadBuildPropsVersion(root);

        Assert.True(
            string.Equals(packageVersion, propsVersion, StringComparison.Ordinal),
            $"A versão do produto diverge: {PackageJsonPath} diz \"{packageVersion}\" e a <Version> do " +
            $"{BuildPropsPath} diz \"{propsVersion}\". O PR de release muda os dois juntos.");
    }

    [Fact]
    public void NoCsproj_DefinesItsOwnVersion()
    {
        var root = FindRepositoryRoot();

        var projects = EnumerateRepositoryFiles(root, "*.csproj").ToList();
        Assert.NotEmpty(projects);

        var violations = projects
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => VersionProperties.Contains(element.Name.LocalName, StringComparer.Ordinal))
                .Select(element => $"{Relative(root, path)}: <{element.Name.LocalName}>{element.Value.Trim()}</{element.Name.LocalName}>"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"O produto tem uma versão só, a do {BuildPropsPath}; nenhum .csproj define a sua. Encontrado:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void EveryPackageJson_HasTheProductVersion()
    {
        var root = FindRepositoryRoot();
        var propsVersion = ReadBuildPropsVersion(root);

        var packages = EnumerateRepositoryFiles(root, "package.json").ToList();
        Assert.Contains(packages, path => Relative(root, path) == PackageJsonPath);

        var violations = packages
            .Select(path => (Path: Relative(root, path), Version: TryReadPackageJsonVersion(path)))
            .Where(package => !string.Equals(package.Version, propsVersion, StringComparison.Ordinal))
            .Select(package => package.Version is null
                ? $"{package.Path}: sem \"version\""
                : $"{package.Path}: \"{package.Version}\"")
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Todo package.json do repositório segue a versão do produto, \"{propsVersion}\" (a <Version> do " +
            $"{BuildPropsPath}). Diverge:\n" + string.Join("\n", violations));
    }

    private static string ReadPackageJsonVersion(string path) =>
        TryReadPackageJsonVersion(path) ?? throw new InvalidOperationException($"{PackageJsonPath} sem \"version\" de texto.");

    private static string? TryReadPackageJsonVersion(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
            ? version.GetString()
            : null;
    }

    private static string ReadBuildPropsVersion(string root)
    {
        var versions = XDocument.Load(Path.Combine(root, BuildPropsPath)).Descendants("Version")
            .Select(element => element.Value.Trim())
            .ToList();
        if (versions.Count != 1)
        {
            throw new InvalidOperationException(
                $"{BuildPropsPath} precisa de exatamente uma <Version>; tem {versions.Count}.");
        }

        return versions[0];
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string root, string pattern)
    {
        var pending = new Stack<DirectoryInfo>([new DirectoryInfo(root)]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in dir.EnumerateFiles(pattern))
            {
                yield return file.FullName;
            }

            foreach (var child in dir.EnumerateDirectories())
            {
                if (!child.Name.StartsWith('.') && !SkippedDirectories.Contains(child.Name))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Sobe de <see cref="AppContext.BaseDirectory"/> até a pasta que tem os dois arquivos. Não achar é falha, nunca "pulado".</summary>
    private static string FindRepositoryRoot()
    {
        var start = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, BuildPropsPath))
                && File.Exists(Path.Combine(dir.FullName, PackageJsonPath)))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"{BuildPropsPath} e {PackageJsonPath} não encontrados subindo de '{start}'.");
    }
}
