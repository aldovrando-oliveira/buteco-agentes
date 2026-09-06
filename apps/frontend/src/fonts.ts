// Fontes da identidade visual servidas pelo próprio bundle, não por CDN
// (design.md, D7): o painel é entregue containerizado e não deve depender de
// internet nem de um terceiro no caminho de render.
//
// Importamos subset a subset em vez do arquivo de peso inteiro
// (`@fontsource/ibm-plex-sans/400.css`, que traz cirílico, grego, vietnamita e
// japonês): o navegador só baixaria o latino de qualquer jeito por causa do
// unicode-range, mas o build emitiria todos — 4,2 MB de fontes em `dist` contra
// 168 KB. `latin` + `latin-ext` cobrem o português inteiro.

import '@fontsource/ibm-plex-sans/latin-400.css';
import '@fontsource/ibm-plex-sans/latin-500.css';
import '@fontsource/ibm-plex-sans/latin-600.css';
import '@fontsource/ibm-plex-sans/latin-700.css';
import '@fontsource/ibm-plex-sans/latin-ext-400.css';
import '@fontsource/ibm-plex-sans/latin-ext-500.css';
import '@fontsource/ibm-plex-sans/latin-ext-600.css';
import '@fontsource/ibm-plex-sans/latin-ext-700.css';

import '@fontsource/ibm-plex-mono/latin-400.css';
import '@fontsource/ibm-plex-mono/latin-500.css';
import '@fontsource/ibm-plex-mono/latin-600.css';
import '@fontsource/ibm-plex-mono/latin-ext-400.css';
import '@fontsource/ibm-plex-mono/latin-ext-500.css';
import '@fontsource/ibm-plex-mono/latin-ext-600.css';
