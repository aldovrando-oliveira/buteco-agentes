using System.Text.RegularExpressions;

namespace Buteco.Connectors.Connectors;

/// <summary>
/// Os códigos de motivo deste app, no formato que o <c>apps/api</c> aceita (D1 da
/// change catalogo-base-sincronizada, <c>SyncCode</c>): <c>^[a-z0-9]+(-[a-z0-9]+)*</c>
/// até 64 caracteres. O texto exibido é do frontend.
/// </summary>
public static partial class ConnectorCodes
{
    public const string ProviderNotConfigured = "provider-not-configured";
    public const string AccessDenied = "access-denied";
    public const string ApiNotConfigured = "api-not-configured";
    public const string RateLimited = "rate-limited";
    public const string DownloadBlocked = "download-blocked";
    public const string FileNotFound = "file-not-found";
    public const string ProviderAuthFailed = "provider-auth-failed";
    public const string ProviderUnavailable = "provider-unavailable";
    public const string ProviderError = "provider-error";
    public const string NotAFolder = "not-a-folder";
    public const string FolderTrashed = "folder-trashed";
    public const string UnsupportedType = "unsupported-type";
    public const string ShortcutNotFollowed = "shortcut-not-followed";
    public const string SubfolderNotSynced = "subfolder-not-synced";

    public const int MaxLength = 64;

    // \z e não $: em .NET o $ casa antes de um \n final (achado da #102, SyncCode).
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex Format();

    public static bool IsValid(string? code) =>
        code is { Length: > 0 and <= MaxLength } && Format().IsMatch(code);
}
