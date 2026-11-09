## ADDED Requirements

### Requirement: 403 renova a sessão uma vez e não repete a chamada
Quando uma chamada pelo `sessionFetch` responde `403`, com ou sem corpo, o painel SHALL pedir uma renovação, sob o lock da renovação, com o access token que recebeu o `403`, e SHALL devolver à tela **o mesmo** `403`, sem repetir a chamada. A renovação existe para o id token trazer o papel gravado.

- Um `403` recebido com um access token que já pediu renovação por `403` SHALL NOT pedir outra.
- A renovação que termina em sessão encerrada (`invalid_grant`, ou registro ausente) SHALL levar a `/entrar?motivo=sessao-expirada`, como no `401`.
- A renovação indisponível SHALL NOT mudar o que a tela recebe.
- Nenhum corpo de pedido, inclusive a senha de quem chama, SHALL ser reenviado por causa de um `403`.

#### Scenario: Um 403, uma renovação, nenhuma repetição
- **WHEN** `POST /agents/a1/deactivate` responde `403`
- **THEN** o `/connect/token` recebe um pedido de renovação, a rota recebe um pedido só, e a tela recebe o `403`

#### Scenario: Dois 403 seguidos, uma renovação
- **WHEN** a sessão renova por um `403`, e duas chamadas seguintes, com o access token novo, respondem `403`
- **THEN** o `/connect/token` recebe uma renovação só depois da primeira, e nenhuma por causa das duas seguintes

#### Scenario: A senha não é reenviada
- **WHEN** `POST /users/invitations` com a senha de quem chama responde `403`
- **THEN** a rota recebe um pedido só, e o corpo com a senha não sai de novo

#### Scenario: invalid_grant na renovação leva ao /entrar
- **WHEN** uma chamada responde `403`, e a renovação responde `400` com `error: invalid_grant`
- **THEN** o registro é apagado, e o painel vai a `/entrar?motivo=sessao-expirada`

#### Scenario: A renovação traz o papel novo
- **WHEN** um Admin rebaixado a Operador recebe `403` em `GET /users`, e a renovação traz um id token com `role` `operator`
- **THEN** a barra lateral perde a seção "Acesso" sem recarregar

## MODIFIED Requirements

### Requirement: 503 é indisponibilidade passageira e nunca desloga
Nenhum `503` de nenhum dos três apps SHALL apagar o registro nem levar a `/entrar`. A chamada SHALL falhar com um erro que traga o `Retry-After` lido do cabeçalho.

O `QueryClient` do painel SHALL repetir consulta que falhou com `503` até 3 vezes, esperando o `Retry-After` limitado a 1–30 segundos (1 segundo sem cabeçalho). Mutação SHALL NOT ser repetida. Consulta que falhou com `403` SHALL NOT ser repetida: o papel não muda entre as tentativas. Os outros erros SHALL manter a repetição de antes, e consulta com `retry` próprio SHALL NOT mudar.

#### Scenario: Consulta com 503 repete esperando o Retry-After
- **WHEN** uma consulta responde `503` com `Retry-After: 2` e depois `200`
- **THEN** a segunda tentativa sai 2 segundos depois da primeira, e a tela mostra o dado

#### Scenario: Mutação com 503 não repete
- **WHEN** uma mutação responde `503`
- **THEN** só um pedido é feito, a tela mostra o erro, e o registro continua

#### Scenario: Retry-After fora da faixa é limitado
- **WHEN** uma consulta responde `503` com `Retry-After: 120`
- **THEN** a tentativa seguinte espera 30 segundos

#### Scenario: Consulta com 403 não repete
- **WHEN** uma consulta responde `403`
- **THEN** só um pedido é feito à rota, e a tela mostra o estado do `403` sem esperar novas tentativas
