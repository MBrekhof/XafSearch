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

        var typeInfo = XafTypesInfo.Instance.FindTypeInfo(config.TargetEntityType);
        if (typeInfo == null)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Type '{config.TargetEntityType}' not found in XAF type system.",
                InformationType.Error, 5000, InformationPosition.Top);
            return;
        }

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
