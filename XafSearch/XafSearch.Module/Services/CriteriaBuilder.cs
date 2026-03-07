using DevExpress.Data.Filtering;
using System.Reflection;

namespace XafSearch.Module.Services;

public static class CriteriaBuilder
{
    public static CriteriaOperator BuildCriteria(object searchObj, int maxFilters = 20)
    {
        if (searchObj == null) return null;

        var groupOp = new GroupOperator(GroupOperatorType.And);
        int filterCount = 0;

        var properties = searchObj.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.Name != "Oid");

        foreach (var prop in properties)
        {
            if (filterCount >= maxFilters) break;

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

    public static int GetActiveFilterCount(object searchObj)
    {
        if (searchObj == null) return 0;
        return searchObj.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.CanRead && p.Name != "Oid" && !IsNullOrEmpty(p.GetValue(searchObj)));
    }

    private static CriteriaOperator CreateCriterion(PropertyInfo property, object value)
    {
        var propName = property.Name;

        // Check for UseExactMatch custom attribute on the DTO property
        bool useExactMatch = property.GetCustomAttributes()
            .Any(a => a.GetType().Name == "UseExactMatchAttribute");

        if (value is string text)
            return CreateStringCriterion(propName, text, useExactMatch);

        if (value is DateTime dateTime)
            return CreateDateCriterion(propName, dateTime);

        if (IsXafBusinessObject(value))
            return CreateReferenceCriterion(propName, value);

        return new BinaryOperator(propName, value, BinaryOperatorType.Equal);
    }

    public static CriteriaOperator CreateStringCriterion(string propertyName, string value, bool useExactMatch)
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

    public static bool IsNullOrEmpty(object value)
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
}
