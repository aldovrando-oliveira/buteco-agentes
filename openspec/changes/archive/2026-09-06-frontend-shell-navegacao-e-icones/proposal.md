## Why

A casca do painel é o scaffold inicial intacto: um header de 60px com o nome do
produto e o toggle de tema à direita, e um navbar de 260px com três itens de
texto puro. O protótipo do handoff elimina o header, unifica tudo numa barra
lateral de 224px com a marca no topo e o toggle no rodapé, e dá ícone a cada
item de navegação.

Duas coisas tornam esta a etapa certa agora. A primeira é que o tema já está no
lugar (change `frontend-tema-identidade-visual`): o item ativo da navegação
precisa do fundo do accent a ~8%, que só passou a existir depois daquela etapa.
A segunda é que o painel **não tem icon set** — os dois ícones que existem hoje
foram transcritos à mão, path por path, dentro dos componentes que os usam. Sem
uma dependência de ícones, cada item de navegação viraria mais um SVG copiado.

A conferência visual da etapa anterior registrou isto como achado 8, com a
observação de que nada disso é tema e portanto não cabia lá.

## What Changes

- Entra `lucide-react` como dependência de `apps/frontend`, a ser usada sob
  demanda — não como varredura para iconizar botões e alertas.
- Os dois ícones desenhados à mão são substituídos pelos equivalentes do
  Lucide. **Sem mudança visual**: os paths atuais são a geometria do Lucide,
  transcrita; a troca devolve a cópia ao original.
- **BREAKING (estrutura visível)**: o header desaparece. A marca do produto sobe
  para o topo da barra lateral, com o quadrado de identidade ao lado do nome, e
  o toggle de tema desce para o rodapé da barra.
- A barra lateral encolhe de 260px para 224px e cada item de navegação ganha um
  ícone. O item ativo passa a usar o fundo do accent a ~8% com texto na cor de
  destaque, em vez do bloco de largura total do Mantine.
- O `Burger` e o colapso em drawer saem junto com o header. Eram
  `hiddenFrom="sm"`, ou seja, nunca apareciam no desktop; abaixo desse ponto o
  conteúdo passa a apertar em vez de virar gaveta. Decisão do usuário: o painel
  é de desktop, e o rail estreito de 58px do protótipo fica para quando
  incomodar.
- O rodapé **não** exibe nome nem e-mail do operador, ao contrário do protótipo.
  O login devolve apenas token e validade; não há endpoint de identidade nem
  claim no token. Exibir o que a pessoa digitou no formulário seria afirmar o
  que o backend não confirma — o mesmo tipo de invenção que as etapas
  anteriores recusaram. O lugar fica preparado para quando o dado existir.

Fora de escopo: o acabamento estrutural das telas (achados 1 a 6 e 9 da nota da
etapa anterior), a preferência de densidade e o card "Primeiros passos".

## Capabilities

### New Capabilities
- `frontend-app-shell`: a casca do painel como contrato — a barra lateral única
  com marca, navegação com ícone e estado ativo, o rodapé com o controle de
  tema, e a regra de que o conteúdo roteado vive na área principal. Hoje esses
  requisitos estão diluídos no scaffold, que é sobre bootstrap de projeto; a
  casca virou superfície de produto com decisões próprias e passa a ter spec
  própria.

### Modified Capabilities
- `frontend-scaffold`: o requisito "Mantine configurado com AppShell inicial"
  descreve um shell com "navbar/header estruturais" e exige header e navbar
  visíveis. Passa a cobrir só o que é scaffold — as bibliotecas, os providers e
  o `<Outlet/>` —, delegando a composição da casca para `frontend-app-shell`. O
  requisito de roteamento tem um cenário que afirma que "o header e a navbar
  permanecem visíveis" ao navegar; passa a afirmar a barra lateral.

## Impact

Afeta **apenas `apps/frontend`**. Nenhuma mudança em `apps/api`, `apps/workers`
ou em contrato de rota HTTP.

- O componente de casca é reescrito: sai o header, entram as seções de topo,
  navegação e rodapé da barra lateral.
- O componente de campos de skills perde o ícone desenhado à mão.
- Uma dependência nova, sem dependências de runtime próprias.
- Os testes de casca ganham cobertura do que a estrutura nova promete: a marca,
  o ícone por item, o item ativo por grupo de rotas e o toggle no rodapé. Os
  casos de esquema de cor da etapa anterior seguem valendo sem alteração.
- Risco de regressão visual concentrado numa tela só — a casca aparece em
  todas, mas é um componente. A conferência é manual, como na etapa anterior:
  a suíte não enxerga layout.
