# Respostas gravadas do Google Drive

Origem de cada arquivo (tarefa 1.1 e 1.2 da change `apps-connectors-google-drive`).
Tudo sanitizado: ids, e-mails e nomes de pasta foram trocados por valores sintéticos,
e nenhum e-mail de conta aparece aqui.

| arquivo | origem |
|---|---|
| `raiz-p3.json` | listagem da raiz da P3 da etapa 0 (`out/p2-p3-inicial.json`): os mesmos nove itens, com `mimeType`, `modifiedTime`, `capabilities.canDownload`, `shortcutDetails` e `md5Checksum` medidos. Ids trocados. `doc_bloqueado` vem do acréscimo à P1 (`out/p1-bloqueio-ligado.json`): `canDownload=false` e `copyRequiresWriterPermission=true` |
| `pasta-principal.json` | `files.get` da pasta (`out/sonda.json`), id e e-mail trocados. `webViewLink` não estava no `fields` da etapa 0: é campo documentado, **não medido** |
| `export-doc-com-imagem.md` | `out/export/doc_com_imagem.md`, com o base64 da imagem (784.526 B) trocado por um PNG 1×1 válido na mesma forma `[image1]: <data:image/png;base64,…>` |
| `export-doc-com-imagem.esperado.md` | o arquivo acima sem `![][image1]` e sem a definição: o que o conector entrega |
| `export-doc-tabela-e-listas.md` | `out/export/doc_tabela_e_listas.md`, sem alteração (texto de teste, sem dado de conta) |
| `download-md.md` | sintético: os `.md` medidos eram o `01` e o `02` do repositório |

Os corpos de erro são montados em código (`GoogleDriveFakeHandler.GoogleError`) no
formato documentado em
<https://developers.google.com/workspace/drive/api/guides/handle-errors>.
`accessNotConfigured` e `cannotExportFile` têm o `reason` e a mensagem **medidos** na
etapa 0 (número do projeto trocado); `notFound`, `insufficientFilePermissions`,
`userRateLimitExceeded`, `rateLimitExceeded`, `authError`, `badRequest`, a troca de
token recusada, `drives.list` e `sharedWithMe` são **documentados, não medidos**.
