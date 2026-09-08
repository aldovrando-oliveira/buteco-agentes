# Documentação

Documentação técnica do Buteco Agents, em português. A porta de entrada do
projeto é o [README da raiz](../README.md), em inglês.

---

## Por onde começar

Depende do que trouxe você aqui.

| Você quer… | Leia |
|---|---|
| rodar o projeto na sua máquina | [development.md](development.md) |
| saber o que uma variável de ambiente faz | [configuration.md](configuration.md) |
| entender como o sistema funciona por dentro | [architecture.md](architecture.md) |
| escrever código que combine com o resto | [conventions.md](conventions.md) |
| chamar um agente a partir de outro sistema | [a2a-integration.md](a2a-integration.md) |
| subir o stack num servidor | [deployment.md](deployment.md) |
| contribuir com o projeto | [CONTRIBUTING.md](../CONTRIBUTING.md) |

---

## Os documentos

### [development.md](development.md)

Ambiente de desenvolvimento: pré-requisitos, subida da infraestrutura,
execução dos quatro apps, autenticação em dev, checklists de round-trip manual
com WAHA e Telegram, execução dos testes e construção das imagens Docker.

Inclui o troubleshooting de **Testcontainers com Podman** — leitura obrigatória
antes de rodar os testes nessa configuração, porque a falha mais comum parece
defeito de código e é de infraestrutura.

### [configuration.md](configuration.md)

Inventário das variáveis de ambiente por processo, cruzando `.env.example` e
`.env.prod.example`. Explica quais são obrigatórias com fail-fast no boot,
quais são resolvidas em tempo de build, e **quais três segredos precisam ter o
mesmo valor em mais de um processo** — a causa mais comum de falha de
configuração no sistema, porque o sintoma aparece longe da causa.

### [architecture.md](architecture.md)

Os quatro apps e o papel de cada um, o isolamento estrito entre eles, o modelo
de domínio completo, as regras de negócio transversais (exclusão de catálogo
versus conteúdo, tratamento de credenciais), o protocolo A2A, o contexto do
agente, o contrato de plugin de canal, autenticação e fuso horário do sistema.

Descreve o estado atual do sistema, sem narrar histórico.

### [conventions.md](conventions.md)

As premissas que governam esta base: isolamento entre apps e a régua para
criar uma `libs/`, sequenciamento de trabalho, ausência de abstração
prematura, tratamento de credenciais, degradação graciosa, checagem de
integridade no startup nas suas duas formas, contratos entre apps, padrões de
teste, convenções de frontend e as regras de documentação.

Vale conferir contra elas **antes** de propor qualquer mudança.

### [a2a-integration.md](a2a-integration.md)

Guia para desenvolvedores integrando um cliente externo via A2A: o endpoint,
descoberta pelo AgentCard, `SendMessage`, `GetTask`, push notifications,
enumeradores, estados da task, erros do protocolo, diagramas de fluxo e
recomendações de polling.

### [deployment.md](deployment.md)

Runbook do stack de servidor: primeiro deploy numa VM do zero, sequência de
redeploy (que é a garantia, não um detalhe), variáveis por processo, riscos
aceitos e non-goals explícitos.

---

## Documentos internos do mantenedor

Dois arquivos na raiz do repositório, em português, são notas de trabalho do
mantenedor — memória de decisões, histórico por mudança e itens em aberto:

- `01-ARQUITETURA_E_CONVENCOES.md`
- `02-HISTORICO_E_STATUS.md`

Eles são a **fonte** de onde esta documentação foi escrita, nunca o destino.
O fluxo é em sentido único: `docs/` descreve o estado atual para quem nunca
viu o sistema, e não narra histórico. Ver
[conventions.md](conventions.md#documentos-internos-do-mantenedor).

---

## Mantendo esta documentação íntegra

```bash
python3 ../scripts/check-docs.py
```

Verifica links relativos quebrados, referências a mudanças arquivadas sem o
prefixo `archive/`, apps ausentes da documentação de arquitetura (nos dois
sentidos) e a presença da seção `[Unreleased]` no `CHANGELOG.md`.

Rode antes de abrir um pull request. As regras por trás de cada checagem estão
em [conventions.md](conventions.md#documentação).
