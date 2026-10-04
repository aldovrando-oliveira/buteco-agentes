# Referência visual — cópia do protótipo

- **Protótipo:** "Bases sincronizadas — protótipo",
  <https://claude.ai/artifact/XzvXSpPRpqaCjNZ45ooAPY>
- **Lido em:** 03/10/2026. A listagem de arquivos informou a versão
  `1790951011-246d` do canvas, e a leitura dos arquivos veio da versão
  `1791078174-798e`; as cópias abaixo são as da leitura.
- **Copiado aqui:** as pranchas desta change e o `canvas.json`, sem edição.

| arquivo | prancha | sha256 |
|---|---|---|
| `NovaBaseManual.dc.html` | 2a | `381649a81bab1f52c436a866df847e37f05fe70dd0ba9e2bbb4b3238450eb1e4` |
| `NovaBase.dc.html` | 2b | `00e9133b4371a918fbdfed80afd192bf8d8aadcd2b37ad9e3793274e230a1692` |
| `SeletorPasta.dc.html` | 2c | `fabcdfaa2e7e4e3af9b744490742247855a2dfb78ca39626c06d48041fb08e2d` |
| `SeletorPastaDrive.dc.html` | 2d | `dc922171d11da4a0e50dfed2e4b7c9f89d6433956ff4a4c095af1f7c4e9c33c4` |
| `canvas.json` | títulos e posição das pranchas; sem notas do autor | `94b61a2ac1d26ae76565e58811db876555179a508cf8465bc05d249cf3d2b926` |

O canvas é editável; estas cópias são o que serviu de referência. Os arquivos
dependem do `support.js` do canvas e não abrem sozinhos fora dele — valem como
registro do markup e das medidas.

É referência visual, **não** especificação de comportamento: a issue e a spec
mandam, e regra que o sistema já tem vence o protótipo (convenção 17).
Nomes, e-mails e pastas das pranchas são exemplos.

## O que cada prancha mostra

| prancha | arquivo | o que mostra |
|---|---|---|
| 2a | `NovaBaseManual.dc.html` | formulário de nova base com o card "Origem dos documentos" e Manual marcada; nota de rodapé da base manual. 1440×1060 |
| 2b | `NovaBase.dc.html` | o mesmo formulário com Sincronizada marcada: provedor (só os configurados), e-mail da conta de serviço com "Copiar" e a instrução de Leitor, pasta escolhida com "Abrir no Drive ↗" e "Trocar pasta"; nota de rodapé da base sincronizada. 1440×1400 |
| 2c | `SeletorPasta.dc.html` | modal "Escolher pasta" no nível de cima: grupos "Drives compartilhados" e "Pastas compartilhadas com a conta", só navegação, "Selecionar pasta" desabilitado, e a nota "só aparece o que foi compartilhado com <conta>". 720×640 |
| 2d | `SeletorPastaDrive.dc.html` | o modal dentro de um Drive: caminho "Início › Suporte › Documentação", contagem "3 pastas", pasta escolhida em destaque, pasta já usada por outra base desabilitada com o nome dela, rodapé "Os arquivos da raiz de “X” entram na base." e "Selecionar “X”". 720×640 |

As divergências entre estas pranchas e o sistema, e o que prevalece em cada uma,
estão na tabela da D6 do `../design.md`.

`capturas/` recebe as capturas e medidas da conferência visual (D7), no apply.
