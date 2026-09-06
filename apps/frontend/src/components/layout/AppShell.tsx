import {
  ActionIcon,
  AppShell as MantineAppShell,
  Burger,
  Group,
  NavLink,
  Text,
  useComputedColorScheme,
  useMantineColorScheme,
} from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { Link, Outlet, useLocation } from 'react-router';

function SunIcon() {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41" />
    </svg>
  );
}

function MoonIcon() {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79Z" />
    </svg>
  );
}

export function AppShell() {
  const [opened, { toggle }] = useDisclosure();
  const location = useLocation();
  const { setColorScheme } = useMantineColorScheme();
  // useMantineColorScheme devolve a preferência crua, que com o padrão "auto"
  // é a string 'auto' — nunca 'dark'. Quem resolve a preferência do sistema
  // operacional para o esquema efetivo é useComputedColorScheme; sem ele o
  // rótulo do botão mentiria e o primeiro clique não mudaria nada na tela de
  // quem usa o SO no escuro.
  const isDark = useComputedColorScheme('light') === 'dark';

  return (
    <MantineAppShell
      header={{ height: 60 }}
      navbar={{ width: 260, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      padding="md"
    >
      <MantineAppShell.Header>
        <Group h="100%" px="md" justify="space-between">
          <Group>
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
            <Text fw={700}>Buteco Agentes</Text>
          </Group>
          <ActionIcon
            variant="default"
            size="lg"
            aria-label={isDark ? 'Mudar para tema claro' : 'Mudar para tema escuro'}
            onClick={() => setColorScheme(isDark ? 'light' : 'dark')}
          >
            {isDark ? <SunIcon /> : <MoonIcon />}
          </ActionIcon>
        </Group>
      </MantineAppShell.Header>

      <MantineAppShell.Navbar p="md">
        <NavLink
          component={Link}
          to="/agents"
          label="Agentes"
          active={location.pathname.startsWith('/agents')}
        />
        <NavLink
          component={Link}
          to="/mcp-servers"
          label="Servidores MCP"
          active={location.pathname.startsWith('/mcp-servers')}
        />
        <NavLink
          component={Link}
          to="/channels"
          label="Canais"
          active={location.pathname.startsWith('/channels')}
        />
      </MantineAppShell.Navbar>

      <MantineAppShell.Main>
        <Outlet />
      </MantineAppShell.Main>
    </MantineAppShell>
  );
}
