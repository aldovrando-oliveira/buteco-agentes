# open-source-governance Specification

## Purpose

TBD - defined by change documentacao-repositorio-open-source. Update Purpose after archive.

## Requirements

### Requirement: Licenciamento Apache-2.0 declarado na raiz

O repositório SHALL conter na raiz um arquivo `LICENSE` com o texto integral
e não modificado da Apache License 2.0, e um arquivo `NOTICE` com a linha de
copyright `Copyright 2026 Aldovrando Oliveira`.

O `README.md` SHALL conter uma seção de licença apontando para o `LICENSE`.
Arquivos-fonte SHALL NOT receber headers de licença: a Apache-2.0 os
recomenda mas não os exige, e a declaração em `LICENSE` e `NOTICE` é
suficiente.

#### Scenario: Repositório é legalmente open-source

- **WHEN** alguém avalia se pode usar, modificar ou redistribuir o código
- **THEN** encontra o `LICENSE` com o texto Apache-2.0 na raiz e a
  titularidade declarada no `NOTICE`, sem precisar solicitar permissão ao
  autor

#### Scenario: Código-fonte permanece sem headers de licença

- **WHEN** arquivos `.cs`, `.ts` ou `.tsx` do repositório são inspecionados
- **THEN** nenhum deles contém header de copyright ou de licença adicionado
  por esta change

### Requirement: Canal de reporte de vulnerabilidade

O repositório SHALL conter na raiz um `SECURITY.md` em inglês descrevendo
como reportar uma vulnerabilidade de forma privada e o que esperar após o
reporte.

O documento SHALL orientar contra a abertura de issue pública para
vulnerabilidade, e SHALL indicar quais áreas do sistema são sensíveis:
credenciais criptografadas em AES-GCM, tokens de operador e de serviço, e
segredos compartilhados entre processos.

#### Scenario: Descoberta de vulnerabilidade tem caminho privado

- **WHEN** alguém identifica uma falha de segurança no projeto
- **THEN** `SECURITY.md` informa por qual canal privado reportá-la e adverte
  explicitamente contra abrir uma issue pública

### Requirement: Código de conduta

O repositório SHALL conter na raiz um `CODE_OF_CONDUCT.md` adotando o
Contributor Covenant 2.1, com o canal de contato para reporte preenchido.

#### Scenario: Contribuidor encontra as regras de convivência

- **WHEN** um contribuidor procura as regras de conduta do projeto
- **THEN** encontra `CODE_OF_CONDUCT.md` na raiz, com o texto do Contributor
  Covenant 2.1 e um canal de contato efetivo para reportes

### Requirement: Guia de contribuição alinhado ao fluxo OpenSpec

O repositório SHALL conter na raiz um `CONTRIBUTING.md` em português
descrevendo como contribuir: o fluxo spec-driven via OpenSpec
(`/opsx:explore` → `/opsx:propose` → revisão → `/opsx:apply` → `/opsx:sync`
→ `/opsx:archive`), a convenção de mensagens de commit em uso, os requisitos
para abrir um pull request e como executar os testes de cada app.

O `CONTRIBUTING.md` SHALL citar a execução do script de integridade da
documentação como passo de pull request, e SHALL declarar explicitamente que
o repositório não possui integração contínua configurada, em vez de sugerir
que a verificação ocorre automaticamente.

#### Scenario: Contribuidor descobre o fluxo antes de escrever código

- **WHEN** alguém pretende propor uma mudança no projeto
- **THEN** `CONTRIBUTING.md` descreve o fluxo OpenSpec completo, deixando
  claro que propostas e specs precedem a implementação

#### Scenario: Ausência de CI é declarada honestamente

- **WHEN** um contribuidor lê as instruções de verificação antes de abrir um
  pull request
- **THEN** o documento informa que não há CI configurada e que os testes e o
  script de integridade da documentação precisam ser executados localmente

### Requirement: Templates de issue e pull request

O repositório SHALL conter templates em `.github/`: pelo menos um template de
relato de defeito e um de proposta de funcionalidade em
`.github/ISSUE_TEMPLATE/`, e um `.github/PULL_REQUEST_TEMPLATE.md`.

O template de pull request SHALL solicitar a referência à change OpenSpec
correspondente e a confirmação de que testes e verificação de documentação
foram executados localmente.

#### Scenario: Abertura de issue é guiada

- **WHEN** alguém abre uma issue no repositório
- **THEN** encontra templates distintos para relato de defeito e para
  proposta de funcionalidade, cada um solicitando as informações necessárias
  para triagem

#### Scenario: Pull request declara sua change e suas verificações

- **WHEN** um contribuidor abre um pull request
- **THEN** o template solicita a change OpenSpec correspondente e a
  confirmação de que os testes dos apps afetados e o script de integridade
  da documentação foram executados localmente

### Requirement: Seleção dos testes das rodadas intermediárias derivada do diff
O repositório SHALL oferecer uma forma de derivar, a partir do diff da branch contra a linha principal (incluindo o que ainda não foi commitado e os arquivos novos), o que rodar nas rodadas intermediárias de uma mudança. A forma SHALL mostrar o plano antes de qualquer execução, com cada passo, o comando e o motivo que o selecionou, e o mesmo diff SHALL produzir sempre o mesmo plano. Um caminho que as regras não reconhecem SHALL selecionar todas as suítes. Todo projeto de teste do repositório SHALL ser alcançado por alguma regra. A seleção SHALL NOT substituir a suíte de cada app afetado antes do pull request, e o `CONTRIBUTING.md` SHALL dizer isso, junto da forma de uso descrita em `docs/development.md`.

#### Scenario: O plano aparece antes de rodar e diz por quê
- **WHEN** um contribuidor pede a seleção numa branch com mudanças
- **THEN** recebe a lista dos passos, cada um com o comando e os caminhos e a regra que o selecionaram, sem que nenhuma suíte seja executada

#### Scenario: O mesmo diff dá o mesmo plano
- **WHEN** a seleção é pedida duas vezes sobre o mesmo diff e a mesma árvore
- **THEN** os dois planos são iguais, passo a passo e na mesma ordem

#### Scenario: Caminho não reconhecido roda tudo
- **WHEN** o diff toca um caminho que nenhuma regra reconhece
- **THEN** o plano inclui todas as suítes do repositório

#### Scenario: Projeto de teste sem regra que o alcance reprova
- **WHEN** existe no repositório um projeto de teste que nenhuma regra seleciona
- **THEN** a verificação da própria seleção reprova e nomeia o projeto

#### Scenario: A seleção não dispensa a suíte completa antes do PR
- **WHEN** um contribuidor lê as instruções de verificação antes de abrir um pull request
- **THEN** o `CONTRIBUTING.md` diz que a seleção vale para as rodadas intermediárias e que a suíte de cada app afetado continua obrigatória antes do pull request
