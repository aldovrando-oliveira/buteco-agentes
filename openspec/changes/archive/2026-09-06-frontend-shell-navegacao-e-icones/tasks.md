## 1. Biblioteca de ícones

- [x] 1.1 Adicionar `lucide-react@1.41.0` às dependências de `apps/frontend`
- [x] 1.2 Confirmar, contra o pacote instalado, os nomes exportados dos ícones a usar: sol, lua, fechar, e os três da navegação (agente, servidor, conversa) — o typecheck falha se um nome não existir
- [x] 1.3 Substituir os dois ícones desenhados à mão pelos equivalentes do Lucide, na casca e no componente de campos de skills, mantendo tamanho e `aria-hidden` (D2)
- [x] 1.4 Rodar a suíte antes de mexer na estrutura — os três ícones passaram, e a identidade visual da troca é demonstrável sem browser: o Lucide emite os mesmos atributos de traço e o mesmo `aria-hidden` que as versões à mão, sobre a mesma geometria. A conferência a olho fica no grupo 5

## 2. Estrutura da barra lateral

- [x] 2.1 Remover o header da casca e a configuração de altura correspondente
- [x] 2.2 Remover o `Burger` e o estado de colapso em gaveta, junto do hook de disclosure que só ele usava (D4)
- [x] 2.3 Reduzir a largura da barra de 260px para 224px
- [x] 2.4 Dividir a barra em três seções: topo, meio com `grow` e rolagem, e rodapé
- [x] 2.5 Montar a identificação do produto no topo — quadrado de 24px, raio 6px, fundo na cor de destaque, letra "B" em monoespaçada branca — ao lado do nome, com borda inferior separando da navegação (D3)
- [x] 2.6 Mover o controle de tema para o rodapé, preservando rótulo, comportamento e o esquema efetivo lido pelo hook computado (D6)

## 3. Navegação

- [x] 3.1 Dar ícone a cada um dos três itens, decorativo, mantendo o rótulo como nome acessível
- [x] 3.2 Ajustar o item ativo: raio `sm` e cor do texto na cor de destaque, sobrescrevendo o tom 9 que a variante clara aplica por padrão (D5). O `NavLink` não expõe prop `radius` — o raio foi junto do override de cor, no mesmo `styles`
- [x] 3.3 Confirmar que o fundo do item ativo resolve para o tom 1 da escala de destaque, sem declarar cor no componente
- [x] 3.4 Preservar a marcação de ativo por grupo de rotas, inclusive em rotas profundas de detalhe, criação e edição

## 4. Testes

- [x] 4.1 Cobrir a estrutura nova: os três itens apontando para as rotas certas, ícone presente em cada um, e o nome acessível vindo do rótulo
- [x] 4.2 Cobrir o item ativo em rota profunda de cada grupo, não só na raiz do grupo
- [x] 4.3 Cobrir que a identificação do produto aparece uma única vez e que o rodapé não exibe nome, e-mail ou avatar
- [x] 4.4 Confirmar que os três casos de esquema de cor da etapa anterior seguem passando sem alteração — o controle mudou de lugar, não de comportamento
- [x] 4.5 Rodar a suíte inteira e corrigir o que quebrar por dependência da estrutura antiga

## 5. Conferência visual

- [x] 5.1 Comparar a casca lado a lado com o protótipo do handoff, nos dois esquemas de cor: proporção da barra, marca, item ativo, rodapé
- [x] 5.2 Percorrer uma rota de cada grupo e conferir que o item ativo acompanha, incluindo detalhe e edição
- [x] 5.3 Conferir que nenhuma tela perdeu espaço ou ganhou rolagem horizontal com a barra 36px mais estreita e sem o header
- [x] 5.4 Registrar em nota da change o que for divergência estrutural das telas, para a etapa seguinte, em vez de corrigir aqui — a conferência não encontrou nenhuma; a nota registra isso e o estado dos achados herdados da etapa anterior

## 6. Fechamento

- [x] 6.1 Rodar lint, typecheck, `format:check` e build de produção
- [ ] 6.2 Sincronizar as specs `frontend-app-shell` (nova) e `frontend-scaffold` (modificada) e arquivar a change
