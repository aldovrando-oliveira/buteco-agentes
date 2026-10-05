# Referência visual — cópia do protótipo

- **Protótipo:** "Bases sincronizadas — protótipo",
  <https://claude.ai/artifact/XzvXSpPRpqaCjNZ45ooAPY>
- **Lido em:** 04/10/2026, versão `1791078174-798e` do canvas (a mesma que a #106
  leu em 03/10).
- **Copiado aqui:** as pranchas desta change e o `canvas.json`, sem edição.

| arquivo | prancha | sha256 |
|---|---|---|
| `Main.dc.html` | 1 | `502fb3a1e8773108c00346253effca6a4f84218036e75500f79cde7a2b0edcd8` |
| `Detalhe.dc.html` | 4a | `3415a3a13f6213bb25ca5f7b5121d702ea5f434f082f67fe1f5fc91600b2156d` |
| `DetalheFalha.dc.html` | 4b | `4375e0443e3d6e48aeab168bb250eb4c691eddf43af1f1ffc80b9a56deae4038` |
| `canvas.json` | títulos e posição das pranchas; sem notas do autor | `94b61a2ac1d26ae76565e58811db876555179a508cf8465bc05d249cf3d2b926` |

O canvas é editável; estas cópias são o que serviu de referência. Os arquivos
dependem do `support.js` do canvas e não abrem sozinhos fora dele — valem como
registro do markup e das medidas.

É referência visual, **não** especificação de comportamento: a issue e a spec
mandam, e regra que o sistema já tem vence o protótipo (convenção 17). Nomes,
e-mails, pastas e datas das pranchas são exemplos.

## O que cada prancha mostra

| prancha | arquivo | o que mostra |
|---|---|---|
| 1 | `Main.dc.html` | listagem de bases com busca, filtro de quatro opções e cinco colunas; sob a descrição de cada base, a linha de origem ("Manual" com ícone de documento, ou "Google Drive › pasta" com ícone de pasta) e, na base com falha, a linha vermelha "Sincronização falhando desde 25/09, 09:10". 1440×900 |
| 4a | `Detalhe.dc.html` | detalhe de base sincronizada saudável: card "Origem — pasta sincronizada" entre a descrição e os agentes, com "Sincronizar agora" no cabeçalho, pasta com "Abrir no Google Drive ↗", provedor, última sincronização concluída e "Sem erros no último ciclo"; abas Documentos, Histórico e Diagnóstico; card de documentos sem coluna de ações, com "Somente leitura — o conteúdo vem da pasta", faixa de falha com "Reindexar documento" e "Mais 7 documentos"; card "Arquivos da pasta que não entraram na base" com nome e motivo. 1440×1500 |
| 4b | `DetalheFalha.dc.html` | a mesma tela sem acesso à pasta: card de Origem com borda vermelha, "Tentar sincronizar agora", rótulo "Pasta — nome na última sincronização concluída", "Falhando desde …", e o alerta "Sem acesso à pasta" com a conta, a garantia de que nenhum documento saiu e o caminho de recuperação. 1440×1300 |

Fora desta change, no mesmo canvas: 2a–2d (cadastro, #106, já copiadas na
`archive/2026-10-04-frontend-cadastro-base-sincronizada/design/`), 3a (detalhe de
base manual, que esta change não muda), 3b e 4c (histórico, #101).

As divergências entre estas pranchas e o sistema, e o que prevalece em cada uma,
estão na tabela da D8 do `../design.md`.

`capturas/` recebe as capturas e medidas da conferência visual (D9), no apply.
