## MODIFIED Requirements

### Requirement: Nome estável e sem colisão para a tool de delegação
O nome de cada tool de delegação SHALL ser derivado de forma
determinística do `Agent.Name` do Target correspondente, e SHALL
permanecer único **no conjunto final de tools entregue ao LLM** — não apenas
entre as tools de delegação —, mesmo quando dois Targets vinculados ao mesmo
Source têm `Agent.Name` colidente, e mesmo quando o nome derivado colide com o
de uma tool MCP resolvida para o mesmo agente.

A unicidade SHALL ser garantida no ponto que une os dois conjuntos, não dentro
da resolução de delegação isoladamente. O sufixo determinístico de dedupe SHALL
respeitar o limite de 64 caracteres do nome exposto: aplicá-lo a um nome já no
limite SHALL encurtar a base para caber, nunca produzir um nome mais longo que
o limite.

#### Scenario: Dois Targets com o mesmo Agent.Name geram nomes de tool distintos
- **WHEN** o Source tem `AgentDelegation` para dois Targets diferentes,
  ambos com `Agent.Name` igual a "Atendimento"
- **THEN** as duas tools de delegação resolvidas têm nomes distintos,
  derivados do mesmo slug base com um sufixo determinístico de dedupe

#### Scenario: Mesmo conjunto de delegações produz os mesmos nomes de tool entre execuções
- **WHEN** a mesma lista de `AgentDelegation` do Source é resolvida em
  duas execuções diferentes, sem nenhuma mudança no cadastro
- **THEN** os nomes das tools de delegação resolvidas são idênticos nas
  duas execuções

#### Scenario: Target com nome longo que colide gera nomes dentro do limite
- **WHEN** o Source tem `AgentDelegation` para dois Targets diferentes com
  `Agent.Name` colidente e longo o bastante para o nome derivado ocupar
  exatamente 64 caracteres
- **THEN** as duas tools de delegação têm nomes distintos e ambos os nomes têm
  no máximo 64 caracteres
