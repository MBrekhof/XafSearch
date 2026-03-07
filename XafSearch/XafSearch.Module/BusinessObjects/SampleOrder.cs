using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace XafSearch.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(OrderNumber))]
public class SampleOrder : BaseObject
{
    public virtual string OrderNumber { get; set; }

    public virtual DateTime? OrderDate { get; set; }

    public virtual decimal TotalAmount { get; set; }

    public virtual int Quantity { get; set; }

    public virtual OrderStatus Status { get; set; }

    public virtual Guid? CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public virtual SampleCustomer Customer { get; set; }
}

public enum OrderStatus
{
    New,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}
