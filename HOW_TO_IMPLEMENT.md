# How to Integrate XafSearch Runtime Search Panel into an Existing XAF EF Core Application

This guide walks through adding the XafSearch runtime search panel feature to an existing DevExpress XAF application using EF Core.

The search panel system allows you to define searchable fields for any entity at runtime through a configuration UI, compile them into dynamic search DTOs using Roslyn, and present users with an "Advanced Search" popup on any ListView. Once satisfied with a configuration, you can export it as a standalone `.cs` file and remove the runtime configuration entirely.

---

## Prerequisites

- DevExpress XAF 25.2.3 or later with EF Core
- .NET 8 or later
- An existing XAF solution with a Module project (platform-agnostic) and at least one UI host project (Blazor Server or WinForms)

---

## Step 1: Add Roslyn NuGet Packages

Add the following packages to your **Module** project (`.csproj`):

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.10.0" />
```

This is required by `SearchDtoCompiler` to compile search DTO classes at runtime.

---

## Step 2: Copy Required Files

Copy the following files from `XafSearch.Module` into your own Module project, preserving the directory structure. After copying, update all `namespace` declarations to match your project's namespace (e.g., replace `XafSearch.Module` with `YourApp.Module`).

### Business Objects

| Source File | Target Location |
|---|---|
| `BusinessObjects/BaseObjectInt.cs` | `YourModule/BusinessObjects/` |
| `BusinessObjects/SearchConfiguration.cs` | `YourModule/BusinessObjects/` |
| `BusinessObjects/SearchField.cs` | `YourModule/BusinessObjects/` |
| `BusinessObjects/SourceExportView.cs` | `YourModule/BusinessObjects/` |

**Note on BaseObjectInt:** `SearchConfiguration` and `SearchField` inherit from `BaseObjectInt`, which provides an integer primary key and implements `IXafEntityObject` and `IObjectSpaceLink`. If you already have a base class with an integer key, you can change the inheritance of `SearchConfiguration` and `SearchField` to use your own base class instead, and skip copying `BaseObjectInt.cs`.

### Services

| Source File | Target Location |
|---|---|
| `Services/SearchDtoCompiler.cs` | `YourModule/Services/` |
| `Services/SearchDtoRegistry.cs` | `YourModule/Services/` |
| `Services/CriteriaBuilder.cs` | `YourModule/Services/` |
| `Services/PropertyEligibility.cs` | `YourModule/Services/` |

### Attributes

| Source File | Target Location |
|---|---|
| `Attributes/UseExactMatchAttribute.cs` | `YourModule/Attributes/` |

### Controllers

| Source File | Target Location |
|---|---|
| `Controllers/SearchControllerBase.cs` | `YourModule/Controllers/` |
| `Controllers/SearchPanelController.cs` | `YourModule/Controllers/` |
| `Controllers/SearchConfigurationController.cs` | `YourModule/Controllers/` |
| `Controllers/GenerateSearchPanelController.cs` | `YourModule/Controllers/` |

### Namespace Updates

After copying, perform a find-and-replace across all copied files:

- Replace `XafSearch.Module.BusinessObjects` with `YourApp.Module.BusinessObjects`
- Replace `XafSearch.Module.Services` with `YourApp.Module.Services`
- Replace `XafSearch.Module.Controllers` with `YourApp.Module.Controllers`
- Replace `XafSearch.Module.Attributes` with `YourApp.Module.Attributes`
- Replace `XafSearch.Module` with `YourApp.Module` (for the module class reference)

**Important:** In `SearchDtoCompiler.cs`, the constant `RuntimeNamespace` is set to `"XafSearch.RuntimeSearch"`. You may want to change this to match your own project name (e.g., `"YourApp.RuntimeSearch"`). This namespace is used for the dynamically compiled DTO types.

In `SearchConfigurationController.cs`, update the module type reference on line 113-114:

```csharp
// Change this:
var module = Application.Modules
    .OfType<XafSearch.Module.XafSearchModule>()
    .FirstOrDefault();

// To this:
var module = Application.Modules
    .OfType<YourApp.Module.YourAppModule>()
    .FirstOrDefault();
```

Also update the error message that references "XafSearchModule" to match your module class name.

---

## Step 3: Register DbSets

Open your EF Core `DbContext` class and add the following `DbSet` properties:

```csharp
public DbSet<SearchConfiguration> SearchConfigurations { get; set; }
public DbSet<SearchField> SearchFields { get; set; }
```

These two tables store the runtime search panel configurations and their field definitions.

---

## Step 4: Bootstrap the Registry

In your Module class (the one that inherits from `ModuleBase`), add the bootstrap logic in the `Setup` method. This ensures all active search configurations are compiled and registered when the application starts.

```csharp
public override void Setup(XafApplication application)
{
    base.Setup(application);
    application.SetupComplete += Application_SetupComplete;
}

private void Application_SetupComplete(object sender, EventArgs e)
{
    var application = (XafApplication)sender;
    application.SetupComplete -= Application_SetupComplete;

    try
    {
        using var objectSpace = application.CreateObjectSpace(typeof(SearchConfiguration));
        SearchDtoRegistry.Instance.Bootstrap(objectSpace, this);
    }
    catch (Exception ex)
    {
        Tracing.Tracer.LogError($"SearchDtoRegistry bootstrap failed: {ex.Message}");
    }
}
```

Required usings:

```csharp
using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;
using YourApp.Module.BusinessObjects;
using YourApp.Module.Services;
```

The bootstrap process loads all `SearchConfiguration` records where `IsActive == true`, compiles each one into a DTO type using Roslyn, and registers those types with `XafTypesInfo` so that XAF recognizes them as non-persistent domain components.

---

## Step 5: Add NonPersistent ObjectSpaceProvider

The search DTOs and `SourceExportView` are non-persistent domain components. XAF requires a `NonPersistentObjectSpaceProvider` to create object spaces for these types.

In your UI host project's `Startup.cs` (Blazor Server) or equivalent configuration, ensure `.AddNonPersistent()` is called on the `ObjectSpaceProviders` builder:

```csharp
builder.ObjectSpaceProviders
    .AddEFCore(options =>
    {
        options.PreFetchReferenceProperties();
    })
    .WithDbContext<YourDbContext>((serviceProvider, options) =>
    {
        // your connection string configuration
    })
    .AddNonPersistent();  // <-- This line is required
```

If you already have `.AddNonPersistent()` in your configuration, no changes are needed here.

For WinForms (`Startup.cs` in the Win project), the same `.AddNonPersistent()` call is needed in the corresponding `ObjectSpaceProviders` configuration.

---

## Step 6: Update Database

The new `SearchConfigurations` and `SearchFields` tables need to be created in your database. Run one of the following:

```bash
# Option A: Run the app and let XAF auto-create the tables
dotnet run --project YourApp.Blazor.Server/YourApp.Blazor.Server.csproj

# Option B: Use the CLI update command
dotnet run --project YourApp.Blazor.Server/YourApp.Blazor.Server.csproj -- --updateDatabase --forceUpdate --silent
```

After the database is updated, the application is ready to use.

---

## Step 7: Usage Walkthrough

### 7.1 Create a Search Configuration

1. Launch the application and navigate to the **Search Configuration** section in the navigation menu.
2. Click **New** to create a new search configuration.
3. Set the **Name** to something descriptive (e.g., "Customer Search").
4. Set the **Target Entity Type** to the fully qualified type name of the entity you want to search. For example: `YourApp.Module.BusinessObjects.Customer`. This must include the full namespace.
5. Click the **Populate Properties** action button in the toolbar. This reads all eligible properties from the target entity and creates `SearchField` entries for each one.

### 7.2 Customize Search Fields

After populating, review the generated fields in the **Fields** list:

- **Remove** fields you do not want to expose in the search form. Delete the row.
- **Reorder** fields by changing the **Sort Order** value. Lower numbers appear first.
- **Rename** fields by editing the **Display Name** column.
- **UseRangeFilter:** Enable this for `DateTime`, `int`, `decimal`, `double`, `float`, or `long` fields where you want From/To range inputs instead of a single equality filter. When enabled, the search form will show two fields (e.g., "Order Date (From)" and "Order Date (To)") and filter records that fall within the specified range.
- **UseExactMatch:** Enable this for `string` fields where you want exact equality matching instead of the default "contains" behavior. When disabled (default), string searches use a case-insensitive contains filter, and users can use wildcards (`*` for any characters, `?` for a single character).
- **IsReference / ReferencedType:** These are auto-detected for navigation properties pointing to other persistent entities. Reference fields render as lookup editors in the search form.

### 7.3 Compile and Activate

1. Save the configuration.
2. Click the **Compile & Activate** action button.
3. If compilation succeeds, you will see a confirmation message with the generated DTO type name.
4. If compilation fails, the error messages will indicate what went wrong (see Troubleshooting below).

### 7.4 Use the Search Panel

1. Navigate to the target entity's **ListView** (e.g., the Customer list).
2. An **Advanced Search** action button now appears in the toolbar.
3. Click it to open a popup form with all the configured search fields.
4. Fill in any combination of fields and click **OK** to apply the filter.
5. A confirmation message shows how many filters were applied.
6. To clear the search, open Advanced Search again with all fields empty and click OK.

### 7.5 Customize the Search Form Layout (Optional)

The search DTO's DetailView can be customized through the XAF Model Editor:

1. Open the Model Editor in your UI project.
2. Navigate to **Views** and locate the DetailView for the generated DTO (e.g., `CustomerSearchDTO_DetailView`).
3. Rearrange fields, set column spans, group fields, or adjust editor settings as desired.

Note: The DTO type name follows the pattern `{EntityShortName}SearchDTO` under the runtime namespace. Model Editor customizations persist across recompilations as long as the DTO type name stays the same.

### 7.6 Generate Search Panel from a ListView

As an alternative to manually creating a configuration, you can generate one directly from any entity's ListView:

1. Navigate to any entity's ListView.
2. If no search panel exists for that entity, a **Generate Search Panel** action button appears in the Tools category.
3. Clicking it creates a pre-populated `SearchConfiguration` in a modal window with all eligible properties already filled in.
4. Review, customize, save, and compile as described above.

---

## Step 8: Graduating to Compiled Code (Export)

Once you are satisfied with a search configuration, you can export it as a standalone C# source file. This removes the dependency on the runtime compilation system for that particular search panel.

### 8.1 Export the Source

1. Open the `SearchConfiguration` detail view for the configuration you want to export.
2. Click the **Export C# Source** action button.
3. A modal window appears showing the generated source code and a suggested file name.
4. Copy the source code and save it as a `.cs` file in your Module project (e.g., `Controllers/CustomerSearch.cs`).

### 8.2 What the Exported File Contains

The exported file includes two things:

1. **A DTO class** (e.g., `CustomerSearchDTO`) inheriting from `NonPersistentBaseObject` with the `[DomainComponent]` attribute. This class has all the search fields as properties with the appropriate types, display names, and attributes.

2. **A controller class** (e.g., `CustomerSearchController`) inheriting from `SearchControllerBase<Customer, CustomerSearchDTO>`. This controller is empty because all the logic is in the base class. It simply connects the entity type to the DTO type.

### 8.3 Finalize the Migration

After adding the exported `.cs` file to your project:

1. **Update namespaces** in the exported file to match your project conventions. The exported code uses the `RuntimeNamespace` value from `SearchDtoCompiler` (default: `XafSearch.RuntimeSearch`). Change this to whatever namespace you prefer.

2. **Delete or deactivate the runtime SearchConfiguration** for that entity. Either:
   - Delete the `SearchConfiguration` record from the database, or
   - Set `IsActive` to `false` on the configuration.

   This prevents both the runtime and compiled controllers from being active simultaneously, which would result in duplicate "Advanced Search" buttons.

3. **Rebuild and run** the application. The compiled controller will be picked up automatically by XAF's controller discovery.

### 8.4 Further Customization

Once exported, the DTO and controller are regular C# code that you can modify freely:

- Add validation attributes to DTO properties.
- Override `SearchAction_Execute` in the controller to add custom filter logic.
- Add additional computed properties to the DTO.
- Change the `MaxActiveFilters` property in the controller (default: 20).
- Modify the popup behavior by overriding `SearchAction_CustomizePopupWindowParams`.

Note: `SearchControllerBase<TEntity, TSearchDTO>` and `CriteriaBuilder` are still required dependencies for the exported controller to function. Do not remove these files from your project.

---

## Troubleshooting

### "Type not found" when clicking Populate Properties

The **Target Entity Type** must be the fully qualified type name including the namespace. For example:

- Correct: `YourApp.Module.BusinessObjects.Customer`
- Wrong: `Customer`

The type must be registered in `XafTypesInfo`. If you just added the entity class, rebuild and restart the application first.

### Compilation errors after clicking Compile & Activate

Common causes:

- **Missing type references:** The Roslyn compiler loads all assemblies currently in the AppDomain. If a referenced type (e.g., a lookup entity type used as a reference property) is not loaded yet, compilation will fail. Ensure all referenced entity types are used somewhere in the application so their assemblies are loaded.
- **Invalid property type names:** If a `SearchField` has a `PropertyTypeName` that does not resolve to a valid CLR type, compilation fails. Re-run Populate Properties to refresh the field metadata.
- **Namespace conflicts:** If your entity lives in a namespace that conflicts with a generated `using` directive, you may need to adjust the `RuntimeNamespace` constant in `SearchDtoCompiler.cs`.

### Search panel does not appear on the ListView

1. **Check IsActive:** The `SearchConfiguration` record must have `IsActive` set to `true`.
2. **Check compilation:** The configuration must be compiled successfully. Open the configuration and click Compile & Activate. Look for success or error messages.
3. **Restart on first run:** The bootstrap process runs during `Application.SetupComplete`. If you created a new configuration and compiled it within the same session, it should be immediately available. However, if the application was restarted before the configuration was compiled, you need to compile it again after startup.
4. **Verify NonPersistent provider:** If `.AddNonPersistent()` is missing from your ObjectSpaceProviders, the application cannot create object spaces for the DTO types. This typically causes an exception rather than a silent failure.

### Duplicate "Advanced Search" buttons

This happens when both a runtime `SearchConfiguration` and a compiled `SearchControllerBase<T,D>` subclass exist for the same entity. The runtime `SearchPanelController` and your compiled controller both activate on the same ListView.

To fix: either deactivate/delete the runtime `SearchConfiguration`, or remove the compiled controller class. Only one should be active for any given entity type.

### Search returns no results when expected

- **String fields (default):** By default, string searches use "contains" matching. The search term `son` will match `Johnson`, `Sonata`, etc. If `UseExactMatch` is enabled, only exact equality matches are returned.
- **Wildcard support:** When `UseExactMatch` is off, users can use `*` (any characters) and `?` (single character) wildcards. For example, `J*son` matches `Johnson` and `Jackson`.
- **Date fields:** Single date fields match the entire day (midnight to midnight). Range filters with From/To are inclusive on the From date and exclusive on the To date (up to but not including the next day).
- **Nullable types:** Numeric and date DTO properties are generated as nullable. An unfilled field (null value) is ignored in the filter, not treated as "filter for null."

### CriteriaBuilder does not handle a custom property type

`CriteriaBuilder` handles strings, dates, booleans, numeric types, and XAF business objects (detected by the presence of an `Oid` or `ID` property). For unsupported types, it falls back to an equality comparison. If you need custom logic for a specific type, modify the `CreateCriterion` method in `CriteriaBuilder.cs`.
