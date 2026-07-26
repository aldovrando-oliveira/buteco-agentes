using global::A2A.AspNetCore;
using Buteco.Api.A2A;
using Buteco.Api.Agents.Endpoints;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Buteco.Api.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);
// Scoped, não o default do pacote (Singleton) — os handlers dependem de
// AppDbContext, que é Scoped; Singleton falharia a validação de DI no
// Build() (não é possível consumir um serviço Scoped a partir de um Singleton).
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
builder.Services.AddSingleton<AgentA2AServerRegistry>();
builder.Services.AddSingleton<IAgentA2AServerRegistry>(sp => sp.GetRequiredService<AgentA2AServerRegistry>());
builder.Services.AddSingleton<RoutingA2ARequestHandler>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapHealthChecks("/health");
app.MapAgentEndpoints();
app.MapA2A(app.Services.GetRequiredService<RoutingA2ARequestHandler>(), "/agents/{id}/a2a");

app.Run();

public partial class Program;
