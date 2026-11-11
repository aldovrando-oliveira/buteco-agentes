**Issue:** #188 (M2 · 6 de 6; depende da #185, da #186 e da #187, as três em `main`). Acompanhamento: #195.

## Why

A #186 e a #187 deram ao `apps/api` as aplicações externas (cadastro, client ID, segredos mostrados uma vez,
rotação, revogação, desativação) e os agentes liberados por aplicação, com as operações de cada um. Nada disso
tem tela: hoje, criar uma aplicação, copiar o segredo e liberar um agente exige chamar a API à mão com o token
de um Admin (`docs/deployment.md`, "Liberar um agente sem a tela (até a #188)"). E quem desativa um agente não
tem como saber que alguma aplicação depende dele.

## What Changes

**`apps/frontend`** (é o único app que muda):
- **Menu e rotas.** "Aplicações externas" na seção "Acesso", entre "Usuários" e "Identidade", só para o Admin.
  As rotas `/applications`, `/applications/new`, `/applications/:id` e `/applications/:id/edit` entram na
  guarda de rota da #185 como só do Admin.
- **Lista** (`AplicacoesLista`, `entrega=m2`): busca por nome ou client ID, filtro de estado, os agentes
  liberados em chips, o estado e o último uso; os estados de lista vazia, busca sem resultado, carregando e erro.
- **Nova aplicação** (`AplicacaoNova`, `federadoDisponivel=false`): nome, descrição e a senha de quem chama;
  o `400` por campo, o `409` de nome repetido, o `401` e o `423` da senha.
- **Credencial criada, sem modal:** o client ID, o segredo e o endpoint de token num cartão no lugar do
  formulário, com "Copiar" e "Guardei o segredo em um local seguro" antes de "Ir para a aplicação". O segredo
  vive só no estado do componente que recebeu a resposta: nunca no estado do router, no cache do TanStack
  Query, no `sessionStorage` nem no `localStorage`.
- **Detalhe da aplicação** com o cabeçalho (estado, "Editar", "Desativar"/"Reativar", confirmação na página)
  e as abas:
  - **Credencial e conexão:** os quatro estados do segredo, "Gerar novo segredo" com a senha dentro do cartão
    e o segredo novo mostrado uma vez no mesmo cartão, "Revogar"/"Revogar agora" com confirmação na linha, o
    limite de 3 segredos vivos, a aplicação sem segredo vivo e a desativada, e o `curl` de conexão;
  - **Agentes:** a grade agente × operação (três operações), "Adicionar agente" dentro do cartão, "Editar" na
    linha, "Remover" com confirmação na linha, uma ação por vez; o agente inativo, a aplicação desativada, o
    `409` de já liberado e o `404` de vínculo retirado por outro Admin.
- **Editar aplicação** (quadro `AplicacaoEditar`): nome e descrição em `/applications/:id/edit`, sem senha, com o `400` e o `409`.
- **Detalhe do agente, card "Aplicações com acesso"** na Visão geral, abaixo de "Protocolo A2A": a lista para o
  Admin (`GET /agents/{id}/applications`), a contagem `{ total, ativas }` para o Operador e o Leitor
  (`GET /agents/{id}/applications/count`), e o texto de quando não há nenhuma; e a frase "afeta N aplicações
  ativas" no modal de desativar o agente, que já existe.

**Documentação:**
- `docs/deployment.md`: a #188 nas notas da #186/#187 (a ordem, a volta atrás só do `frontend`) e o roteiro
  "sem a tela" passa a alternativa;
- `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`.

**Fora:**
- qualquer rota ou resposta do backend: a #186 e a #187 servem tudo o que os quadros mostram (design, b);
- a aba Atividade e as recusas por aplicação (#199), métricas por aplicação, a aplicação federada (M4);
- o cancelamento de task como operação (#264);
- a linha "Autenticação declarada" do quadro `AgenteDetalhe` no card A2A do agente (design, Q10).

## Capabilities

### New Capabilities

- `panel-external-applications`: as telas de aplicações externas no painel — lista, nova, credencial criada,
  detalhe com a aba Credencial e conexão, edição — e a regra do segredo mostrado uma vez.
- `panel-external-application-agents`: a aba Agentes do detalhe da aplicação e o card "Aplicações com acesso"
  no detalhe do agente.

### Modified Capabilities

- `frontend-app-shell`: a seção "Acesso" ganha "Aplicações externas" entre "Usuários" e "Identidade" (sai o
  "SHALL NOT ter o item" e o cenário "Sem Aplicações externas").
- `panel-roles`: as rotas de `/applications` na tabela da guarda, só do Admin.
- `external-applications`: o detalhe traz os campos da lista **exceto `agentes`**, como o `apps/api` já faz
  desde a #187 (premissa falsa achada na conferência; só a spec muda, nenhum código do backend).
- `agent-catalog-ui`: o modal de desativar o agente ganha a frase "afeta N aplicações ativas" (o card fica em
  `panel-external-application-agents`).

## Impact

- **Contrato:** nenhum. O painel passa a chamar as oito rotas de `/applications/*` da #186, as quatro de
  `/applications/{id}/agents` e as duas de `/agents/{id}/applications*` da #187, que já existem.
- **Código:** só `apps/frontend` (`src/features/applications/` nova, `app/routes.tsx`, `app/routeRoles.ts`, a
  casca, o detalhe do agente).
- **Suítes:** `frontend` (design, Baseline).
- **Deploy:** só a imagem do `frontend`. A #186 e a #187 podem estar no ar antes; a volta atrás só do `frontend`
  é segura (design, j).
- **Protótipo:** versão 65 (`1794278589-23e3`), com todos os estados do design (g) desenhados pelo dono.
- **Validação visual:** na homologação, sem prender o PR (regra da M1), com o checklist no `tasks.md`, no `02` e
  no PR.
