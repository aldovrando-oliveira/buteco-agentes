## Context

A casca do painel nunca foi tocada desde o scaffold: header de 60px com o nome
do produto à esquerda e o toggle de tema à direita, navbar de 260px com três
`NavLink` de texto puro, e um `Burger` `hiddenFrom="sm"` que colapsa a navbar em
gaveta abaixo desse ponto.

O protótipo do handoff descarta essa divisão: barra lateral única de 224px, com
o quadrado de identidade e o nome do produto no topo, a navegação com ícone no
meio, e o toggle de tema no rodapé. Sem header.

A etapa anterior (`frontend-tema-identidade-visual`) deixou dois pré-requisitos
prontos. O tom de fundo do item ativo — o `--acsf` do protótipo, accent a 8% —
existe agora como o tom 1 da escala de destaque. E a conferência visual daquela
etapa registrou a ausência de ícones como achado 8, separando explicitamente o
que era tema do que era casca.

O painel **não tem icon set**. Os dois ícones existentes foram transcritos à
mão, path por path, dentro dos componentes que os usam.

## Goals / Non-Goals

**Goals:**
- Uma barra lateral única que carregue marca, navegação e controle de tema.
- Ícone em cada item de navegação, vindo de uma biblioteca em vez de SVG
  copiado.
- Estado ativo que corresponda ao protótipo: fundo do accent a 8%, texto na cor
  de destaque, raio de 7px, sem ocupar a largura inteira.
- Nenhuma regressão nos comportamentos de esquema de cor entregues na etapa
  anterior.

**Non-Goals:**
- Acabamento estrutural das telas — faixa e divisores de tabela, badges na
  variante clara, link de volta no detalhe, rótulo do campo de busca (achados 1
  a 6 e 9 da nota da etapa anterior).
- Iconizar botões, alertas e estados vazios. A dependência entra para ser usada
  sob demanda.
- Rail estreito de 58px, preferência de densidade, card "Primeiros passos".
- Identificação do operador no rodapé.

## Decisions

### D1 — `lucide-react` como biblioteca de ícones

Versão 1.41.0, verificada no registry em 2026-09-06: peer de React
`^16.5.1 || ^17 || ^18 || ^19`, compatível com o React 19.2 do projeto, e sem
nenhuma dependência de runtime própria.

*Alternativa descartada:* `@tabler/icons-react`, que é o que a documentação do
Mantine usa. São ~5.900 ícones contra ~1.600, e o barrel import do Tabler é
conhecido por deixar o servidor de desenvolvimento do Vite lento no primeiro
boot. Para um painel que precisa de meia dúzia de ícones, o menor com melhor
tree-shaking serve melhor.

*Alternativa descartada:* continuar desenhando à mão. Funciona para dois
ícones; a partir do terceiro item de navegação vira cópia sistemática de paths
que alguém vai ter que manter alinhados em peso e tamanho.

### D2 — A troca dos dois ícones existentes é substituição, não redesenho

Os paths atuais **são** a geometria do Lucide, transcrita:

```
MoonIcon    M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79Z
            └─ o path do `moon`, caractere por caractere
SunIcon     <circle r="4"/> + oito raios num único path
RemoveIcon  M18 6 6 18M6 6l12 12
            └─ os dois traços do `x`, concatenados
```

Trocar não muda um pixel. É a forma mais barata de estrear a dependência: se
algo quebrar visualmente na casca, não terá sido o ícone.

Os nomes exatos dos ícones de navegação ficam para confirmar na implementação,
contra o pacote instalado — este documento registra a **intenção** de cada um
(algo que remeta a agente, a servidor e a conversa), não um nome que eu não
verifiquei.

### D3 — O header sai; a barra lateral ganha três seções

`AppShell` do Mantine aceita ser montado sem `header`. A barra passa a usar
`AppShell.Section`: topo com a marca, meio com `grow` e rolagem para a
navegação, rodapé com o controle de tema.

A marca é o quadrado de 24px com raio de 6px, fundo na cor de destaque e a
letra "B" em monoespaçada branca, ao lado do nome do produto, com borda
inferior separando do bloco de navegação.

### D4 — O `Burger` e o colapso em gaveta saem junto

O `Burger` era `hiddenFrom="sm"` — nunca apareceu no desktop. Abaixo desse
ponto o conteúdo passa a apertar em vez de virar gaveta.

Decisão do usuário, tomada com o dado na mão: o painel é de desktop. O rail
estreito de 58px que o protótipo oferece como segunda variante fica para quando
a largura incomodar de fato. Some junto o único estado de interação da casca,
que é simplificação e não perda.

### D5 — O item ativo precisa de dois ajustes sobre o default do Mantine

O `NavLink` na variante clara já lê exatamente o tom certo de fundo:

```css
--nl-bg:    var(--mantine-primary-color-light);        /* tom 1 = o --acsf do protótipo */
--nl-color: var(--mantine-primary-color-light-color);  /* tom 9 = azul-marinho profundo */
```

O fundo cai certo por construção — o tom 1 da escala de destaque é o accent a
8% sobre branco, que é como o protótipo define o `--acsf`. **A cor do texto,
não:** o protótipo quer o item ativo na cor de destaque, e a variante clara usa
o tom 9, bem mais escuro.

Além disso o `NavLink` é um bloco de largura total sem raio. O protótipo quer
raio de 7px com recuo — que é o `sm` do tema, já correto, mais o padding da
barra.

Os dois se resolvem no componente da casca, não no tema: sobrescrever a cor do
texto ativo e pedir o raio. Um override global de `NavLink` no tema seria
tentador, mas o `NavLink` não é usado em mais lugar nenhum do painel — colocar
no tema seria declarar uma regra de aplicação inteira para um único uso.

### D6 — O rodapé não identifica o operador

O protótipo põe avatar e e-mail no rodapé. O resultado do login devolve apenas
token e validade; não há endpoint de identidade nem claim no token, e o nome de
usuário digitado no formulário é descartado.

Exibi-lo seria mostrar o que a pessoa digitou, não o que o backend confirma —
a mesma categoria de invenção que as etapas anteriores recusaram (contagem de
tools que a API não dá, progresso que ninguém mediu, canais que a tela não
consulta). O rodapé fica com o controle de tema, e o lugar preparado para
quando o dado existir.

### D7 — Os testes cobrem estrutura, não aparência

A suíte não enxerga layout. O que dá para afirmar é o contrato de navegação: os
três itens existem e apontam para as rotas certas, cada um tem ícone acessível
como decoração, o item correspondente ao grupo de rotas atual está marcado como
ativo — inclusive em rota profunda, não só na raiz do grupo —, a marca do
produto aparece uma vez, e o controle de tema continua no documento com os
mesmos rótulos.

Os três casos de esquema de cor entregues na etapa anterior seguem valendo sem
alteração: o controle muda de lugar, não de comportamento.

### Árvore de arquivos

```
apps/frontend/
├── package.json                      (M) + lucide-react
└── src/
    ├── components/layout/
    │   ├── AppShell.tsx              (M) header removido; barra lateral em três
    │   │                                 seções; ícones; item ativo (D3, D5)
    │   └── AppShell.test.tsx          (M) cobertura da estrutura nova (D7)
    └── features/agents/components/
        └── AgentSkillsFields.tsx     (M) ícone à mão → Lucide (D2)
```

Nenhum arquivo em `apps/api` ou `apps/workers`. Nenhuma referência entre apps.

## Risks / Trade-offs

**A casca aparece em todas as telas** — um erro aqui é visível em toda a
aplicação, não numa página. → *Mitigação:* é um componente só, com os testes de
estrutura do D7 e a conferência manual. E o risco é bem menor que o da etapa
anterior, que mudava tokens lidos por todas as telas ao mesmo tempo.

**Abaixo de `sm` o conteúdo aperta** — sem gaveta, uma janela estreita perde
224px para a barra. → *Mitigação:* aceito por decisão explícita. O caminho de
volta é o rail de 58px do protótipo, que não precisa de estado novo — só de
largura condicional e rótulo acessível nos itens.

**A dependência pode virar convite para iconizar tudo** — → *Mitigação:*
registrado aqui e na proposta que ela entra para uso sob demanda. A linguagem
visual de ícones do painel não foi desenhada por ninguém, e o protótipo não a
resolve: os "ícones" dele são letras em quadradinhos, explicitamente marcados
como placeholder.

**Os nomes dos ícones de navegação ainda não foram verificados** — → *Mitigação:*
é tarefa da implementação, contra o pacote instalado, e falha ruidosamente no
typecheck se um nome não existir.

## Migration Plan

Uma implantação, sem etapas: frontend estático, o build sai inteiro. Nenhuma
migração de dados, feature flag ou compatibilidade a manter.

**Rollback:** reverter o commit. A casca volta ao header com navbar, e a
preferência de esquema de cor salva no navegador de cada operador continua
válida nos dois mundos.

## Open Questions

Nenhuma. As decisões de produto foram fechadas antes desta proposta: Lucide sob
demanda, estrutura da casca sim, identificação do operador não, e história de
mobile deliberadamente ausente.
