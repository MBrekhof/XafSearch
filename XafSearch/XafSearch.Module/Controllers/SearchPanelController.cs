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
            SelectionDependencyType = SelectionDependencyType.Independent,
            PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
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

        // Types loaded from cache are registered before model generation,
        // so XAF creates proper model classes and default DetailViews automatically.
        var detailView = Application.CreateDetailView(os, searchObj);
        detailView.ViewEditMode = ViewEditMode.Edit;
        e.View = detailView;
        e.Maximized = false;
    }

    private void SearchAction_Execute(object sender, PopupWindowShowActionExecuteEventArgs e)
    {
        var searchObj = e.PopupWindowViewCurrentObject;
        if (searchObj == null || _dtoType == null) return;

        var criteria = CriteriaBuilder.BuildCriteria(searchObj);

        if (criteria is not null)
        {
            View.CollectionSource.Criteria[CriteriaKey] = criteria;
            var filterCount = CriteriaBuilder.GetActiveFilterCount(searchObj);
            Application.ShowViewStrategy.ShowMessage(
                $"Search applied with {filterCount} filter(s).",
                InformationType.Success, 3000, InformationPosition.Top);
        }
        else
        {
            View.CollectionSource.Criteria.Remove(CriteriaKey);
        }
    }
}
