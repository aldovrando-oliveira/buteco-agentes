**Issue:** #272 (defeito de entrega da M2; relacionadas: #270, PR #271, e #188, PR #267). Acompanhamento: #195, fechada; a issue entra na milestone da M2 sem reabrir o acompanhamento.

## Why

Na lista de Aplicações externas, a linha da busca e do filtro de estado só some no estado `vazia`. As listas de
Agentes e de Bases a escondem também na carga e no erro, porque só a mostram com dados. Por isso, numa instalação
sem nenhuma aplicação, a linha aparece na primeira carga e some assim que chega o `200 []`.

A #270 registrou a diferença no design (D2) como deliberada. O PR #271 foi mergeado antes do ajuste pedido pelo
dono, que vira esta correção.

## What Changes

**`ApplicationsListPage`:**
- a linha da busca e do filtro só aparece depois da primeira resposta com dados. Não aparece na primeira carga,
  no erro da primeira carga nem na lista vazia, como no `AgentListPage`;
- daí em diante, ela continua nas buscas e trocas de filtro seguintes, inclusive enquanto a nova lista carrega e
  quando ela falha, porque a busca vai ao servidor e não pode sumir enquanto a pessoa digita.

**Spec `panel-external-applications`:** o requisito "Lista de aplicações externas" diz quando a linha aparece;
as linhas `carregando` e `erro` da tabela de estados dizem que ela não aparece na primeira carga; dois cenários
novos.

**Documentação:** `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`.

**Fora:** a rota, a busca no servidor, a espera de 300 ms, o `?estado=`, os textos e as outras listas.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `panel-external-applications`: o requisito "Lista de aplicações externas", com a regra de quando a linha da
  busca e do filtro aparece.

## Impact

- **Arquivos:** `ApplicationsListPage.tsx` e `ApplicationsListPage.test.tsx`.
- **Deploy:** só a imagem do `frontend`.
- **Suíte:** `frontend`.
