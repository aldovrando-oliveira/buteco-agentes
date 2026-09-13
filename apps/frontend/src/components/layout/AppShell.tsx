import {
  ActionIcon,
  AppShell as MantineAppShell,
  Group,
  NavLink,
  ScrollArea,
  Text,
} from '@mantine/core';
import { BookOpen, Bot, MessagesSquare, Moon, Server, Sun } from 'lucide-react';
import { Link, Outlet, useLocation } from 'react-router';
import { Logo } from '../brand/Logo';
import { useComputedColorScheme, useMantineColorScheme } from '@mantine/core';

const navItems = [
  { to: '/agents', label: 'Agentes', icon: Bot },
  { to: '/mcp-servers', label: 'Servidores MCP', icon: Server },
  // Terceira posição, antes de Canais, como no protótipo. O ícone é BookOpen do
  // lucide-react, e não o placeholder de letra "K" do protótipo nem o Tabler que
  // o README do handoff supõe: o painel usa lucide desde a change da casca
  // (design.md da change frontend-knowledge-base-catalogo, D8).
  { to: '/knowledge-bases', label: 'Conhecimento', icon: BookOpen },
  { to: '/channels', label: 'Canais', icon: MessagesSquare },
];

export function AppShell() {
  const location = useLocation();
  const { setColorScheme } = useMantineColorScheme();
  // useMantineColorScheme devolve a preferência crua, que com o padrão "auto"
  // é a string 'auto' — nunca 'dark'. Quem resolve a preferência do sistema
  // operacional para o esquema efetivo é useComputedColorScheme; sem ele o
  // rótulo do botão mentiria e o primeiro clique não mudaria nada na tela de
  // quem usa o SO no escuro.
  const isDark = useComputedColorScheme('light') === 'dark';

  return (
    // Sem header: a marca sobe para o topo da barra e o controle de tema desce
    // para o rodapé (design.md da change frontend-shell-navegacao-e-icones,
    // D3). Sem colapso em gaveta: o Burger era hiddenFrom="sm" e nunca aparecia
    // no desktop; abaixo desse ponto o conteúdo aperta em vez de virar gaveta
    // (D4).
    <MantineAppShell navbar={{ width: 224, breakpoint: 'sm' }} padding="md">
      <MantineAppShell.Navbar>
        <MantineAppShell.Section
          p="sm"
          style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
        >
          <Group gap="xs" wrap="nowrap">
            <Logo size={24} />
            <Text fw={600} fz="sm" style={{ letterSpacing: '-0.01em' }}>
              Buteco Agentes
            </Text>
          </Group>
        </MantineAppShell.Section>

        <MantineAppShell.Section grow component={ScrollArea} p="xs">
          {navItems.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              component={Link}
              to={to}
              label={label}
              leftSection={<Icon size={18} />}
              // Ativo para todo o grupo de rotas, não só para a raiz dele: o
              // detalhe, a criação e a edição continuam marcando o item.
              active={location.pathname.startsWith(to)}
              // O fundo já resolve para o tom 1 da escala de destaque — o
              // --acsf do protótipo — sem declarar cor aqui. A cor do texto,
              // não: a variante clara do NavLink usa o tom 9, e o protótipo
              // quer a cor de destaque. O raio também não vem por prop: o
              // NavLink não expõe `radius`, e o default é um bloco reto de
              // largura inteira (D5).
              styles={{
                root: {
                  borderRadius: 'var(--mantine-radius-sm)',
                  '--nl-color': 'var(--mantine-primary-color-filled)',
                },
              }}
            />
          ))}
        </MantineAppShell.Section>

        <MantineAppShell.Section
          p="sm"
          style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
        >
          <ActionIcon
            variant="subtle"
            color="gray"
            size="lg"
            aria-label={isDark ? 'Mudar para tema claro' : 'Mudar para tema escuro'}
            onClick={() => setColorScheme(isDark ? 'light' : 'dark')}
          >
            {isDark ? <Sun size={18} /> : <Moon size={18} />}
          </ActionIcon>
        </MantineAppShell.Section>
      </MantineAppShell.Navbar>

      <MantineAppShell.Main>
        <Outlet />
      </MantineAppShell.Main>
    </MantineAppShell>
  );
}
