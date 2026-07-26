import { Text, Title } from '@mantine/core';
import { AppShell } from './components/layout/AppShell';

function App() {
  return (
    <AppShell>
      <Title order={2}>Buteco Agents</Title>
      <Text c="dimmed">Scaffold inicial do frontend.</Text>
    </AppShell>
  );
}

export default App;
