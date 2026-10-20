using System;
using ESRI.ArcGIS.ArcMapUI;
using ESRI.ArcGIS.Framework;

namespace ArcAI.AddIn
{
    /// <summary>
    /// Safe access to the live ArcMap session. CoCreate AppRef inside ArcMap and
    /// cast only to interfaces (casting to the RCW class fails on the COM singleton).
    /// Call these helpers on the STA/UI thread.
    /// </summary>
    internal static class ArcSession
    {
        public static IApplication App()
        {
            object appRef = Activator.CreateInstance(Type.GetTypeFromProgID("esriFramework.AppRef"));
            return (IApplication)appRef;
        }

        public static IMxDocument Doc(IApplication app)
        {
            IMxDocument doc = app.Document as IMxDocument;
            if (doc == null)
                throw new InvalidOperationException("The active document is not an ArcMap document (IMxDocument).");
            return doc;
        }

        /// <summary>Path of the active .mxd, or null for an unsaved document.</summary>
        public static string MxdPath(IApplication app)
        {
            try
            {
                ITemplates templates = app.Templates;
                if (templates != null && templates.Count > 0)
                    return templates.get_Item(templates.Count - 1);
            }
            catch
            {
            }
            return null;
        }

        public static string VersionText()
        {
            try
            {
                Version v = typeof(IApplication).Assembly.GetName().Version;
                return "10.8 (ArcObjects " + v + ")";
            }
            catch
            {
                return "10.8";
            }
        }
    }
}
