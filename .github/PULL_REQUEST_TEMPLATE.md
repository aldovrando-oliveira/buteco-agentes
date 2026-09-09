# Pull request

## Mudança OpenSpec

<!--
Nome da mudança em openspec/changes/<nome>/ que este PR implementa.
Se não houver uma, explique por quê — correção trivial e ajuste de
documentação podem dispensar, mudança de comportamento não.
Ver CONTRIBUTING.md.
-->

**Mudança:** `<nome-da-mudanca>`

## O que muda

<!-- Resumo do que este PR faz. Uma ou duas frases. -->

## Apps afetados

<!-- Marque todos que este PR toca. -->

- [ ] `apps/api`
- [ ] `apps/workers`
- [ ] `apps/inbox`
- [ ] `apps/frontend`
- [ ] `libs/`
- [ ] stack de deploy
- [ ] documentação

## Verificações executadas localmente

> **Este repositório não tem CI.** Nada roda automaticamente ao abrir o PR.
> Esta seção é a única evidência que o revisor tem — marque apenas o que você
> de fato executou, e cole o resultado quando algo falhar ou for pulado.

- [ ] `dotnet test` das solutions afetadas
- [ ] Testes cruzados em `tests/`, se o PR toca contrato entre apps
- [ ] `npm run lint && npm run format:check && npm run test && npm run build`, se toca `apps/frontend`
- [ ] `python3 scripts/check-docs.py`
- [ ] Conferência manual tela a tela, **nos dois esquemas de cor**, se há mudança visual

<!-- Resultado, se algo falhou ou foi pulado: -->

## O que ficou de fora

<!--
Escopo reduzido é decisão legítima; escopo reduzido em silêncio não é.
Se nada ficou de fora, escreva "nada".
-->

## Checklist

- [ ] As tarefas concluídas estão marcadas em `tasks.md`
- [ ] O `design.md` foi corrigido, se a implementação divergiu do que foi aprovado
- [ ] Nenhuma referência de projeto nova entre `apps/`
- [ ] Nenhum segredo, token ou credencial no diff
- [ ] `CHANGELOG.md` atualizado em `[Unreleased]`, se a mudança é visível para quem usa
- [ ] Todo risco nomeado no `design.md` tem cenário na spec e teste, ou justificativa explícita de por que não é testável
