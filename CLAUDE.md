# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

XafSearch is a DevExpress XAF (eXpressApp Framework) application providing runtime-configurable search panels for any XAF entity. Admins define search screens through the UI; Roslyn compiles search DTOs at runtime. Uses EF Core with SQL Server.

## Solution Structure

- **Solution file:** `XafSearch.slnx` (XML-based solution format)
- **XafSearch.Module** — Platform-agnostic shared module. Business objects (EF Core entities), controllers, services, and XAF module configuration. Both UI projects reference this.
- **XafSearch.Blazor.Server** — Blazor Server UI host. Also exposes a Web API (OData + Swagger) and a Reports API controller.
- **XafSearch.Win** — Windows Forms UI host (net8.0-windows).

## Key Architecture

### Runtime Search Panel System

- **SearchConfiguration / SearchField** — Persistent entities defining search screens (target entity, fields, display names, filter modes).
- **SearchDtoCompiler** — Roslyn-based compiler that generates `NonPersistentBaseObject` DTOs from configuration metadata.
- **SearchDtoRegistry** — Singleton that compiles all active configs from DB on startup, registers types with XAF.
- **SearchPanelController** — Adds "Advanced Search" popup to any ListView with an active config.
- **CriteriaBuilder** — Builds `CriteriaOperator` filters from DTO property values (wildcards, ranges, lookups).

### Startup Lifecycle (Critical)

Types must be registered **before** XAF model generation for proper `TypeInfo`:

1. `Module.Setup(XafApplication)` → `SearchDtoRegistry.CompileFromDatabase()` using raw `DbContext`
2. Compiles all active configs, registers with `XafTypesInfo` + `AdditionalExportedTypes`
3. Cleans orphaned model diffs (deleted configs only — preserves Model Editor layouts)
4. `base.Setup()` → XAF model generation creates proper `IModelClass` nodes

**Restart required** after creating/modifying search configurations. Model Editor customizations persist across restarts for active configs.

## Build Commands

```bash
# Build entire solution
dotnet build XafSearch.slnx

# Build specific project
dotnet build XafSearch/XafSearch.Blazor.Server/XafSearch.Blazor.Server.csproj

# Run Blazor Server app
dotnet run --project XafSearch/XafSearch.Blazor.Server/XafSearch.Blazor.Server.csproj

# Update database via CLI
dotnet run --project XafSearch/XafSearch.Blazor.Server/XafSearch.Blazor.Server.csproj -- --updateDatabase --forceUpdate --silent
```

Build configurations: `Debug`, `Release`, `EasyTest`.

## Database

- **ORM:** EF Core 8 with SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`)
- **DbContext:** `XafSearch.Module.BusinessObjects.XafSearchEFCoreDbContext`
- **Connection string:** configured in `XafSearch/XafSearch.Blazor.Server/appsettings.json` under `ConnectionStrings:ConnectionString`
- **Default:** LocalDB (`(localdb)\mssqllocaldb`, catalog `XafSearch`)
- EF Core Design package is included with `PrivateAssets=none` for migrations support
- **Runtime tables:** `SearchConfigurations`, `SearchFields`

## Key Conventions

- **Business objects** go in `XafSearch.Module/BusinessObjects/` as EF Core entities. Register `DbSet<T>` properties on `XafSearchEFCoreDbContext`.
- **Controllers** go in `XafSearch.Module/Controllers/` for platform-agnostic, or in the respective UI project for platform-specific.
- **Services** go in `XafSearch.Module/Services/`.
- **Database seed data** goes in `XafSearch.Module/DatabaseUpdate/Updater.cs`.
- **XAF model customizations** use `.xafml` files: `Model.DesignedDiffs.xafml` (Module), `Model.xafml` (Blazor/Win).
- The DbContext uses deferred deletion, optimistic locking, and `ChangingAndChangedNotificationsWithOriginalValues` change tracking strategy.
- Runtime-compiled types use namespace `XafSearch.RuntimeSearch` and naming pattern `{EntityShortName}SearchDTO`.

## Workflow

1. **Plan first.** Before implementing anything, create a plan. Use the brainstorming skill for creative/feature work, writing-plans skill for multi-step tasks.
2. **Track with todos.** Register all planned work items as todos (TodoWrite) before starting implementation. Update todo status as work progresses.
3. **Commit often.** Commit after each meaningful unit of work, not just at the end.
4. **Test thoroughly.** Write and run tests for all changes. Use unit tests for business logic, integration tests for EF Core/XAF interactions.
5. **Playwright E2E tests.** For UI and Web API testing, use Playwright (Python) running in a Docker container. Prefer E2E tests for any user-facing functionality.
6. **Session handoff.** At the end of every session:
   - Create a `session_handoff.md` in the project root summarizing current state, open items, and next steps.
   - Update all todos to reflect actual progress.
   - Record interesting findings, patterns, or gotchas as memories in the appropriate skill or memory file.

## DevExpress / XAF Specifics

- DevExpress version: **25.2.3** across all packages
- Blazor Server uses `DevExpress.Drawing.Skia` for cross-platform rendering
- Web API is configured with OData v4.01 (max 100 query results)
- Swagger UI available in Development mode at `/swagger`
- Modules enabled: ConditionalAppearance, Dashboards, FileAttachments, Notifications, Office, Reports, Validation, ViewVariants, Chart, PivotGrid, TreeListEditors
- `#pragma warning disable XAF0013` is used in Module.cs to access `application.ConnectionString` before DI is available
