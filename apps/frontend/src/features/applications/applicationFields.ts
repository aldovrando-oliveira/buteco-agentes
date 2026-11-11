import { DESCRICAO_LONGA, INFORME_O_NOME, NOME_LONGO } from './applicationsTexts';
import type { SecretSummary } from './types';

// As regras de campo e de segredo que as telas compartilham, fora dos componentes (Fast Refresh).
// A conferência dos campos é a do servidor (spec `external-applications`: 1 a 80 caracteres
// aparados, descrição até 280); o painel confere antes para não mandar a senha à toa.

export interface CamposAplicacao {
  nome: string;
  descricao: string;
}

export interface ErrosAplicacao {
  nome?: string;
  descricao?: string;
}

export function conferirCampos({ nome, descricao }: CamposAplicacao): ErrosAplicacao {
  const erros: ErrosAplicacao = {};
  const aparado = nome.trim();
  if (aparado === '') erros.nome = INFORME_O_NOME;
  else if (aparado.length > 80) erros.nome = NOME_LONGO;
  if (descricao.trim().length > 280) erros.descricao = DESCRICAO_LONGA;
  return erros;
}

/** O corpo de `POST`/`PUT`: o nome aparado e a descrição vazia como `null`. */
export function corpoDosCampos({ nome, descricao }: CamposAplicacao) {
  const d = descricao.trim();
  return { nome: nome.trim(), descricao: d === '' ? null : d };
}

/** Vivo é não revogado e não vencido: `ativo` E `substituido` (spec `external-applications`). */
export const estaVivo = (s: SecretSummary) => s.estado === 'ativo' || s.estado === 'substituido';
