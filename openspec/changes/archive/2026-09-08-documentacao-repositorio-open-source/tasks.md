> **Nota de escopo:** nenhuma tarefa desta change roda dentro de um app.
> Todas atuam na raiz do monorepo, em `docs/`, `deploy/`, `.github/` ou
> `openspec/` — exceto 6.3, que remove um arquivo de `apps/frontend` sem
> tocar código, build, teste ou configuração de lint.
>
> A ordem dos grupos é a da Decisão 5 do `design.md`: conteúdo primeiro,
> porta de entrada por último. Escrever o `README.md` antes de `docs/`
> reproduziria o defeito diagnosticado — links para arquivos inexistentes.
>
> **Restrição inegociável:** `01-ARQUITETURA_E_CONVENCOES.md` e
> `02-HISTORICO_E_STATUS.md` são fonte de leitura e NUNCA destino de escrita.
> Nenhuma tarefa pode editar, mover, renomear ou dividir esses dois arquivos.

## 1. Documentação técnica em `docs/` (raiz, pt-BR)

- [x] 1.1 Criar `docs/development.md` extraindo do `README.md` atual:
      pré-requisitos (.NET 10, Node 20+, Docker/Podman), subida da infra via
      `docker-compose.yml`, subida dos quatro apps (`apps/api` com
      `dotnet ef database update` antes do primeiro run, `apps/workers`,
      `apps/inbox`, `apps/frontend`), execução dos testes por app e dos dois
      projetos cruzados em `tests/`, e build das cinco imagens Docker com a
      nota de que o build context é sempre a raiz
- [x] 1.2 Transportar integralmente para `docs/development.md` a seção de
      troubleshooting de Testcontainers com Podman — `DOCKER_HOST`,
      `TESTCONTAINERS_RYUK_DISABLED`, o trecho condicional, como cada
      variável falha de forma diferente e o sinal que distingue falha de
      infraestrutura de falha de código (`ResourceReaper.GetAndStartNewAsync`
      dentro de `InitializeAsync`). Conteúdo verificado empiricamente, não
      resumir
- [x] 1.3 Criar `docs/configuration.md` inventariando as variáveis de
      ambiente por processo, cruzando `.env.example` (29 variáveis) e
      `.env.prod.example`, indicando quais são obrigatórias, quais são
      build-time no frontend (`VITE_API_BASE_URL`, `VITE_INBOX_BASE_URL`) e
      quais precisam ter o mesmo valor em mais de um processo
- [x] 1.4 Mover `deploy/runbook.md` para `docs/deployment.md` preservando o
      conteúdo; confirmar que `deploy/migrate/` permanece intacto
- [x] 1.5 Atualizar em `docs/deployment.md` as referências que citam o
      `README.md` como fonte de build de imagens, apontando para
      `docs/development.md`

## 2. Documentação de arquitetura e premissas (raiz, pt-BR)

- [x] 2.1 Criar `docs/architecture.md` derivado de
      `01-ARQUITETURA_E_CONVENCOES.md` (leitura apenas): os quatro apps e o
      papel de cada um, stacks e bancos, isolamento estrito sem
      `ProjectReference` cruzado, e validação entre apps sempre via HTTP
      autenticado
- [x] 2.2 Documentar em `docs/architecture.md` o modelo de domínio e as
      regras de negócio: `Agent` (com `Provider`/`Model` nullable e o estado
      "precisa de reconfiguração"), `McpServer`, vínculos N:N de MCP e
      delegação unidirecional, `Channel`, o CRM `Contact`/`Session` e a
      semântica oposta de `Metadata` e `DisplayName`, exclusão de catálogo
      versus conteúdo, autenticação e fuso horário do sistema
- [x] 2.3 Documentar em `docs/architecture.md` o protocolo A2A e o contrato
      de plugin de canal, linkando `docs/a2a-integration.md` para o detalhe
      de integração externa
- [x] 2.4 Criar `docs/conventions.md` com as premissas oficiais derivadas da
      seção "Convenções estabelecidas" de `01-ARQUITETURA_E_CONVENCOES.md`:
      sequenciamento de linha de trabalho, ausência de abstração prematura,
      tratamento de credenciais, degradação graciosa, testes com
      infraestrutura real, verificação de SDK de terceiro, convenções de
      frontend e checagem de integridade no startup nas suas duas formas
- [x] 2.5 Registrar em `docs/conventions.md` a fronteira de idioma da
      Decisão 2 (raiz em inglês para vitrine e legal, `docs/` e derivados em
      pt-BR) e a regra de link para `openspec/changes/` usar sempre o
      caminho de `archive/` quando a change estiver arquivada
- [x] 2.6 Conferir que `docs/architecture.md` e `docs/conventions.md` não
      narram histórico de changes nem decisões superadas — descrevem o
      estado atual para quem nunca viu o sistema

## 3. Changelog (raiz, pt-BR)

- [x] 3.1 Criar `CHANGELOG.md` no formato Keep a Changelog com cabeçalho,
      referência a SemVer, nota de que nenhuma versão oficial foi fechada e
      seção `[Unreleased]` com subseções `Added`, `Changed` e `Fixed`
- [x] 3.2 Preencher `[Unreleased]` agrupando as 55 changes arquivadas pelas
      linhas de trabalho já usadas em `02-HISTORICO_E_STATUS.md` (leitura
      apenas): estrutura do monorepo, catálogo de agentes, protocolo A2A,
      multi-provedor de LLM, MCP, delegação, inbox e adapters de canal,
      autenticação, contexto temporal e de canal, containerização,
      redesenho do painel e base de conhecimento
- [x] 3.3 Verificar que nenhuma seção de versão datada foi criada e que
      `[Unreleased]` é a única seção de release

## 4. Governança open-source (raiz, inglês)

- [x] 4.1 Adicionar `LICENSE` com o texto integral e não modificado da
      Apache License 2.0
- [x] 4.2 Adicionar `NOTICE` com `Copyright 2026 Aldovrando Oliveira`,
      deixando a URL do repositório canônico como lacuna explícita a
      preencher na publicação
- [x] 4.3 Confirmar que nenhum header de licença foi adicionado a arquivos
      `.cs`, `.ts` ou `.tsx` (Decisão 3)
- [x] 4.4 Criar `SECURITY.md` com canal privado de reporte, advertência
      contra issue pública e indicação das áreas sensíveis (credenciais
      AES-GCM, tokens de operador e de serviço, segredos compartilhados
      entre processos)
- [x] 4.5 Criar `CODE_OF_CONDUCT.md` com Contributor Covenant 2.1 e canal de
      contato preenchido
- [x] 4.6 Criar `CONTRIBUTING.md` (pt-BR) com o fluxo OpenSpec completo,
      convenção de commits observada no histórico, requisitos de pull
      request e como rodar os testes de cada app
- [x] 4.7 Declarar em `CONTRIBUTING.md` que não há CI configurada e que
      testes e script de integridade rodam localmente — sem prometer
      verificação automática inexistente
- [x] 4.8 Criar `.github/ISSUE_TEMPLATE/bug_report.yml` e
      `.github/ISSUE_TEMPLATE/feature_request.yml`
- [x] 4.9 Criar `.github/PULL_REQUEST_TEMPLATE.md` solicitando a change
      OpenSpec correspondente e a confirmação de testes e verificação de
      documentação executados localmente

## 5. Script de integridade da documentação (raiz)

- [x] 5.1 Criar o script de verificação (em `scripts/`, executável à mão),
      que falha identificando arquivo e problema
- [x] 5.2 Implementar a checagem de links relativos em arquivos `.md` que
      não resolvem para caminho existente
- [x] 5.3 Implementar a checagem de link para `openspec/changes/<nome>/`
      quando a change está sob `openspec/changes/archive/`, indicando o
      caminho correto na mensagem de erro
- [x] 5.4 Implementar a checagem de apps nos dois sentidos: diretório em
      `apps/` ausente de `docs/architecture.md` e app descrito no documento
      sem diretório correspondente
- [x] 5.5 Implementar a checagem de presença da seção `[Unreleased]` em
      `CHANGELOG.md`
- [x] 5.6 Verificar que o script reprova no estado atual do `README.md`
      (cinco links quebrados) antes da correção do grupo 6 — é a prova de
      que ele detecta o defeito real
- [x] 5.7 Excluir da varredura os arquivos protegidos
      `01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md`, já que
      não podem ser corrigidos por esta change

## 6. Porta de entrada e correções finais (raiz)

- [x] 6.1 Reescrever `README.md` em inglês, de 548 para aproximadamente 120
      linhas: o que o sistema é, os quatro apps, quickstart mínimo, links
      para `docs/`, `CONTRIBUTING.md` e `CHANGELOG.md`, e seção de licença
- [x] 6.2 Incluir no `README.md` a declaração de que `docs/` e
      `CONTRIBUTING.md` estão em português, e a linha que apresenta
      `01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md` como
      notas internas de trabalho do mantenedor
- [x] 6.3 Remover `apps/frontend/README.md` (template cru do Vite);
      confirmar que não é referenciado por `package.json`, `Dockerfile` ou
      configuração de lint, e que nenhum build ou teste é afetado
- [x] 6.4 Criar `docs/README.md` como índice navegável de `docs/`, listando
      todos os documentos, incluindo `a2a-integration.md`
- [x] 6.5 Corrigir o bloco `context` de `openspec/config.yaml`: quatro apps
      em vez de três, e WAHA e Telegram como adapters de canal em vez de
      ChatWoot e Waha
- [x] 6.6 Corrigir os links para `openspec/changes/` em `.env.example` para
      o caminho de `archive/`

## 7. Verificação final

- [x] 7.1 Executar o script de integridade e confirmar que passa sem
      violações
- [x] 7.2 Confirmar por `git diff` que `01-ARQUITETURA_E_CONVENCOES.md` e
      `02-HISTORICO_E_STATUS.md` não aparecem entre os arquivos alterados,
      renomeados ou movidos
- [x] 7.3 Conferir manualmente que cada uma das nove seções do `README.md`
      antigo tem destino em `docs/` — nada foi perdido na reescrita
- [x] 7.4 Rodar `openspec validate documentacao-repositorio-open-source`
