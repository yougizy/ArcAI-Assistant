using System;
using ESRI.ArcGIS.Desktop.AddIns;

namespace ArcAI.AddIn
{
    /// <summary>
    /// Auto-loaded extension. It only initializes the per-user folders and the log;
    /// the UI itself lives in AIWindow (dockable) and must not do heavy work here.
    /// </summary>
    public class AIExtension : ESRI.ArcGIS.Desktop.AddIns.Extension
    {
        private static AIExtension _instance;

        public static AIExtension Instance
        {
            get { return _instance; }
        }

        protected override void OnStartup()
        {
            _instance = this;
            AppPaths.Ensure();
            Log.Write("ArcAI Assistant extension started. ArcGIS " + ArcSession.VersionText());
        }

        protected override void OnShutdown()
        {
            Log.Write("ArcAI Assistant extension stopped.");
            _instance = null;
        }
    }
}
