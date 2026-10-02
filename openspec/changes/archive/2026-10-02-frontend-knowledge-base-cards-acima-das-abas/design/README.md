# Referência visual — cópia do protótipo

- **Protótipo:** "Bases sincronizadas — protótipo",
  <https://claude.ai/artifact/XzvXSpPRpqaCjNZ45ooAPY>
- **Lido em:** 02/10/2026, versão `1790951011-246d` do canvas.
- **Copiado aqui:** as pranchas desta change, sem edição.

| arquivo | sha256 |
|---|---|
| `DetalheManual.dc.html` (3a) | `d9859ed1c0ab08ed962fb70d20299351a25a939ff79e5a31b93041229a891669` |
| `Detalhe.dc.html` (4a) | `3415a3a13f6213bb25ca5f7b5121d702ea5f434f082f67fe1f5fc91600b2156d` |

O canvas é editável; estas cópias são o que serviu de referência. Os arquivos
dependem do `support.js` do canvas e não abrem sozinhos fora dele — valem como
registro do markup e das medidas.

É a referência visual das changes de frontend da linha (#99, #101, #106,
#107), **não** especificação de comportamento: a issue e a spec mandam, e regra
que o sistema já tem vence o protótipo (convenção 17). Datas e números das
pranchas são exemplos.

| prancha | arquivo | o que mostra |
|---|---|---|
| 1 | `Main.dc.html` | listagem com origem e falha de sincronização (#107) |
| 2a, 2b | `NovaBaseManual.dc.html`, `NovaBase.dc.html` | formulário com o card Origem (#106) |
| 2c, 2d | `SeletorPasta*.dc.html` | modal de escolha de pasta (#106) |
| **3a** | `DetalheManual.dc.html` | **cards da base acima das abas (#99)**; aba Histórico na barra (#101) |
| 3b | `HistoricoManual.dc.html` | aba Histórico, base manual (#101) |
| **4a** | `Detalhe.dc.html` | **card de agentes com sete agentes (#99)**; card Origem, documentos somente leitura, arquivos ignorados (#107) |
| 4b | `DetalheFalha.dc.html` | falta de acesso à pasta (#107) |
| 4c | `Historico.dc.html` | aba Histórico, base sincronizada (#101) |

Desta change: **3a**, e da **4a** só o card de agentes com muitos agentes.

Correções de protótipo aplicadas (ver `../design.md`, D2):

- os chips mostram só o nome; o estado do agente continua visível por texto;
- o estado "agentes indisponíveis" não está desenhado e continua existindo.
