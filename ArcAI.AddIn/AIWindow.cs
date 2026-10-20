using System;
using ESRI.ArcGIS.Desktop.AddIns;

namespace ArcAI.AddIn
{
    /// <summary>
    /// Dockable window host. ArcMap embeds the HWND returned by OnCreateChild;
    /// the actual UI lives in AIWindowControl.
    /// </summary>
    public class AIWindow : DockableWindow
    {
        private AIWindowControl _control;

        public AIWindow()
        {
            Log.Write("AIWindow constructed.");
        }

        protected override IntPtr OnCreateChild()
        {
            Log.Write("AIWindow.OnCreateChild start.");
            try
            {
                if (_control == null)
                {
                    _control = new AIWindowControl();
                    Log.Write("AIWindowControl constructed.");
                    _control.CreateControl();
                    Log.Write("AIWindow child handle=" + _control.Handle);
                }
                return _control.Handle;
            }
            catch (Exception ex)
            {
                Log.Error("AIWindow.OnCreateChild failed.", ex);
                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _control != null)
            {
                _control.Dispose();
                _control = null;
            }
            base.Dispose(disposing);
        }
    }
}
