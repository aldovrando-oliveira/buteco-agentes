using System.Text.Json.Serialization;

namespace Buteco.Inbox.Messages.Entities;

// Espelha o ciclo de vida de PendingDispatch nas mensagens de entrada que o
// integram, sobrevivendo à remoção da PendingDispatch (design.md, Decisão
// 6). Failed cobre três causas distintas que convergem na mesma consequência
// prática (nenhuma resposta virá): esgotamento de tentativas de transporte,
// rejeição síncrona do disparo, rejeição de protocolo A2A.
[JsonConverter(typeof(JsonStringEnumConverter<MessageDispatchStatus>))]
public enum MessageDispatchStatus
{
    Pending,
    Dispatching,
    Failed,
    Completed,
}
