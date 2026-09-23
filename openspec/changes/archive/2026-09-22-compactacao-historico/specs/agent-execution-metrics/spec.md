## MODIFIED Requirements

### Requirement: Linha por requisição ao provedor, com finalidade
Toda requisição ao provedor de LLM feita dentro de uma execução SHALL produzir
uma linha em `provider_calls` com o `TaskId` da execução, provedor e modelo do
client que fez a requisição, duração, tokens reportados, se falhou, e
`Purpose` igual a `Turn` para as requisições do turno do agente e `Compaction`
para a requisição de resumo do histórico. Isso SHALL valer tanto para o caminho
não-streaming quanto para o streaming do client.

`HttpStatus` SHALL ser gravado sempre que o SDK do provedor expuser o status de
forma tipada, **inclusive quando o tipo da exceção declarar o status em
propriedade própria que oculta a da classe base** — caso do SDK do Gemini, cujas
exceções derivam de `HttpRequestException` e declaram `new int StatusCode`,
deixando nula a propriedade da base. `HttpStatus` SHALL permanecer nulo apenas
quando o status realmente não estiver disponível de forma tipada, nunca por
consequência de como o SDK declarou a propriedade.

#### Scenario: Turno com resumo do histórico
- **WHEN** uma task cruza o limiar de resumo do histórico
- **THEN** existe ao menos uma linha com `Purpose = Compaction` e ao menos uma com `Purpose = Turn`, todas com o `TaskId` dela

#### Scenario: Caminho de streaming
- **WHEN** uma requisição passa pelo caminho de streaming do client dentro de uma execução e o provedor reporta uso
- **THEN** existe uma linha com os tokens reportados e a duração da enumeração inteira

#### Scenario: Requisição que falha
- **WHEN** a requisição ao provedor lança uma exceção HTTP com status tipado
- **THEN** existe uma linha com `Failed = true` e `HttpStatus` igual ao status, e a exceção continua propagando para quem chamou

#### Scenario: Exceção do SDK que oculta o status da classe base
- **WHEN** a requisição ao provedor lança uma exceção que deriva de `HttpRequestException` e declara o status em propriedade própria, deixando nula a da base
- **THEN** existe uma linha com `Failed = true` e `HttpStatus` igual ao status declarado pela própria exceção, e não nulo

#### Scenario: Exceção sem status tipado
- **WHEN** a requisição ao provedor lança uma exceção que não expõe status de forma tipada em nenhuma das duas propriedades
- **THEN** existe uma linha com `Failed = true` e `HttpStatus` nulo, sem status inventado

#### Scenario: Requisição fora de qualquer execução
- **WHEN** o client é chamado sem execução de task em andamento
- **THEN** nenhuma linha é produzida e a chamada não falha por isso
