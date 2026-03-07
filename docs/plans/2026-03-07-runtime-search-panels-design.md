# Runtime Search Panels for XAF

**Date:** 2026-03-07
**Status:** Approved

## Problem

XAF's built-in column filtering is limited. The WLNCentral source generator creates compile-time search panels via `[Searchable]` attributes, but these can't be customized at runtime (layout, field selection) and require recompilation to change.

## Solution

A runtime search panel system where admins configure search screens for any entity via UI. Search DTOs are compiled at runtime via Roslyn as `NonPersistentBaseObject` types, registered with XAF's type system without restart. Search form layout is customizable via XAF's Model Editor. Generated C# source can be exported for "graduation" to compiled code.

## Data Model

### BaseObjectInt

Int PK base class (ported from WLNCentral), used for all new entities:

```csharp
public abstract class BaseObjectInt : IXafEntityObject, IObjectSpaceLink
{
    [Key]
    [VisibleInListView(false)]
    [VisibleInDetailView(false)]
    [VisibleInLookupListView(false)]
    public virtual int ID { get; set; }
    // IObjectSpaceLink, IXafEntityObject implementation
}
```

### SearchConfiguration

Persistent entity representing a search panel for a target entity type.

| Property | Type | Description |
|---|---|---|
| Name | string | Display name, e.g. "Customer Search" |
| TargetEntityType | string | Fully qualified CLR type name |
| IsActive | bool | Enable/disable without deleting |
| Fields | IList\<SearchField\> | Aggregated child collection |

### SearchField

One searchable property within a configuration.

| Property | Type | Description |
|---|---|---|
| SearchConfiguration | FK (int) | Parent reference |
| PropertyName | string | Property name on target entity |
| PropertyTypeName | string | CLR type name, stored for compilation |
| DisplayName | string? | Custom label, falls back to property name |
| UseExactMatch | bool | Strings: exact vs contains/wildcard |
| SortOrder | int | Display order in search form |
| IsReferenceProperty | bool | Navigation/lookup property flag |
| ReferencedTypeName | string? | CLR type of referenced entity |

## Architecture

### SearchDtoCompiler (Service)

Takes a `SearchConfiguration` with its `Fields` collection and:

1. Generates C# source for a `NonPersistentBaseObject` subclass:
   - Class name: `{TargetEntityShortName}SearchDTO`
   - Namespace: `XafSearch.RuntimeSearch`
   - `[DomainComponent]` + `[XafDisplayName("Search {Name}")]`
   - One property per `SearchField`, correctly typed
   - Reference properties use the referenced type directly (XAF handles lookup editors)
2. Compiles via Roslyn into a non-collectible `AssemblyLoadContext`
3. Returns the compiled `Type` and the source string

### SearchDtoRegistry (Singleton Service)

- Holds `Dictionary<int, (Type DtoType, string Source)>` keyed by `SearchConfiguration.ID`
- **Startup:** loads all active configs, compiles, registers with `XafTypesInfo.Instance.RegisterEntity()` + `AdditionalExportedTypes`
- **Runtime change:** recompiles affected DTO, re-registers (old type leaks harmlessly in memory)
- Provides source string for export

### Key Insight: No Restart Required

`NonPersistentBaseObject` DTOs have no database table, no EF Core model involvement, no DDL. This eliminates the entire restart mechanism that XafDynamicAssemblies needed.

## Controllers

### SearchConfigurationController (ObjectViewController<DetailView, SearchConfiguration>)

Actions:
- **Populate Properties** — reads target entity's `ITypeInfo`, enumerates members, populates `Fields` with eligible properties (strings, numerics, dates, bools, enums, references)
- **Compile & Activate** — triggers `SearchDtoCompiler`, registers via `SearchDtoRegistry`
- **Export C# Source** — displays generated `.cs` in popup or downloads as file

### SearchPanelController (ViewController<ListView>)

- Activates on any ListView whose entity type has an active `SearchConfiguration`
- Adds **"Advanced Search"** `PopupWindowShowAction` to toolbar
- On click: creates DTO instance via non-persistent object space, shows popup DetailView
- On execute: builds `CriteriaOperator` from DTO values, applies to ListView

### SearchControllerBase<TEntity, TSearchDTO> (Abstract Base)

Reusable criteria-building logic (ported from WLNCentral):
- String properties: Contains by default, Like with wildcards (* and ?), or exact match
- DateTime properties: full-day range matching
- Numeric/bool/enum: exact match
- Reference properties: match by referenced object's key
- Combined with AND operator
- Applied to ListView with key `"RuntimeAdvancedSearch"`

## Application Lifecycle

### Startup

1. `XafSearchModule.Setup()` fires
2. Queries all `SearchConfiguration` where `IsActive = true`
3. `SearchDtoCompiler.Compile()` for each → gets Type
4. Registers with `XafTypesInfo` + `AdditionalExportedTypes`
5. `SearchDtoRegistry` caches type + source per config ID

### Runtime Config Change

1. Admin creates/edits `SearchConfiguration`, clicks "Compile & Activate"
2. New type compiled and registered
3. Next ListView open picks up the new search panel
4. DTO's DetailView appears in Model Editor for layout customization

### Ad-hoc Generation (from any ListView)

1. User triggers "Generate Search Panel" on a ListView with no config
2. Creates `SearchConfiguration`, auto-populates all eligible properties
3. Opens DetailView for admin to customize
4. Save triggers compile & register

## Export C# Source

The "Export C# Source" action produces a complete `.cs` file containing:
- The search DTO class (NonPersistentBaseObject, [DomainComponent], typed properties)
- A concrete controller class inheriting `SearchControllerBase<TEntity, TSearchDTO>`
- Copy-paste ready into any XAF Module project

## Component Summary

| Component | Location | Purpose |
|---|---|---|
| BaseObjectInt | BusinessObjects/ | Int PK base class |
| SearchConfiguration | BusinessObjects/ | Persisted search panel definition |
| SearchField | BusinessObjects/ | Property selection with display/order/match settings |
| SearchDtoCompiler | Services/ | Roslyn: metadata to DTO type + source |
| SearchDtoRegistry | Services/ | Singleton cache, startup bootstrap, type registration |
| SearchControllerBase\<T,D\> | Controllers/ | Reusable popup + criteria building logic |
| SearchConfigurationController | Controllers/ | Populate, compile, activate, export |
| SearchPanelController | Controllers/ | Generic "Advanced Search" on any ListView |

## What's NOT Needed (vs XafDynamicAssemblies)

- No SchemaSynchronizer — no database tables for DTOs
- No DynamicModelCacheKeyFactory — no EF Core model changes
- No restart mechanism — NonPersistentBaseObject registers cleanly
- No AssemblyGenerationManager lifecycle — old types leak harmlessly

## Supported Property Types

- string (with wildcard/exact match options)
- int, long, decimal, double, float
- bool
- DateTime
- Enums
- Reference/navigation properties (lookup dropdowns)
