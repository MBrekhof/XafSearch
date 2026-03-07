using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafSearch.Module.BusinessObjects;

[DomainComponent]
[XafDisplayName("Exported Source Code")]
public class SourceExportView : NonPersistentBaseObject
{
    [XafDisplayName("File Name")]
    public string FileName { get; set; }

    [XafDisplayName("Source Code")]
    [StringLength(int.MaxValue)]
    public string SourceCode { get; set; }

    public override string ToString() => FileName ?? "Source Code";
}
