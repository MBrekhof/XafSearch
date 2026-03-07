using DevExpress.ExpressApp.DC;

namespace XafSearch.Module.Services;

public static class PropertyEligibility
{
    public static bool IsEligibleProperty(IMemberInfo member)
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
