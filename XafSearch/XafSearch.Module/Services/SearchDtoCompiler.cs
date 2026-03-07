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
        sb.AppendLine("using XafSearch.Module.Attributes;");

        var lastDot = config.TargetEntityType.LastIndexOf('.');
        if (lastDot > 0)
        {
            var targetNamespace = config.TargetEntityType.Substring(0, lastDot);
            sb.AppendLine($"using {targetNamespace};");
        }

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

            if (field.IsReferenceProperty && !string.IsNullOrWhiteSpace(field.ReferencedTypeName))
            {
                sb.AppendLine($"        [XafDisplayName(\"{EscapeString(displayName)}\")]");
                sb.AppendLine($"        public {field.ReferencedTypeName} {field.PropertyName} {{ get; set; }}");
            }
            else if (field.UseRangeFilter && IsRangeEligibleType(field.PropertyTypeName))
            {
                var clrType = GetNullableTypeName(field.PropertyTypeName);
                sb.AppendLine($"        [XafDisplayName(\"{EscapeString(displayName)} (From)\")]");
                sb.AppendLine($"        public {clrType} {field.PropertyName}From {{ get; set; }}");
                sb.AppendLine();
                sb.AppendLine($"        [XafDisplayName(\"{EscapeString(displayName)} (To)\")]");
                sb.AppendLine($"        public {clrType} {field.PropertyName}To {{ get; set; }}");
            }
            else
            {
                sb.AppendLine($"        [XafDisplayName(\"{EscapeString(displayName)}\")]");
                var clrType = GetNullableTypeName(field.PropertyTypeName);
                var normalizedType = NormalizeTypeName(field.PropertyTypeName);
                if (normalizedType == "System.String" && field.UseExactMatch)
                {
                    sb.AppendLine($"        [UseExactMatch]");
                }
                if (normalizedType == "System.String" && !field.UseExactMatch)
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

        sb.Append(GenerateSource(config));
        sb.AppendLine();

        sb.AppendLine($"namespace {RuntimeNamespace}");
        sb.AppendLine("{");
        sb.AppendLine($"    public class {controllerName} : XafSearch.Module.Controllers.SearchControllerBase<{config.TargetEntityType}, {dtoName}>");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Strips Nullable wrapper from CLR type names.
    /// e.g. "System.Nullable`1[[System.Int32, ...]]" => "System.Int32"
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return typeName;
        if (typeName.StartsWith("System.Nullable`1"))
        {
            var start = typeName.IndexOf("[[");
            var end = typeName.IndexOf(',', start > 0 ? start : 0);
            if (start >= 0 && end >= 0)
                return typeName.Substring(start + 2, end - start - 2);
            // Simpler form: System.Nullable`1[System.Int32]
            start = typeName.IndexOf('[');
            end = typeName.IndexOf(']');
            if (start >= 0 && end >= 0)
                return typeName.Substring(start + 1, end - start - 1);
        }
        return typeName;
    }

    private static string GetNullableTypeName(string typeName)
    {
        var normalized = NormalizeTypeName(typeName);
        return normalized switch
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
            _ when normalized.Contains('`') => "object", // safety fallback for generics
            _ => normalized + "?"
        };
    }

    private static bool IsRangeEligibleType(string typeName)
    {
        var normalized = NormalizeTypeName(typeName);
        return normalized is "System.DateTime" or "System.Int32" or "System.Int64"
            or "System.Decimal" or "System.Double" or "System.Single";
    }

    private static string EscapeString(string value)
        => value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;

    private static List<MetadataReference> GetMetadataReferences()
    {
        var references = new List<MetadataReference>();

        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedAssemblies != null)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                try { references.Add(MetadataReference.CreateFromFile(path)); }
                catch { }
            }
        }

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
