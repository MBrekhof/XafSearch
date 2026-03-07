using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using XafSearch.Module.BusinessObjects;
using XafSearch.Module.Services;

namespace XafSearch.Module.Controllers;

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

        var hasPanel = SearchDtoRegistry.Instance.HasSearchPanel(entityType.FullName);
        Active["CanGenerate"] = !hasPanel;

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
