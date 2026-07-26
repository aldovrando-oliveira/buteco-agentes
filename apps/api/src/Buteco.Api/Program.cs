using global::A2A.AspNetCore;
using Buteco.Api.A2A;
using Buteco.Api.Agents.Endpoints;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Buteco.Api.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
builder.Services.AddSingleton<AgentA2AServerRegistry>();
builder.Services.AddSingleton<RoutingA2ARequestHandler>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapHealthChecks("/health");
app.MapAgentEndpoints();
app.MapA2A(app.Services.GetRequiredService<RoutingA2ARequestHandler>(), "/agents/{id}/a2a");

app.Run();

public partial class Program;
