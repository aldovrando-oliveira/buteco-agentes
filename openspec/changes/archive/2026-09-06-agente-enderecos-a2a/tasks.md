## 1. Montagem única do endereço no servidor

- [x] 1.1 Extrair a montagem dos endereços A2A para um ponto único, a partir da URL pública configurada, e fazer o endpoint do card de descoberta passar a consumi-lo em vez de montar a url inline (D1)
- [x] 1.2 Definir o comportamento sem URL pública configurada: o ponto único não devolve endereço, em vez de devolver caminho relativo (D2)
- [x] 1.3 Cobrir com teste que a montagem é a mesma nos dois usos, para que resposta de agente e card de descoberta não possam divergir

## 2. Resposta de agente

- [x] 2.1 Acrescentar à resposta de agente o bloco com os dois endereços, opcional, sem alterar nenhum campo existente (D5)
- [x] 2.2 Repassar a URL pública aos handlers. **Eram oito, não dois**: toda ação que devolve um agente produz a mesma resposta, e deixar só as consultas com os endereços faria a resposta de ativar um agente perdê-los
- [x] 2.3 **Não** acrescentar campo de "habilitado": o estado do agente já está na resposta e um segundo campo que precisa concordar com o primeiro um dia discorda (D3)
- [x] 2.4 Cobrir na suíte de `apps/api` — cinco testes, executados de fato contra o Postgres efêmero depois de apontar o Testcontainers para o Podman: endereços presentes na consulta por id e na listagem, iguais aos do card de descoberta, presentes também para agente inativo, e ausentes sem URL pública configurada (D6)

## 3. Painel

- [x] 3.1 Declarar os dois endereços no tipo de agente do frontend, como **opcionais e anuláveis**: a API antiga omite o campo, e a primeira versão só tratava o nulo — quebrava a tela inteira contra uma API que ainda não subiu (D7)
- [x] 3.2 Criar o card de endereços A2A: os dois valores em monoespaçada, ação de copiar em cada um, e ação de abrir o card de descoberta (D4)
- [x] 3.3 Exibir, para agente inativo, o aviso de que ele segue descobrível e rejeita as mensagens que receber — derivado do estado que já existe, sem campo novo
- [x] 3.4 Exibir, sem endereços na resposta, a mensagem de que o endereço público do servidor não está configurado, em vez de endereço vazio
- [x] 3.5 Montar o card como quarto da coluna direita da visão geral, usando o card seccionado e o rótulo de seção compartilhados
- [x] 3.6 Estender as fixtures de agente dos testes com os campos novos

## 4. Testes do painel

- [x] 4.1 Cobrir que os dois endereços aparecem e que cada um tem ação de copiar
- [x] 4.2 Cobrir que o aviso de inativo aparece só para agente inativo
- [x] 4.3 Cobrir que a ausência dos endereços vira mensagem de configuração, e não endereço vazio
- [x] 4.4 Rodar a suíte inteira dos dois apps e corrigir o que quebrar por fixture desatualizada — 26 fixtures de agente em 22 arquivos ganharam o campo novo

- [x] 4.5 Documentar no README como apontar o Testcontainers para o Podman: sem `DOCKER_HOST` a suíte de integração falha inteira, com erro que se parece com código quebrado

- [x] 4.6 Fechar a instabilidade da suíte que vinha desde a etapa da casca: era o prazo padrão das consultas assíncronas, curto para dropdowns em portal sob paralelismo (D8)

- [x] 2.5 Fixar o nome do campo no fio: a política camelCase do serializador transformava `A2A` em `a2A`, e o painel lia `a2a` como ausente. Coberto por teste que inspeciona o JSON como texto, porque os que desserializam para o record são cegos a isso (D9)

## 5. Conferência

- [x] 5.1 Subir a API e o painel e conferir o card de ponta a ponta. Foi essa conferência que revelou o D9: o card exibia a mensagem de configuração ausente com tudo configurado, porque o nome do campo no fio não era o que o painel lia
- [x] 5.2 Conferir o card nos dois esquemas de cor, com agente ativo e inativo
- [x] 5.3 Conferir o caso sem URL pública configurada, que é o que hoje falha em silêncio
- [x] 5.4 Registrar em nota da change o que sobrar

## 6. Fechamento

- [x] 6.1 Rodar lint, typecheck, `format:check` e build do frontend, e a suíte de `apps/api`
- [x] 6.2 Sincronizar as specs `agent-catalog` e `agent-catalog-ui` e arquivar a change
