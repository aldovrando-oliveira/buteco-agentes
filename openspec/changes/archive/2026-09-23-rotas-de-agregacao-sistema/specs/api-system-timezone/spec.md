## ADDED Requirements

### Requirement: `apps/api` recebe o fuso do sistema e o lê como configuração

`apps/api` SHALL receber o fuso horário do sistema pela variável de ambiente
`TZ`, com o **mesmo nome IANA canônico** entregue a `apps/workers`, declarado uma
única vez e propagado aos dois serviços com `:?` no compose — de modo que um
valor ausente impeça a subida de qualquer um dos dois, e que nenhuma réplica
possa responder com um fuso diferente de outra.

O fuso usado para agrupar dados por dia local SHALL ser lido dessa configuração,
e NÃO SHALL ser obtido de `TimeZoneInfo.Local`, de `CultureInfo.CurrentCulture`,
de cabeçalho de requisição nem de parâmetro de rota. O nome do fuso SHALL usar o
identificador IANA canônico sem o prefixo POSIX `:`.

Esta decisão **contraria a letra** de uma decisão registrada — "o nome vem de
configuração explícita da agregação, nunca do `TZ` do processo de `apps/api`" — e
SHALL preservar a razão dela: como o `:?` garante o mesmo valor em toda réplica,
o balde continua não dependendo de qual container respondeu.

#### Scenario: O fuso da agregação vem da configuração, não do relógio do processo

- **WHEN** o código de agregação precisa converter um instante para dia local
- **THEN** o nome do fuso vem da configuração do sistema, e nenhuma chamada a
  `TimeZoneInfo.Local` ou `CultureInfo.CurrentCulture` participa da conversão

#### Scenario: `TZ` ausente impede a subida do serviço

- **WHEN** o compose é executado sem `TZ` definida
- **THEN** o serviço `api` não sobe, pela mesma interpolação `:?` que já protege
  `workers`, e a mensagem nomeia a variável e o formato esperado

### Requirement: O nome de fuso inválido reprova o boot, não a primeira consulta

`apps/api` SHALL validar, no startup e depois do `Build()`, que o nome de fuso
configurado **resolve** — e SHALL falhar a inicialização com
`InvalidOperationException` quando não resolver, sem modo de tolerância e sem
bypass (convenção 8).

A checagem existe porque um nome digitado errado não produz erro de configuração:
ele chega intacto até o `AT TIME ZONE` da consulta e falha ali, como **`500` no
painel**, em uma requisição de operador — muito depois e muito mais longe da
causa do que o boot.

A checagem SHALL registrar, ao passar, o fuso resolvido e o offset em vigor, para
que a ausência de falha seja distinguível da ausência da própria checagem.

#### Scenario: Nome de fuso que não resolve derruba o boot

- **WHEN** o fuso configurado é um nome que a tz database não conhece (por
  exemplo `Amrica/Sao_Paulo`, com um erro de digitação)
- **THEN** a aplicação não sobe, e a exceção nomeia o valor recebido e o formato
  esperado

#### Scenario: Nome de fuso válido sobe e se anuncia

- **WHEN** o fuso configurado é um nome IANA canônico válido
- **THEN** a aplicação sobe e registra uma linha com o fuso resolvido e o offset
  atual

#### Scenario: A checagem roda na composição real

- **WHEN** a chamada da checagem é removida do `Program.cs` e a configuração está
  divergente
- **THEN** existe teste que reprova — a verificação não depende apenas de exercitar
  a extensão isoladamente

### Requirement: `TimeProvider` é o ponto de acesso a relógio em `apps/api`

`apps/api` SHALL registrar `TimeProvider.System` no contêiner de injeção de
dependências, e todo código novo que precise do instante atual SHALL obtê-lo por
`TimeProvider` injetado — nunca por `DateTimeOffset.UtcNow` ou `DateTime.Now`
estáticos.

O requisito NÃO SHALL exigir a conversão do código existente: as entidades que
hoje carimbam com `DateTimeOffset.UtcNow` permanecem como estão, e convertê-las é
trabalho de outra change.

A razão de registrar em vez de resolver a janela com `now()` no banco: uma janela
relativa (7d, 30d, 90d) cujo "agora" vem do SQL **não é verificável de forma
determinística** — não há como afirmar em teste onde a janela termina.

#### Scenario: A janela relativa é verificável com relógio controlado

- **WHEN** um teste fixa o instante atual por `TimeProvider` falso e pede uma
  janela relativa
- **THEN** os limites da janela são exatamente os derivados daquele instante, sem
  depender do relógio da máquina que roda o teste
