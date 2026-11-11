## ADDED Requirements

### Requirement: Aba Agentes do detalhe da aplicação
A aba "Agentes" de `/applications/{id}` SHALL mostrar, no cartão "Agentes liberados" (faixa cinza com "Adicionar agente", botão pequeno azul, à direita), a grade de `GET /applications/{id}/agents` como o quadro `AplicacaoDetalhe` (`aba=agentes`):

- as colunas Agente (o nome, com link para `/agents/{agenteId}`, e a descrição), "Enviar mensagem", "Consultar task", "Notificação push" e Ações, com ✓ ou — por operação e o texto acessível "Permitido" ou "Não permitido";
- o selo "Inativo" ao lado do nome do agente com `ativo` `false`;
- "Editar" e "Remover" em cada linha;
- abaixo da grade, "Agentes alcançados por delegação não precisam ser liberados.";
- com a lista vazia, "Nenhum agente liberado. Enquanto a lista estiver vazia, toda chamada A2A desta aplicação é recusada.";
- o contador da aba com o tamanho da lista.

A carga SHALL mostrar linhas fantasmas no cartão; a falha que não é `403`, "Não foi possível carregar os agentes." com "Tentar de novo", sem derrubar o cabeçalho.

#### Scenario: Agente inativo na grade
- **WHEN** a lista traz um agente com `ativo` `false` e `operacoes` `["enviar-mensagem"]`
- **THEN** a linha mostra o selo "Inativo", ✓ em "Enviar mensagem" e — nas outras duas

#### Scenario: Aplicação recém-criada
- **WHEN** a lista vem vazia
- **THEN** o cartão mostra "Nenhum agente liberado. Enquanto a lista estiver vazia, toda chamada A2A desta aplicação é recusada.", e o contador da aba é 0

#### Scenario: Falha da grade
- **WHEN** `GET /applications/{id}/agents` falha com `500`
- **THEN** o cartão mostra "Não foi possível carregar os agentes." e o cabeçalho da aplicação continua na tela

### Requirement: Adicionar agente dentro do cartão
"Adicionar agente" SHALL abrir, dentro do cartão e acima da grade, o formulário "Novo agente para esta aplicação", sem janela modal:

- "Agente": o seletor "Selecione um agente…" com os agentes de `GET /agents/` que não estão na grade, com " (inativo)" no nome do inativo, e a descrição do escolhido abaixo; sem nenhum disponível, "Todos os agentes já estão liberados para esta aplicação." no lugar do seletor;
- "O que a aplicação pode fazer com este agente": as três operações com a descrição ("Inicia conversa ou task", "Acompanha o andamento", "Aviso ao concluir"), "Enviar mensagem" e "Consultar task" marcadas de início;
- com o agente inativo escolhido, "Este agente está inativo. Ele fica liberado, mas as tasks que a aplicação abrir nele terminam recusadas até ele ser ativado.";
- sem nenhuma operação marcada, "Marque ao menos uma operação." e "Adicionar" desabilitado; sem agente escolhido, "Adicionar" desabilitado;
- "Adicionar" (azul) e "Cancelar".

"Adicionar" SHALL enviar `POST /applications/{id}/agents` com `{ agenteId, operacoes }`, as operações na ordem da tabela do vínculo. As respostas:

| resposta | o que a tela mostra |
|---|---|
| `201` | o formulário fecha e a linha aparece na grade |
| `409` | o `title` do servidor no formulário ("Este agente já está liberado para a aplicação."), a grade e o seletor recarregados, o seletor limpo e o formulário aberto |
| `400` com `errors.agenteId` | "Agente não encontrado." no seletor, e o catálogo recarregado |
| `400` com `errors.operacoes` | a mensagem abaixo das operações |

#### Scenario: Adicionar com as operações de início
- **WHEN** o Admin abre o formulário, escolhe "Suporte Técnico" e aciona "Adicionar"
- **THEN** o corpo é `{ agenteId, operacoes: ["enviar-mensagem", "consultar-task"] }`, e a linha aparece na grade

#### Scenario: Já liberado por outro Admin
- **WHEN** `POST …/agents` responde `409` com `title` "Este agente já está liberado para a aplicação."
- **THEN** o formulário mostra esse `title`, e a grade recarregada traz o agente

#### Scenario: O agente sumiu
- **WHEN** `POST …/agents` responde `400` com `errors.agenteId` "Agente não encontrado."
- **THEN** o seletor mostra "Agente não encontrado.", e `GET /agents/` é chamado de novo

#### Scenario: Nenhum agente disponível
- **WHEN** todos os agentes de `GET /agents/` estão na grade
- **THEN** o formulário mostra "Todos os agentes já estão liberados para esta aplicação.", e "Adicionar" está desabilitado

### Requirement: Editar as operações na linha
"Editar" SHALL trocar só a linha por caixas nas três operações, com "Salvar" (azul) e "Cancelar" na coluna Ações; sem nenhuma caixa marcada, "Marque ao menos uma operação" no lugar da descrição e "Salvar" desabilitado. "Salvar" SHALL enviar `PUT /applications/{id}/agents/{agenteId}` com `{ operacoes }`; o `200` volta a linha ao normal. O `404` SHALL mostrar, em vermelho no cartão, "Agente não liberado para esta aplicação. A lista foi atualizada." e recarregar a grade.

#### Scenario: Vínculo retirado por outro Admin
- **WHEN** o Admin edita a linha de "Atendimento Varejo" e `PUT` responde `404` com `title` "Agente não liberado para esta aplicação."
- **THEN** o cartão mostra "Agente não liberado para esta aplicação. A lista foi atualizada.", e a grade recarregada não traz a linha

#### Scenario: Sem operação não salva
- **WHEN** o Admin desmarca as três caixas da linha
- **THEN** "Salvar" está desabilitado e nenhum `PUT` é feito

### Requirement: Remover com confirmação na linha
"Remover" SHALL abrir, numa faixa logo abaixo da linha e sem janela modal, "Remover o acesso da aplicação a {agente}? A próxima chamada dela a este agente é recusada; as tasks já abertas não mudam.", com "Remover" vermelho e "Cancelar". Só a confirmação SHALL enviar `DELETE /applications/{id}/agents/{agenteId}`. O `204` **e o `404`** SHALL tirar a linha e recarregar a grade, sem mensagem de erro: o vínculo pedido já não existe. Outra falha SHALL aparecer na faixa.

#### Scenario: Remover pede confirmação
- **WHEN** o Admin aciona "Remover"
- **THEN** a faixa aparece e nenhum `DELETE` é feito antes de "Remover" na faixa

#### Scenario: Já retirado por outro Admin
- **WHEN** o Admin confirma a remoção e `DELETE` responde `404`
- **THEN** a linha sai, a grade é recarregada, e nenhuma mensagem de erro aparece

### Requirement: Uma ação por vez na aba Agentes
Enquanto o formulário de adição, a edição de uma linha ou uma confirmação de remoção estiver aberta, as outras ações do cartão SHALL ficar desabilitadas: "Adicionar agente" durante a edição e a confirmação; "Editar" e "Remover" das linhas durante a adição, a edição de outra linha e a confirmação.

#### Scenario: Editar uma linha bloqueia o resto
- **WHEN** o Admin está editando a linha de "Atendimento Varejo"
- **THEN** "Adicionar agente" e o "Editar" e o "Remover" das outras linhas estão desabilitados

### Requirement: Aplicação desativada na aba Agentes
Com a aplicação desativada, o cartão SHALL mostrar, abaixo da faixa, "Aplicação desativada: as chamadas A2A dela são recusadas. Os agentes liberados ficam guardados e voltam a valer ao reativar.", e adicionar, editar e remover SHALL continuar disponíveis.

#### Scenario: Desativada continua editável
- **WHEN** a aplicação está desativada
- **THEN** o aviso aparece, e "Adicionar agente", "Editar" e "Remover" estão habilitados

### Requirement: Card "Aplicações com acesso" no detalhe do agente
A Visão geral de `/agents/{id}` SHALL ter, na coluna da direita, logo abaixo de "Protocolo A2A", o card "Aplicações com acesso", para os três papéis, como o quadro `AgenteDetalhe`:

- **Admin:** `GET /agents/{id}/applications`; "Gerenciar" na faixa, para `/applications`; uma linha por aplicação com o nome (link para `/applications/{id}`), o selo "Desativada" quando `ativa` é `false`, as operações por extenso separadas por " · " e "aplicação usada há …" ou "aplicação nunca usada". Sem nenhuma: "Nenhuma aplicação externa tem acesso a este agente.".
- **Operador e Leitor:** `GET /agents/{id}/applications/count`, **e nunca a lista**; "Usado por **{ativas} aplicações externas ativas** ({total} no total).", com "1 aplicação externa ativa" no singular; com `total > 0` e `ativas = 0`, "Usado por {total} aplicações externas, todas desativadas." ("Usado por 1 aplicação externa, desativada." no singular); com `total = 0`, "Nenhuma aplicação externa usa este agente.". Sem "Gerenciar", sem nomes e sem links.
- **Rodapé, para os três**, só com aplicações ativas: "Desativar este agente afeta **{N} aplicações ativas**: as tasks que elas abrirem nele passam a terminar recusadas."; no singular, "Desativar este agente afeta **1 aplicação ativa**: as tasks que ela abrir nele passam a terminar recusadas.". N é `ativas` da contagem, ou as linhas com `ativa` da lista.
- **Carga e falha** no próprio card: "Carregando…"; "Não foi possível carregar as aplicações." com "Tentar de novo"; o `403` mostra o aviso de ação de `panel-roles` no card. Nenhum dos três derruba a página.

#### Scenario: Admin vê a lista
- **WHEN** um Admin abre um agente com três aplicações, uma desativada
- **THEN** o card lista as três com os links, o selo "Desativada" na terceira, e o rodapé diz "afeta 2 aplicações ativas"

#### Scenario: Leitor vê só a contagem
- **WHEN** um Leitor abre o mesmo agente e a contagem responde `{ total: 3, ativas: 2 }`
- **THEN** o card diz "Usado por 2 aplicações externas ativas (3 no total).", sem nomes, sem links e sem "Gerenciar", e nenhum `GET /agents/{id}/applications` é feito

#### Scenario: Operador com só desativadas
- **WHEN** um Operador abre um agente com `{ total: 2, ativas: 0 }`
- **THEN** o card diz "Usado por 2 aplicações externas, todas desativadas.", sem rodapé

#### Scenario: Nenhuma aplicação
- **WHEN** um Admin abre um agente sem aplicação, e depois um Operador
- **THEN** o Admin vê "Nenhuma aplicação externa tem acesso a este agente." com "Gerenciar", o Operador vê "Nenhuma aplicação externa usa este agente.", e nenhum dos dois vê o rodapé

#### Scenario: Falha do card
- **WHEN** a rota do card responde `500`
- **THEN** o card mostra "Não foi possível carregar as aplicações." com "Tentar de novo", e o resto da Visão geral continua
