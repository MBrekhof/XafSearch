using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel;

namespace XafSearch.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class SampleCustomer : BaseObject
{
    public virtual string Name { get; set; }
    public virtual string Email { get; set; }
    public virtual string City { get; set; }
    public virtual int? Age { get; set; }
    public virtual DateTime? CreatedDate { get; set; }
    public virtual bool IsActive { get; set; }
}
