# Handoff: Painel Buteco Agentes — Agentes, Servidores MCP e Vínculos

> **Anexado como contexto do redesenho já implementado** — não como especificação a seguir. As
> oito etapas descritas aqui foram aplicadas e arquivadas entre 2026-09-05 e 2026-09-06. Vale por
> duas coisas que nada mais carrega: os padrões gerais do painel que a tela de conhecimento reusa,
> e a nota de 09/09/2026 sobre o card A2A, que sustenta a seção 4.1 do `CONHECIMENTO.md`.
>
> **Dois pontos em que este arquivo envelheceu**, para não serem seguidos:
> 1. A seção *Assets* sugere o icon set **Tabler**. O código usa `lucide-react` (`AppShell.tsx`) —
>    ver D8 do `design.md` da change.
> 2. A seção *App shell* descreve **identificação de operador** (avatar + e-mail) no rodapé da
>    barra lateral. Foi removida: o login devolve credencial e validade, não nome nem e-mail — ver
>    a divergência 4 do `CONHECIMENTO.md`.
>
> Nome trocado de `README.md` para `README-painel.md` para não colidir com o README da change.

## Overview

Redesenho da interface administrativa do **Buteco Agentes** (painel do operador: agentes de IA,
servidores MCP e as tools que cada agente pode usar). O redesenho não muda o backend: usa
exatamente as rotas já existentes descritas no briefing.

Mudanças estruturais em relação à UI atual:

1. **O vínculo agente ↔ servidores MCP deixa de ser página própria** (`/agents/:id/mcp-servers`)
   e passa a ser uma **aba dentro do detalhe do agente**. Delegações também são aba. Isso resolve a
   inconsistência de ter uma relação em página separada e outra inline.
2. **Aviso explícito de "vinculado sem tools"** (`allowedTools: []`) — badge na linha do servidor,
   callout no topo da aba e coluna de aviso na lista de agentes.
3. **Visão inversa no servidor MCP** — quais agentes usam o servidor e com quais tools; catálogo de
   tools do servidor no próprio detalhe; modal de desativação nomeia os agentes afetados.
4. **Description e skills do agente ganham UI** (formulário + card no detalhe). Instruções são
   renderizadas como Markdown.
5. **Teste de conexão com mensagem por `failureReason`**, e na edição avisa que sem digitar a
   credencial o teste usa a credencial salva.
6. **Busca e filtro** nas duas listas; barra de salvamento fixa com progresso do handshake.

## About the Design Files

O arquivo deste bundle é uma **referência de design feita em HTML** — um protótipo que demonstra
aparência e comportamento pretendidos, **não código de produção para copiar**. A tarefa é
**recriar esse design no codebase oficial** (React + Mantine, pt-BR, tema claro/escuro), usando os
componentes e padrões já estabelecidos lá: `Table`, `Card`, `Badge`, `Tabs`, `Checkbox`,
`TextInput`, `Textarea`, `Select`, `MultiSelect`, `Modal`, `Alert`, `Notification`, `Loader`,
`CopyButton`, `SegmentedControl`, `AppShell`. Os valores literais abaixo (px, hex, pesos) existem
para descrever a intenção; onde o tema Mantine já define um token equivalente, **use o token do tema**.

O protótipo usa dados fictícios em memória, sem chamadas de rede. Toda latência é simulada com
`setTimeout`.

## Fidelity

**Alta fidelidade (hifi)** no layout, hierarquia, copy, estados e interações.
**Não** é fiel à identidade visual atual do painel: o protótipo foi feito sem acesso ao codebase, com
uma paleta e tipografia próprias (IBM Plex Sans/Mono). **Substitua tipografia e cores pelo tema
Mantine do projeto**; mantenha estrutura, densidade, copy e comportamento.

## Rotas

Mesmo mapa de rotas de hoje, **menos uma**:

```
/agents                      lista de agentes
/agents/new                  criar agente
/agents/:id                  detalhe — abas: Visão geral | Ferramentas | Conhecimento | Delegações
/agents/:id/edit             editar agente
/mcp-servers                 lista de servidores MCP
/mcp-servers/new             criar servidor
/mcp-servers/:id             detalhe
/mcp-servers/:id/edit        editar servidor
/channels...                 fora de escopo
```

**`/agents/:id/mcp-servers` é removida.** Sugestão de transição: manter a rota por um release
redirecionando para `/agents/:id?tab=ferramentas`, para não quebrar links salvos. O estado da aba
deve viver na URL (query param ou sub-rota) para ser linkável e sobreviver a refresh.

---

## Screens / Views

### 1. App shell

- **Layout**: linha flex. Sidebar fixa à esquerda (`position: sticky; top: 0; height: 100vh`),
  conteúdo em `<main>` com `flex: 1; min-width: 0` e `padding-bottom: 96px` (espaço para a barra de
  salvamento fixa).
- **Sidebar**: 224px na variante completa, 58px na variante ícones. Fundo `--sf`, borda direita 1px
  `--bd`.
  - Topo: quadrado 24×24, radius 6px, fundo accent, letra "B" branca mono 12px/700; ao lado
    "Buteco Agentes" 13px/600, tracking -0.01em. Borda inferior 1px `--bd2`, padding `16px 14px 14px`.
  - Nav: itens com `padding: 7px 8px`, radius 7px, gap 9px, ícone-placeholder 20×20 (radius 5px,
    borda 1px `--bd`, letra mono 10px). Ativo: fundo `--acsf`, texto `--ac`, peso 600. Inativo:
    texto `--mut`, peso 500.
  - Itens: **Agentes** (A), **Servidores MCP** (M), **Canais** (C), e **Conhecimento** (ver
    `CONHECIMENTO.md`). Um item fica ativo para todas as rotas do seu grupo (ex.: Agentes ativo em
    `/agents`, `/agents/:id`, `/agents/new`).
  - Rodapé: toggle de tema (☾ / ☀) e identificação do operador (avatar 20px circular + e-mail 11px
    `--mut2`).
- **Toast**: `position: fixed; right: 18px; bottom: 18px`, max-width 330px, radius 9px, borda 1px
  `--bd`, fundo `--sf`, sombra `0 6px 24px rgba(0,0,0,.14)`, entrada `translateY(8px) → 0` +
  opacidade em 180ms ease-out, auto-dismiss em 3.6s. Marcador circular 15px à esquerda: ✓ verde
  (ok), ! vermelho (erro), i neutro (info). No codebase, usar o sistema de notificações do Mantine.

### 2. Lista de agentes (`/agents`)

- **Header**: título "Agentes" 21px/600 tracking -0.015em; subtítulo 12px `--mut`:
  "{n} agentes cadastrados · atendem mensagens de Telegram e WhatsApp". À direita, botão primário
  "Novo agente" (`padding: 7px 13px`, radius 7px, fundo `--ac`, texto branco 12.5px/600).
- **Primeiros passos** (dismissível, aparece só enquanto algum passo está pendente): card com borda
  esquerda 3px accent, radius 9px, padding `14px 16px`. Título "Primeiros passos" 12.5px/600 e
  "Ocultar" à direita. Três colunas (`flex: 1 1 210px`, wrap): marcador circular 18px (✓ em
  `--okbg/--ok` quando feito, número em `--sf2/--mut` quando pendente), passos feitos com
  `opacity: .6`.
  - Passo 1 — "Cadastrar um servidor MCP" / "Fonte das tools que os agentes vão usar." — feito se
    `servers.length > 0`.
  - Passo 2 — "Criar um agente" / "Nome, instruções, provedor e modelo." — feito se `agents.length > 0`.
  - Passo 3 — "Vincular tools ao agente" / "Aba Ferramentas no detalhe do agente." — feito se algum
    agente tem `allowedTools` não vazio.
- **Filtros**: input de busca (max-width 340px, placeholder "Buscar por nome ou descrição") +
  segmented control **Todos · Ativos · Inativos · Reconfigurar**. Busca casa em nome e descrição,
  case-insensitive. Client-side (a API não tem busca).
- **Tabela**: card radius 10px, borda 1px `--bd`, header `--sf2` com labels 10.5px/600 uppercase
  tracking 0.05em `--mut2`. Grid de colunas:
  `minmax(0,2.3fr) minmax(0,1.3fr) minmax(0,1.5fr) minmax(0,1.2fr) 190px`, gap 12px,
  padding `var(--ry) 14px`. Linhas com hover `--sf2`, clicáveis, borda inferior 1px `--bd2`.
  - **Agente**: nome 13px/600 em `--ac` + descrição 11px `--mut2`, ambos com ellipsis.
  - **Provedor / modelo**: mono 11.5px `--mut`, `{provider} / {model}`; nulos viram
    "— / não configurado".
  - **Ferramentas**: "{n} servidores · {m} tools" (ou "Nenhum servidor"), e abaixo, quando aplicável,
    "⚠ {k} servidores sem tools" em 11px/500 `--wa`.
  - **Delega para**: nomes dos agentes-alvo, ou "—".
  - **Estado**: badge Ativo (`--okbg/--ok`) ou Inativo (`--sf2/--mut`); mais badge âmbar
    "Precisa de reconfiguração" (`--wabg/--wa`) quando `provider` ou `model` é nulo.
    Badges: `padding: 2px 7px`, radius 5px, 10.5px/600.
- **Vazio**: 34px de padding, centralizado, 12.5px `--mut2`. "Nenhum agente cadastrado ainda." se a
  lista é vazia; "Nenhum agente corresponde à busca." se é filtro.

### 3. Detalhe do agente (`/agents/:id`)

- **Header**: link "← Agentes" 11.5px/500 `--mut2`; título 21px/600 com badges de estado ao lado;
  descrição 12px `--mut` (ou "Sem descrição."). À direita: **Editar** e **Desativar/Ativar**
  (botões secundários, borda `--bd`, fundo `--sf`; Desativar em `--da`, Ativar em `--ok`, peso 600).
- **Abas** (borda inferior 1px `--bd`, aba ativa com `border-bottom: 2px solid --ac` e texto
  `--fg`/600; inativa `--mut`/500): **Visão geral · Ferramentas (n) · Delegações (n)**. O contador é
  um pill 10.5px mono em `--sf2` com borda `--bd2`, escondido quando 0.

#### 3.1 Aba Visão geral

Grid `minmax(0,1.7fr) minmax(0,1fr)`, gap 16px.

Grade `minmax(0,7fr) minmax(0,5fr)` com `align-items: start` — os spans 7/5 do `Grid.Col` do
codebase.

- **Esquerda — Instruções (system prompt)**: card com header `--sf2` ("INSTRUÇÕES (SYSTEM PROMPT)"
  + pill "markdown"), corpo com **altura fixa** `calc(100vh - 360px)` e `min-height: 220px`,
  `overflow: auto`, padding 14px, Markdown
  renderizado: h1 → 14px/600, h2 → 12.5px/600 com `margin: 12px 0 4px`, parágrafos 12.5px/1.55,
  listas com `padding-left: 18px` e gap 3px, `**bold**` → `<strong>`, `` `code` `` → mono 11.5px em
  `--sf2` com borda `--bd2`, radius 4px, `padding: 1px 4px`. No codebase, usar o renderizador de
  Markdown já existente na tela de detalhe.
  **Altura fixa, não `max-height`**: o Viewport interno do `ScrollArea` do Mantine é estilizado
  com `height: 100%`, que só resolve contra altura explícita do pai; com altura automática a
  rolagem fica inerte. O prompt, portanto, **não** empurra o resto da página — quem define a altura
  da grade é a coluna direita.
- **Direita — quatro cards** (radius 10px, padding `13px 14px`), cada um com header 11px/600
  uppercase tracking 0.05em `--mut2`:
  1. **Modelo** — linhas "Provedor" e "Modelo" (label `--mut` à esquerda, valor mono à direita).
     Se `provider` ou `model` é nulo: callout âmbar "Este agente foi criado antes do multi-provedor.
     Escolha provedor e modelo em Editar."
  2. **Skills** — **lista vertical**, não linha de chips: cada skill é um badge (`padding: 3px 8px`,
     radius 6px, fundo `--sf2`, borda `--bd2`, 11.5px) com a `description` em 11.5px `--mut`
     abaixo, quando existir. A descrição não é decorativa: alimenta o `AgentSkill.Description` do
     Agent Card A2A. Sem skills: "Nenhuma skill declarada." em 11.5px `--mut2`.
  3. **Protocolo A2A** — dois endereços, cada um com rótulo 11px `--mut2` acima e valor em mono
     11px com `word-break: break-all`: **Endpoint de execução** (`a2a.url`) e **Card de descoberta**
     (`a2a.agentCardUrl`, este com link externo ↗). Botão **Copiar** por endereço, que passa a
     "Copiado" por ~1,6s. Quando `a2a` é nulo ou ausente: "O endereço público do servidor não está
     configurado, então não há como exibir os endereços deste agente." Quando o agente está
     inativo: faixa âmbar "Este agente continua descobrível por estes endereços, mas rejeita as
     mensagens que receber enquanto estiver inativo." — as duas coisas valem ao mesmo tempo, e o
     endereço visível sozinho sugere o contrário.
  4. **Datas** — "Criado em" e "Atualizado em", 11.5px `--mut`. No codebase os valores saem de
     `toLocaleString('pt-BR')`, com hora; o protótipo mostra só a data.

> **Nota (resolvida em 09/09/2026).** O card "Protocolo A2A" havia sido removido do protótipo por
> falta de suporte no backend. Ele **já existe no codebase** —
> `features/agents/components/AgentA2ACard.tsx`, alimentado por `agent.a2a: { url, agentCardUrl }`,
> com `CopyButton`, link para o Agent Card e callout âmbar quando o agente está inativo. A coluna
> direita da Visão geral hoje tem quatro cards: Modelo, Skills, Protocolo A2A e Datas. Isso importa
> para o vínculo de conhecimento — ver `CONHECIMENTO.md`, seção 4.1.

#### 3.2 Aba Ferramentas — o vínculo (substitui `/agents/:id/mcp-servers`)

Coluna com gap 11px.

- **Resumo** 12px `--mut`: "{n} servidores vinculados · {m} tools permitidas · as tools são
  descobertas ao vivo em cada servidor".
- **Alerta de erro do save (502)**: card `--dabg` com borda esquerda 3px `--da`; título
  "Não foi possível validar as tools do servidor MCP {nome}" 12.5px/600 e detalhe
  "O servidor não respondeu ao handshake em tempo. Nenhum vínculo foi salvo."
- **Callout "vinculado sem tools"** (âmbar, borda esquerda 3px `--wa`, 12px/500): "Vinculado sem
  tools — {nomes} está(ão) vinculado(s) sem nenhuma tool marcada. Nenhuma ferramenta desse servidor
  será oferecida ao modelo em runtime."
- **Lista de servidores** (card único, uma seção por servidor, separadas por 1px `--bd2`). Mostra
  **todos** os servidores cadastrados, ativos e inativos:
  - Linha: checkbox 15px (vincula/desvincula) · nome 12.5px/600 · badges ("Inativo" cinza,
    "Sem tools" âmbar) · url mono 11px `--mut2` com ellipsis · contador
    "{k} de {total} selecionadas" ou "Não vinculado" (11.5px, `--wa` quando vinculado com 0) ·
    botão "Ver tools"/"Ocultar tools".
  - **Marcar o checkbox** vincula, expande e dispara a descoberta.
  - Se vinculado e servidor inativo: callout âmbar recuado (`margin-left: 40px`): "Servidor inativo:
    as tools marcadas aqui não estão sendo oferecidas ao agente até que ele seja reativado."
  - Área expandida (`padding: 0 14px 12px 40px`), quatro estados de `GET /mcp-servers/{id}/tools`:
    - **carregando**: spinner 12px (borda 2px `--bd`, topo `--ac`, `rotate` 0.7s linear infinite) +
      "Buscando tools...";
    - **erro**: faixa `--dabg` "⚠ Não foi possível buscar as tools deste servidor." + botão
      "Tentar novamente";
    - **vazio**: "Este servidor não oferece nenhuma tool.";
    - **sucesso**: grid `repeat(auto-fill, minmax(260px, 1fr))` de checkboxes — nome mono 12px/500 +
      descrição 11px `--mut2`; **desabilitados com `opacity: .45` se o servidor não está marcado**.
  - Descoberta é **cacheada enquanto a tela está aberta**; só refaz via "Tentar novamente" ou
    "Atualizar".
  - **Drift**: ao chegar a lista de tools, tools salvas que não existem mais são removidas
    silenciosamente da seleção (sem erro, sem aviso).
- **Vazio (nenhum servidor cadastrado)**: bloco com borda tracejada, "Nenhum servidor MCP cadastrado
  ainda." + botão primário **"Cadastrar servidor MCP"** → `/mcp-servers/new`.
- **Barra de salvamento fixa** (só quando há diff, ou durante o save):
  `position: fixed; left/right/bottom: 0`, `padding: 11px 26px`, fundo `--sf`, borda superior 1px
  `--bd`, sombra `0 -6px 20px rgba(0,0,0,.07)`. Texto "Alterações não salvas neste vínculo";
  durante o save, spinner + "Validando {servidor}… ({i}/{total})". Ações: **Descartar** (secundário)
  e **Salvar vínculo** (primário, vira "Salvando…" e fica inerte).

#### 3.3 Aba Delegações

Mesmo padrão da aba Ferramentas, max-width 620px.

- Resumo 12px `--mut`: "{n} agentes-alvo · delegação é unidirecional".
- Input "Buscar agente".
- Lista de checkboxes: nome 12.5px/500 à esquerda; à direita, mono 11.5px, o modelo do agente em
  `--mut2` ou **"(inativo)"** em `--wa`. O próprio agente nunca aparece (não pode delegar para si).
- Mesma barra fixa, com "Alterações não salvas nas delegações" / **Salvar delegações**.

### 4. Lista de servidores MCP (`/mcp-servers`)

- Header "Servidores MCP" + "{n} servidores · fontes de tools para os agentes" + botão
  "Novo servidor MCP". Busca por nome ou url (max-width 340px).
- Grid: `minmax(0,1.6fr) minmax(0,2fr) 130px minmax(0,1.2fr) 110px`.
  Colunas: **Servidor** (nome 13px/600 `--ac`) · **Url** (mono 11.5px `--mut`, ellipsis) ·
  **Autenticação** ("Nenhuma" / "Bearer Token") · **Usado por** ("{n} agentes" ou "Nenhum agente",
  com "⚠ {k} sem tools" abaixo em `--wa`) · **Estado** (badge).
- "Usado por" é **derivado no cliente** a partir de `GET /agents` (a API não tem consulta inversa).
  Ver "Notas de implementação".
- Vazio: "Nenhum servidor MCP cadastrado ainda." / "Nenhum servidor corresponde à busca."

### 5. Detalhe do servidor MCP (`/mcp-servers/:id`)

- Header: "← Servidores MCP", nome + badge de estado, descrição. Ações: **Editar** ·
  **Testar conexão** · **Desativar/Ativar**.
- **Resultado do teste** (`POST /mcp-servers/{id}/test`, usa a credencial cifrada):
  - carregando: card neutro com spinner + "Conectando ao servidor MCP…";
  - sucesso: card `--okbg` borda esquerda 3px `--ok`, "Conexão bem-sucedida — {n} tools
    disponíveis." + linha 11px "testado agora · resultado não é persistido";
  - falha: card `--dabg` borda esquerda 3px `--da`, "Falha na conexão" + **pill mono com o
    `failureReason`** + mensagem específica:
    - `HostUnreachable` — "Host inalcançável — a URL não respondeu. Verifique o endereço e se o
      servidor está no ar."
    - `CredentialRejected` — "Credencial rejeitada — o servidor respondeu 401. Gere um novo token e
      salve a credencial novamente."
    - `CredentialDecryptionFailed` — "Não foi possível decifrar a credencial — a chave de
      criptografia do ambiente mudou desde que ela foi salva. Salve a credencial novamente."
    - `Unknown` — "Falha desconhecida ao conectar ao servidor MCP."
- **Grid `minmax(0,1fr) minmax(0,1.5fr)`, gap 14px, `align-items: start`:**
  - **Configuração**: Url (mono 11.5px, `word-break: break-all`), Autenticação, e quando
    `authType ≠ None` a linha "Credencial · •••••••• cifrada" em `--mut2` (a API nunca devolve o
    valor). Datas em 11.5px `--mut2`.
  - **Catálogo de tools** (`GET /mcp-servers/{id}/tools` — já existe, hoje só usado no fluxo de
    vínculo): header `--sf2` + botão "Atualizar". Estado inicial **idle**, sem disparar rede:
    "As tools são descobertas ao vivo. Clique em Atualizar para consultar o servidor." Depois:
    carregando / erro com "Tentar novamente" / vazio / lista. Cada tool: nome mono 12px/500 +
    descrição 11px `--mut2`, e à direita **"permitida em {n} agentes"** (em `--ac`) ou
    "não permitida em nenhum agente" (em `--mut2`).
- **Agentes que usam este servidor** (visão inversa, card full-width): header `--sf2` com
  "{n} agentes usam este servidor". Grid `minmax(0,1.2fr) minmax(0,2.4fr) 90px`:
  nome do agente (link) · chips mono 11px com as tools permitidas, mais badge âmbar
  **"Vinculado sem tools"** quando `allowedTools` é vazio · estado do agente (Ativo/Inativo).
  Linha clicável → detalhe do agente. Vazio: "Nenhum agente usa este servidor. Desativá-lo não
  afeta ninguém agora."

### 6. Formulário de agente (`/agents/new`, `/agents/:id/edit`)

Max-width 660px, campos com gap 14px, labels 11.5px/600, inputs `padding: 7px 10px`, radius 7px,
borda 1px `--bd`.

- **Nome*** — erro por campo em 11px `--da` abaixo do input.
- **Descrição** — textarea 2 linhas, com hint "Uso interno: ajuda o operador a identificar o agente
  nas listas." (campo novo; já existe na API).
- **Instruções*** — textarea 12 linhas, redimensionável, **fonte mono 12px/1.6**, label
  "— system prompt, aceita Markdown", contador de caracteres 11px `--mut2` abaixo.
- **Provedor*** e **Modelo*** — dois selects lado a lado (grid `1fr 1fr`). Opções vêm de
  `GET /providers`. Trocar o provedor **limpa o modelo**. Modelo desabilitado sem provedor, com
  placeholder "Escolha o provedor primeiro". Valor salvo que não existe mais no catálogo aparece
  como `"{valor} (indisponível)"` **desabilitado** (comportamento atual, manter).
- **Skills** — chips removíveis (× ao lado do nome) + input "ex.: Segunda via de boleto" e botão
  "Adicionar" (campo novo; já existe na API). Hint: "Rótulos do que o agente sabe fazer. Não afetam
  o runtime."
- Ações: **Criar agente / Salvar alterações** (primário) e **Cancelar**.
- Se nenhum provedor está configurado no ambiente: alerta âmbar e **não renderizar o form**
  (comportamento atual, manter).

### 7. Formulário de servidor MCP (`/mcp-servers/new`, `/mcp-servers/:id/edit`)

Max-width 560px.

- **Nome*** · **Descrição** (3 linhas) · **Url*** (mono, placeholder `https://mcp.exemplo.com/sse`) ·
  **Tipo de autenticação*** (Nenhuma / Bearer Token) · **Credencial*** (`type="password"`, mono,
  aparece só quando `authType ≠ None`).
  - Hint da credencial: criação → "Enviada cifrada (AES-GCM) e nunca retorna na API.";
    edição → "Deixe em branco para manter a credencial atual."
- **Unificação do teste de conexão** (lacuna 12 do briefing): quando é edição e a credencial está em
  branco, mostrar a nota "Sem digitar a credencial, o teste usa a credencial salva deste servidor." e
  chamar `POST /mcp-servers/{id}/test` (servidor salvo). Nos outros casos, chamar
  `POST /mcp-servers/test` com a config digitada.
- Estados do teste: carregando ("Testando a configuração…") / sucesso verde / erro vermelho com a
  mesma copy por `failureReason`. O resultado **é invalidado** se url, authType ou credencial mudarem
  depois do teste.
- Ações: **Cadastrar servidor / Salvar alterações** · **Cancelar** · **Testar conexão** (empurrado
  para a direita com `margin-left: auto`).
- Validar Url preenchida antes de testar ("Informe a Url antes de testar.").

### 8. Modal de desativação

Max-width 420px, radius 11px, padding 18px, overlay `rgba(10,10,12,.45)`.

- Agente: "Desativar agente “{nome}”?" + "Mensagens enviadas a este agente enquanto ele estiver
  inativo serão rejeitadas."
- Servidor: "Desativar servidor MCP “{nome}”?" + "Agentes vinculados a este servidor MCP deixarão de
  conseguir usar suas tools enquanto ele estiver inativo." **Mais**, quando houver vínculos, faixa
  âmbar: "Afeta {n} agentes: {nomes}".
- Ações: **Cancelar** (secundário) e **Desativar** (fundo `--da`, texto branco).

---

## Interactions & Behavior

- **Navegação**: linhas de tabela inteiras são clicáveis (hover `--sf2`, `cursor: pointer`). No
  codebase, garantir também um alvo focável por teclado — o nome do item deve ser um `<a>` real.
- **Aba Ferramentas — diff**: comparar o conjunto `{serverId: allowedTools[]}` normalizado (ordenado)
  contra o estado salvo do agente. A barra fixa aparece **somente** se houver diferença.
- **Save do vínculo** (`PUT /agents/{id}/mcp-servers`): é substituição do conjunto inteiro e
  atômica. Antes de salvar, o backend faz handshake com cada servidor que tem ≥1 tool selecionada —
  pode ser lento. Mostrar progresso por servidor. Em 502, o alerta vermelho no topo da aba com
  `title`/`detail` e "Nenhum vínculo foi salvo" — **e manter o rascunho do usuário intacto**.
  Tool inexistente retorna 400.
- **Sucesso**: toast "Vínculo salvo.", rascunho descartado, o agente é atualizado no cache. Diferente
  de hoje, **não navega para outro lugar** — o operador já está no detalhe.
- **Delegações** (`PUT /agents/{id}/delegations`): mesma mecânica, sem handshake.
- **Ativar/desativar**: ativar é imediato; desativar sempre passa pelo modal. Ambos emitem toast.
- **Descoberta de tools**: cache por servidor no escopo da tela; invalidado por "Tentar novamente" /
  "Atualizar".
- **Teste de conexão**: nunca persistido. Exibir "testado {quando}" a partir de um timestamp em
  memória, deixando explícito que não é histórico.
- **Loading**: spinners 12–13px (borda 2px `--bd`, topo `--ac`, radius 50%,
  `animation: rotate .7s linear infinite`) inline no lugar do conteúdo, nunca overlay de página.
- **Responsivo**: a partir de ~900px, os grids de duas colunas (Visão geral, detalhe do servidor)
  passam a uma coluna; tabelas ganham scroll horizontal ou viram cards.

## State Management

Por tela, o que precisa existir (nomes livres; no codebase usar o mesmo data-layer/react-query já
adotado):

- `agents`, `servers`, `providers` — coleções vindas da API.
- `q`, `filter` (lista de agentes) e `sq` (lista de servidores) — busca/filtro client-side.
- `tab` — aba do detalhe do agente; **espelhar na URL**.
- `discovery: { [serverId]: { state: 'idle'|'loading'|'error'|'ok', tools[] } }` — cache da descoberta.
- `attempts: { [serverId]: number }` — só para telemetria/retry; opcional.
- `draft: { agentId, links: { [serverId]: string[] }, expanded: { [serverId]: boolean } }` —
  rascunho do vínculo. Inicializado de `agent.mcpServers`. Ao receber a descoberta, **podar** tools
  que não existem mais.
- `delegDraft: { agentId, ids: string[] }`.
- `saving: { i, total, name } | null` — progresso do handshake.
- `bindError: { title, detail } | null` — erro 502 do save.
- `tests: { [key]: { state, reason, n, at } }` — `key` é o id do servidor, ou `form:{id|new}` para o
  teste dentro do formulário.
- `modal: { kind: 'agent'|'server', id } | null`.
- `form` — campos controlados + `errors: { [campo]: msg }` preenchido a partir do
  `ProblemDetails.errors` das respostas 400.
- `theme` — claro/escuro (já existe no painel).

**Sair de uma aba/rota com rascunho sujo deve avisar** (o protótipo não implementa; no app real,
guard de navegação).

## Notas de implementação (o que a API não dá)

1. **Consulta inversa** ("agentes que usam o servidor X") não existe: derivar de `GET /agents`,
   percorrendo `mcpServers[]`. Usada na lista de servidores (coluna "Usado por"), no detalhe
   (visão inversa e contagem por tool) e no modal de desativação. Se a lista de agentes crescer,
   pedir um `GET /mcp-servers/{id}/agents` ao backend.
2. **Busca, filtro e paginação** não existem: client-side. Com dezenas de itens está ok; documentar
   como dívida.
3. **Não há exclusão** — apenas ativar/desativar. Não desenhar botão de excluir.
4. **Não há saúde nem histórico de conexão** — nada de "última verificação" persistida, nada de
   semáforo de status na lista.
5. **A2A** já existe: `agent.a2a` chega pronto da API (`{ url, agentCardUrl }`), opcional e
   anulável — `undefined` na janela de migração, `null` quando a URL pública do servidor não está
   configurada. Não montar a URL no frontend por concatenação de host + id.
6. **Conhecimento** não existe em nenhuma camada; é feature nova por inteiro. Ver `CONHECIMENTO.md`.

## Design Tokens

Do protótipo, para referência de intenção. **Preferir os tokens equivalentes do tema Mantine do
projeto**; adotar apenas os que não existirem lá (sobretudo os semânticos de aviso/erro/sucesso).

### Cores — tema claro
| token | valor | uso |
|---|---|---|
| `--bg` | `#f6f5f3` | fundo da página |
| `--sf` | `#ffffff` | superfície de cards, sidebar, inputs |
| `--sf2` | `#faf9f8` | headers de tabela, chips, hover |
| `--bd` | `rgba(20,18,15,.11)` | bordas de card/input |
| `--bd2` | `rgba(20,18,15,.06)` | divisores internos |
| `--fg` | `#191a1c` | texto principal |
| `--mut` | `#686d74` | texto secundário |
| `--mut2` | `#8b9097` | texto terciário, labels |
| `--ac` | `#2a6ecb` | accent: ações primárias, links |
| `--acsf` | accent a 8% | fundo do item de nav ativo |
| `--ok` / `--okbg` | `#1a7a49` / `#e9f6ee` | sucesso, badge Ativo |
| `--wa` / `--wabg` | `#8a5a00` / `#fbf1de` | aviso: sem tools, reconfiguração, inativo |
| `--da` / `--dabg` | `#b02a1f` / `#fdeceb` | erro, ação destrutiva |
| `--sh` | `0 1px 2px rgba(16,14,10,.05)` | sombra de card |

### Cores — tema escuro
`--bg #121316` · `--sf #1b1d21` · `--sf2 #22252b` · `--bd rgba(255,255,255,.11)` ·
`--bd2 rgba(255,255,255,.06)` · `--fg #e9eaec` · `--mut #9aa0a8` · `--mut2 #7d838b` ·
`--ac` = accent clareado ~26% · `--ok #4cc38a` · `--wa #e2a63f` · `--da #f0766a` ·
fundos semânticos a 13% de opacidade · sem sombra.

Opções de accent oferecidas: `#2a6ecb` (padrão), `#1f7a5c`, `#7b4bc4`, `#b4531f`.

### Tipografia (protótipo)
- Texto: **IBM Plex Sans** — 21px/600 (título de página), 14px/600 (h1 do Markdown),
  13px/600 (nome em tabela), 12.5px (corpo e botões), 12px (secundário),
  11.5px/600 (labels de form), 11px/600 uppercase tracking .05em (headers de card),
  10.5px/600 (badges).
- Técnico: **IBM Plex Mono** — 12px (textarea de instruções, url em form),
  11.5px (provider/model, url em tabela), 11px (chips de tool, pill de `failureReason`).
- **No codebase, trocar pela família do tema Mantine**; manter a distinção sans/mono — valores
  técnicos (url, nome de tool, provider/model, failureReason) são sempre mono.
- Line-height base 1.45; Markdown 1.55–1.6.

### Espaçamento, raio e densidade
- Padding de página: `22px 26px` (header) / `26px` horizontal no conteúdo.
- Gap entre cards 12–16px; entre campos de form 14px.
- Raio: 5px (badge) · 6px (chip, botão pequeno) · 7px (botão, input) · 9–11px (card, modal).
- **Densidade**: variável `--ry` no padding vertical das linhas de tabela —
  **compacta 7px** (padrão) e **confortável 11px**, com o tamanho de fonte base indo de 12.5px a
  13.5px. Implementar como uma preferência do painel, não como duas telas.

## Assets

Nenhum. Os "ícones" da navegação são placeholders de letra (A / M / C) em quadrados com borda —
**substituir pelo icon set já usado no painel** (Tabler, se for o padrão do Mantine no projeto).
Nenhuma imagem, nenhum SVG customizado.

## Files

- `Buteco Agentes.dc.html` — protótipo completo, abre direto no navegador. Todas as rotas, dados
  fictícios em memória, latências e falhas simuladas.
  - Controles de protótipo disponíveis: navegação lateral completa/compacta, densidade
    compacta/confortável, cor de accent, e **simular 502** — liga a falha do handshake no
    "Salvar vínculo" para inspecionar o alerta de erro.
  - Cenários fictícios embutidos, úteis para conferir estados: o agente "Atendimento Financeiro"
    tem um servidor **vinculado sem tools**; "Triagem de Mensagens" está em **"Precisa de
    reconfiguração"**; "Cobrança Ativa" está **inativo**; o "Servidor CRM" está **inativo mas
    vinculado**; o "Servidor Agenda" **falha na primeira descoberta** e funciona no
    "Tentar novamente"; o "Servidor Notas Fiscais" **não oferece nenhuma tool**; o teste de conexão
    retorna `CredentialRejected` no CRM e `CredentialDecryptionFailed` nas Notas Fiscais.
