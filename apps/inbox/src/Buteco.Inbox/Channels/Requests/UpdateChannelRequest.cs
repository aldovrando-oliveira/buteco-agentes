namespace Buteco.Inbox.Channels.Requests;

// Credential é opcional na atualização: null/omitido mantém as credenciais
// já persistidas (spec: "Atualizar canal mantendo as credenciais existentes").
// ChannelType não é atualizável (ver Channel.UpdateDetails).
public record UpdateChannelRequest(string? Name, string? Credential, Guid? AgentId);
