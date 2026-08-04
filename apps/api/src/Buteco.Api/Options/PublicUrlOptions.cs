namespace Buteco.Api.Options;

public sealed class PublicUrlOptions
{
    public const string SectionName = "PublicUrl";

    public string BaseUrl { get; set; } = "";
}
