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
            // Compile all active search configs from DB BEFORE model generation
            // so XAF creates proper IModelClass nodes with full TypeInfo.
#pragma warning disable XAF0013
            var compiledTypes = SearchDtoRegistry.Instance.CompileFromDatabase(application.ConnectionString, this);
#pragma warning restore XAF0013

            // Only clean model diffs for DELETED configs (orphaned entries).
            // Active config entries are preserved so Model Editor customizations persist.
            CleanOrphanedModelDiffs(compiledTypes);

            base.Setup(application);
        }

        /// <summary>
        /// Removes model diff entries for runtime search types that are no longer active.
        /// Preserves entries for active types so layout customizations via Model Editor persist.
        /// </summary>
        private static void CleanOrphanedModelDiffs(HashSet<string> activeTypeNames)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var file in Directory.GetFiles(baseDir, "Model.User*.xafml"))
                {
                    CleanModelFile(file, activeTypeNames);
                }
            }
            catch { }
        }

        private static void CleanModelFile(string path, HashSet<string> activeTypeNames)
        {
            try
            {
                var doc = XDocument.Load(path);
                bool modified = false;

                // Remove BOModel/Class nodes for deleted runtime search types
                foreach (var node in doc.Descendants("Class")
                    .Where(c =>
                    {
                        var name = c.Attribute("Name")?.Value;
                        return name != null
                            && name.StartsWith("XafSearch.RuntimeSearch.")
                            && !activeTypeNames.Contains(name);
                    })
                    .ToList())
                {
                    node.Remove();
                    modified = true;
                }

                // Remove DetailView nodes for deleted runtime search types
                foreach (var node in doc.Descendants("DetailView")
                    .Where(v =>
                    {
                        var className = v.Attribute("ClassName")?.Value;
                        if (className != null && className.StartsWith("XafSearch.RuntimeSearch."))
                            return !activeTypeNames.Contains(className);
                        var id = v.Attribute("Id")?.Value;
                        if (id != null && id.StartsWith("XafSearch_RuntimeSearch_"))
                        {
                            // Convert view ID back to type name: XafSearch_RuntimeSearch_FooSearchDTO_DetailView
                            var typeName = id.Replace("_DetailView", "").Replace("_", ".");
                            return !activeTypeNames.Contains(typeName);
                        }
                        return false;
                    })
                    .ToList())
                {
                    node.Remove();
                    modified = true;
                }

                // Remove FormState nodes for deleted runtime search types
                foreach (var node in doc.Descendants("FormState")
                    .Where(f =>
                    {
                        var id = f.Attribute("Id")?.Value;
                        if (id != null && id.StartsWith("XafSearch_RuntimeSearch_"))
                        {
                            var typeName = id.Replace("_DetailView", "").Replace("_", ".");
                            return !activeTypeNames.Contains(typeName);
                        }
                        return false;
                    })
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
