import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import svgr from 'vite-plugin-svgr';

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    // A marca é inlineada no DOM, e não servida por <img>, porque SVG carregado
    // como imagem renderiza em documento isolado — sem as variáveis CSS do
    // Mantine e sem as @font-face de src/fonts.ts (design.md, D1).
    //
    // As duas cores da marca são reescritas aqui, em tempo de build, para
    // tokens do tema. É o que faz a marca trocar de cor junto com o painel sem
    // um `if (isDark)` em componente — e o que dispensa os seis arquivos
    // `*-escuro.svg` do pacote de marca, já que não existe mais "a versão
    // escura", só uma versão que lê o token (D2).
    //
    // A tinta vira `currentColor`, e não uma segunda variável, porque `color` é
    // herdável: quem usa o Logo escolhe a tinta, e uma marca sobre fundo
    // colorido só precisa herdar outro `color`.
    svgr({
      svgrOptions: {
        replaceAttrValues: {
          '#191a1c': 'currentColor',
          '#2a6ecb': 'var(--mantine-primary-color-filled)',
        },
      },
    }),
  ],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    globals: false,
    // Precisa ser maior que o asyncUtilTimeout do setup: um teste que espera
    // 5s por um dropdown não pode ser cortado pelo prazo do próprio teste.
    testTimeout: 15000,
  },
});
