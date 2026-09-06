import { Text } from '@mantine/core';
import type { ReactNode } from 'react';

interface SectionLabelProps {
  children: ReactNode;
}

// Rótulo em caixa alta dos cabeçalhos de card e de coluna. Existia como cinco
// cópias idênticas em cinco arquivos; aqui vira uma.
//
// O tom NÃO é o terciário que o protótipo usa. Medido sobre a faixa do
// cabeçalho, aquele tom dá 3,06:1, abaixo do mínimo de 4,5:1 que a capability
// frontend-visual-theme exige — e nenhum tom mais claro que o atual passa. O
// recuo em relação ao corpo vem do espaçamento entre letras, da caixa alta e do
// tamanho menor (design.md da change frontend-acabamento-telas, D2).
export function SectionLabel({ children }: SectionLabelProps) {
  return (
    <Text size="xs" fw={600} tt="uppercase" c="dimmed" style={{ letterSpacing: '0.05em' }}>
      {children}
    </Text>
  );
}
