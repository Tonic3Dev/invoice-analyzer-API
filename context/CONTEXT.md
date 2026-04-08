# bringeri-api Context

## Purpose
This API powers Invoice Analyzer as a multi-tenant backend using one database and one API instance for all tenants.

## Current Features (Phase 1)
- Tenant model stored in database with branding and language:
  - tenant slug
  - tenant display name
  - page title
  - primary and secondary colors
  - default language
  - logo (optional)
- User model stored with tenantId and role.
- Roles currently available:
  - Admin
  - User
- Login endpoint with JWT bearer token.
- Tenant mismatch detection on login:
  - if credentials belong to another tenant, API returns `TENANT_MISMATCH`
  - response includes `tenantSlug` and `tenantEncoded` to support frontend redirect
- Open endpoint to create admin users (for tenant bootstrapping).
- Open endpoint to create regular users (for phase-1 testing).
- Tenant branding endpoint to bootstrap frontend branding.
- Invoice batch persistence per tenant:
  - one saved batch can contain one or many invoice files
  - each file is analyzed and stored as its own invoice document inside the batch
  - original uploaded file bytes are stored in the database
  - normalized issuer/document/recipient/totals fields are stored in relational columns
  - extracted line items are stored in a child table
  - raw Serenity agent response is stored per invoice document
- Serenity-backed invoice analysis integration:
  - uploads files to `VolatileKnowledge`
  - executes `InvoiceBridge`
  - prefers `jsonContent` from the response and falls back to parsing `content`
- Protected invoice batch endpoints for:
  - previewing analysis from uploaded files without persisting drafts
  - saving reviewed batches
  - listing saved batches
  - retrieving saved batch detail
  - admin-only post-save updates
  - downloading saved batch zip archives
  - downloading individual saved invoice files
- Swagger UI enabled.
- CORS enabled for all origins, headers, and methods.
- Startup database flow uses EF migrations (`MigrateAsync`) and then seeding.

## Tenant Resolution
- Frontend reads tenant from URL query (encoded `t`, `tenantName`, `tenant`, and shorthand variants).
- Frontend sends tenant in `X-Tenant` header.
- API resolves tenant context from `X-Tenant`.
- User queries are tenant-scoped through DbContext query filter.

## Auth Flow
1. Client calls `POST /api/auth/login` with email/password and `X-Tenant`.
2. API validates user in tenant scope.
3. If credentials are valid but belong to another tenant, API returns `TENANT_MISMATCH` with redirect hint.
4. Otherwise API returns JWT + user payload.
5. Client uses token as Bearer auth.
6. Client can fetch authenticated profile from `GET /api/auth/me`.

## Important Endpoints
- `POST /api/auth/login`
- `GET /api/auth/me`
- `GET /api/tenants/branding?tenantName={slug}`
- `POST /api/admin-users/admin`
- `POST /api/admin-users/user`
- `POST /api/invoice-batches/preview`
- `POST /api/invoice-batches`
- `GET /api/invoice-batches`
- `GET /api/invoice-batches/{batchId}`
- `PUT /api/invoice-batches/{batchId}`
- `GET /api/invoice-batches/{batchId}/download`
- `GET /api/invoice-batches/files/{fileId}`

## Environment Variables
- `INVOICE_ANALYZER_CONNECTION_STRING`: PostgreSQL connection string.
- `INVOICE_ANALYZER_JWT_KEY`: secret key for token signing.
- `INVOICE_SERENITY_API_KEY`: Serenity Star API key used for invoice upload and analysis.
- Optional: `JWT_EXPIRATION_DAYS`.
- Optional: `Serenity:BaseUrl` (defaults to `https://api.serenitystar.ai/api/v2/`).

## Seeded Tenants
- `tonic3`
  - primary: `#cb4b27`
  - secondary: `#180901`
  - language: `en`
- `bringeri`
  - primary: `#ad160d`
  - secondary: `#f5f5f5`
  - language: `es`

## Notes For Next Iterations
- Add role-based authorization policies.
- Add stronger file validation and size/type restrictions for invoice uploads.
- Consider background processing if synchronous multi-file analysis becomes too slow.
- Add audit metadata for who performed post-save admin edits.
- Expand i18n support for backend responses if required.
