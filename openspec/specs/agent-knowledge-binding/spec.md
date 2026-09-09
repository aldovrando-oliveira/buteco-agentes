# agent-knowledge-binding Specification

## Purpose

Cobre o cadastro (catálogo) do vínculo N:N entre agente e base de
conhecimento em `apps/api`: a substituição integral do conjunto de bases
vinculadas a um agente, a atomicidade da operação, a deduplicação de ids
repetidos, a aceitação de vínculo com base inativa e a persistência durável
desse vínculo. Esta capability trata apenas do cadastro/persistência do
vínculo — não cobre a execução nem a resolução do conhecimento em tempo de
execução, que pertencem a outra capability de outra etapa.

## Requirements

### Requirement: Substituição do conjunto de bases de conhecimento de um agente
O sistema SHALL permitir, via `apps/api`, definir o conjunto completo de bases
de conhecimento vinculadas a um agente, substituindo qualquer vínculo anterior
pelo conjunto informado. A operação SHALL ser idempotente quando o mesmo
conjunto for enviado repetidamente.

O sistema SHALL rejeitar a operação por completo (nenhum vínculo do payload
aplicado) quando o agente identificado na URL não existir, ou quando qualquer
id do conjunto não corresponder a nenhuma base de conhecimento cadastrada.

#### Scenario: Vincular bases a um agente sem vínculo prévio
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` com
  `knowledgeBaseIds` cujos ids são todos de bases existentes, para um agente
  que ainda não tem nenhuma base vinculada
- **THEN** a API responde com HTTP 200 e o agente passa a ter todas as bases
  informadas vinculadas

#### Scenario: Substituir o conjunto de bases por um conjunto diferente
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` com
  `knowledgeBaseIds` diferente do conjunto atualmente vinculado ao agente
- **THEN** a API remove os vínculos que não estão na nova lista, adiciona os
  que estão presentes e ainda não existiam, e responde com HTTP 200 e o
  conjunto atualizado

#### Scenario: Remover todas as bases vinculadas a um agente
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` com
  `knowledgeBaseIds` vazio para um agente que tem bases vinculadas
- **THEN** a API remove todos os vínculos existentes e responde com HTTP 200 e
  o agente sem nenhuma base vinculada

#### Scenario: Reenviar o mesmo conjunto é idempotente
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` duas vezes
  seguidas com o mesmo `knowledgeBaseIds`
- **THEN** ambas as chamadas respondem com HTTP 200 e o mesmo conjunto de bases
  vinculadas, sem erro na segunda chamada

#### Scenario: Vincular uma base inexistente é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` incluindo em
  `knowledgeBaseIds` um id que não corresponde a nenhuma base cadastrada
- **THEN** a API responde com erro de validação (HTTP 400) identificando os ids
  inválidos e não altera nenhum vínculo existente do agente

#### Scenario: Vincular bases a um agente inexistente retorna 404
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` para um `id` de
  agente que não existe
- **THEN** a API responde com HTTP 404

#### Scenario: Omitir o conjunto é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` sem o campo
  `knowledgeBaseIds`, ou com ele nulo
- **THEN** a API responde com erro de validação (HTTP 400) indicando que o
  conjunto é obrigatório e que uma lista vazia remove todos os vínculos, e não
  altera nenhum vínculo existente do agente

#### Scenario: Id repetido no mesmo payload é aceito uma vez só
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` com o mesmo id
  de base repetido em `knowledgeBaseIds`
- **THEN** a API responde com HTTP 200 e o agente fica com **um** vínculo para
  essa base, sem erro de duplicidade

#### Scenario: Vincular bases a um agente inativo é permitido
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` para um agente
  com `isActive: false`
- **THEN** a API cria os vínculos normalmente e responde com HTTP 200 —
  desativar um agente impede que ele execute, não que o operador configure os
  vínculos dele, pelo mesmo motivo que permite criar e atualizar documento em
  base inativa

### Requirement: Vínculo com base de conhecimento inativa é permitido
O sistema SHALL aceitar o vínculo entre um agente e uma base de conhecimento
com `isActive: false`, sem rejeitar a operação por causa do estado inativo da
base, e SHALL preservar esse vínculo enquanto a base permanecer inativa.

O estado inativo da base afeta apenas o momento em que o conhecimento é
oferecido ao agente em execução — comportamento que pertence à capability de
execução e não a esta.

#### Scenario: Vincular uma base inativa é aceito
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` incluindo em
  `knowledgeBaseIds` o id de uma base com `isActive: false`
- **THEN** a API cria o vínculo normalmente e responde com HTTP 200

#### Scenario: Desativar uma base não remove os vínculos existentes
- **WHEN** uma base de conhecimento vinculada a um agente é desativada via
  `POST /knowledge-bases/{id}/deactivate`
- **THEN** o vínculo entre o agente e essa base continua existindo, e uma
  consulta subsequente ao agente continua listando essa base

#### Scenario: Reativar a base não exige reconfigurar o vínculo
- **WHEN** uma base de conhecimento é desativada e depois reativada via
  `POST /knowledge-bases/{id}/activate`, sem nenhuma chamada a
  `PUT /agents/{id}/knowledge-bases` entre as duas
- **THEN** o vínculo entre o agente e essa base continua o mesmo de antes da
  desativação

### Requirement: Resposta da substituição reflete o agente completo
O sistema SHALL responder à substituição do conjunto de bases de conhecimento
com a representação completa do agente, incluindo os demais vínculos já
existentes (servidores MCP e delegações de saída), e não apenas o conjunto
alterado.

#### Scenario: Resposta inclui os demais vínculos do agente
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` para um agente
  que já tem servidores MCP vinculados e delegações de saída cadastradas
- **THEN** a API responde com HTTP 200 e o corpo inclui `knowledgeBases`
  atualizado, `mcpServers` e `delegatesTo` inalterados, além dos demais campos
  do agente

#### Scenario: Substituir servidores MCP não afeta as bases vinculadas
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` para um agente que
  tem bases de conhecimento vinculadas
- **THEN** a API responde com HTTP 200 e o corpo inclui `knowledgeBases` com as
  bases vinculadas do agente, inalteradas

#### Scenario: Substituir delegações não afeta as bases vinculadas
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` para um agente que
  tem bases de conhecimento vinculadas
- **THEN** a API responde com HTTP 200 e o corpo inclui `knowledgeBases` com as
  bases vinculadas do agente, inalteradas

### Requirement: Formato de fio do conjunto de bases vinculadas
O sistema SHALL expor o conjunto de bases vinculadas sob a chave
`knowledgeBases`, com cada item contendo `id` e `name`, ordenados por nome e,
para nomes iguais, **com desempate estável por identificador**. A chave e os
nomes dos campos SHALL ser verificados sobre o texto do JSON da resposta, não
por desserialização para o mesmo tipo.

O desempate é requisito, não detalhe de implementação: nome de base de
conhecimento **não é único** (`knowledge-base-catalog`, cenário "Nome duplicado
é permitido"), então ordenar só por nome deixa a ordem entre homônimas a cargo
do plano de consulta do PostgreSQL, e a mesma requisição pode devolver ordens
diferentes sem nada ter mudado no cadastro.

**A forma da verificação faz parte do requisito.** Um teste que afirma apenas
"duas consultas devolvem a mesma ordem" é asserção sobre não-determinação e
**passa com o defeito presente** sempre que o plano do PostgreSQL calhar de ser
estável — é o perfil de guarda que esta base já teve de consertar quatro vezes.
A asserção que vale é sobre a **ordem crescente de identificador**, e o arranjo
precisa criar as bases homônimas em ordem de inserção **oposta** à ordem de
`id`; sem isso, a ordem "natural" do banco coincide com a esperada e o teste
fica verde com e sem o desempate.

#### Scenario: Chave e campos aparecem no JSON da resposta
- **WHEN** um cliente consulta um agente com pelo menos uma base vinculada e o
  corpo bruto da resposta é inspecionado como texto
- **THEN** o JSON contém a chave `knowledgeBases`, e cada item contém as chaves
  `id` e `name`

#### Scenario: Bases vinculadas vêm ordenadas por nome
- **WHEN** um cliente consulta um agente com várias bases vinculadas cujos
  nomes, em ordem alfabética, diferem da ordem em que foram vinculadas
- **THEN** a API retorna `knowledgeBases` em ordem alfabética de `name`

#### Scenario: Bases de nome igual vêm em ordem crescente de identificador
- **WHEN** um agente tem três bases vinculadas com o **mesmo** `name`, criadas e
  vinculadas em ordem de inserção **oposta** à ordem crescente dos seus `id`
- **THEN** a API retorna essas bases em ordem **crescente de `id`**, tanto na
  consulta por id quanto na listagem — nunca na ordem de inserção nem em ordem
  indefinida pelo banco

### Requirement: Autenticação da rota de vínculo de bases de conhecimento
O sistema SHALL exigir autenticação de operador na rota de substituição do
conjunto de bases de conhecimento de um agente, sem nenhuma entrada nova na
allowlist de rotas anônimas.

#### Scenario: Requisição sem token é rejeitada
- **WHEN** um cliente envia `PUT /agents/{id}/knowledge-bases` sem token de
  autenticação
- **THEN** a API responde com HTTP 401 e nenhum vínculo é alterado

### Requirement: Persistência durável do vínculo entre agente e base de conhecimento
O sistema SHALL persistir o vínculo entre agente e base de conhecimento em
PostgreSQL, em tabela relacional própria, sem uso de armazenamento em memória,
garantindo que os dados sobrevivam a reinícios da aplicação.

O modelo de dados de `apps/workers` SHALL espelhar exatamente o mapeamento de
`apps/api` para essa tabela — nome, chave primária composta e comportamento de
exclusão das chaves estrangeiras —, ainda que nenhum código de `apps/workers`
leia a tabela nesta etapa.

#### Scenario: Vínculo sobrevive a restart da API
- **WHEN** uma base de conhecimento é vinculada a um agente e o processo de
  `apps/api` é reiniciado
- **THEN** uma consulta subsequente ao agente continua refletindo o mesmo
  vínculo

#### Scenario: Schema gerado por apps/workers corresponde ao de apps/api
- **WHEN** as migrações de `apps/workers` são aplicadas a um PostgreSQL limpo
- **THEN** a tabela de vínculo existe com as mesmas colunas, a mesma chave
  primária composta e as mesmas chaves estrangeiras em cascata que as migrações
  de `apps/api` produzem
