## ADDED Requirements

### Requirement: Aviso de senha alterada no /entrar
Depois da troca da própria senha em Minha conta (`panel-account-page`), o painel SHALL gravar a chave `buteco.aviso.senha-alterada` no `sessionStorage` da aba **antes** de chamar o logout do emissor. A chave SHALL chegar intacta ao `/entrar` carregado pelo `/connect/logout`: enquanto o logout desta aba está em andamento, a rota protegida não leva ao `/entrar` do próprio painel (`panel-issuer-session`, "Encerrar sessão revoga pelo emissor e avisa as outras abas"). Ao montar, o `/entrar` SHALL ler essa chave uma vez e apagá-la na mesma hora. Com a chave, SHALL mostrar acima do formulário de e-mail e senha o aviso "Senha alterada. Entre com a nova senha.", em `role="status"`, no lugar de "Sua sessão expirou. Entre novamente para continuar de onde parou.", mesmo quando a URL trouxer `motivo=sessao-expirada`. O aviso SHALL sumir na primeira submissão do formulário. Nada sobre a troca SHALL ir na URL, no `state` do router ou no `localStorage`.

#### Scenario: Depois da troca, o /entrar avisa
- **WHEN** a chave `buteco.aviso.senha-alterada` está no `sessionStorage`, e o `/entrar` abre
- **THEN** o aviso "Senha alterada. Entre com a nova senha." aparece em `role="status"`, e a chave sai do `sessionStorage`

#### Scenario: A chave sobrevive ao logout até o /entrar de verdade
- **WHEN** a troca responde `204`, o logout apaga o registro, e a página ainda não foi descarregada pela navegação ao `/connect/logout`
- **THEN** o `/entrar` do próprio painel não é renderizado, a chave continua no `sessionStorage`, e o `/entrar` carregado em seguida mostra o aviso e apaga a chave

#### Scenario: O aviso aparece uma vez só
- **WHEN** o `/entrar` mostrou o aviso, e a pessoa recarrega a página
- **THEN** o aviso não aparece de novo

#### Scenario: O aviso vence a sessão expirada
- **WHEN** a chave está no `sessionStorage`, e o `/entrar` abre com `?motivo=sessao-expirada`
- **THEN** aparece "Senha alterada. Entre com a nova senha.", e não "Sua sessão expirou…"

#### Scenario: Sem a chave, nada muda
- **WHEN** o `/entrar` abre sem a chave no `sessionStorage`
- **THEN** nenhum aviso de senha alterada aparece, e os outros avisos seguem as suas regras

#### Scenario: O aviso some ao enviar
- **WHEN** o aviso está na tela, e a pessoa envia e-mail e senha
- **THEN** o aviso sai, e o resultado do envio segue as regras do `/entrar`
