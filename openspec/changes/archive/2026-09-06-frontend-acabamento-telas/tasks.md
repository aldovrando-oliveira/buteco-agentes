## 1. Componentes compartilhados

- [x] 1.1 Criar o componente de card seccionado (D1): cabeçalho sobre superfície própria com borda abaixo, e uma seção por linha com borda entre elas, sem borda solta quando há uma linha só — o padding sai do card e vai para cada seção
- [x] 1.2 Criar o componente de rótulo de seção (D2) com o espaçamento entre letras, eliminando as cinco cópias locais. O tom terciário do achado 2 foi **recusado**: mede 3,06:1 sobre a faixa do cabeçalho, abaixo do mínimo que a identidade visual exige, e nenhum tom mais claro que o atual passa
- [x] 1.3 Criar o componente de cabeçalho de página de detalhe (D4) com link de volta nomeando a listagem de origem, título, estado e descrição
- [x] 1.4 Cobrir os três com teste de contrato: que recebem e renderizam o que prometem, e que o link de volta aponta para a rota da listagem

## 2. Badge no tema

- [x] 2.1 Declarar no tema a variante clara e a preservação da caixa como padrão do badge (D3) — sem editar nenhum dos dezenove call sites
- [x] 2.2 Afirmar o padrão no teste de tema que já existe, junto das âncoras de cor
- [x] 2.3 Conferir que os badges de aviso em âmbar e os de estado em verde deixaram de usar a variante preenchida, que não atinge o contraste mínimo

## 3. Catálogo de tools do servidor MCP

- [x] 3.1 Migrar o catálogo para o card seccionado, com o cabeçalho em faixa e uma seção por tool (achado 1)
- [x] 3.2 Corrigir o defeito da coluna direita, que é comprimida pela descrição ao lado e quebra em duas linhas (achado 4, D6)
- [x] 3.3 Trocar o rótulo local pelo componente compartilhado

## 4. Listagens

- [x] 4.1 Migrar as três tabelas para o card seccionado, com faixa no cabeçalho — o divisor entre as linhas já vinha de graça: o Mantine liga `withRowBorders` por padrão e usa a mesma cor de borda da identidade visual
- [x] 4.2 Aplicar o rótulo de seção às colunas do cabeçalho da tabela, em maiúsculas com espaçamento entre letras
- [x] 4.3 Tirar o rótulo visível dos **três** campos de busca — as duas listagens e a aba Delegações, que também tinha rótulo e largura máxima fora do padrão, mantendo o texto de exemplo como nome acessível (achado 9, D5)
- [x] 4.4 Pôr busca e filtro na mesma linha, com o controle segmentado compacto, com borda e sem os separadores verticais que são default do Mantine (resíduo do achado 10, D5)
- [x] 4.5 Atualizar os testes de busca: a premissa desta tarefa estava errada, os quinze casos buscavam pelo rótulo visível `Buscar`, e não pelo texto de exemplo. O nome acessível mudou de fato, e os testes passaram a usar o novo
- [x] 4.6 Alinhar a listagem de canais às outras duas: contagem de cadastrados no subtítulo e busca por nome, tipo e agente responsável, que ela era a única a não ter

## 5. Detalhes

- [x] 5.1 Adotar o cabeçalho compartilhado nas três páginas de detalhe, com o link de volta (achado 6). O detalhe de canal não tinha cabeçalho de página nenhum — título e estado moravam dentro da aba de configuração e subiram para o cabeçalho, eliminando a duplicação
- [x] 5.2 Trocar pelos componentes compartilhados os rótulos de seção restantes nos cards de detalhe e nas abas. Trocar o rótulo **não** era migrar o card: os seis cards que o usam continuavam com card cru, sem faixa. Migrados na 5.3
- [x] 5.3 Migrar para o card seccionado **todos** os cards que têm rótulo de seção, não só os que exibem linhas — instruções, modelo, datas, skills, configuração e agentes que usam o servidor. **Estava marcada como feita sem ter sido**: o card de agentes que usam o servidor tinha só o rótulo trocado, e seguia com card cru, sem faixa nem divisor. Migrado, com a contagem saindo de dentro do rótulo e as tools voltando para junto do nome do agente
- [x] 5.4 Estender a volta às seis telas de formulário, que não tinham nenhuma (D9): criação volta para a listagem, edição volta para o registro editado
- [x] 5.5 Compactar a faixa de cabeçalho, que estava mais alta que uma linha — no protótipo ela é mais baixa
- [x] 5.6 Migrar as abas Ferramentas e Delegações para o card seccionado: os divisores nasciam recuados porque o padding horizontal estava no card, e não na linha (D10)
- [x] 5.7 Mover a url do servidor para baixo do nome na aba Ferramentas (D11)

## 6. Testes

- [x] 6.1 Cobrir que cada página de detalhe, de criação e de edição oferece volta, inclusive em acesso direto pela URL, e que a edição volta para o registro e não para a listagem
- [x] 6.2 Cobrir que os campos de busca continuam alcançáveis pelo nome acessível sem rótulo visível
- [x] 6.3 Rodar a suíte inteira e corrigir o que quebrar por dependência da composição antiga — as tabelas têm teste de conteúdo, que pega perda de célula

## 7. Conferência visual

- [x] 7.1 Percorrer as duas listagens, as três telas de detalhe e o catálogo de tools contra o protótipo, nos dois esquemas de cor. A conferência foi iterativa e rendeu nove correções além dos sete achados de origem — cada uma registrada no `design.md` como decisão numerada
- [x] 7.2 Conferir o histórico de sessões, que muda por herança do padrão de badge e não foi desenhado pelo protótipo — apareceram dois defeitos próprios: sessão selecionada ilegível no tema escuro e altura fixa desperdiçando espaço
- [x] 7.3 Registrar em nota da change o que sobrar, separando o que é etapa futura do que é defeito
- [x] 7.4 Fechar com guarda estático a classe de defeito que apareceu três vezes: tom fixo da escala neutra como fundo de superfície (D12)

## 8. Fechamento

- [x] 8.1 Rodar lint, typecheck, `format:check` e build de produção
- [x] 8.2 Sincronizar as specs `frontend-visual-patterns` (nova), `frontend-visual-theme` e `inbox-channel-catalog-ui` (modificadas) e arquivar a change
