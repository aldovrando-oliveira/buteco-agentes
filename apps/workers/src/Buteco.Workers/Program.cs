using System.ClientModel;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Messaging;
using Buteco.Workers.Options;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<ChatClientOptions>(builder.Configuration.GetSection(ChatClientOptions.SectionName));

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var chatClientOptions = builder.Configuration.GetSection(ChatClientOptions.SectionName).Get<ChatClientOptions>()
        ?? new ChatClientOptions();

    var openAiClient = new OpenAIClient(
        new ApiKeyCredential(chatClientOptions.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(chatClientOptions.BaseUrl) });

    return openAiClient.GetChatClient(chatClientOptions.Model).AsIChatClient();
});

// Singleton porque é injetado no TaskJobConsumer (BackgroundService, singleton) —
// AgentExecutionService não segura estado escopado diretamente, abre um
// IServiceScope novo por execução via IServiceScopeFactory quando precisa do
// AppDbContext.
builder.Services.AddSingleton<AgentExecutionService>();
builder.Services.AddHostedService<TaskJobConsumer>();

var host = builder.Build();
host.Run();
