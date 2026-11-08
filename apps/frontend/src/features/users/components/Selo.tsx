import { Badge } from '@mantine/core';
import type { Tom } from '../usersTexts';

// O selo de estado de Usuários e de Editar usuário (saiu do `UsersTable` quando o detalhe virou o
// segundo consumidor; design.md da change editar-usuario-painel, D3).

const COR: Record<Tom, string> = {
  green: 'green',
  yellow: 'yellow',
  gray: 'gray',
  neutral: 'gray',
};

export function Selo({ texto, tom }: { texto: string; tom: Tom }) {
  // O neutro é o contorno, para nunca se confundir com o selo cinza de um estado conhecido.
  return (
    <Badge
      color={COR[tom]}
      variant={tom === 'neutral' ? 'outline' : 'light'}
      tt="none"
      data-tom={tom}
    >
      {texto}
    </Badge>
  );
}
