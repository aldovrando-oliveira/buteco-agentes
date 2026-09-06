// Normaliza texto para comparação de busca: minúsculas e sem sinais
// diacríticos. `normalize('NFD')` decompõe cada letra acentuada em letra
// base mais marca de acento, e a faixa U+0300–U+036F descarta essas
// marcas — é o que faz "cobranca" encontrar "Cobrança", que é como se
// digita depressa em um painel em português (Decision 2 do design.md da
// change frontend-listas-busca-e-colunas).
export function normalizeForSearch(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase();
}

// Verdadeiro quando qualquer um dos campos contém o termo buscado, já
// normalizados os dois lados. Termo em branco casa com tudo, para que
// limpar a busca devolva a lista inteira.
export function matchesSearch(term: string, ...fields: (string | null | undefined)[]): boolean {
  const normalizedTerm = normalizeForSearch(term.trim());
  if (!normalizedTerm) {
    return true;
  }

  return fields.some((field) => !!field && normalizeForSearch(field).includes(normalizedTerm));
}
