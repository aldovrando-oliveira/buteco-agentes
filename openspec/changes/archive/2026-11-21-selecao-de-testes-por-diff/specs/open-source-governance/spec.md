## ADDED Requirements

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
