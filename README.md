# Vivat Flow

Vivat Flow is a multi-tenant SaaS platform for service companies. The product is intentionally generic at its core: tenant administration, customers, service catalog, service lines, quotes, contracts, work orders, documents, notifications, audit, and billing-related capabilities must not depend on a specific vertical.

SST/TST is the first vertical and CDS – Treinamento e Segurança do Trabalho is the first pilot context. Cleaning, Flooring, clinics, and other service businesses must be able to use the same core without duplicating the product.

## Architecture at a glance

- ASP.NET Core, C#, Entity Framework Core, PostgreSQL, ASP.NET Core Identity
- React, TypeScript, Vite
- Modular monolith
- Multi-tenant Organization boundary
- Separate Vivat Flow Control Plane for platform administration
- Global Service Lines enabled per Organization
- Tenant-owned Customers, Services, Quotes, Contracts, Work Orders, and future operational records
- Privacy-by-design, security-by-design, and LGPD-aware data minimization

The current technical namespace and solution still use the historical `Tsdt` name. Do not rename those identifiers as incidental cleanup; a repository-wide rename is a separate refactor.

## Development workflow

Repository-changing work follows the mandatory workflow in [AGENTS.md](AGENTS.md): create a task branch before editing, implement and validate proportionally, commit, push, open a PR to `main`, then stop for ChatGPT review and user final merge.

DEV and DEMO are isolated. Standard DEV uses:

- Frontend: `http://127.0.0.1:5175`
- API: `https://localhost:7227`
- Health: `https://localhost:7227/health`
- Database: `vivatflow_dev`

See [Development setup](docs/SETUP.md) for first-time prerequisites and bootstrap. Follow the canonical [runtime environment contract](docs/ENVIRONMENTS.md) to operate DEV, DEMO, or PREVIEW.

## Documentation

- [Product model](docs/PRODUCT_MODEL.md)
- [MVP scope](docs/MVP.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Architecture decisions](docs/DECISIONS.md)
- [Frontend standards](docs/FRONTEND_STANDARDS.md)
- [Development setup](docs/SETUP.md)
- [Runtime environments](docs/ENVIRONMENTS.md)
- [Contributor/agent instructions](AGENTS.md)
