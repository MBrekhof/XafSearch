using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using XafSearch.Module.BusinessObjects;
using XafSearch.Module.Services;

namespace XafSearch.Module.Controllers;

/// <summary>
/// DetailView controller: Populate, Compile & Activate, Export actions.
/// Auto-compiles on save when config has fields and is active.
/// </summary>
public class SearchConfigurationController : ObjectViewController<DetailView, SearchConfiguration>
{
    private SimpleAction populateAction;
    private SimpleAction compileAction;
    private SimpleAction exportAction;

    public SearchConfigurationController()
    {
        populateAction = new SimpleAction(this, "PopulateProperties", PredefinedCategory.View)
        {
            Caption = "Populate Properties",
            ImageName = "Action_Reload",
            ToolTip = "Read properties from the target entity and populate the fields list",
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
        };
        populateAction.Execute += PopulateAction_Execute;

        compileAction = new SimpleAction(this, "CompileAndActivate", PredefinedCategory.View)
        {
            Caption = "Compile & Activate",
            ImageName = "Action_Grant",
            ToolTip = "Compile the search DTO and register it with XAF",
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
        };
        compileAction.Execute += CompileAction_Execute;

        exportAction = new SimpleAction(this, "ExportCSharpSource", PredefinedCategory.View)
        {
            Caption = "Export C# Source",
            ImageName = "Action_Export",
            ToolTip = "Generate and display the C# source code for this search panel",
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
        };
        exportAction.Execute += ExportAction_Execute;
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.Committed += ObjectSpace_Committed;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.Committed -= ObjectSpace_Committed;
        base.OnDeactivated();
    }

    private void ObjectSpace_Committed(object sender, EventArgs e)
    {
        var config = ViewCurrentObject;
        if (config == null) return;
        if (!config.IsActive) return;
        if (string.IsNullOrWhiteSpace(config.TargetEntityType)) return;
        if (config.Fields.Count == 0) return;

        var module = Application.Modules
            .OfType<XafSearchModule>()
            .FirstOrDefault();
        if (module == null) return;

        var result = SearchDtoRegistry.Instance.CompileAndRegister(config, module);

        if (result.Success)
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Search panel auto-compiled: {result.DtoType.Name}",
                InformationType.Success, 3000, InformationPosition.Top);
        }
        else
        {
            Application.ShowViewStrategy.ShowMessage(
                $"Auto-compile failed: {string.Join("; ", result.Errors.Take(3))}",
                InformationType.Warning, 5000, InformationPosition.Top);
        }
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
            if (!PropertyEligibility.IsEligibleProperty(member)) continue;

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
            .OfType<XafSearchModule>()
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
}

/// <summary>
/// ListView controller: "Compile All" action to batch-compile all active configurations.
/// </summary>
public class SearchConfigurationListController : ObjectViewController<ListView, SearchConfiguration>
{
    private SimpleAction compileAllAction;

    public SearchConfigurationListController()
    {
        compileAllAction = new SimpleAction(this, "CompileAllSearchPanels", PredefinedCategory.View)
        {
            Caption = "Compile All",
            ImageName = "Action_Grant",
            ToolTip = "Compile and activate all active search configurations",
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage,
            SelectionDependencyType = SelectionDependencyType.Independent
        };
        compileAllAction.Execute += CompileAllAction_Execute;
    }

    private void CompileAllAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var module = Application.Modules
            .OfType<XafSearchModule>()
            .FirstOrDefault();

        if (module == null) return;

        var configs = ObjectSpace.GetObjectsQuery<SearchConfiguration>()
            .Where(c => c.IsActive && c.TargetEntityType != null)
            .ToList();

        int success = 0;
        int failed = 0;

        foreach (var config in configs)
        {
            if (config.Fields.Count == 0) continue;

            var result = SearchDtoRegistry.Instance.CompileAndRegister(config, module);
            if (result.Success)
                success++;
            else
                failed++;
        }

        var message = $"Compiled {success} search panel(s).";
        if (failed > 0)
            message += $" {failed} failed.";

        Application.ShowViewStrategy.ShowMessage(
            message,
            failed > 0 ? InformationType.Warning : InformationType.Success,
            3000, InformationPosition.Top);
    }
}
