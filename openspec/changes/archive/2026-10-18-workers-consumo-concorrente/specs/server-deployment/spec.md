## RENAMED Requirements

- FROM: `### Requirement: Serviços com coordenação em memória rodam em réplica única`
- TO: `### Requirement: Serviços de réplica única declaram o motivo de cada um`

## MODIFIED Requirements

### Requirement: Serviços de réplica única declaram o motivo de cada um
O `docker-compose.prod.yml` SHALL declarar uma única instância de `apps/workers`,
`apps/inbox` e `apps/connectors`, sem `deploy.replicas` maior que 1, e o
compose e o runbook de deploy SHALL nomear os três como serviços que nunca usam
`replicas > 1`, com o motivo de cada um:

- `apps/connectors`: o lock em memória que impede dois ciclos de sincronização da
  mesma base (#105) — restrição técnica de coordenação;
- `apps/workers`: decisão de disponibilidade, em aberto na #48. A capacidade é
  contada em slots (`réplicas × N`), e não é motivo para a réplica única;
- `apps/inbox`: decisão operacional, sem restrição técnica de coordenação, a rever
  na #165.

O compose e o runbook SHALL NOT dar como motivo, para `apps/workers` ou
`apps/inbox`, a falta de coordenação entre instâncias: `pg_advisory_lock` e `xmin`
vivem no Postgres compartilhado e funcionam entre instâncias.

#### Scenario: apps/connectors sobe com uma instância
- **WHEN** o stack de servidor é iniciado
- **THEN** existe exatamente um contêiner do serviço `connectors`

#### Scenario: Runbook lista o apps/connectors entre os serviços de réplica única
- **WHEN** o runbook de deploy é consultado sobre réplicas
- **THEN** ele lista `apps/workers`, `apps/inbox` e `apps/connectors` como serviços
  que nunca usam `replicas > 1`, com o motivo de cada um

#### Scenario: Motivo de cada serviço corresponde à sua restrição real
- **WHEN** o compose ou o runbook de deploy é consultado sobre o motivo da réplica
  única de cada serviço
- **THEN** o `apps/connectors` tem como motivo o lock em memória (#105), o
  `apps/workers` a decisão de disponibilidade da #48, e o `apps/inbox` a decisão
  operacional da #165, e nenhum texto afirma que `pg_advisory_lock`/`xmin` deixam
  de funcionar entre instâncias
