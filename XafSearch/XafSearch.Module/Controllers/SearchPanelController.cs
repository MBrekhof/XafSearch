using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Layout;
using DevExpress.ExpressApp.Model;
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

        var detailViewId = EnsureDetailView(_dtoType);

        var detailView = Application.CreateDetailView(os, detailViewId, true, searchObj);
        detailView.ViewEditMode = ViewEditMode.Edit;
        e.View = detailView;
        e.Maximized = false;
    }

    private string EnsureDetailView(Type type)
    {
        var detailViewId = $"{type.FullName.Replace(".", "_")}_DetailView";

        // Check if DetailView already exists with items
        if (Application.Model.Views[detailViewId] is IModelDetailView existing
            && existing.Items.Count > 0)
            return detailViewId;

        // Ensure BOModel class
        var boModel = Application.Model.BOModel;
        var modelClass = boModel.GetClass(type);
        if (modelClass == null)
        {
            modelClass = boModel.AddNode<IModelClass>(type.FullName);
            modelClass.SetValue("Name", type.FullName);
        }

        // Create or get DetailView
        IModelDetailView detailViewModel;
        if (Application.Model.Views[detailViewId] is IModelDetailView existingView)
        {
            detailViewModel = existingView;
        }
        else
        {
            detailViewModel = Application.Model.Views.AddNode<IModelDetailView>(detailViewId);
            detailViewModel.ModelClass = modelClass;
        }

        // Populate items from the DTO's public properties (skip Oid)
        if (detailViewModel.Items.Count == 0)
        {
            var typeInfo = XafTypesInfo.Instance.FindTypeInfo(type);
            if (typeInfo != null)
            {
                foreach (var member in typeInfo.Members)
                {
                    if (member.Name == "Oid") continue;
                    if (!member.IsPublic || !member.IsVisible) continue;

                    var itemId = member.Name;
                    if (detailViewModel.Items[itemId] != null) continue;

                    var propertyEditor = detailViewModel.Items.AddNode<IModelPropertyEditor>(itemId);
                    propertyEditor.PropertyName = member.Name;
                }
            }

            // Create a simple vertical layout group
            if (detailViewModel.Layout.Count == 0)
            {
                var mainGroup = detailViewModel.Layout.AddNode<IModelLayoutGroup>("Main");
                mainGroup.Direction = FlowDirection.Vertical;

                foreach (var item in detailViewModel.Items.OfType<IModelPropertyEditor>())
                {
                    var layoutItem = mainGroup.AddNode<IModelLayoutViewItem>(((IModelViewItem)item).Id);
                    layoutItem.ViewItem = item;
                }
            }
        }

        return detailViewId;
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
