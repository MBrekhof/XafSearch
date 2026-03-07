using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.Base;
using System.Xml.Linq;
using XafSearch.Module.BusinessObjects;
using XafSearch.Module.Services;

namespace XafSearch.Module
{
    // For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.ModuleBase.
    public sealed class XafSearchModule : ModuleBase
    {
        public XafSearchModule()
        {
            //
            // XafSearchModule
            //
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
            // Clean persisted model diffs that reference runtime-compiled search DTO types.
            // These types don't exist yet at model-load time (compiled later in SetupComplete)
            // and would cause "localizationNode is null" assertion failures.
            CleanRuntimeSearchModelDiffs();

            base.Setup(application);
            application.SetupComplete += Application_SetupComplete;
        }

        private void Application_SetupComplete(object sender, EventArgs e)
        {
            var application = (XafApplication)sender;
            application.SetupComplete -= Application_SetupComplete;

            try
            {
                using var objectSpace = application.CreateObjectSpace(typeof(SearchConfiguration));
                SearchDtoRegistry.Instance.Bootstrap(objectSpace, this);
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError($"SearchDtoRegistry bootstrap failed: {ex.Message}");
            }
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
            catch
            {
                // Non-critical — if cleaning fails, the worst case is the startup crash
                // the user already knows about, and they can delete Model.User.xafml manually.
            }
        }

        private static void CleanModelFile(string path)
        {
            try
            {
                var doc = XDocument.Load(path);
                bool modified = false;

                // Remove BOModel/Class nodes for runtime search types
                foreach (var node in doc.Descendants("Class")
                    .Where(c => c.Attribute("Name")?.Value?.StartsWith("XafSearch.RuntimeSearch.") == true)
                    .ToList())
                {
                    node.Remove();
                    modified = true;
                }

                // Remove Views/DetailView nodes for runtime search types
                foreach (var node in doc.Descendants("DetailView")
                    .Where(v => v.Attribute("Id")?.Value?.StartsWith("XafSearch_RuntimeSearch_") == true
                             || v.Attribute("ClassName")?.Value?.StartsWith("XafSearch.RuntimeSearch.") == true)
                    .ToList())
                {
                    node.Remove();
                    modified = true;
                }

                // Remove FormState nodes for runtime search popup windows
                foreach (var node in doc.Descendants("FormState")
                    .Where(f => f.Attribute("Id")?.Value?.StartsWith("XafSearch_RuntimeSearch_") == true)
                    .ToList())
                {
                    node.Remove();
                    modified = true;
                }

                if (modified)
                {
                    doc.Save(path);
                }
            }
            catch
            {
                // Ignore per-file errors — non-critical cleanup
            }
        }

        public override void Setup(ApplicationModulesManager moduleManager)
        {
            base.Setup(moduleManager);
        }
    }
}
