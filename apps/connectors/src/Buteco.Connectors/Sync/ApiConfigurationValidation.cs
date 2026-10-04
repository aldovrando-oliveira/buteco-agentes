using Buteco.Connectors.Options;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Sync;

/// <summary>
/// Checagem de integridade no startup de <c>Api:BaseUrl</c> (convenção 8; design.md da
/// change ciclo-de-sincronizacao, D2), chamada depois do <c>Build()</c>. Molde de
/// <c>ConnectorsConfigurationValidation</c> do <c>apps/api</c>.
///
/// <para>
/// <b>Ausente ou vazio sobe</b>, com um aviso: a navegação e a descrição de pasta não
/// dependem do <c>apps/api</c>, e quem lê o log precisa saber por que nenhuma base é
/// sincronizada. <b>Presente e inválido derruba o boot</b>: é erro de digitação, e subir
/// deixaria o ciclo desligado sem ninguém notar. A mensagem nomeia a chave e não ecoa o
/// valor.
/// </para>
/// </summary>
public static class ApiConfigurationValidation
{
    public static void ValidateApiConfiguration(this IHost host)
    {
        var options = host.Services.GetRequiredService<IOptions<ApiOptions>>().Value;
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ApiConfigurationValidation));

        if (!options.IsConfigured)
        {
            logger.LogWarning(
                "Api:BaseUrl não configurado: o ciclo de sincronização não roda, e \"Sincronizar agora\" responde {Code}. A navegação e a descrição de pasta não dependem dele.",
                SyncCodes.NotConfigured);
            return;
        }

        if (!IsAbsoluteHttpUri(options.BaseUrl!))
        {
            throw new InvalidOperationException(
                "Api:BaseUrl inválido: o valor precisa ser uma URI absoluta http ou https, com esquema e host, " +
                "ou ficar vazio para subir sem o ciclo de sincronização.");
        }

        logger.LogInformation("apps/api configurado: o ciclo de sincronização roda.");
    }

    internal static bool IsAbsoluteHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
