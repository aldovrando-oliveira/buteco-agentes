namespace Buteco.Inbox.Channels.Adapters.Waha;

// Serializada como JSON antes de passar pelo fluxo de criptografia já
// existente (IChannelCredentialCipher, opaco a todo o resto do sistema —
// design.md, Decision 4). ServiceUrl é a base da API do WAHA (ex.
// http://localhost:3000); SessionName é o identificador da sessão WAHA
// (campo "session" no sendText/webhook); AuthToken vai no header
// X-Api-Key das chamadas de saída.
public sealed record WahaCredential(string ServiceUrl, string SessionName, string AuthToken);
