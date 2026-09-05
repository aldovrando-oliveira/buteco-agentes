import { RouterProvider, createBrowserRouter } from 'react-router';
import { appRoutes } from './routes';

// Criado uma única vez, no escopo do módulo: um router recriado a cada
// render perderia o estado de navegação. A árvore vem de routes.tsx, que
// não tem efeito colateral e por isso pode ser importada por testes sem
// arrastar este histórico junto (Decision 2 do design.md da change
// frontend-roteamento-data-router).
//
// Data mode (createBrowserRouter + RouterProvider) e não o modo
// declarativo (<BrowserRouter> + <Routes>) porque as APIs de interceptação
// de navegação do react-router — useBlocker, usado para avisar sobre
// alterações não salvas — resolvem o contexto de data router e estouram
// fora dele.
const router = createBrowserRouter(appRoutes);

export function AppRouter() {
  return <RouterProvider router={router} />;
}
