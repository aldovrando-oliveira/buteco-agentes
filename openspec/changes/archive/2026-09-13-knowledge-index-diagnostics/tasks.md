## 1. Baseline antes de tocar em qualquer arquivo (`apps/api`)

Convenção 19: "pré-existente" e "ambiental" são conclusões que exigem a baseline,
e ela não fecha sozinha. Três vezes nesta base uma falha foi classificada errado.

- [x] 1.1 (`apps/api`) Criar `git worktree` limpo no `HEAD` atual e rodar a suíte
      completa de `apps/api` lá, **antes** de qualquer edição. Guardar a saída
      **inteira** em arquivo (`tee`), nunca `| grep | head` — o cano fechado mata o
      produtor por `SIGPIPE` e a rodada parece ter terminado sem produzir sucesso
      nem falha. Registrar o número (`N/N`) no `design.md` ou no item de fechamento.
- [x] 1.2 (`apps/api`) Conferir que `DOCKER_HOST` está apontando para o socket do
      podman desta máquina — sem isso a suíte de integração falha inteira, e a
      falha parece defeito de código.
- [x] 1.3 (`apps/api`) Se algo já reprovar na baseline, **parar e investigar em três
      passos** antes de seguir: isolar o teste, comparar com o worktree limpo, e
      nunca absolver por "a suíte passou depois".

## 2. Consulta e resposta (`apps/api`)

- [x] 2.1 (`apps/api`) Criar
      `KnowledgeFragments/Queries/GetKnowledgeIndexDiagnostics/GetKnowledgeIndexDiagnosticsQuery.cs`
      — `IQuery<IReadOnlyList<KnowledgeIndexProvenanceResponse>>`, **sem
      parâmetro**. A ausência de parâmetro é o contrato de D1 e merece a linha de
      comentário dizendo por quê (não existe proveniência por base).
- [x] 2.2 (`apps/api`) Criar
      `KnowledgeFragments/Responses/KnowledgeIndexProvenanceResponse.cs` —
      `(string Provider, string Model, int Dimensions, long FragmentCount)`.
      `long` para a contagem, não `int`: `count(*)` do PostgreSQL é `bigint` e o
      cast não tem motivo. Documentar no XML-doc por que a lista pode ter mais de
      um item e por que isso **não** é erro desta rota (D3).
- [x] 2.3 (`apps/api`) Criar
      `GetKnowledgeIndexDiagnosticsQueryHandler.cs` — uma consulta:
      `AsNoTracking`, `GroupBy` nas três colunas, `Select` com `Count()`,
      `OrderBy`/`ThenBy` nas três colunas **na consulta** (comparador do banco, não
      em memória — `api-response-ordering`), `ToListAsync`. Comentar que não há
      quarto critério de desempate porque as três colunas **são** o identificador
      da combinação, para que ninguém "conserte" depois.

## 3. Endpoint e wiring (`apps/api`)

- [x] 3.1 (`apps/api`) Criar
      `KnowledgeFragments/Endpoints/KnowledgeIndexEndpoints.cs` no molde de
      `ProviderEndpoints` — `app.MapGet("/knowledge-index/diagnostics", ...)` direto
      em `app`, **sem `MapGroup`**, devolvendo
      `Ok<IReadOnlyList<KnowledgeIndexProvenanceResponse>>` (array nu, D4).
- [x] 3.2 (`apps/api`) Adicionar `app.MapKnowledgeIndexEndpoints();` em
      `Program.cs`, junto aos outros `Map*` e **antes** de
      `ValidateRouteAuthenticationClassification`.
- [x] 3.3 (`apps/api`) **Não** acrescentar a rota à lista de
      `ValidateRouteAuthenticationClassification` (D8): aquela lista é de rotas que
      precisam estar **anônimas**, e incluir a rota nova reprovaria o boot. Deixar
      uma linha de comentário no `Program.cs` só se ela não ficar redundante com o
      comentário que já existe ali.
- [x] 3.4 (`apps/api`) Subir o app localmente uma vez e confirmar que o boot passa
      — é a checagem que prova que 3.2 e 3.3 estão coerentes.

## 4. Testes de contrato (`apps/api`)

Um cenário da spec por teste, e o par "com item"/"sem item" explícito (convenção
5). Semear fragmentos por SQL cru no molde de
`KnowledgeDocumentIndexingContractTests.cs:198-206`; `ApiFactoryFixture` já roda
`pgvector/pgvector:pg18` — nenhum fixture novo.

- [x] 4.1 (`apps/api`) Criar `Knowledge/KnowledgeIndexDiagnosticsTests.cs` com um
      helper de semeadura de fragmento (base, documento, provedor, modelo, dimensão),
      reaproveitando o literal de 4.096 dimensões.
- [x] 4.2 (`apps/api`) Índice vazio: HTTP 200, lista vazia, **nunca 404**. Asserção
      **negativa** de que o corpo não contém nenhum nome de provedor nem de modelo.
- [x] 4.3 (`apps/api`) Uma combinação: um item, com os três valores e
      `fragmentCount` igual ao número semeado.
- [x] 4.4 (`apps/api`) Duas combinações: dois itens, cada um com a sua contagem, e
      HTTP 200 — não erro.
- [x] 4.5 (`apps/api`) Duas bases com a mesma combinação: **um** item, com a soma
      das duas.
- [x] 4.6 (`apps/api`) Ordem determinística com mais de uma combinação, semeando em
      ordem **oposta** à esperada — a asserção é sobre a ordem crescente, não sobre
      "duas chamadas concordam", que passa com o defeito presente sempre que o plano
      calhar de ser estável.
- [x] 4.7 (`apps/api`) Custo: `EmittedSqlCapture` provando **uma** consulta, com
      fragmentos em várias bases e vários documentos.
- [x] 4.8 (`apps/api`) 401 sem token de operador. *(Acrescentado a
      `KnowledgeRouteAuthenticationTests`, ao lado do caso análogo de
      `indexing-summary`, em vez da classe nova — é onde o leitor procura.)*
- [x] 4.9 (`apps/api`) 403 com o token de serviço de `apps/inbox`, estruturalmente
      válido. *(Acrescentado a `ServiceScopeAuthorizationTests`, pelo mesmo motivo.)*
- [x] 4.10 (`apps/api`) Nomes no fio: asserção sobre o **texto** do JSON
      (`provider`, `model`, `dimensions`, `fragmentCount`), nunca desserializando
      para o mesmo tipo — a ida e a volta passam pela mesma política e sempre casam.

## 5. Exercitar os guardas contra o defeito real (`apps/api`)

Convenção 15: um guarda só vale depois de ter falhado contra o defeito que ele
guarda. Cada item abaixo é uma inversão **temporária**, confirmada vermelha e
desfeita na hora.

- [x] 5.1 (`apps/api`) Fazer a consulta filtrar por uma base e confirmar que 4.5
      reprova. Desfazer.
- [x] 5.2 (`apps/api`) Trocar `ORDER BY` por ordenação em memória com
      `StringComparer.Ordinal` e confirmar que 4.6 reprova com os nomes escolhidos.
      Se **não** reprovar, os nomes do arranjo não separam os dois comparadores —
      trocar os nomes, não o teste. Desfazer.
- [x] 5.3 (`apps/api`) Renomear um campo da resposta (por exemplo `fragmentCount`
      para `fragmentsCount`) e confirmar que 4.10 reprova enquanto um teste que
      desserializa para o mesmo tipo passaria. Desfazer.
- [x] 5.4 (`apps/api`) Trocar a agregação por `LIMIT 1` e confirmar que 4.4 reprova
      — é a asserção que sustenta D3 contra a alternativa barata e cega.
- [x] 5.5 (`apps/api`) Registrar no `design.md`, em uma linha por guarda, que a
      inversão foi exercida. Guarda não exercido é guarda que ninguém sabe se
      funciona.

## 6. Suíte e conferência (`apps/api`)

- [x] 6.1 (`apps/api`) Rodar a suíte completa de `apps/api`, saída inteira guardada
      em arquivo. Comparar com a baseline de 1.1 — número contra número, não
      impressão.
- [x] 6.2 (`apps/api`) Qualquer falha nova: discriminar em três passos antes de
      classificar (isolar, baseline em worktree limpo, e nunca absolver por "passou
      na segunda vez"). Nenhuma falha é "ambiental" sem a baseline que o prove.
- [x] 6.3 (`apps/api`) Chamar a rota à mão com o app de pé e conferir o JSON real —
      índice vazio e índice com fragmentos. É a única conferência que vê o corpo que
      a 5c vai consumir.

## 7. Documentação, um arquivo por vez

Dizer explicitamente "conferido, nada a mudar" onde não houver o que mudar — não
pular em silêncio.

- [x] 7.1 `docs/architecture.md` — **conferido, nada a mudar.** O arquivo não tem
      inventário de rotas: cita rota só onde ela é contrato entre apps ou mecanismo
      específico (cinco ocorrências, conferidas). E `KnowledgeFragment` não está
      entre as entidades que ele documenta — lacuna da change de indexação, não
      desta. Acrescentar uma seção de entidade aqui seria escopo de outra change.
- [x] 7.2 `CHANGELOG.md` — entrada em pt-BR.
- [x] 7.3 `01-ARQUITETURA_E_CONVENCOES.md` — avaliar se D8 (o guarda de
      autenticação é a `FallbackPolicy`, e a checagem de classificação cobre só o
      caminho anônimo) merece entrar na convenção 8. **Avaliado: não merece — a
      convenção já está correta**, diz "classificação de rotas **anônimas**"
      (`01-ARQUITETURA_E_CONVENCOES.md:702-703`). A imprecisão era do prompt desta
      change, não do `01`. A seção `KnowledgeFragment` do `01` **foi** atualizada:
      ganhou o segundo leitor das três colunas, o custo medido e o gatilho do índice.
- [x] 7.4 `02-HISTORICO_E_STATUS.md` — fechar o item "backend do diagnóstico do
      índice (`apps/api`) — **não proposta**" da fila, marcar a 5c como
      desbloqueada, e registrar: os números medidos **com o estado da máquina**
      (convenção 22), o aviso de que `TIMING ON` infla 7–9× nesta VM, e as três
      correções que a 5c herda (D9). Toda afirmação sobre código leva arquivo e
      linha de onde foi **lida**, ou vira suspeita explícita.
- [x] 7.5 `README.md` — **conferido, nada a mudar.** Nenhuma rota HTTP aparece no
      arquivo (`grep` por `GET /`/`POST /`/`PUT /`/`DELETE /`: zero ocorrências).
- [x] 7.6 Varrer os artefatos desta change pelo nome do modelo: tem de ser
      `qwen-qwen3-embedding-8b`, nunca a forma curta nem
      `text-embedding-3-small` (que é mock do protótipo). Já foi corrigido em 10
      sítios antes; não reintroduzir.

## 8. Fechamento

- [x] 8.1 Rodar `/opsx:sync` para levar a delta de
      `specs/knowledge-document-indexing/spec.md` para a spec viva.
- [x] 8.2 Abrir `openspec/specs/knowledge-document-indexing/spec.md` **depois** do
      sync e conferir o `Purpose` no arquivo vivo — `Purpose` placeholder não é
      esquecimento individual, é o passo 4d da skill mandando escrever `TBD`
      (39 ocorrências). **Conferido: não havia placeholder, o `Purpose` era real.
      Mas estava INCOMPLETO** — não nomeava a superfície de leitura que a
      capability passou a cobrir. Complementado com o resumo por base e a
      proveniência, dizendo por que as duas vivem aqui (leem as mesmas três
      colunas que a checagem de boot).
- [x] 8.3 Conferir que o requisito aterrou inteiro no arquivo vivo: os dez cenários,
      com quatro `#` cada.
- [x] 8.4 Arquivar com `/opsx:archive`.
- [x] 8.5 Deixar o trabalho na árvore e **não commitar** — commit é autorização por
      mensagem, e terminar a change não é autorização.
