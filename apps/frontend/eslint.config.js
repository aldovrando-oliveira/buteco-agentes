import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';
import eslintConfigPrettier from 'eslint-config-prettier';

export default tseslint.config(
  { ignores: ['dist'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      ...tseslint.configs.recommended,
      reactHooks.configs.flat['recommended-latest'],
      reactRefresh.configs.vite,
      eslintConfigPrettier,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    // Nenhum HTML vindo de dado (design.md da change painel-entra-pelo-emissor, D2): o
    // refresh token fica no localStorage, e um XSS o levaria. Regras do próprio ESLint,
    // sem dependência nova: o eslint-plugin-react, que tem `react/no-danger`, não está
    // no projeto.
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']",
          message: 'Sem HTML vindo de dado no painel (D2 da change painel-entra-pelo-emissor).',
        },
      ],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: 'rehype-raw',
              message: 'O ReactMarkdown não recebe HTML cru (D2 da change painel-entra-pelo-emissor).',
            },
          ],
        },
      ],
    },
  },
);
