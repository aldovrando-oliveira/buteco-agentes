import { AppShell as MantineAppShell, Burger, Group, NavLink, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import type { PropsWithChildren } from 'react';

const NAV_ITEMS = ['Dashboard', 'Agents', 'Inboxes'];

export function AppShell({ children }: PropsWithChildren) {
  const [opened, { toggle }] = useDisclosure();

  return (
    <MantineAppShell
      header={{ height: 60 }}
      navbar={{ width: 260, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      padding="md"
    >
      <MantineAppShell.Header>
        <Group h="100%" px="md">
          <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
          <Text fw={700}>Buteco Agents</Text>
        </Group>
      </MantineAppShell.Header>

      <MantineAppShell.Navbar p="md">
        {NAV_ITEMS.map((item) => (
          <NavLink key={item} label={item} />
        ))}
      </MantineAppShell.Navbar>

      <MantineAppShell.Main>{children}</MantineAppShell.Main>
    </MantineAppShell>
  );
}
