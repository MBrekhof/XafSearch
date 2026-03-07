using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Editors;
using DevExpress.Persistent.Base;
using XafSearch.Module.Services;

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

        var criteria = CriteriaBuilder.BuildCriteria(searchObj, MaxActiveFilters);

        if (criteria is not null)
        {
            View.CollectionSource.Criteria[CriteriaKey] = criteria;
            Application.ShowViewStrategy.ShowMessage(
                $"Search applied with {CriteriaBuilder.GetActiveFilterCount(searchObj)} filter(s).",
                InformationType.Success, 3000, InformationPosition.Top);
        }
        else
        {
            View.CollectionSource.Criteria.Remove(CriteriaKey);
        }
    }
}
