## ADDED Requirements

### Requirement: Chaves do emissor são persistidas e nunca geradas por boot
As chaves do emissor SHALL ser gravadas no banco do `apps/api` (tabela `identity_keys`), com:

- identificador (`kid`);
- uso (`signing` ou `encryption`);
- algoritmo;
- instantes de criação, de início de assinatura, de fim de assinatura e de fim de validação.

Reiniciar o processo SHALL NOT criar chave nova enquanto houver chave vigente. O emissor SHALL NOT usar chave de desenvolvimento, efêmera ou de certificado do sistema operacional, em nenhum ambiente.

#### Scenario: Reinício preserva a chave
- **WHEN** o app sobe, publica o JWKS, é parado e sobe de novo contra o mesmo banco
- **THEN** o JWKS da segunda subida publica o mesmo `kid` da primeira, e a tabela não ganhou linha nova

#### Scenario: Primeira subida cria exatamente uma chave de cada uso
- **WHEN** o app sobe com o emissor ligado contra um banco sem chave e recebe a primeira requisição
- **THEN** a tabela passa a ter uma chave de assinatura ativa e uma de cifra, e o JWKS publica a de assinatura

### Requirement: Parte privada cifrada em repouso com chave de configuração própria
O material privado de cada chave SHALL ser gravado cifrado com AES-256-GCM, usando a chave de `Identity:SigningKeyEncryptionKey` (32 bytes em base64), que vem da configuração ou de variável de ambiente e nunca do banco. Com o emissor ligado, chave de cifra ausente, fora de base64 ou com tamanho diferente de 32 bytes SHALL falhar o boot com mensagem que nomeia a configuração. Uma chave gravada que não decifra com a chave configurada SHALL falhar a carga sem ser substituída por chave nova.

#### Scenario: Banco não guarda chave privada legível
- **WHEN** a chave de assinatura é criada
- **THEN** a coluna de material privado não contém a chave em PKCS#8, PEM nem JWK legível, e decifrá-la com `Identity:SigningKeyEncryptionKey` devolve a chave que assina os tokens

#### Scenario: Emissor ligado sem chave de cifra derruba o boot
- **WHEN** o app sobe com `PublicUrl:BaseUrl` configurada e sem `Identity:SigningKeyEncryptionKey`
- **THEN** o boot falha com uma mensagem que cita `Identity:SigningKeyEncryptionKey`

#### Scenario: Chave de cifra trocada não gera chave nova em silêncio
- **WHEN** o banco tem chaves gravadas com uma chave de cifra e o app sobe com outra
- **THEN** a carga das chaves falha, o emissor responde `503`, e a tabela continua com as mesmas linhas

### Requirement: Rotação automática da chave de assinatura, sem reinício
A chave de assinatura SHALL ser trocada a cada 90 dias, com três janelas:

- **antes de assinar:** a próxima chave SHALL ser criada e publicada no JWKS 7 dias antes de começar a assinar;
- **depois de parar de assinar:** a chave anterior SHALL continuar publicada e validando por 24 horas;
- **depois disso:** a chave anterior SHALL sair do JWKS.

A troca de qual chave assina SHALL acontecer sem reiniciar o processo, até uma hora depois do instante previsto. Em qualquer instante, exatamente uma chave SHALL assinar.

#### Scenario: Próxima chave publicada antes de assinar
- **WHEN** faltam menos de 7 dias para o fim de assinatura da chave ativa e a verificação de rotação roda
- **THEN** existe uma segunda chave, publicada no JWKS, e os tokens emitidos continuam com o `kid` da chave ativa

#### Scenario: Troca de chave sem reinício
- **WHEN** o relógio passa do início de assinatura da próxima chave e a verificação de rotação roda, no mesmo processo
- **THEN** o token emitido a seguir traz o `kid` da nova chave, e o JWKS ainda publica a anterior

#### Scenario: Chave aposentada some do JWKS depois da retenção
- **WHEN** passam mais de 24 horas do fim de assinatura de uma chave e a verificação de rotação roda
- **THEN** o JWKS não publica mais aquele `kid`

#### Scenario: Token assinado pela chave anterior continua verificável na retenção
- **WHEN** um token emitido pela chave anterior, ainda dentro da validade, é verificado contra o JWKS depois da troca
- **THEN** a assinatura valida

### Requirement: Rotação é segura com mais de uma instância
A criação de chave SHALL acontecer sob um lock do Postgres. Duas instâncias do `apps/api` verificando a rotação ao mesmo tempo contra o mesmo banco SHALL resultar em uma única chave nova.

#### Scenario: Duas verificações concorrentes criam uma chave
- **WHEN** duas verificações de rotação rodam em paralelo contra um banco sem chave
- **THEN** a tabela termina com exatamente uma chave de assinatura e uma de cifra
