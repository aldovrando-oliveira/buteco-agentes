import { useState } from 'react';
import {
  ActionIcon,
  AppShell as MantineAppShell,
  Avatar,
  Group,
  NavLink,
  ScrollArea,
  Stack,
  Text,
  UnstyledButton,
} from '@mantine/core';
import {
  BookOpen,
  Bot,
  ChartColumn,
  LayoutGrid,
  LogOut,
  MessagesSquare,
  Moon,
  Plug,
  Server,
  Shield,
  Sun,
  Users,
} from 'lucide-react';
import { Link, Outlet, useLocation } from 'react-router';
import { Logo } from '../brand/Logo';
import { useComputedColorScheme, useMantineColorScheme } from '@mantine/core';
import { logout, usePapel, useSession } from '../../auth/session';
import { permissoes, rotuloDoPapel } from '../../auth/roles';
import { userInitials } from './userInitials';

const navItems = [
  // Aponta para /inventory, e NÃO para '/', embora a raiz redirecione para cá:
  // o `active` abaixo é `startsWith(to)`, e com `to="/"` este item ficaria
  // permanentemente ativo em toda rota do painel (design.md, D1).
  { to: '/inventory', label: 'Inventário', icon: LayoutGrid },
  { to: '/agents', label: 'Agentes', icon: Bot },
  { to: '/mcp-servers', label: 'Servidores MCP', icon: Server },
  // Terceira posição, antes de Canais, como no protótipo. O ícone é BookOpen do
  // lucide-react, e não o placeholder de letra "K" do protótipo nem o Tabler que
  // o README do handoff supõe: o painel usa lucide desde a change da casca
  // (design.md da change frontend-knowledge-base-catalogo, D8).
  { to: '/knowledge-bases', label: 'Conhecimento', icon: BookOpen },
  { to: '/channels', label: 'Canais', icon: MessagesSquare },
  // Última posição, como no `Insights-Claro.dc.html` desenha a barra. O ícone é
  // ChartColumn do lucide-react — o painel usa lucide desde a change da casca.
  { to: '/insights', label: 'Insights', icon: ChartColumn },
];

// A seção "Acesso" do protótipo de autenticação, depois de Insights, só para o Admin (spec
// `frontend-app-shell`; design.md da change papeis-no-painel, D2). "Aplicações externas" entra
// entre Usuários e Identidade, como os quadros da #188, com o plugue do protótipo (design.md da
// change aplicacoes-externas-painel, D1).
const accessItems = [
  { to: '/users', label: 'Usuários', icon: Users },
  { to: '/applications', label: 'Aplicações externas', icon: Plug },
  { to: '/identity', label: 'Identidade', icon: Shield },
];

export function AppShell() {
  const location = useLocation();
  const naMinhaConta = location.pathname === '/account' || location.pathname.startsWith('/account/');
  const { setColorScheme } = useMantineColorScheme();
  // useMantineColorScheme devolve a preferência crua, que com o padrão "auto"
  // é a string 'auto' — nunca 'dark'. Quem resolve a preferência do sistema
  // operacional para o esquema efetivo é useComputedColorScheme; sem ele o
  // rótulo do botão mentiria e o primeiro clique não mudaria nada na tela de
  // quem usa o SO no escuro.
  const isDark = useComputedColorScheme('light') === 'dark';
  const session = useSession();
  // O papel do id token, relido quando a renovação grava um id token novo: o menu e o rodapé
  // mudam sem recarregar (design.md da change papeis-no-painel, D1).
  const papel = usePapel();
  const [saindo, setSaindo] = useState(false);

  // O logout espera o lock da renovação: com uma renovação em voo em outra aba, isso
  // leva até ela terminar (no pior caso, 35 s), e o item diz "Saindo…" enquanto isso
  // (design.md da change painel-entra-pelo-emissor, D3 e D7).
  const sair = async () => {
    setSaindo(true);
    await logout();
  };

  const navItem = ({ to, label, icon: Icon }: (typeof navItems)[number]) => (
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
  );

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
          {navItems.map(navItem)}
          {/* O rótulo da seção, como o `.navsec` do quadro: não é link nem item de
              navegação, e nunca fica ativo. O tom é o `dimmed`, o mesmo do `SectionLabel`.
              Só para o Admin; o papel desconhecido vale Leitor. */}
          {permissoes(papel).gerirAcesso && (
            <>
              <Text size="xs" fw={600} tt="uppercase" c="dimmed" px={10} pt={14} pb={4} style={{ letterSpacing: '0.04em' }}>
                Acesso
              </Text>
              {accessItems.map(navItem)}
            </>
          )}
        </MantineAppShell.Section>

        {/* O rodapé do protótipo de autenticação (quadros da entrega `m2`): avatar com as
            iniciais, o nome e, abaixo, o papel do id token, e os botões de tema e de Sair.
            Nenhum dado que não venha do id token (design.md da change
            painel-entra-pelo-emissor, D7). A segunda linha passou do e-mail ao papel com a
            #185 (design.md da change papeis-no-painel, D5); com papel desconhecido, fica vazia. */}
        <MantineAppShell.Section
          p={10}
          style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
        >
          <Group gap={6} wrap="nowrap">
            <Avatar
              size={30}
              radius="xl"
              aria-hidden="true"
              // O fundo vai no placeholder, que cobre a raiz inteira com o cinza do
              // próprio Avatar. Visto só no navegador: o jsdom não carrega o CSS do
              // Mantine (validação visual da 11.5).
              styles={{
                placeholder: {
                  background: 'var(--buteco-avatar-bg)',
                  color: 'var(--buteco-avatar-ink)',
                  fontSize: 11,
                  fontWeight: 600,
                },
              }}
            >
              {userInitials(session?.nome, session?.email)}
            </Avatar>
            {/* O nome e a segunda linha levam a Minha conta, como no protótipo; o avatar e
                os botões ficam fora do link (design.md da change minha-conta-painel, D1). O
                `title` "Minha conta" diz para onde vai; o nome completo continua no texto. */}
            <UnstyledButton
              component={Link}
              to="/account"
              title="Minha conta"
              aria-current={naMinhaConta ? 'page' : undefined}
              style={{ flex: 1, minWidth: 0, borderRadius: 4 }}
            >
              <Stack gap={0} style={{ lineHeight: 1.25 }}>
                <Text fz="sm" fw={500} lh={1.25} truncate="end">
                  {session?.nome ?? session?.email}
                </Text>
                {papel && (
                  <Text fz={11} c="dimmed" lh={1.25} truncate="end">
                    {rotuloDoPapel(papel)}
                  </Text>
                )}
              </Stack>
            </UnstyledButton>
            <ActionIcon
              variant="subtle"
              color="gray"
              size={32}
              aria-label={isDark ? 'Mudar para tema claro' : 'Mudar para tema escuro'}
              title={isDark ? 'Mudar para tema claro' : 'Mudar para tema escuro'}
              onClick={() => setColorScheme(isDark ? 'light' : 'dark')}
            >
              {isDark ? <Sun size={16} /> : <Moon size={16} />}
            </ActionIcon>
            <ActionIcon
              variant="subtle"
              color="gray"
              size={32}
              aria-label={saindo ? 'Saindo…' : 'Sair'}
              title={saindo ? 'Saindo…' : 'Sair'}
              loading={saindo}
              loaderProps={{ size: 14 }}
              disabled={saindo}
              onClick={() => void sair()}
            >
              <LogOut size={16} />
            </ActionIcon>
          </Group>
        </MantineAppShell.Section>
      </MantineAppShell.Navbar>

      <MantineAppShell.Main>
        <Outlet />
      </MantineAppShell.Main>
    </MantineAppShell>
  );
}
