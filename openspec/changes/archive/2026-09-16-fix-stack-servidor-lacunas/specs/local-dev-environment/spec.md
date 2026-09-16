## ADDED Requirements

### Requirement: Arquivo versionado não carrega credencial de serviço externo

Nenhum arquivo versionado do repositório SHALL conter credencial funcional de
serviço externo — chave de API, token ou senha que autentique contra um endpoint
real. Vale para `appsettings*.json`, `.env*.example`, `docker-compose*.yml` e
qualquer outro artefato rastreado.

Arquivos de configuração de desenvolvimento versionados SHALL trazer o **nome** de
cada variável com valor vazio ou placeholder reconhecível, nunca o valor efetivo,
e a credencial SHALL ser fornecida por variável de ambiente pelo desenvolvedor.

Quando uma credencial for encontrada em arquivo versionado, removê-la do estado
atual SHALL NOT ser tratado como correção suficiente: a credencial permanece
recuperável no histórico do controle de versão, e a correção é a **rotação** no
provedor que a emitiu.

Este requisito existe porque uma chave de API funcional, com endpoint de gateway
interno, viveu commitada num `appsettings.Development.json`, entregue a todo
clone do repositório — e o contorno documentado para ela era sobrescrever o
endpoint na linha de comando a cada execução, o que preservava a chave no arquivo.

#### Scenario: Configuração de desenvolvimento não traz credencial funcional

- **WHEN** um arquivo `appsettings.Development.json` versionado é inspecionado
- **THEN** os campos de credencial estão vazios ou com placeholder reconhecível,
  e nenhum valor autentica contra um endpoint real

#### Scenario: Credencial exposta exige rotação, não apenas remoção

- **WHEN** uma credencial funcional é identificada em arquivo versionado
- **THEN** a resposta registrada inclui a rotação da credencial no provedor, e a
  remoção do arquivo **não** é declarada como resolução do problema

### Requirement: Exemplo de ambiente consistente com a configuração versionada

Os valores de `.env.example` SHALL ser consistentes com o que os arquivos de
configuração versionados dos apps esperam, de modo que seguir o procedimento
documentado de preparação do ambiente produza um sistema que conecta.

Divergência entre a porta publicada pelo compose de desenvolvimento e a porta
declarada nos `appsettings.Development.json` SHALL ser tratada como defeito de
configuração, não como ajuste esperado do desenvolvedor.

Este requisito existe porque `.env.example` publicava Postgres e RabbitMQ nas
portas padrão enquanto os `appsettings.Development.json` versionados apontavam
para portas não-padrão: copiar o exemplo como a documentação manda fazia os três
apps falharem a conexão, com sintoma de banco fora do ar.

#### Scenario: Procedimento documentado produz ambiente que conecta

- **WHEN** um desenvolvedor copia `.env.example` para `.env`, sobe as
  dependências com o compose de desenvolvimento e inicia os apps, sem editar
  nenhum dos dois arquivos
- **THEN** `apps/api`, `apps/workers` e `apps/inbox` conectam ao PostgreSQL e ao
  RabbitMQ sem ajuste manual de porta
