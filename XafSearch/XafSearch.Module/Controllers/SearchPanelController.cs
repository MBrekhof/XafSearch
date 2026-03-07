using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using System.Reflection;
using XafSearch.Module.Services;

namespace XafSearch.Module.Controllers;

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
