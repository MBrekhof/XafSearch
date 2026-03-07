# Runtime Search Panels Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a runtime search panel system where admins configure search screens for any XAF entity, compiled via Roslyn at runtime as NonPersistentBaseObject DTOs, with no restart required.

**Architecture:** Persistent metadata (SearchConfiguration + SearchField) drives Roslyn compilation of search DTO types at runtime. A singleton registry bootstraps on startup and re-registers on config changes. A generic ViewController adds "Advanced Search" to any ListView with a registered config. Criteria building logic is ported from WLNCentral's SearchControllerBase.

**Tech Stack:** .NET 8, DevExpress XAF 25.2.3, EF Core 8 (SQL Server), Roslyn (Microsoft.CodeAnalysis.CSharp 4.10.0 — already in project)

---

### Task 1: BaseObjectInt Base Class

**Files:**
- Create: `XafSearch/XafSearch.Module/BusinessObjects/BaseObjectInt.cs`

**Step 1: Create BaseObjectInt**

Port from WLNCentral, change namespace to `XafSearch.Module.BusinessObjects`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;
using System.ComponentModel.DataAnnotations;

namespace XafSearch.Module.BusinessObjects;

public abstract class BaseObjectInt : IXafEntityObject, IObjectSpaceLink
{
    protected IObjectSpace ObjectSpace;

    [Key]
    [VisibleInListView(false)]
    [VisibleInDetailView(false)]
    [VisibleInLookupListView(false)]
    public virtual int ID { get; set; }

    IObjectSpace IObjectSpaceLink.ObjectSpace
    {
        get => ObjectSpace;
        set => ObjectSpace = value;
    }

    public virtual void OnCreated() { }
    public virtual void OnSaving() { }
    public virtual void OnLoaded() { }
}
```

**Step 2: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add XafSearch/XafSearch.Module/BusinessObjects/BaseObjectInt.cs
git commit -m "feat: add BaseObjectInt base class with int PK"
```

---

### Task 2: SearchConfiguration and SearchField Entities

**Files:**
- Create: `XafSearch/XafSearch.Module/BusinessObjects/SearchConfiguration.cs`
- Create: `XafSearch/XafSearch.Module/BusinessObjects/SearchField.cs`
- Modify: `XafSearch/XafSearch.Module/BusinessObjects/XafSearchDbContext.cs` (add DbSets)

**Step 1: Create SearchConfiguration entity**

```csharp
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace XafSearch.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("Search Configuration")]
[DefaultProperty(nameof(Name))]
[XafDisplayName("Search Configuration")]
public class SearchConfiguration : BaseObjectInt
{
    public virtual string Name { get; set; }

    [XafDisplayName("Target Entity Type")]
    public virtual string TargetEntityType { get; set; }

    [XafDisplayName("Active")]
    public virtual bool IsActive { get; set; } = true;

    [DevExpress.ExpressApp.DC.Aggregated]
    public virtual IList<SearchField> Fields { get; set; } = new ObservableCollection<SearchField>();

    public override string ToString() => Name ?? "New Search Configuration";
}
```

**Step 2: Create SearchField entity**

```csharp
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using System.ComponentModel;

namespace XafSearch.Module.BusinessObjects;

[DefaultProperty(nameof(PropertyName))]
[XafDisplayName("Search Field")]
public class SearchField : BaseObjectInt
{
    public virtual int? SearchConfigurationId { get; set; }
    public virtual SearchConfiguration SearchConfiguration { get; set; }

    [XafDisplayName("Property Name")]
    public virtual string PropertyName { get; set; }

    [XafDisplayName("Property Type")]
    public virtual string PropertyTypeName { get; set; }

    [XafDisplayName("Display Name")]
    public virtual string DisplayName { get; set; }

    [XafDisplayName("Exact Match")]
    public virtual bool UseExactMatch { get; set; }

    [XafDisplayName("Sort Order")]
    public virtual int SortOrder { get; set; }

    [XafDisplayName("Is Reference")]
    public virtual bool IsReferenceProperty { get; set; }

    [XafDisplayName("Referenced Type")]
    public virtual string ReferencedTypeName { get; set; }

    public override string ToString() => DisplayName ?? PropertyName ?? "New Field";
}
```

**Step 3: Register DbSets on XafSearchEFCoreDbContext**

Add to `XafSearchDbContext.cs` after existing DbSet declarations:

```csharp
public DbSet<SearchConfiguration> SearchConfigurations { get; set; }
public DbSet<SearchField> SearchFields { get; set; }
```

**Step 4: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add XafSearch/XafSearch.Module/BusinessObjects/SearchConfiguration.cs XafSearch/XafSearch.Module/BusinessObjects/SearchField.cs XafSearch/XafSearch.Module/BusinessObjects/XafSearchDbContext.cs
git commit -m "feat: add SearchConfiguration and SearchField entities"
```

---

### Task 3: SearchDtoCompiler — Roslyn Source Generation & Compilation

**Files:**
- Create: `XafSearch/XafSearch.Module/Services/SearchDtoCompiler.cs`

**Step 1: Create the compiler service**

This service takes a `SearchConfiguration` and produces a compiled `Type` plus the C# source string.

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using XafSearch.Module.BusinessObjects;

namespace XafSearch.Module.Services;

public class SearchDtoCompiler
{
    private const string RuntimeNamespace = "XafSearch.RuntimeSearch";

    public CompilationResult Compile(SearchConfiguration config)
    {
        var result = new CompilationResult { ConfigurationId = config.ID };

        var source = GenerateSource(config);
        result.Source = source;

        var syntaxTree = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12));

        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: $"SearchDTO_{config.ID}_{Guid.NewGuid():N}",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            result.Errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToList();
            return result;
        }

        ms.Seek(0, SeekOrigin.Begin);
        var alc = new AssemblyLoadContext($"SearchDTO_{config.ID}", isCollectible: false);
        var assembly = alc.LoadFromStream(ms);

        var targetShortName = config.TargetEntityType.Split('.').Last();
        var dtoTypeName = $"{RuntimeNamespace}.{targetShortName}SearchDTO";
        result.DtoType = assembly.GetType(dtoTypeName);

        if (result.DtoType == null)
        {
            result.Errors = new List<string> { $"Compiled assembly does not contain type '{dtoTypeName}'" };
        }

        return result;
    }

    public string GenerateSource(SearchConfiguration config)
    {
        var targetShortName = config.TargetEntityType.Split('.').Last();
        var dtoName = $"{targetShortName}SearchDTO";
        var fields = config.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.PropertyName))
            .OrderBy(f => f.SortOrder)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using DevExpress.ExpressApp.DC;");
        sb.AppendLine("using DevExpress.ExpressApp.Model;");
        sb.AppendLine("using DevExpress.Persistent.Base;");
        sb.AppendLine("using DevExpress.ExpressApp;");
        sb.AppendLine();
        sb.AppendLine($"namespace {RuntimeNamespace}");
        sb.AppendLine("{");
        sb.AppendLine($"    [DomainComponent]");
        sb.AppendLine($"    [XafDisplayName(\"Search {EscapeString(config.Name)}\")]");
        sb.AppendLine($"    public class {dtoName} : NonPersistentBaseObject");
        sb.AppendLine("    {");

        foreach (var field in fields)
        {
            var displayName = field.DisplayName ?? field.PropertyName;
            sb.AppendLine($"        [XafDisplayName(\"{EscapeString(displayName)}\")]");

            if (field.IsReferenceProperty && !string.IsNullOrWhiteSpace(field.ReferencedTypeName))
            {
                // Reference properties: use the referenced type for lookup editors
                sb.AppendLine($"        public {field.ReferencedTypeName} {field.PropertyName} {{ get; set; }}");
            }
            else
            {
                var clrType = GetNullableTypeName(field.PropertyTypeName);
                if (field.PropertyTypeName == "System.String" && !field.UseExactMatch)
                {
                    sb.AppendLine($"        [ToolTip(\"Supports wildcards: * (any chars), ? (single char)\")]");
                }
                sb.AppendLine($"        public {clrType} {field.PropertyName} {{ get; set; }}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"        public override string ToString() => \"Search {EscapeString(config.Name)}\";");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a complete .cs file with DTO + standalone controller for export.
    /// </summary>
    public string GenerateExportSource(SearchConfiguration config)
    {
        var targetShortName = config.TargetEntityType.Split('.').Last();
        var dtoName = $"{targetShortName}SearchDTO";
        var controllerName = $"{targetShortName}SearchController";

        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated search panel - exported from XafSearch runtime configuration");
        sb.AppendLine($"// Configuration: {config.Name}");
        sb.AppendLine($"// Target Entity: {config.TargetEntityType}");
        sb.AppendLine($"// Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Append the DTO source
        sb.Append(GenerateSource(config));
        sb.AppendLine();

        // Append a standalone controller
        sb.AppendLine($"namespace {RuntimeNamespace}");
        sb.AppendLine("{");
        sb.AppendLine($"    public class {controllerName} : XafSearch.Module.Controllers.SearchControllerBase<{config.TargetEntityType}, {dtoName}>");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetNullableTypeName(string typeName) => typeName switch
    {
        "System.String" => "string",
        "System.Int32" => "int?",
        "System.Int64" => "long?",
        "System.Decimal" => "decimal?",
        "System.Double" => "double?",
        "System.Single" => "float?",
        "System.Boolean" => "bool?",
        "System.DateTime" => "DateTime?",
        "System.Guid" => "Guid?",
        _ => typeName + "?"
    };

    private static string EscapeString(string value)
        => value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;

    private static List<MetadataReference> GetMetadataReferences()
    {
        var references = new List<MetadataReference>();

        // Trusted platform assemblies
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedAssemblies != null)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch { /* skip unreadable assemblies */ }
            }
        }

        // Also add currently loaded assemblies (DevExpress, XAF, etc.)
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
            try
            {
                if (!references.Any(r => r.Display == asm.Location))
                    references.Add(MetadataReference.CreateFromFile(asm.Location));
            }
            catch { }
        }

        return references;
    }
}

public class CompilationResult
{
    public int ConfigurationId { get; set; }
    public Type DtoType { get; set; }
    public string Source { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0 && DtoType != null;
}
```

**Step 2: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add XafSearch/XafSearch.Module/Services/SearchDtoCompiler.cs
git commit -m "feat: add SearchDtoCompiler with Roslyn compilation and export"
```

---

### Task 4: SearchDtoRegistry — Singleton Cache & Type Registration

**Files:**
- Create: `XafSearch/XafSearch.Module/Services/SearchDtoRegistry.cs`

**Step 1: Create the registry**

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using XafSearch.Module.BusinessObjects;

namespace XafSearch.Module.Services;

public class SearchDtoRegistry
{
    private static readonly Lazy<SearchDtoRegistry> _instance = new(() => new SearchDtoRegistry());
    public static SearchDtoRegistry Instance => _instance.Value;

    private readonly Dictionary<int, RegistryEntry> _entries = new();
    private readonly SearchDtoCompiler _compiler = new();
    private readonly object _lock = new();

    /// <summary>
    /// Maps target entity full type name to the config ID for quick lookup.
    /// </summary>
    private readonly Dictionary<string, int> _entityTypeIndex = new(StringComparer.OrdinalIgnoreCase);

    private SearchDtoRegistry() { }

    /// <summary>
    /// Bootstrap: compile and register all active configurations on startup.
    /// Called from XafSearchModule.Setup().
    /// </summary>
    public void Bootstrap(IObjectSpace objectSpace, ModuleBase module)
    {
        var configs = objectSpace.GetObjectsQuery<SearchConfiguration>()
            .Where(c => c.IsActive)
            .ToList();

        foreach (var config in configs)
        {
            CompileAndRegister(config, module);
        }

        Tracing.Tracer.LogText($"SearchDtoRegistry bootstrapped: {_entries.Count} search panel(s) registered.");
    }

    /// <summary>
    /// Compile a configuration and register the resulting DTO type with XAF.
    /// </summary>
    public CompilationResult CompileAndRegister(SearchConfiguration config, ModuleBase module)
    {
        var result = _compiler.Compile(config);
        if (!result.Success)
        {
            Tracing.Tracer.LogError($"Search DTO compilation failed for '{config.Name}': {string.Join("; ", result.Errors)}");
            return result;
        }

        lock (_lock)
        {
            // Remove old entry for this config if exists
            if (_entries.TryGetValue(config.ID, out var oldEntry))
            {
                module.AdditionalExportedTypes.Remove(oldEntry.DtoType);
                _entityTypeIndex.Remove(oldEntry.TargetEntityType);
            }

            var entry = new RegistryEntry
            {
                ConfigurationId = config.ID,
                DtoType = result.DtoType,
                Source = result.Source,
                TargetEntityType = config.TargetEntityType
            };

            _entries[config.ID] = entry;
            _entityTypeIndex[config.TargetEntityType] = config.ID;

            // Register with XAF type system
            XafTypesInfo.Instance.RegisterEntity(result.DtoType);
            module.AdditionalExportedTypes.Add(result.DtoType);
        }

        Tracing.Tracer.LogText($"Search DTO registered: {result.DtoType.FullName} for {config.TargetEntityType}");
        return result;
    }

    /// <summary>
    /// Remove a configuration from the registry.
    /// Note: the CLR type remains in memory (non-collectible ALC).
    /// </summary>
    public void Unregister(int configId, ModuleBase module)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(configId, out var entry))
            {
                module.AdditionalExportedTypes.Remove(entry.DtoType);
                _entityTypeIndex.Remove(entry.TargetEntityType);
                _entries.Remove(configId);
            }
        }
    }

    /// <summary>
    /// Get the DTO type for a given target entity type name.
    /// </summary>
    public Type GetDtoType(string targetEntityTypeName)
    {
        lock (_lock)
        {
            if (_entityTypeIndex.TryGetValue(targetEntityTypeName, out var configId)
                && _entries.TryGetValue(configId, out var entry))
            {
                return entry.DtoType;
            }
            return null;
        }
    }

    /// <summary>
    /// Get the generated C# source for a configuration.
    /// </summary>
    public string GetSource(int configId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(configId, out var entry) ? entry.Source : null;
        }
    }

    /// <summary>
    /// Get the export source (DTO + controller) for a configuration.
    /// </summary>
    public string GetExportSource(SearchConfiguration config)
    {
        return _compiler.GenerateExportSource(config);
    }

    /// <summary>
    /// Check if a search configuration exists for the given entity type.
    /// </summary>
    public bool HasSearchPanel(string targetEntityTypeName)
    {
        lock (_lock)
        {
            return _entityTypeIndex.ContainsKey(targetEntityTypeName);
        }
    }

    /// <summary>
    /// Get all registered entries (for diagnostics).
    /// </summary>
    public IReadOnlyList<RegistryEntry> GetAll()
    {
        lock (_lock)
        {
            return _entries.Values.ToList();
        }
    }
}

public class RegistryEntry
{
    public int ConfigurationId { get; set; }
    public Type DtoType { get; set; }
    public string Source { get; set; }
    public string TargetEntityType { get; set; }
}
```

**Step 2: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add XafSearch/XafSearch.Module/Services/SearchDtoRegistry.cs
git commit -m "feat: add SearchDtoRegistry singleton with bootstrap and type registration"
```

---

### Task 5: SearchControllerBase — Reusable Criteria Building

**Files:**
- Create: `XafSearch/XafSearch.Module/Controllers/SearchControllerBase.cs`

**Step 1: Create the base controller**

Port from WLNCentral with adaptations for reference properties and removal of `[Searchable]` attribute dependency:

```csharp
using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using System.Reflection;

namespace XafSearch.Module.Controllers;

/// <summary>
/// Base controller for search functionality.
/// TEntity: The persistent business object being searched.
/// TSearchDTO: The non-persistent search DTO.
/// </summary>
public abstract class SearchControllerBase<TEntity, TSearchDTO> : ObjectViewController<ListView, TEntity>
    where TEntity : class
    where TSearchDTO : class
{
    private PopupWindowShowAction searchAction;
    private const string CriteriaKey = "RuntimeAdvancedSearch";

    protected virtual int MaxActiveFilters { get; set; } = 20;

    public SearchControllerBase()
    {
        searchAction = new PopupWindowShowAction(
            this,
            $"Search_{typeof(TEntity).Name}",
            PredefinedCategory.View)
        {
            Caption = "Advanced Search",
            ImageName = "Action_Search",
            ToolTip = $"Open advanced search for {typeof(TEntity).Name}",
            SelectionDependencyType = SelectionDependencyType.Independent
        };

        searchAction.CustomizePopupWindowParams += SearchAction_CustomizePopupWindowParams;
        searchAction.Execute += SearchAction_Execute;
    }

    private void SearchAction_CustomizePopupWindowParams(object sender, CustomizePopupWindowParamsEventArgs e)
    {
        var os = Application.CreateObjectSpace(typeof(TSearchDTO));
        var searchObj = os.CreateObject<TSearchDTO>();
        var detailView = Application.CreateDetailView(os, searchObj);
        detailView.ViewEditMode = ViewEditMode.Edit;
        e.View = detailView;
        e.Maximized = false;
    }

    private void SearchAction_Execute(object sender, PopupWindowShowActionExecuteEventArgs e)
    {
        var searchObj = e.PopupWindowViewCurrentObject as TSearchDTO;
        if (searchObj == null) return;

        var criteria = BuildCriteria(searchObj);

        if (criteria is not null)
        {
            View.CollectionSource.Criteria[CriteriaKey] = criteria;
            Application.ShowViewStrategy.ShowMessage(
                $"Search applied with {GetActiveFilterCount(searchObj)} filter(s).",
                InformationType.Success, 3000, InformationPosition.Top);
        }
        else
        {
            View.CollectionSource.Criteria.Remove(CriteriaKey);
        }
    }

    protected virtual CriteriaOperator BuildCriteria(TSearchDTO searchObj)
    {
        if (searchObj == null) return null;

        var groupOp = new GroupOperator(GroupOperatorType.And);
        int filterCount = 0;

        var properties = typeof(TSearchDTO).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.Name != "Oid");

        foreach (var prop in properties)
        {
            if (filterCount >= MaxActiveFilters) break;

            var val = prop.GetValue(searchObj);
            if (IsNullOrEmpty(val)) continue;

            var criterion = CreateCriterion(prop, val);
            if (criterion is not null)
            {
                groupOp.Operands.Add(criterion);
                filterCount++;
            }
        }

        return groupOp.Operands.Count > 0 ? groupOp : null;
    }

    protected virtual CriteriaOperator CreateCriterion(PropertyInfo property, object value)
    {
        var propName = property.Name;

        if (value is string text)
            return CreateStringCriterion(propName, text, useExactMatch: false);

        if (value is DateTime dateTime)
            return CreateDateCriterion(propName, dateTime);

        // Reference objects: match by key (Oid/ID property)
        if (IsXafBusinessObject(value))
            return CreateReferenceCriterion(propName, value);

        // Exact match for enums, numbers, booleans
        return new BinaryOperator(propName, value, BinaryOperatorType.Equal);
    }

    protected virtual CriteriaOperator CreateStringCriterion(string propertyName, string value, bool useExactMatch)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (value.Contains('*') || value.Contains('?'))
        {
            var sqlPattern = value.Replace("*", "%").Replace("?", "_");
            return CriteriaOperator.Parse($"[{propertyName}] LIKE ?", sqlPattern);
        }

        if (useExactMatch)
            return new BinaryOperator(propertyName, value, BinaryOperatorType.Equal);

        return new FunctionOperator(
            FunctionOperatorType.Contains,
            new OperandProperty(propertyName),
            new OperandValue(value));
    }

    protected virtual CriteriaOperator CreateDateCriterion(string propertyName, DateTime value)
    {
        var startOfDay = value.Date;
        var endOfDay = startOfDay.AddDays(1);

        return new GroupOperator(
            GroupOperatorType.And,
            new BinaryOperator(propertyName, startOfDay, BinaryOperatorType.GreaterOrEqual),
            new BinaryOperator(propertyName, endOfDay, BinaryOperatorType.Less));
    }

    protected virtual CriteriaOperator CreateReferenceCriterion(string propertyName, object referenceObject)
    {
        // Navigate to the reference's key for the criteria
        // XAF handles "PropertyName.Oid" or "PropertyName.ID" style criteria
        var keyProp = referenceObject.GetType().GetProperty("Oid")
                      ?? referenceObject.GetType().GetProperty("ID");

        if (keyProp != null)
        {
            var keyValue = keyProp.GetValue(referenceObject);
            return new BinaryOperator($"{propertyName}.{keyProp.Name}", keyValue, BinaryOperatorType.Equal);
        }

        return null;
    }

    private static bool IsXafBusinessObject(object value)
    {
        if (value == null) return false;
        var type = value.GetType();
        return type.IsClass && !type.IsPrimitive && type != typeof(string)
            && (type.GetProperty("Oid") != null || type.GetProperty("ID") != null);
    }

    protected virtual bool IsNullOrEmpty(object value)
    {
        if (value == null) return true;
        if (value is string s) return string.IsNullOrWhiteSpace(s);

        var type = value.GetType();
        if (type.IsValueType)
        {
            var defaultValue = Activator.CreateInstance(type);
            return value.Equals(defaultValue);
        }

        return false;
    }

    protected virtual int GetActiveFilterCount(TSearchDTO searchObj)
    {
        if (searchObj == null) return 0;
        return typeof(TSearchDTO).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.CanRead && !IsNullOrEmpty(p.GetValue(searchObj)));
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add XafSearch/XafSearch.Module/Controllers/SearchControllerBase.cs
git commit -m "feat: add SearchControllerBase with criteria building and reference support"
```

---

### Task 6: SearchPanelController — Generic ListView "Advanced Search" Action

**Files:**
- Create: `XafSearch/XafSearch.Module/Controllers/SearchPanelController.cs`

**Step 1: Create the generic search panel controller**

This controller activates on any ListView where a search configuration exists. It uses reflection to call `SearchControllerBase` logic dynamically since the DTO type is only known at runtime.

```csharp
using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using System.Reflection;
using XafSearch.Module.Services;

namespace XafSearch.Module.Controllers;

/// <summary>
/// Activates on any ListView that has a registered search configuration.
/// Adds "Advanced Search" action dynamically.
/// </summary>
public class SearchPanelController : ViewController<ListView>
{
    private PopupWindowShowAction searchAction;
    private Type _dtoType;
    private const string CriteriaKey = "RuntimeAdvancedSearch";

    public SearchPanelController()
    {
        searchAction = new PopupWindowShowAction(
            this,
            "RuntimeAdvancedSearch",
            PredefinedCategory.View)
        {
            Caption = "Advanced Search",
            ImageName = "Action_Search",
            ToolTip = "Open advanced search panel",
            SelectionDependencyType = SelectionDependencyType.Independent
        };

        searchAction.CustomizePopupWindowParams += SearchAction_CustomizePopupWindowParams;
        searchAction.Execute += SearchAction_Execute;
    }

    protected override void OnActivated()
    {
        base.OnActivated();

        var entityType = View.ObjectTypeInfo?.Type;
        if (entityType == null)
        {
            Active["HasSearchConfig"] = false;
            return;
        }

        _dtoType = SearchDtoRegistry.Instance.GetDtoType(entityType.FullName);
        Active["HasSearchConfig"] = _dtoType != null;
    }

    protected override void OnDeactivated()
    {
        _dtoType = null;
        base.OnDeactivated();
    }

    private void SearchAction_CustomizePopupWindowParams(object sender, CustomizePopupWindowParamsEventArgs e)
    {
        if (_dtoType == null) return;

        var os = Application.CreateObjectSpace(_dtoType);
        var searchObj = os.CreateObject(_dtoType);
        var detailView = Application.CreateDetailView(os, searchObj);
        detailView.ViewEditMode = ViewEditMode.Edit;
        e.View = detailView;
        e.Maximized = false;
    }

    private void SearchAction_Execute(object sender, PopupWindowShowActionExecuteEventArgs e)
    {
        var searchObj = e.PopupWindowViewCurrentObject;
        if (searchObj == null || _dtoType == null) return;

        var criteria = BuildCriteria(searchObj);

        if (criteria is not null)
        {
            View.CollectionSource.Criteria[CriteriaKey] = criteria;

            var filterCount = GetActiveFilterCount(searchObj);
            Application.ShowViewStrategy.ShowMessage(
                $"Search applied with {filterCount} filter(s).",
                InformationType.Success, 3000, InformationPosition.Top);
        }
        else
        {
            View.CollectionSource.Criteria.Remove(CriteriaKey);
        }
    }

    private CriteriaOperator BuildCriteria(object searchObj)
    {
        var groupOp = new GroupOperator(GroupOperatorType.And);
        int filterCount = 0;

        var properties = searchObj.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.Name != "Oid");

        foreach (var prop in properties)
        {
            if (filterCount >= 20) break;

            var val = prop.GetValue(searchObj);
            if (IsNullOrEmpty(val)) continue;

            var criterion = CreateCriterion(prop, val);
            if (criterion is not null)
            {
                groupOp.Operands.Add(criterion);
                filterCount++;
            }
        }

        return groupOp.Operands.Count > 0 ? groupOp : null;
    }

    private CriteriaOperator CreateCriterion(PropertyInfo property, object value)
    {
        var propName = property.Name;

        if (value is string text)
            return CreateStringCriterion(propName, text);

        if (value is DateTime dateTime)
            return CreateDateCriterion(propName, dateTime);

        if (IsXafBusinessObject(value))
            return CreateReferenceCriterion(propName, value);

        return new BinaryOperator(propName, value, BinaryOperatorType.Equal);
    }

    private static CriteriaOperator CreateStringCriterion(string propertyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (value.Contains('*') || value.Contains('?'))
        {
            var sqlPattern = value.Replace("*", "%").Replace("?", "_");
            return CriteriaOperator.Parse($"[{propertyName}] LIKE ?", sqlPattern);
        }

        return new FunctionOperator(
            FunctionOperatorType.Contains,
            new OperandProperty(propertyName),
            new OperandValue(value));
    }

    private static CriteriaOperator CreateDateCriterion(string propertyName, DateTime value)
    {
        var startOfDay = value.Date;
        var endOfDay = startOfDay.AddDays(1);
        return new GroupOperator(
            GroupOperatorType.And,
            new BinaryOperator(propertyName, startOfDay, BinaryOperatorType.GreaterOrEqual),
            new BinaryOperator(propertyName, endOfDay, BinaryOperatorType.Less));
    }

    private static CriteriaOperator CreateReferenceCriterion(string propertyName, object referenceObject)
    {
        var keyProp = referenceObject.GetType().GetProperty("Oid")
                      ?? referenceObject.GetType().GetProperty("ID");

        if (keyProp != null)
        {
            var keyValue = keyProp.GetValue(referenceObject);
            return new BinaryOperator($"{propertyName}.{keyProp.Name}", keyValue, BinaryOperatorType.Equal);
        }

        return null;
    }

    private static bool IsXafBusinessObject(object value)
    {
        if (value == null) return false;
        var type = value.GetType();
        return type.IsClass && !type.IsPrimitive && type != typeof(string)
            && (type.GetProperty("Oid") != null || type.GetProperty("ID") != null);
    }

    private static bool IsNullOrEmpty(object value)
    {
        if (value == null) return true;
        if (value is string s) return string.IsNullOrWhiteSpace(s);

        var type = value.GetType();
        if (type.IsValueType)
        {
            var defaultValue = Activator.CreateInstance(type);
            return value.Equals(defaultValue);
        }

        return false;
    }

    private int GetActiveFilterCount(object searchObj)
    {
        return searchObj.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.CanRead && p.Name != "Oid" && !IsNullOrEmpty(p.GetValue(searchObj)));
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add XafSearch/XafSearch.Module/Controllers/SearchPanelController.cs
git commit -m "feat: add SearchPanelController - generic Advanced Search on any ListView"
```

---

### Task 7: SearchConfigurationController — Admin Actions

**Files:**
- Create: `XafSearch/XafSearch.Module/Controllers/SearchConfigurationController.cs`

**Step 1: Create the configuration controller**

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using XafSearch.Module.BusinessObjects;
using XafSearch.Module.Services;

namespace XafSearch.Module.Controllers;

public class SearchConfigurationController : ObjectViewController<DetailView, SearchConfiguration>
{
    private SimpleAction populateAction;
    private SimpleAction compileAction;
    private SimpleAction exportAction;

    public SearchConfigurationController()
    {
        populateAction = new SimpleAction(this, "PopulateProperties", PredefinedCategory.Edit)
        {
            Caption = "Populate Properties",
            ImageName = "Action_Reload",
            ToolTip = "Read properties from the target entity and populate the fields list"
        };
        populateAction.Execute += PopulateAction_Execute;

        compileAction = new SimpleAction(this, "CompileAndActivate", PredefinedCategory.Edit)
        {
            Caption = "Compile & Activate",
            ImageName = "Action_Grant",
            ToolTip = "Compile the search DTO and register it with XAF"
        };
        compileAction.Execute += CompileAction_Execute;

        exportAction = new SimpleAction(this, "ExportCSharpSource", PredefinedCategory.Export)
        {
            Caption = "Export C# Source",
            ImageName = "Action_Export",
            ToolTip = "Generate and display the C# source code for this search panel"
        };
        exportAction.Execute += ExportAction_Execute;
    }

    private void PopulateAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var config = ViewCurrentObject;
        if (string.IsNullOrWhiteSpace(config.TargetEntityType))
        {
            Application.ShowViewStrategy.ShowMessage(
                "Please set the Target Entity Type first.",
                InformationType.Warning, 3000, InformationPosition.Top);
            return;
        }

        // Find the type in XAF's type system
        var typeInfo = XafTypesInfo.Instance.FindTypeInfo(config.TargetEntityType);
        if (typeInfo == null)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Type '{config.TargetEntityType}' not found in XAF type system.",
                InformationType.Error, 5000, InformationPosition.Top);
            return;
        }

        // Clear existing fields
        while (config.Fields.Count > 0)
        {
            ObjectSpace.Delete(config.Fields[0]);
        }

        int sortOrder = 0;
        foreach (var member in typeInfo.Members.OrderBy(m => m.Name))
        {
            if (!IsEligibleProperty(member)) continue;

            var field = ObjectSpace.CreateObject<SearchField>();
            field.PropertyName = member.Name;
            field.PropertyTypeName = member.MemberType.FullName;
            field.DisplayName = member.DisplayName ?? member.Name;
            field.SortOrder = sortOrder++;
            field.IsReferenceProperty = member.MemberTypeInfo?.IsPersistent == true;

            if (field.IsReferenceProperty)
            {
                field.ReferencedTypeName = member.MemberType.FullName;
            }

            config.Fields.Add(field);
        }

        ObjectSpace.SetModified(config);

        Application.ShowViewStrategy.ShowMessage(
            $"Populated {config.Fields.Count} properties from {typeInfo.Name}.",
            InformationType.Success, 3000, InformationPosition.Top);
    }

    private void CompileAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var config = ViewCurrentObject;

        if (string.IsNullOrWhiteSpace(config.TargetEntityType) || config.Fields.Count == 0)
        {
            Application.ShowViewStrategy.ShowMessage(
                "Configure target entity and fields before compiling.",
                InformationType.Warning, 3000, InformationPosition.Top);
            return;
        }

        // Save first
        if (ObjectSpace.IsModified)
        {
            ObjectSpace.CommitChanges();
        }

        var module = Application.Modules
            .OfType<XafSearch.Module.XafSearchModule>()
            .FirstOrDefault();

        if (module == null)
        {
            Application.ShowViewStrategy.ShowMessage(
                "XafSearchModule not found.",
                InformationType.Error, 5000, InformationPosition.Top);
            return;
        }

        var result = SearchDtoRegistry.Instance.CompileAndRegister(config, module);

        if (result.Success)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Search panel compiled and activated: {result.DtoType.FullName}",
                InformationType.Success, 3000, InformationPosition.Top);
        }
        else
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Compilation failed: {string.Join("; ", result.Errors.Take(3))}",
                InformationType.Error, 10000, InformationPosition.Top);
        }
    }

    private void ExportAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var config = ViewCurrentObject;
        var source = SearchDtoRegistry.Instance.GetExportSource(config);

        if (string.IsNullOrWhiteSpace(source))
        {
            Application.ShowViewStrategy.ShowMessage(
                "No source available. Compile the configuration first.",
                InformationType.Warning, 3000, InformationPosition.Top);
            return;
        }

        // Show source in a popup DetailView using a temporary non-persistent object
        var os = Application.CreateObjectSpace(typeof(SourceExportView));
        var exportView = os.CreateObject<SourceExportView>();
        exportView.SourceCode = source;
        exportView.FileName = $"{config.TargetEntityType.Split('.').Last()}Search.cs";

        var detailView = Application.CreateDetailView(os, exportView);
        detailView.ViewEditMode = ViewEditMode.View;

        e.ShowViewParameters.CreatedView = detailView;
        e.ShowViewParameters.TargetWindow = TargetWindow.NewModalWindow;
    }

    private static bool IsEligibleProperty(IMemberInfo member)
    {
        // Skip non-public, key fields, internal XAF fields
        if (!member.IsPublic || !member.IsVisible) return false;
        if (member.IsKey) return false;
        if (member.Name is "Oid" or "ID" or "GCRecord" or "OptimisticLockField" or "ObjectType") return false;

        // Skip collections
        if (member.IsList) return false;

        // Only include supported types
        var type = member.MemberType;
        if (type == typeof(string)) return true;
        if (type == typeof(int) || type == typeof(int?)) return true;
        if (type == typeof(long) || type == typeof(long?)) return true;
        if (type == typeof(decimal) || type == typeof(decimal?)) return true;
        if (type == typeof(double) || type == typeof(double?)) return true;
        if (type == typeof(float) || type == typeof(float?)) return true;
        if (type == typeof(bool) || type == typeof(bool?)) return true;
        if (type == typeof(DateTime) || type == typeof(DateTime?)) return true;
        if (type == typeof(Guid) || type == typeof(Guid?)) return true;
        if (type.IsEnum) return true;

        // Reference types (persistent XAF objects)
        if (member.MemberTypeInfo?.IsPersistent == true) return true;

        return false;
    }
}
```

**Step 2: Create SourceExportView non-persistent object**

Create file `XafSearch/XafSearch.Module/BusinessObjects/SourceExportView.cs`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafSearch.Module.BusinessObjects;

[DomainComponent]
[XafDisplayName("Exported Source Code")]
public class SourceExportView : NonPersistentBaseObject
{
    [XafDisplayName("File Name")]
    public string FileName { get; set; }

    [XafDisplayName("Source Code")]
    [DevExpress.Persistent.Base.Size(SizeAttribute.Unlimited)]
    [EditorAlias(DevExpress.ExpressApp.Editors.EditorAliases.StringPropertyEditor)]
    public string SourceCode { get; set; }

    public override string ToString() => FileName ?? "Source Code";
}
```

**Step 3: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add XafSearch/XafSearch.Module/Controllers/SearchConfigurationController.cs XafSearch/XafSearch.Module/BusinessObjects/SourceExportView.cs
git commit -m "feat: add SearchConfigurationController with populate, compile, and export actions"
```

---

### Task 8: Module Bootstrap — Register on Startup

**Files:**
- Modify: `XafSearch/XafSearch.Module/Module.cs`

**Step 1: Add bootstrap logic to XafSearchModule.Setup**

Add to the `Setup(XafApplication application)` method:

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

Add required usings at top:

```csharp
using XafSearch.Module.BusinessObjects;
using XafSearch.Module.Services;
```

**Step 2: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add XafSearch/XafSearch.Module/Module.cs
git commit -m "feat: bootstrap SearchDtoRegistry on application startup"
```

---

### Task 9: Ad-hoc "Generate Search Panel" Action on Any ListView

**Files:**
- Create: `XafSearch/XafSearch.Module/Controllers/GenerateSearchPanelController.cs`

**Step 1: Create the ad-hoc controller**

This activates on ListViews that do NOT have a search panel, offering to create one:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using XafSearch.Module.BusinessObjects;
using XafSearch.Module.Services;

namespace XafSearch.Module.Controllers;

/// <summary>
/// Appears on ListViews without a search panel configured.
/// Creates a SearchConfiguration and opens it for customization.
/// </summary>
public class GenerateSearchPanelController : ViewController<ListView>
{
    private SimpleAction generateAction;

    public GenerateSearchPanelController()
    {
        generateAction = new SimpleAction(this, "GenerateSearchPanel", PredefinedCategory.Tools)
        {
            Caption = "Generate Search Panel",
            ImageName = "Action_New",
            ToolTip = "Create a search panel configuration for this entity",
            SelectionDependencyType = SelectionDependencyType.Independent
        };
        generateAction.Execute += GenerateAction_Execute;
    }

    protected override void OnActivated()
    {
        base.OnActivated();

        var entityType = View.ObjectTypeInfo?.Type;
        if (entityType == null)
        {
            Active["CanGenerate"] = false;
            return;
        }

        // Only show if NO search panel exists for this entity
        var hasPanel = SearchDtoRegistry.Instance.HasSearchPanel(entityType.FullName);
        Active["CanGenerate"] = !hasPanel;

        // Also hide for SearchConfiguration's own ListView
        if (entityType == typeof(SearchConfiguration) || entityType == typeof(SearchField))
        {
            Active["CanGenerate"] = false;
        }
    }

    private void GenerateAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var entityType = View.ObjectTypeInfo?.Type;
        if (entityType == null) return;

        var os = Application.CreateObjectSpace(typeof(SearchConfiguration));
        var config = os.CreateObject<SearchConfiguration>();
        config.Name = entityType.Name;
        config.TargetEntityType = entityType.FullName;
        config.IsActive = true;

        // Auto-populate properties using the same logic as SearchConfigurationController
        int sortOrder = 0;
        var typeInfo = View.ObjectTypeInfo;
        foreach (var member in typeInfo.Members.OrderBy(m => m.Name))
        {
            if (!IsEligibleProperty(member)) continue;

            var field = os.CreateObject<SearchField>();
            field.PropertyName = member.Name;
            field.PropertyTypeName = member.MemberType.FullName;
            field.DisplayName = member.DisplayName ?? member.Name;
            field.SortOrder = sortOrder++;
            field.IsReferenceProperty = member.MemberTypeInfo?.IsPersistent == true;

            if (field.IsReferenceProperty)
                field.ReferencedTypeName = member.MemberType.FullName;

            config.Fields.Add(field);
        }

        // Open DetailView for the admin to customize
        var detailView = Application.CreateDetailView(os, config);
        detailView.ViewEditMode = DevExpress.ExpressApp.Editors.ViewEditMode.Edit;

        e.ShowViewParameters.CreatedView = detailView;
        e.ShowViewParameters.TargetWindow = TargetWindow.NewModalWindow;
    }

    private static bool IsEligibleProperty(DevExpress.ExpressApp.DC.IMemberInfo member)
    {
        if (!member.IsPublic || !member.IsVisible) return false;
        if (member.IsKey) return false;
        if (member.Name is "Oid" or "ID" or "GCRecord" or "OptimisticLockField" or "ObjectType") return false;
        if (member.IsList) return false;

        var type = member.MemberType;
        if (type == typeof(string)) return true;
        if (type == typeof(int) || type == typeof(int?)) return true;
        if (type == typeof(long) || type == typeof(long?)) return true;
        if (type == typeof(decimal) || type == typeof(decimal?)) return true;
        if (type == typeof(double) || type == typeof(double?)) return true;
        if (type == typeof(float) || type == typeof(float?)) return true;
        if (type == typeof(bool) || type == typeof(bool?)) return true;
        if (type == typeof(DateTime) || type == typeof(DateTime?)) return true;
        if (type == typeof(Guid) || type == typeof(Guid?)) return true;
        if (type.IsEnum) return true;
        if (member.MemberTypeInfo?.IsPersistent == true) return true;

        return false;
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build XafSearch/XafSearch.Module/XafSearch.Module.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add XafSearch/XafSearch.Module/Controllers/GenerateSearchPanelController.cs
git commit -m "feat: add GenerateSearchPanelController for ad-hoc search panel creation"
```

---

### Task 10: Integration Test — Full Compile & Search Flow

**Files:**
- Create: `XafSearch/XafSearch.Module/BusinessObjects/SampleCustomer.cs` (test entity)
- Modify: `XafSearch/XafSearch.Module/BusinessObjects/XafSearchDbContext.cs` (add DbSet)

**Step 1: Create a sample entity to test against**

```csharp
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel;

namespace XafSearch.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class SampleCustomer : BaseObject
{
    public virtual string Name { get; set; }
    public virtual string Email { get; set; }
    public virtual string City { get; set; }
    public virtual int? Age { get; set; }
    public virtual DateTime? CreatedDate { get; set; }
    public virtual bool IsActive { get; set; }
}
```

**Step 2: Add DbSet to context**

Add to `XafSearchDbContext.cs`:

```csharp
public DbSet<SampleCustomer> SampleCustomers { get; set; }
```

**Step 3: Build the full solution**

Run: `dotnet build XafSearch.slnx`
Expected: Build succeeded with no errors

**Step 4: Commit**

```bash
git add XafSearch/XafSearch.Module/BusinessObjects/SampleCustomer.cs XafSearch/XafSearch.Module/BusinessObjects/XafSearchDbContext.cs
git commit -m "feat: add SampleCustomer entity for testing search panels"
```

---

### Task 11: Manual Smoke Test

**Step 1: Run the Blazor app**

Run: `dotnet run --project XafSearch/XafSearch.Blazor.Server/XafSearch.Blazor.Server.csproj`

**Step 2: Verify in browser**

1. Navigate to "Search Configuration" in the nav menu
2. Create a new SearchConfiguration:
   - Name: "Customer Search"
   - Target Entity Type: `XafSearch.Module.BusinessObjects.SampleCustomer`
3. Click "Populate Properties" — fields should populate
4. Remove fields you don't want, reorder as needed
5. Click "Compile & Activate" — should show success message
6. Navigate to "Sample Customers" ListView
7. "Advanced Search" button should appear in toolbar
8. Click it — popup with search form should appear
9. Enter search criteria, submit — ListView should filter
10. Navigate back to SearchConfiguration, click "Export C# Source" — modal with generated code

**Step 3: Commit any fixes discovered during smoke test**

---

### Task 12: Final Cleanup & Review

**Step 1: Verify all files are committed**

Run: `git status`

**Step 2: Verify solution builds clean**

Run: `dotnet build XafSearch.slnx`
Expected: Build succeeded, 0 warnings related to our code

**Step 3: Final commit if needed**

---

## File Summary

| File | Action | Task |
|---|---|---|
| `BusinessObjects/BaseObjectInt.cs` | Create | 1 |
| `BusinessObjects/SearchConfiguration.cs` | Create | 2 |
| `BusinessObjects/SearchField.cs` | Create | 2 |
| `BusinessObjects/XafSearchDbContext.cs` | Modify | 2, 10 |
| `BusinessObjects/SourceExportView.cs` | Create | 7 |
| `BusinessObjects/SampleCustomer.cs` | Create | 10 |
| `Services/SearchDtoCompiler.cs` | Create | 3 |
| `Services/SearchDtoRegistry.cs` | Create | 4 |
| `Controllers/SearchControllerBase.cs` | Create | 5 |
| `Controllers/SearchPanelController.cs` | Create | 6 |
| `Controllers/SearchConfigurationController.cs` | Create | 7 |
| `Controllers/GenerateSearchPanelController.cs` | Create | 9 |
| `Module.cs` | Modify | 8 |

All files are in `XafSearch/XafSearch.Module/` unless otherwise noted.
