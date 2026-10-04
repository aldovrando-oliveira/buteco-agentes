/// <reference types="vite/client" />
/// <reference types="vite-plugin-svgr/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
  // Opcional, ao contrário dos outros dois endereços: ausente desliga a origem
  // sincronizada do cadastro de base (frontend-cadastro-base-sincronizada, D1).
  readonly VITE_CONNECTORS_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
