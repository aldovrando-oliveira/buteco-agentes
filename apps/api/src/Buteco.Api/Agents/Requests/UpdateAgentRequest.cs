namespace Buteco.Api.Agents.Requests;

public record UpdateAgentRequest(string? Name, string? Instructions, string? Provider, string? Model);
