## 1. Atributo de serialização nos enums (apps/inbox)

- [x] 1.1 Adicionar `[JsonConverter(typeof(JsonStringEnumConverter<MessageDirection>))]`
      em `Messages/Entities/MessageDirection.cs`.
- [x] 1.2 Adicionar `[JsonConverter(typeof(JsonStringEnumConverter<MessageContentType>))]`
      em `Messages/Entities/MessageContentType.cs`.
- [x] 1.3 Adicionar `[JsonConverter(typeof(JsonStringEnumConverter<MessageDeliveryStatus>))]`
      em `Messages/Entities/MessageDeliveryStatus.cs`.
- [x] 1.4 Adicionar `[JsonConverter(typeof(JsonStringEnumConverter<MessageDispatchStatus>))]`
      em `Messages/Entities/MessageDispatchStatus.cs`.

## 2. Teste do formato de fio (apps/inbox)

- [x] 2.1 Adicionar teste que consulta `GET /sessions/{id}/messages` para
      uma `Session` com **uma mensagem de entrada e uma de saída** na
      mesma resposta (necessário para os quatro campos aparecerem não
      nulos ao mesmo tempo — `DeliveryStatus` é sempre nulo em mensagem
      de entrada, `DispatchStatus` é sempre nulo em mensagem de saída) e
      lê a resposta como JSON bruto (`ReadAsStringAsync`/`JsonDocument`,
      não `ReadFromJsonAsync<MessageResponse>`). Afirmar, para a
      mensagem de entrada, que `Direction`, `ContentType` e
      `DispatchStatus` são exatamente as strings esperadas (ex.
      `"Inbound"`, `"Text"`, `"Failed"`); para a de saída, que
      `Direction` e `DeliveryStatus` são exatamente as strings esperadas
      (ex. `"Outbound"`, `"Sent"`) — comparação por igualdade de string,
      não "não é número". Este é o teste que expõe o formato real
      produzido pelo servidor, em vez de fazer round-trip pelo mesmo
      tipo C#.
- [x] 2.2 Adicionar teste equivalente para `GET /channels/{channelId}/sessions`:
      canal com uma sessão cuja última mensagem é de entrada, lendo a
      resposta como JSON bruto e afirmando que o campo de direção dentro
      da prévia (`LastMessage.Direction`) é exatamente a string
      `"Inbound"`, não um valor numérico — o enum atravessa também esta
      rota (`ChannelSessionResponse` → `MessagePreviewResponse`), não só
      `GET /sessions/{id}/messages`.

## 3. Validação

- [x] 3.1 Rodar a suíte de testes de `apps/inbox` (`dotnet test`) e
      confirmar que passam, incluindo os testes novos de 2.1 e 2.2.
- [x] 3.2 Rodar `openspec validate inbox-enums-json-string --strict` e
      revisar a saída.
