# TSDT ERP

TSDT ERP is a reusable vertical ERP for Brazilian Occupational Health and Safety (SST) service companies. CDS – Treinamento e Segurança do Trabalho is the first pilot, not a product dependency.

## Status

The runnable MVP 1 Authentication & Administration module includes secure cookie login/logout, initial ADMIN bootstrap, forced initial password change, and ADMIN-only user administration. Business modules are not implemented.

## Authentication

There is no public registration endpoint. Set `ConnectionStrings__DefaultConnection`, `BootstrapAdmin__Email`, `BootstrapAdmin__FullName`, and `BootstrapAdmin__Password` in the local environment before the first start. The bootstrap account is created only when the user table is empty and must change its password after login.

## Intended technology

- ASP.NET Core, C#, Entity Framework Core, PostgreSQL, ASP.NET Core Identity
- React, TypeScript, Vite
- xUnit and Playwright
- Docker Compose and private object storage when document upload is implemented

## Documentation

- [MVP scope](docs/MVP.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Decisions](docs/DECISIONS.md)
- [Setup](docs/SETUP.md)
- [Contributor instructions](AGENTS.md)
