using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace XafSearch.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("Search Configuration")]
[DefaultProperty(nameof(Name))]
[XafDisplayName("Search Configuration")]
public class SearchConfiguration : BaseObjectInt
{
    public virtual string Name { get; set; }

    [XafDisplayName("Target Entity Type")]
    [ImmediatePostData]
    public virtual string TargetEntityType { get; set; }

    [XafDisplayName("Active")]
    public virtual bool IsActive { get; set; } = true;

    [DevExpress.ExpressApp.DC.Aggregated]
    public virtual IList<SearchField> Fields { get; set; } = new ObservableCollection<SearchField>();

    public override string ToString() => Name ?? "New Search Configuration";
}
