using System;
using System.Windows.Forms;
using ESRI.ArcGIS.Framework;
using AddInButton = ESRI.ArcGIS.Desktop.AddIns.Button;

namespace ArcAI.AddIn
{
    /// <summary>Toolbar button that focuses (or opens) the dockable AI window.</summary>
    public class ShowAIButton : AddInButton
    {
        // The first id is the one declared in Config.xml. The alternatives make the
        // button resilient to the Add-In Manager's id normalization in older builds.
        private static readonly string[] WindowIds =
        {
            "ArcAI_DockableWindow",
            "ArcAI.DockableWindow",
            "AIWindow",
            "ArcAI.AddIn.AIWindow",
            "ArcAI.AddIn.ArcAI_DockableWindow",
            "AI 助手",
            "ArcAI Assistant",
            "{6f2c9d41-8b7a-4c3e-9f10-2d5a7e4b6c81}.AIWindow",
            "{6f2c9d41-8b7a-4c3e-9f10-2d5a7e4b6c81}_AIWindow"
        };

        protected override void OnClick()
        {
            try
            {
                IApplication app = ArcSession.App();
                IDockableWindowManager manager = app as IDockableWindowManager;
                Log.Write("ShowAIButton: IDockableWindowManager=" + (manager != null));
                if (manager == null)
                {
                    MessageBox.Show("ArcMap did not expose IDockableWindowManager.",
                        "ArcAI", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                IDockableWindow window = null;
                string usedId = null;
                foreach (string id in WindowIds)
                {
                    try
                    {
                        IDockableWindow candidate = manager.GetDockableWindow(MakeUid(id));
                        Log.Write("ShowAIButton: GetDockableWindow('" + id + "') -> "
                            + (candidate != null));
                        if (candidate != null)
                        {
                            window = candidate;
                            usedId = id;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ShowAIButton: GetDockableWindow('" + id + "') threw.", ex);
                    }
                }

                if (window == null)
                {
                    Log.Write("ShowAIButton: no dockable window registered for ids: "
                        + string.Join(", ", WindowIds));
                    MessageBox.Show("找不到 AI 助手停靠窗口。请在“自定义 > 工具条”中确认 ArcAI 已加载，然后重启 ArcMap。\n\n"
                        + "已尝试的窗口 ID：" + string.Join(", ", WindowIds),
                        "ArcAI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Log.Write("ShowAIButton: showing dockable window '" + usedId + "'.");
                window.Show(true);
            }
            catch (Exception ex)
            {
                Log.Error("ShowAIButton.OnClick failed.", ex);
                MessageBox.Show("打开 AI 助手失败：" + ex.Message, "ArcAI",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static ESRI.ArcGIS.esriSystem.UID MakeUid(string value)
        {
            var uid = new ESRI.ArcGIS.esriSystem.UID();
            uid.Value = value;
            return uid;
        }

        protected override void OnUpdate()
        {
            Enabled = true;
        }
    }
}
