using System.Text;

namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// Erro do Google para código (design.md, D5). Decide pelo <c>reason</c>; o status só
/// desempata quando o corpo não traz <c>reason</c>. <c>accessNotConfigured</c>,
/// <c>insufficientFilePermissions</c>, cota e <c>cannotExportFile</c> voltam todos
/// <c>403</c> e pedem ações opostas.
/// </summary>
public static class GoogleDriveErrorMapper
{
    public static ConnectorFailure Map(GoogleApiError error, GoogleErrorTarget target, string accountEmail)
    {
        var accessDenied = new ConnectorFailure(ConnectorCodes.AccessDenied, accountEmail);
        var notFound = target == GoogleErrorTarget.Folder ? accessDenied : new ConnectorFailure(ConnectorCodes.FileNotFound);

        switch (error.Reason)
        {
            // "File not found" é a resposta para sem acesso E para inexistente, e a conta
            // não distingue os dois. Na pasta, o caso real é não ter sido compartilhada.
            case "notFound":
                return notFound;
            case "insufficientFilePermissions":
                return accessDenied;
            case "accessNotConfigured":
                return new ConnectorFailure(ConnectorCodes.ApiNotConfigured);
            case "userRateLimitExceeded" or "rateLimitExceeded":
                return new ConnectorFailure(ConnectorCodes.RateLimited);
            case "cannotExportFile":
                return new ConnectorFailure(ConnectorCodes.DownloadBlocked);
            case "authError":
                return new ConnectorFailure(ConnectorCodes.ProviderAuthFailed);
        }

        return error.Status switch
        {
            0 or >= 500 => new ConnectorFailure(ConnectorCodes.ProviderUnavailable),
            404 when error.Reason is null => notFound,
            401 => new ConnectorFailure(ConnectorCodes.ProviderAuthFailed),
            429 => new ConnectorFailure(ConnectorCodes.RateLimited),
            _ => new ConnectorFailure(ConnectorCodes.ProviderError, ReasonAsCode(error.Reason)),
        };
    }

    /// <summary>
    /// <c>badRequest</c> vira <c>bad-request</c>; o que não casar com o formato de
    /// código vira nulo. Nada além do <c>reason</c> sai do corpo do Google.
    /// </summary>
    internal static string? ReasonAsCode(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var c in reason)
        {
            if (char.IsUpper(c) && builder.Length > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        var code = builder.ToString();
        return ConnectorCodes.IsValid(code) ? code : null;
    }
}
