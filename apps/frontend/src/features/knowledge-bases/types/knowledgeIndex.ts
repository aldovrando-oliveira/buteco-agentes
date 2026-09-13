// Espelha KnowledgeIndexProvenanceResponse de apps/api, servido por
// GET /knowledge-index/diagnostics.
//
// A PROVENIÊNCIA É DO ÍNDICE INTEIRO, NUNCA DE UMA BASE.
//
// A rota não aceita identificador de base, e isso não é economia de parâmetro: o
// schema torna impossível bases com proveniências diferentes — a coluna do vetor
// é `vector(4096)` em AppDbContext, e gravar outra dimensão é erro na hora. Uma
// consulta por base produziria a anomalia de a base A exibir travessão enquanto a
// base B, ao lado e sobre o mesmo índice, exibe o modelo (design.md de
// knowledge-index-diagnostics, D1).
//
// Os quatro nomes vêm dos CORPOS REAIS da rota, capturados à mão com o app de pé
// e guardados na seção "Corpos reais da rota" daquele design.md — não de leitura
// do C#. É a forma que a convenção 12 pede: nome no fio é parte do contrato, e
// teste que desserializa para o mesmo tipo é cego a ele.
//
// Nenhum campo opcional: a rota devolve os quatro sempre, ou não devolve o item.
export interface KnowledgeIndexProvenance {
  provider: string;
  model: string;
  dimensions: number;
  // `long` no servidor (`count(*)` do PostgreSQL é `bigint`), `number` aqui — e
  // não precisa de `bigint`. O gatilho de otimização registrado no backend é 500
  // MB de heap, cerca de 320 mil fragmentos; `Number.MAX_SAFE_INTEGER` é 9×10^15,
  // dez ordens de grandeza acima. Trocar por `bigint` quebraria a serialização
  // JSON e não compraria nada.
  fragmentCount: number;
}

// O nome real do modelo em uso é `qwen-qwen3-embedding-8b`, com dimensão 4096.
// O protótipo do handoff mostra `text-embedding-3-small` / 1536 — é MOCK, não
// contrato, e a dimensão dele nem cabe na coluna. Registrado aqui porque é o
// valor que alguém copia ao escrever um fixture.
