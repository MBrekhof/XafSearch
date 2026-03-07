using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using System.Reflection;

namespace XafSearch.Module.Controllers;

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

        if (IsXafBusinessObject(value))
            return CreateReferenceCriterion(propName, value);

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
