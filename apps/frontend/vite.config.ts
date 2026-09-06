import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    globals: false,
    // Precisa ser maior que o asyncUtilTimeout do setup: um teste que espera
    // 5s por um dropdown não pode ser cortado pelo prazo do próprio teste.
    testTimeout: 15000,
  },
});
