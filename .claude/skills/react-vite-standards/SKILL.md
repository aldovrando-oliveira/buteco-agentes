# SKILL.md — React + Vite Best Practices

## Purpose

This skill defines the architecture, coding standards, component organization and state management practices for React applications built with Vite.

These guidelines aim to produce applications that are scalable, maintainable, testable and easy to evolve.

---

# Core Principles

Every React application should prioritize:

- Simplicity
- Composition over inheritance
- Predictability
- Reusability
- Separation of concerns
- Performance
- Accessibility
- Testability
- Maintainability

The UI should be declarative.

Business rules should never live inside components.

---

# Recommended Stack

- React 19+
- Vite
- TypeScript (mandatory)
- React Router
- TanStack Query
- Zustand
- React Hook Form
- Zod
- Axios (or Fetch wrapper)
- TailwindCSS or Material UI (project decision)
- ESLint
- Prettier

---

# Project Structure

Organize by feature instead of file type.

```
src/

    app/
        App.tsx
        providers/
        router/

    assets/

    components/
        ui/
        layout/
        feedback/

    features/
        auth/
        users/
        products/
        dashboard/

    hooks/

    services/

    lib/

    stores/

    contexts/

    types/

    utils/

    styles/

    main.tsx
```

Avoid large folders such as:

```
components/
pages/
services/
utils/
```

containing hundreds of unrelated files.

---

# Feature Structure

Each feature should be self-contained.

Example

```
features/

    users/

        api/

        components/

        hooks/

        pages/

        schemas/

        services/

        types/

        store/

        utils/
```

Everything related to Users stays inside the Users feature.

---

# Component Organization

Separate components into layers.

```
components/

    ui/
        Button
        Input
        Card

    layout/
        Sidebar
        Header
        Footer

    feedback/
        Loading
        EmptyState
        ErrorMessage
```

Feature-specific components belong inside the feature.

Avoid putting everything inside `components`.

---

# Component Rules

Each component should have a single responsibility.

Good

```
UserAvatar

ProductCard

PriceBadge

OrderStatus

Pagination
```

Bad

```
DashboardComponent

MainComponent

EverythingComponent
```

---

# Component Size

Aim for:

- under 150 lines

Maximum acceptable:

- around 250 lines

If larger, split responsibilities.

---

# Component Responsibilities

Components should only:

- render UI
- receive props
- emit events
- call hooks

Components should not:

- implement business rules
- call APIs directly
- manipulate global state
- contain SQL or complex calculations

---

# Smart vs Dumb Components

Prefer presentational components.

Container components should only coordinate.

Presentation

```
<ProductCard />
```

Container

```
<ProductListPage />
```

---

# State Management

Always ask:

Does this state need to be global?

If not, keep it local.

Priority:

1. Local State
2. Custom Hook
3. Feature Store
4. Global Store

---

# Local State

Use

```
useState
```

for:

- modal open
- input value
- selected tab
- loading indicator

Never place local UI state inside global stores.

---

# Global State

Use Zustand.

Store only data shared across multiple screens.

Examples

- authenticated user
- permissions
- theme
- shopping cart
- language
- notifications

Avoid storing:

- forms
- modal state
- filters used in one screen only

---

# Server State

Never store API data inside Zustand.

Use TanStack Query.

Responsibilities

- cache
- refetch
- pagination
- retries
- optimistic updates
- synchronization

React Query owns server state.

---

# Form State

Use

React Hook Form

Validation

Zod

Never control large forms manually.

---

# API Layer

Never call fetch inside components.

Good

```
services/

    api.ts

features/users/api/

    getUsers.ts

    createUser.ts

    updateUser.ts
```

Components call hooks.

Hooks call services.

Services call HTTP.

---

# Custom Hooks

Move logic into hooks.

Good

```
useCurrentUser()

useProducts()

usePermissions()

useLogin()

usePagination()
```

Avoid hooks with multiple responsibilities.

---

# Context API

Use Context only for:

- Theme
- Authentication provider
- Localization

Avoid using Context as a global state solution.

---

# Routing

Use React Router.

Each feature owns its routes when possible.

Protect routes through wrappers.

```
ProtectedRoute

AdminRoute
```

---

# Pages

Pages should compose components.

Avoid implementing business logic.

Good

```
DashboardPage

UserPage

LoginPage
```

---

# UI Components

Reusable components belong in

```
components/ui
```

Examples

Button

Input

Dialog

Table

Badge

Spinner

Card

Avatar

---

# Business Components

Business-specific components stay inside features.

Example

```
features/orders/components

OrderCard

OrderTimeline

OrderStatusBadge
```

---

# Styling

Keep styling consistent.

Avoid inline styles.

Prefer

TailwindCSS

or

CSS Modules

or

Material UI styling

Choose one strategy.

---

# Naming Convention

Components

```
UserCard.tsx

ProductTable.tsx

LoginForm.tsx
```

Hooks

```
useAuth.ts

useProducts.ts
```

Stores

```
authStore.ts

themeStore.ts
```

Types

```
User.ts

Product.ts
```

---

# Props

Keep props minimal.

Bad

```
<Component
    user
    orders
    products
    settings
    permissions
/>
```

Good

```
<UserCard user={user} />
```

---

# Avoid Prop Drilling

Use

- composition
- custom hooks
- context
- Zustand

Avoid passing props through many levels.

---

# Memoization

Use

```
useMemo
useCallback
React.memo
```

Only when profiling demonstrates benefit.

Do not optimize prematurely.

---

# Keys

Never use array index as key.

Bad

```
key={index}
```

Good

```
key={user.id}
```

---

# Error Handling

Every async operation should handle:

- loading
- success
- empty
- error

Prefer reusable components

```
LoadingState

ErrorState

EmptyState
```

---

# Loading

Never leave blank screens.

Use

Skeleton

Spinner

Progress

depending on context.

---

# Accessibility

Always

- use semantic HTML
- associate labels
- keyboard navigation
- aria attributes when needed
- visible focus

Never sacrifice accessibility for aesthetics.

---

# Performance

Lazy load routes.

```
React.lazy()

Suspense
```

Split bundles by feature.

Avoid unnecessary renders.

---

# Environment Variables

Use only

```
VITE_API_URL

VITE_ENVIRONMENT
```

Never expose secrets.

Everything under VITE_ is public.

---

# Folder Responsibilities

```
components/
Reusable UI

features/
Business modules

hooks/
Reusable hooks

stores/
Global Zustand stores

services/
HTTP

lib/
External libraries

types/
Global types

utils/
Pure helper functions

assets/
Images/fonts/icons

styles/
Global styling
```

---

# Testing

Recommended

Vitest

React Testing Library

Test

- hooks
- utilities
- services
- components

Avoid snapshot abuse.

Prefer behavioral testing.

---

# Linting

Always use

ESLint

Prettier

TypeScript strict mode

Fix warnings before merging.

---

# Code Smells

Refactor when you see:

- Components larger than 250 lines
- Hooks larger than 150 lines
- Deep prop drilling
- Repeated JSX
- Repeated business rules
- Multiple useEffect chains
- API calls inside components
- Large global stores
- Anonymous functions everywhere
- Excessive conditional rendering

---

# Anti-Patterns

Never

- Store server data inside Zustand
- Fetch directly inside UI components
- Mix business logic with rendering
- Use Context for everything
- Create gigantic components
- Create gigantic stores
- Duplicate API requests
- Ignore loading states
- Ignore error states
- Ignore accessibility
- Use any unnecessarily
- Disable TypeScript strict mode

---

# Recommended Architecture Flow

```
Page

↓

Feature Component

↓

Custom Hook

↓

Service

↓

API

↓

Backend
```

Business rules should live inside:

- hooks
- services
- domain logic

Never inside UI components.

---

# Agent Instructions

Whenever generating React + Vite code, always:

- Use TypeScript with strict typing.
- Organize code by feature rather than by file type.
- Keep components small and focused on a single responsibility.
- Separate UI components from business-specific components.
- Place reusable UI elements in `components/ui`.
- Move business logic into custom hooks or services.
- Use TanStack Query for all server state.
- Use Zustand only for shared client state.
- Keep UI-only state local with `useState`.
- Use React Hook Form + Zod for forms and validation.
- Avoid API calls directly in components.
- Favor composition over inheritance.
- Use lazy loading for routes when appropriate.
- Ensure accessibility and responsive behavior by default.
- Write maintainable, scalable and testable code following modern React best practices.