## MODIFIED Requirements

### Requirement: Lista de aplicações externas
`apps/frontend` SHALL mostrar, em `/applications`, a lista de `GET /applications/` como o quadro `AplicacoesLista` (`entrega=m2`):

- o título "Aplicações externas", o subtítulo "Sistemas que chamam agentes pelo protocolo A2A. Cada aplicação só alcança os agentes liberados para ela." e "Nova aplicação" (azul), que leva a `/applications/new`;
- a busca "Buscar por nome ou client ID" e o filtro de estado, um controle segmentado "Todas | Ativas | Desativadas" com o nome acessível "Estado" e sem rótulo visível, na mesma linha da busca, como nas listas de agentes e de bases, enviados ao servidor como `?busca=` (depois de 300 ms sem digitar, aparada, ausente quando vazia) e `?estado=ativas|desativadas` (ausente em Todas), mantendo a lista anterior na tela enquanto a nova carrega;
- a linha da busca e do filtro SHALL aparecer só depois da primeira resposta com dados, e daí em diante SHALL continuar na tela nas buscas e trocas de filtro seguintes, inclusive enquanto a nova lista carrega e quando ela falha, porque a busca vai ao servidor e não pode sumir enquanto a pessoa digita; ela SHALL NOT aparecer na primeira carga, no erro da primeira carga nem na lista vazia, como nas listas de agentes e de bases;
- as colunas Aplicação (o nome, com link para `/applications/{id}`, e a descrição), Identidade (o `clientId` e "Credencial do Buteco"), Agentes liberados (um chip por item de `agentes`, ou "Nenhum" quando a lista vem vazia), Estado ("Ativa" ou "Desativada") e Último uso (relativo, "há 3 min"; "Nunca usada" quando `ultimoUso` é `null`);
- o rodapé "{N} aplicações · as desativadas continuam listadas e podem ser reativadas.", com "1 aplicação" no singular.

Os estados sem tabela SHALL ser:

| estado | quando | o que a tela mostra |
|---|---|---|
| vazia | `200 []` sem busca e com Todas | "Nenhuma aplicação externa cadastrada.", "Cadastre uma aplicação para um sistema de fora chamar agentes pelo A2A." e "Nova aplicação", sem a linha da busca e do filtro |
| busca sem resultado | `200 []` com busca ou filtro | "Nenhuma aplicação encontrada para "{busca}"." — com o filtro, terminando em " entre as ativas." ou " entre as desativadas." — e "Limpar busca", que esvazia a busca e volta o filtro a Todas |
| carregando | a primeira carga | as linhas fantasmas, com `role="status"`, sem a linha da busca e do filtro |
| erro | falha da carga que não é `403` | "Não foi possível carregar as aplicações." com "Tentar de novo", em `role="alert"`; sem a linha da busca e do filtro se a falha é a da primeira carga |

#### Scenario: A lista do quadro
- **WHEN** `GET /applications/` devolve "Portal do Parceiro" com dois agentes, "Chatbot do site" sem agentes e nunca usada, e "CRM legado" desativada
- **THEN** a tabela mostra os chips do primeiro, "Nenhum" e "Nunca usada" no segundo, "Desativada" no terceiro, e o rodapé "3 aplicações · as desativadas continuam listadas e podem ser reativadas."

#### Scenario: Uma aplicação no rodapé
- **WHEN** a lista tem uma aplicação
- **THEN** o rodapé começa por "1 aplicação ·"

#### Scenario: A busca vai ao servidor
- **WHEN** o Admin digita "erp" na busca e escolhe Ativas
- **THEN** a chamada seguinte é `GET /applications/?busca=erp&estado=ativas`, feita depois da espera, e nenhuma chamada sai a cada tecla

#### Scenario: O filtro de estado é o controle segmentado
- **WHEN** a lista tem aplicações
- **THEN** a linha da busca mostra o grupo de opções "Estado", com "Todas" (marcada), "Ativas" e "Desativadas", e nenhuma lista suspensa

#### Scenario: Vazia e sem resultado são estados diferentes
- **WHEN** a resposta é `200 []` sem busca e com Todas
- **THEN** a página mostra "Nenhuma aplicação externa cadastrada.", sem a busca e sem o filtro
- **WHEN** a lista tem aplicações, e a busca "erp" com o filtro Ativas responde `200 []`
- **THEN** a página mostra "Nenhuma aplicação encontrada para "erp" entre as ativas." com "Limpar busca", com a busca e o filtro na tela, e "Limpar busca" esvazia a busca e volta o filtro a Todas

#### Scenario: A primeira carga não mostra a busca nem o filtro
- **WHEN** a primeira carga ainda não respondeu, ou falhou com `500`
- **THEN** a página mostra as linhas fantasmas, ou o erro com "Tentar de novo", sem a busca e sem o filtro, e a linha aparece quando a lista chega

#### Scenario: Na busca seguinte, a busca e o filtro continuam na tela
- **WHEN** a lista já chegou, e o Admin digita na busca ou troca o filtro enquanto a nova lista carrega, ou a nova lista falha com `500`
- **THEN** a busca, com o texto digitado, e o filtro, com a opção escolhida, continuam na tela

#### Scenario: Erro de carga
- **WHEN** `GET /applications/` falha com `500`
- **THEN** a página mostra "Não foi possível carregar as aplicações." e "Tentar de novo" chama a rota de novo
