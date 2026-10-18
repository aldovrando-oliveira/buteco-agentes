# Protótipo: tipo de autenticação por header customizado (#168)

**Fonte:** canvas https://claude.ai/artifact/2apwGhmPQCwYWKc8i9DFEy, versão
`1792295069-a164`, aprovada pelo dono em 18/10/2026. Lida em 18/10/2026 pela
ação `read` do Artifact. A versão lida era a aprovada; nenhuma mais nova foi
usada.

**Cópia:** os três quadros e o `canvas.json` foram copiados sem edição para esta
pasta. Esta cópia é o que sobrevive ao archive; o canvas pode mudar depois. O
`support.js` que os `.dc.html` carregam é do runtime do canvas, não do
protótipo, e **não** foi copiado: abertos fora do canvas, os quadros não
renderizam a parte interativa.

| arquivo | quadro no canvas | sha256 |
|---|---|---|
| `Main.dc.html` | Formulário — alternar autenticação (interativo), 1280 × 1180 | `d4a7e0bb4405cc92cd53558337a98bfd21ab5b5cac912f736f9f9de7ca87ce28` |
| `Erros.dc.html` | Erros ao salvar, 856 × 1000 | `870353f6598d97561b153cf89cbce2e00ece1b6de70b732b3cdcdb50fc24d3f3` |
| `Detalhe.dc.html` | Detalhe — card de configuração, 560 × 420 | `ce0f21197c5ecd7d3b3e42c8af397668540be1364d9958f7c5b9a64693d54521` |
| `canvas.json` | posição e título dos quadros | `6858f2f238723280c5fb758347e22b913a4e26720643b42497f7fb8dd95ba022` |

`Main.dc.html` é interativo. Os ajustes `modo` (`Criar`/`Editar`) e
`tipoSalvo` (`BearerToken`/`CustomHeader`/`None`, seção "Edição") simulam a
edição de um servidor salvo com aquele tipo. Em `Criar`, o tipo inicial é
"Token em header customizado".

## Requisitos de entrada (da #168)

- Seletor de tipo de autenticação como **lista de cartões com radio**, três
  opções lado a lado (Nenhuma, Bearer Token e Token em header customizado), cada
  uma com título e descrição curta. Substitui o `Select` atual nos fluxos de
  criar e editar. Abaixo de uma largura mínima, os cartões empilham: nenhum
  título pode quebrar linha nem estourar o cartão.
- Com "Token em header customizado": campo obrigatório "Nome do header"
  (monoespaçado, placeholder `X-API-TOKEN`), devolvido pela API
  (`authHeaderName`) e pré-preenchido na edição. O campo de credencial passa a
  se chamar "Token".
- Também só nesse tipo: a caixa "Como a credencial sai em cada requisição ao
  servidor", com a linha `<nome do header>: ••••••••` (o nome atualiza enquanto
  se digita, o valor sempre mascarado) e a nota "O valor vai como foi digitado,
  sem prefixo. Authorization não é enviado." Nenhuma e Bearer Token não mostram
  a caixa.
- Edição com troca de tipo entre dois tipos com credencial: aviso de que a
  credencial salva não é reaproveitada, credencial obrigatória (D4 do backend).
  Troca para `None`: aviso de que a credencial atual será apagada. Saindo de
  Nenhuma, não há aviso de credencial anterior, porque não existe credencial
  salva; a credencial é obrigatória.
- "Testar conexão" na edição: desabilitado quando o tipo mudou e a credencial
  nova está em branco, com a nota "Para testar o tipo novo, informe a
  credencial nova". Com credencial em branco e tipo inalterado, a nota diz que o
  teste usa a configuração salva, inclusive o nome do header. Corrige um
  defeito atual: hoje, trocar o tipo e deixar a credencial em branco testa o
  servidor salvo com o tipo antigo.
- Detalhe do servidor: rótulo "Token em header customizado" e linha nova
  "Header" com o nome em monoespaçado.

## Decisões do dono, já fechadas

1. **Rótulo do tipo novo: "Token em header customizado"**, nos cartões, na
   tabela e no card de detalhe (o nome no fio continua `CustomHeader`).
   Escolhido por ser o mais descritivo. Consequência aceita: por ser longo, é o
   que obriga os cartões a empilharem em área estreita, e isso é requisito, não
   ajuste visual.
2. **Caixa de prévia só no tipo "Token em header customizado"** (opção b, entre
   "em todos os tipos" e "não mostrar"). Ela fica onde o nome do header é texto
   livre e um erro de digitação só apareceria no teste como "credencial
   rejeitada", sem pista; nos outros tipos só repetiria a descrição do cartão.
   O que ela afirma ("em cada requisição", "sem prefixo", "sem `Authorization`")
   é fato medido do backend (V1 e o requisito de envio da change de backend).
   Pela convenção 13, se a montagem do header mudar, a caixa muda junto.

## Regras de uso (comentário da #168)

1. **Fonte e cópia.** Esta pasta.
2. **O protótipo define composição e comportamento, não a identidade visual.**
   Cor, tipografia, raio, sombra e espaçamento vêm de
   `apps/frontend/src/theme.ts` e dos componentes Mantine. Os hex e px dos
   `.dc.html` são aproximações. Nenhum valor é copiado do protótipo para o
   código.
3. **Componentes reais, nunca imitação.** Os cartões usam `Radio.Card` do
   Mantine 9.4.2 instalado (`design.md`, V2).
4. **A validação de nome e valor do header não é duplicada no frontend.** O
   `checkHeader` e a expressão do token em `Main.dc.html` existem só para
   ilustrar. A validação local é só de obrigatoriedade; o resto vem do 400 da
   API.
5. **O que é simulação no protótipo não vira produto:** "Conexão bem-sucedida
   (simulada no protótipo)", "protótipo: nada é gravado", o alerta de resumo no
   topo do formulário (é o mesmo espaço que mostra o sucesso simulado), os nomes
   e URLs de exemplo e as datas fixas do card de detalhe.
6. **Divergência é decisão registrada.** Tabela de divergências no `design.md`
   (seção "Divergências do protótipo").
7. **Conferência visual quadro a quadro**, nos dois esquemas, iterativa
   (`tasks.md`, grupo 7). O protótipo só tem o tema claro; o escuro se confere
   por contraste e pelo `theme.ts`.
8. **Escopo:** só `None`, `BearerToken` e `CustomHeader`. Nada da #161.

## Comportamento lido no `Main.dc.html` (referência para a conferência)

| situação | o que o quadro faz |
|---|---|
| tipo `None` | sem campo de credencial, sem nome do header, sem prévia |
| tipo `BearerToken` | campo "Credencial"; sem prévia |
| tipo `CustomHeader` | "Nome do header" (mono) acima de "Token"; prévia depois do token |
| prévia sem nome digitado | `<nome do header>: ••••••••` |
| edição, tipo mantido, credencial em branco | placeholder e descrição "Deixe em branco para manter a credencial atual"; nota de teste com a configuração salva (com `CustomHeader`, "inclusive o nome do header: uma alteração no nome só é testada depois de salvar") |
| edição, troca entre tipos com credencial | aviso amarelo dentro do campo de credencial, acima da descrição; asterisco; descrição "Enviada cifrada (AES-GCM) e nunca retorna na API." |
| edição, troca para `None` (salvo com credencial) | aviso amarelo logo abaixo dos cartões: "Ao salvar, a credencial atual (<tipo salvo>) é apagada. Ela não fica guardada sem uso." |
| edição, saída de `None` | asterisco na credencial, nenhum aviso |
| edição, tipo mudou e credencial em branco | "Testar conexão" desabilitado; nota "Para testar o tipo novo, informe a credencial nova." |
| edição, tipo `None` mantido | nenhuma nota de teste |
