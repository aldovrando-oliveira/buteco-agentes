using System.Collections.Concurrent;
using Buteco.Api.Messaging;
using Buteco.Api.Providers.Vendors;
using Buteco.ProviderCatalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O host da curadoria de modelos (change modelos-curados-por-provedor): Postgres efêmero
/// migrado com a carga inicial, usuários com papel (para o Admin, o Operador e o rebaixado), o
/// publicador de jobs falso (para afirmar "não publicou"), um fornecedor falso por provedor e a
/// captura de TODO o log do host, em qualquer categoria e nível (para afirmar que a chave não
/// aparece em linha nenhuma, R2).
///
/// <para>
/// As chaves dos provedores ficam como o <see cref="ApiFactoryFixture"/> deixa: só a OpenAI
/// configurada (<c>appsettings.Development.json</c>). Os testes ligam e desligam a da Anthropic e
/// a do Gemini em <see cref="IConfiguration"/>, como <c>SendMessageProviderRejectionTests</c>,
/// e chamam <see cref="ResetKeys"/> no fim.
/// </para>
/// </summary>
public class ModelCurationFixture : LocalUsersFixture
{
    public const string OpenAiKey = "chave-openai-de-teste-0123456789";
    public const string AnthropicKey = "chave-anthropic-de-teste-0123456789";
    public const string GeminiKey = "chave-gemini-de-teste-0123456789";

    public FakeTaskJobPublisher TaskJobPublisher { get; } = new();

    public FakeVendorModelsHttpMessageHandler OpenAi { get; } = new();
    public FakeVendorModelsHttpMessageHandler Anthropic { get; } = new();
    public FakeVendorModelsHttpMessageHandler Gemini { get; } = new();

    public AllLogCapture Logs { get; } = new();

    public FakeVendorModelsHttpMessageHandler Vendor(string provider) => provider switch
    {
        LlmProviders.OpenAi => OpenAi,
        LlmProviders.Anthropic => Anthropic,
        LlmProviders.Gemini => Gemini,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public int VendorRequestCount => OpenAi.RequestCount + Anthropic.RequestCount + Gemini.RequestCount;

    public IConfiguration Configuration => Services.GetRequiredService<IConfiguration>();

    public void SetKeys(bool openAi = true, bool anthropic = false, bool gemini = false)
    {
        Configuration["OpenAI:ApiKey"] = openAi ? OpenAiKey : string.Empty;
        Configuration["Anthropic:ApiKey"] = anthropic ? AnthropicKey : string.Empty;
        Configuration["Gemini:ApiKey"] = gemini ? GeminiKey : string.Empty;
    }

    public void ResetKeys() => SetKeys();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = OpenAiKey,
            ["OpenAI:BaseUrl"] = "https://gateway-compativel.test/v1",
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITaskJobPublisher>();
            services.AddSingleton<ITaskJobPublisher>(TaskJobPublisher);

            services.AddHttpClient(VendorHttpClients.Name(LlmProviders.OpenAi)).ConfigurePrimaryHttpMessageHandler(() => OpenAi);
            services.AddHttpClient(VendorHttpClients.Name(LlmProviders.Anthropic)).ConfigurePrimaryHttpMessageHandler(() => Anthropic);
            services.AddHttpClient(VendorHttpClients.Name(LlmProviders.Gemini)).ConfigurePrimaryHttpMessageHandler(() => Gemini);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(Logs);
        });
    }
}

/// <summary>Todo o log do host, de qualquer categoria e nível, com a exceção formatada.</summary>
public sealed class AllLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public void Clear() => _lines.Clear();

    ILogger ILoggerProvider.CreateLogger(string categoryName) => new SinkLogger(_lines, categoryName);

    void IDisposable.Dispose()
    {
    }

    private sealed class SinkLogger(ConcurrentQueue<string> lines, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}
