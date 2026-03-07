# XafSearch

Runtime-configurable search panels for any DevExpress XAF entity. Admins define search screens entirely through the UI — no code changes required. A restart is needed to activate new or changed configurations.

## What It Does

XafSearch lets administrators create search forms for any XAF entity at runtime:

1. **Configure** — Define which entity to search and which fields to expose via `SearchConfiguration` and `SearchField` entities in the UI.
2. **Compile** — Roslyn compiles a search DTO (`NonPersistentBaseObject`) and registers it with XAF's type system. A restart activates it.
3. **Search** — Users open "Advanced Search" from any ListView that has an active configuration, fill in the generated form, and get filtered results.

All configurations persist in the database and are compiled automatically on app startup (before XAF model generation), so types have full `TypeInfo` and Model Editor support.

## Features

- **"Populate Properties" action** — Reads entity metadata and pre-fills `SearchField` entries automatically.
- **"Compile & Activate"** — Compiles the search DTO via Roslyn and caches it. Restart required to activate.
- **"Export C# Source"** — Generates standalone DTO + controller code for "graduating" a runtime config into compiled source.
- **"Generate Search Panel" action** — Available on any ListView; creates a `SearchConfiguration` ad-hoc for that entity.
- **"Advanced Search" popup** — Appears on ListViews that have an active configuration.
- **Customizable layout** — Search form layout can be adjusted via XAF's built-in Model Editor. Customizations persist across restarts as long as the configuration stays active.

### Supported Property Types

| Type | Filter Mode |
|---|---|
| `string` | Exact match or contains with wildcard support (`*` and `?`) |
| `DateTime` | Range filtering (From / To) |
| `int`, `long`, `decimal`, `double`, `float` | Range filtering (From / To) |
| `bool` | Checkbox |
| `Guid` | Exact match |
| Enums | Dropdown selection |
| Reference / Lookup properties | Lookup selection |

## Tech Stack

- **.NET 8** with C# 12
- **DevExpress XAF 25.2.3** (Blazor Server + Windows Forms)
- **EF Core 8** with SQL Server
- **Roslyn** (`Microsoft.CodeAnalysis.CSharp`) for runtime compilation

## Solution Structure

```
XafSearch.Module/              Shared module (entities, services, controllers)
XafSearch.Blazor.Server/       Blazor Server UI host (+ Web API / Swagger)
XafSearch.Win/                 Windows Forms UI host
```

Both UI hosts reference `XafSearch.Module`. All business logic lives in the shared module.

## Key Components

| Component | Role |
|---|---|
| `SearchConfiguration` | Persistent entity defining a search screen (target entity, active flag, etc.). Uses `BaseObjectInt` (int PK). |
| `SearchField` | Persistent entity for each searchable field within a configuration. |
| `SearchDtoCompiler` | Roslyn-based service that compiles search DTOs from `SearchConfiguration` metadata. |
| `SearchDtoRegistry` | Singleton that compiles types from DB on startup, registers them with XAF's type system. |
| `CriteriaBuilder` | Shared logic for building filter criteria — handles wildcards, date ranges, numeric ranges, and reference lookups. |
| `SearchPanelController` | Generic controller that adds "Advanced Search" to any ListView with an active configuration. |
| `SearchConfigurationController` | Admin actions: Populate Properties, Compile & Activate, Export C# Source. |
| `GenerateSearchPanelController` | Adds "Generate Search Panel" action to any ListView for ad-hoc config creation. |
| `SearchControllerBase<T,D>` | Typed base class for exported/graduated controllers (used by generated code). |

## Application Lifecycle

### Startup

1. `XafSearchModule.Setup()` runs **before** model generation
2. `SearchDtoRegistry.CompileFromDatabase()` creates a raw `DbContext`, loads all active `SearchConfiguration` records
3. Each config is compiled via Roslyn into a DTO type
4. Types are registered with `XafTypesInfo` + `AdditionalExportedTypes`
5. Orphaned model diff entries (for deleted configs) are cleaned from `Model.User*.xafml`
6. `base.Setup()` proceeds → XAF model generation creates proper `IModelClass` nodes with full `TypeInfo`

### Runtime Config Change

1. Admin creates/edits `SearchConfiguration`, saves (auto-compiles) or clicks "Compile & Activate"
2. Type is compiled and cached in the registry
3. **Restart required** to activate — types must exist before model generation for proper `TypeInfo`
4. After restart, the search panel appears on the target entity's ListView

## Build and Run

```bash
# Build the solution
dotnet build XafSearch.slnx

# Run the Blazor Server app
dotnet run --project XafSearch/XafSearch.Blazor.Server/XafSearch.Blazor.Server.csproj

# Update the database
dotnet run --project XafSearch/XafSearch.Blazor.Server/XafSearch.Blazor.Server.csproj -- --updateDatabase --forceUpdate --silent
```

## Database

EF Core with SQL Server. Default connection uses LocalDB (`(localdb)\mssqllocaldb`, catalog `XafSearch`). Connection string is in `XafSearch/XafSearch.Blazor.Server/appsettings.json`.
