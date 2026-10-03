using Microsoft.Extensions.Options;

namespace Buteco.Api.KnowledgeSync.Connectors;

/// <summary>
/// Checagem de integridade no startup de <c>Connectors:BaseUrl</c> (convenção 8;
/// design.md da change criacao-base-sincronizada, D1), chamada depois do
/// <c>Build()</c>, como <c>ValidateTimeZoneConfiguration</c>.
///
/// <para>
/// <b>Ausente ou vazio sobe</b>, com um aviso: até a #119 é o estado de produção, e
/// quem lê o log precisa saber por que o cadastro de base sincronizada está fechado.
/// <b>Presente e inválido derruba o boot</b>: é erro de digitação, e subir faria toda
/// base sincronizada ser recusada com um código que diz "não configurado" quando está
/// configurado errado. A mensagem nomeia a chave e não ecoa o valor.
/// </para>
/// </summary>
public static class ConnectorsConfigurationValidation
{
    public static void ValidateConnectorsConfiguration(this IHost host)
    {
        var options = host.Services.GetRequiredService<IOptions<ConnectorsOptions>>().Value;
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ConnectorsConfigurationValidation));

        if (!options.IsConfigured)
        {
            logger.LogWarning(
                "Connectors:BaseUrl não configurado: o cadastro de base sincronizada será recusado com {Code}. As demais rotas não dependem dele.",
                ConnectorsFailureCodes.NotConfigured);
            return;
        }

        if (!IsAbsoluteHttpUri(options.BaseUrl!))
        {
            throw new InvalidOperationException(
                "Connectors:BaseUrl inválido: o valor precisa ser uma URI absoluta http ou https " +
                "(por exemplo http://localhost:5037), ou ficar vazio para subir sem o apps/connectors.");
        }

        logger.LogInformation("apps/connectors configurado para a validação de pasta de base sincronizada.");
    }

    internal static bool IsAbsoluteHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
