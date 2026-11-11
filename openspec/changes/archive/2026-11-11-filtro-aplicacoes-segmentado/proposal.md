**Issue:** #270 (defeito de entrega da M2, relacionado à #188, PR #267). Acompanhamento: #195, fechada; a issue entra na milestone da M2 sem reabrir o acompanhamento.

## Why

Na homologação da M2, o dono achou um desvio de padrão na lista de Aplicações externas: o filtro de estado é
uma lista suspensa (`NativeSelect`) com o rótulo visível "Estado", e a busca tem largura fixa (`w={360}`). As
outras listas do painel (`AgentListPage`, `KnowledgeBaseListPage`) usam o controle segmentado, na linha da busca,
sem rótulo visível, e escondem a linha inteira quando a lista está vazia.

A origem foi o protótipo: o quadro `AplicacoesLista` desenhava a lista suspensa, e a #188 o seguiu. O quadro foi
corrigido na versão 69, com o controle segmentado "Todas | Ativas | Desativadas", sem rótulo, na linha da busca,
e a linha dos controles escondida no estado `vazia`.

## What Changes

**`ApplicationsListPage`** (`apps/frontend/src/features/applications/pages/`):
- o `NativeSelect` e o `Text` "Estado" saem; entra o `SegmentedControl` com as props do `AgentListPage`
  (`size="xs"`, `withItemsBorders={false}`, a borda `var(--mantine-color-default-border)`, `flexShrink: 0`), os
  valores `todas`, `ativas` e `desativadas`, os rótulos "Todas", "Ativas" e "Desativadas", e `aria-label="Estado"`;
- a busca passa de `w={360}` a `maw={340}` com `style={{ flex: 1 }}`, e a linha a `Group gap="sm" wrap="nowrap"`;
- a linha dos controles some no estado `vazia` (nenhuma aplicação, sem busca e com Todas) e continua na
  `busca-sem-resultado`;
- a busca no servidor (`?busca=`, 300 ms de espera) e o `?estado=` ficam como estão.

**Spec `panel-external-applications`:** o requisito "Lista de aplicações externas" descreve o filtro como
controle segmentado com o nome acessível "Estado", diz que a linha dos controles some na `vazia`, e ganha o
cenário "O filtro de estado é o controle segmentado". O cenário "Vazia e sem resultado são estados diferentes"
passa a partir de dois pontos, porque a `vazia` não tem mais a busca para digitar.

**Documentação:** `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`.

**Fora:** a rota, o contrato, as outras telas da #188 (o `NativeSelect` "Agente" da aba Agentes é campo de
formulário, não filtro) e as outras listas do painel (nenhuma usa lista suspensa como filtro de estado; design D3).

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `panel-external-applications`: o requisito "Lista de aplicações externas", com o filtro no controle segmentado
  e a linha dos controles fora da `vazia`.

## Impact

- **Arquivos:** `ApplicationsListPage.tsx` e `ApplicationsListPage.test.tsx`.
- **Deploy:** só a imagem do `frontend`.
- **Suíte:** `frontend`.
