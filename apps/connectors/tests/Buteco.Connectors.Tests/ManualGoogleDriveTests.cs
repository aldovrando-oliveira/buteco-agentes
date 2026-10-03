using Buteco.Connectors.Connectors;
using Buteco.Connectors.Connectors.GoogleDrive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Buteco.Connectors.Tests;

/// <summary>
/// Pulado sem as duas variáveis de ambiente. Roda contra o Drive real (somente
/// leitura) as duas operações que não têm rota nesta change (tarefa 8.3, design.md,
/// D11). Não imprime conteúdo de arquivo nem a chave: só nomes, códigos, tamanhos, MD5
/// e o tempo de cada entrega.
/// </summary>
public sealed class ManualDriveFactAttribute : FactAttribute
{
    public ManualDriveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GoogleDrive__ServiceAccountKeyBase64")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CONNECTORS_MANUAL_FOLDER_ID")))
        {
            Skip = "Manual: defina GoogleDrive__ServiceAccountKeyBase64 e CONNECTORS_MANUAL_FOLDER_ID.";
        }
    }
}

[Trait("Category", "Manual")]
public class ManualGoogleDriveTests(ITestOutputHelper output)
{
    [ManualDriveFact]
    public async Task ListarARaizEEntregarOMarkdown_NoDriveReal()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton(TimeProvider.System);
        services.AddGoogleDriveConnector(configuration);
        await using var provider = services.BuildServiceProvider();
        var source = provider.GetRequiredKeyedService<IFolderContentSource>(GoogleDriveRegistrationExtensions.ProviderKey);

        var listing = await source.ListRootAsync(Environment.GetEnvironmentVariable("CONNECTORS_MANUAL_FOLDER_ID")!, CancellationToken.None);

        foreach (var file in listing.Files)
        {
            output.WriteLine($"suportado: {file.Name} | {file.MimeType} | versão {file.ExternalVersion}");
        }

        foreach (var ignored in listing.Ignored)
        {
            output.WriteLine($"ignorado: {ignored.Name} | {ignored.Code} | {ignored.Detail}");
        }

        foreach (var file in listing.Files)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var markdown = await source.GetMarkdownAsync(file, CancellationToken.None);
                var bytes = System.Text.Encoding.UTF8.GetBytes(markdown);
                var md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes)).ToLowerInvariant();
                output.WriteLine($"markdown: {file.Name} | {bytes.Length} B | md5 {md5} | data:image presente: {markdown.Contains("data:image", StringComparison.Ordinal)} | {started.Elapsed.TotalSeconds:F1} s");
            }
            catch (ConnectorFailure failure)
            {
                output.WriteLine($"markdown: {file.Name} | falhou com {failure.Code} | {started.Elapsed.TotalSeconds:F1} s");
            }
        }
    }
}
