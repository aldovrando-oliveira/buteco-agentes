namespace Buteco.Workers.Options;

/// <summary>
/// Endpoint no formato OpenAI (OpenAI, Azure OpenAI ou gateway compatível —
/// basta trocar <see cref="BaseUrl"/>/<see cref="ApiKey"/>).
/// </summary>
public sealed class ChatClientOptions
{
    public const string SectionName = "ChatClient";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4o-mini";
}
