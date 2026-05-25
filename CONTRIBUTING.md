# Contributing

Contributions are welcome. Please follow the guidelines below to keep the process smooth.

## Getting Started

1. Fork the repository and create a branch from `main`.
2. Follow the setup steps in [README.md](README.md#getting-started) to get the project running locally.
3. Make your changes, add tests where appropriate, and ensure all existing tests pass.

## Pull Requests

- Keep pull requests focused — one feature or fix per PR.
- Write a clear description of what the change does and why.
- All CI checks (backend tests, frontend lint/typecheck/unit tests, E2E tests) must pass before merging.

## Code Style

### Backend (.NET)

- Hard tabs, never spaces.
- Braces on every block, even single-line bodies.
- XML doc comments (`///`) on all public types, methods, and properties.
- Verbose inline `//` comments explaining the reasoning behind non-obvious logic.

### Frontend (TypeScript / React)

- Hard tabs, never spaces.
- Minimal comments — only for non-obvious behaviour.
- Components and types in PascalCase; hooks, functions, and variables in camelCase.
- Forms use React Hook Form + Zod. HTTP via the service/hook pattern documented in `CLAUDE.md`.
- Run `pnpm lint` (ESLint) and `pnpm format:check` (Prettier) before submitting. The pre-commit hook enforces lint on staged files automatically.

## Tests

- Backend: run `dotnet test` from the `backend/` directory.
- Frontend unit: run `pnpm test` from the `frontend/` directory.
- E2E: run `pnpm exec playwright test` from the `frontend/` directory (no manual server startup needed).

New features should include tests. Bug fixes should include a regression test where practical.

## Reporting Issues

Please open a GitHub issue with a clear description of the problem, steps to reproduce, and the expected vs actual behaviour.

## Licence

By contributing you agree that your contributions will be licensed under the [MIT Licence](LICENSE).
