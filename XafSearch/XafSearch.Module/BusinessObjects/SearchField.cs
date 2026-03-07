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

    [XafDisplayName("Range Filter")]
    [DevExpress.Persistent.Base.ToolTip("Generate From/To fields for range filtering (dates, numbers)")]
    public virtual bool UseRangeFilter { get; set; }

    [XafDisplayName("Sort Order")]
    public virtual int SortOrder { get; set; }

    [XafDisplayName("Is Reference")]
    public virtual bool IsReferenceProperty { get; set; }

    [XafDisplayName("Referenced Type")]
    public virtual string ReferencedTypeName { get; set; }

    public override string ToString() => DisplayName ?? PropertyName ?? "New Field";
}
