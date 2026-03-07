using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.Base;
using System.Xml.Linq;
using XafSearch.Module.Services;

namespace XafSearch.Module
{
    public sealed class XafSearchModule : ModuleBase
    {
        public XafSearchModule()
        {
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.SystemModule.SystemModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Chart.ChartModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.ConditionalAppearance.ConditionalAppearanceModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Dashboards.DashboardsModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Notifications.NotificationsModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Office.OfficeModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.PivotGrid.PivotGridModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.ReportsV2.ReportsModuleV2));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.TreeListEditors.TreeListEditorsModuleBase));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Validation.ValidationModule));
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.ViewVariantsModule.ViewVariantsModule));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.FileData));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.FileAttachment));
            AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.HCategory));
        }

        public override IEnumerable<ModuleUpdater> GetModuleUpdaters(IObjectSpace objectSpace, Version versionFromDB)
        {
            ModuleUpdater updater = new DatabaseUpdate.Updater(objectSpace, versionFromDB);
            return new ModuleUpdater[] { updater };
        }

        public override void Setup(XafApplication application)
        {
            // Clean persisted model diffs for runtime search types (from previous sessions).
            CleanRuntimeSearchModelDiffs();

            // Compile all active search configs from DB BEFORE model generation
            // so XAF creates proper IModelClass nodes with full TypeInfo.
            // We need the connection string before DI/IServiceProvider is available.
#pragma warning disable XAF0013
            SearchDtoRegistry.Instance.CompileFromDatabase(application.ConnectionString, this);
#pragma warning restore XAF0013

            base.Setup(application);
        }

        private static void CleanRuntimeSearchModelDiffs()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var file in Directory.GetFiles(baseDir, "Model.User*.xafml"))
                {
                    CleanModelFile(file);
                }
            }
            catch { }
        }

        private static void CleanModelFile(string path)
        {
            try
            {
                var doc = XDocument.Load(path);
                bool modified = false;

                foreach (var node in doc.Descendants("Class")
                    .Where(c => c.Attribute("Name")?.Value?.StartsWith("XafSearch.RuntimeSearch.") == true)
                    .ToList())
                {
                    node.Remove();
                    modified = true;
                }

                foreach (var node in doc.Descendants("DetailView")
                    .Where(v => v.Attribute("Id")?.Value?.StartsWith("XafSearch_RuntimeSearch_") == true
                             || v.Attribute("ClassName")?.Value?.StartsWith("XafSearch.RuntimeSearch.") == true)
                    .ToList())
                {
                    node.Remove();
                    modified = true;
                }

                foreach (var node in doc.Descendants("FormState")
                    .Where(f => f.Attribute("Id")?.Value?.StartsWith("XafSearch_RuntimeSearch_") == true)
                    .ToList())
                {
                    node.Remove();
                    modified = true;
                }

                if (modified)
                    doc.Save(path);
            }
            catch { }
        }

        public override void Setup(ApplicationModulesManager moduleManager)
        {
            base.Setup(moduleManager);
        }
    }
}
