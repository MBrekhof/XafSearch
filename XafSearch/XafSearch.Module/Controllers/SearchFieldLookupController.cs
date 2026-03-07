using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using XafSearch.Module.BusinessObjects;
using XafSearch.Module.Services;

namespace XafSearch.Module.Controllers;

/// <summary>
/// Provides dropdown values for SearchConfiguration.TargetEntityType
/// and SearchField.PropertyName. Auto-fills dependent fields when PropertyName changes.
/// </summary>
public class SearchConfigurationLookupController : ObjectViewController<DetailView, SearchConfiguration>
{
    protected override void OnActivated()
    {
        base.OnActivated();
        SetTargetEntityTypePredefinedValues();
        View.CurrentObjectChanged += View_CurrentObjectChanged;
    }

    protected override void OnDeactivated()
    {
        View.CurrentObjectChanged -= View_CurrentObjectChanged;
        base.OnDeactivated();
    }

    private void View_CurrentObjectChanged(object sender, EventArgs e)
    {
        SetTargetEntityTypePredefinedValues();
    }

    private void SetTargetEntityTypePredefinedValues()
    {
        var item = View.FindItem("TargetEntityType");
        if (item is PropertyEditor editor)
        {
            var modelItem = editor.Model as IModelCommonMemberViewItem;
            if (modelItem != null)
            {
                var types = XafTypesInfo.Instance.PersistentTypes
                    .Where(t => t.IsPersistent && !t.IsAbstract && t.Type != null)
                    .OrderBy(t => t.FullName)
                    .Select(t => t.FullName);
                modelItem.PredefinedValues = string.Join(";", types);
            }
        }
    }
}

/// <summary>
/// Provides dropdown values for SearchField.PropertyName based on the parent
/// SearchConfiguration's target entity. Auto-fills PropertyTypeName, IsReferenceProperty,
/// and ReferencedTypeName when a property is selected.
/// </summary>
public class SearchFieldLookupController : ObjectViewController<DetailView, SearchField>
{
    protected override void OnActivated()
    {
        base.OnActivated();
        SetPropertyNamePredefinedValues();
        ObjectSpace.ObjectChanged += ObjectSpace_ObjectChanged;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.ObjectChanged -= ObjectSpace_ObjectChanged;
        base.OnDeactivated();
    }

    private void ObjectSpace_ObjectChanged(object sender, ObjectChangedEventArgs e)
    {
        if (e.Object != ViewCurrentObject) return;

        if (e.PropertyName == nameof(SearchField.PropertyName))
        {
            AutoFillPropertyDetails();
        }
    }

    private void SetPropertyNamePredefinedValues()
    {
        var field = ViewCurrentObject;
        if (field?.SearchConfiguration == null) return;

        var targetTypeName = field.SearchConfiguration.TargetEntityType;
        if (string.IsNullOrWhiteSpace(targetTypeName)) return;

        var typeInfo = XafTypesInfo.Instance.FindTypeInfo(targetTypeName);
        if (typeInfo == null) return;

        var item = View.FindItem("PropertyName");
        if (item is PropertyEditor editor)
        {
            var modelItem = editor.Model as IModelCommonMemberViewItem;
            if (modelItem != null)
            {
                var propertyNames = typeInfo.Members
                    .Where(m => PropertyEligibility.IsEligibleProperty(m))
                    .OrderBy(m => m.Name)
                    .Select(m => m.Name);
                modelItem.PredefinedValues = string.Join(";", propertyNames);
            }
        }
    }

    private void AutoFillPropertyDetails()
    {
        var field = ViewCurrentObject;
        if (field?.SearchConfiguration == null || string.IsNullOrWhiteSpace(field.PropertyName)) return;

        var targetTypeName = field.SearchConfiguration.TargetEntityType;
        if (string.IsNullOrWhiteSpace(targetTypeName)) return;

        var typeInfo = XafTypesInfo.Instance.FindTypeInfo(targetTypeName);
        var member = typeInfo?.FindMember(field.PropertyName);
        if (member == null) return;

        // Store the underlying type, not the Nullable wrapper
        var underlyingType = Nullable.GetUnderlyingType(member.MemberType) ?? member.MemberType;
        field.PropertyTypeName = underlyingType.FullName;
        field.DisplayName = member.DisplayName ?? member.Name;
        field.IsReferenceProperty = member.MemberTypeInfo?.IsPersistent == true;
        field.ReferencedTypeName = field.IsReferenceProperty ? member.MemberType.FullName : null;

        // Auto-suggest range filter for dates and numerics
        if (underlyingType.FullName is "System.DateTime" or "System.Int32" or "System.Int64"
            or "System.Decimal" or "System.Double" or "System.Single")
        {
            field.UseRangeFilter = true;
        }
    }
}
