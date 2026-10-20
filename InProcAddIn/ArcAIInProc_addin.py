# -*- coding: utf-8 -*-
"""ArcMap Python Add-In: execute the pending ArcAI job in-process.

The ArcAI .NET add-in writes %APPDATA%\\ArcAI\\inproc_job.json and waits for
inproc_result.json. This button runs the same runner.py used by the external
mode, but inside ArcMap's own Python 2.7 process, so current map, ArcMap
licenses and arcpy.mapping.MapDocument("CURRENT") behave like the Python window.
"""
import imp
import json
import os
import sys
import traceback


def _root():
    return os.path.join(os.environ.get("APPDATA") or os.path.expanduser("~"), "ArcAI")


class InProcButton(object):
    """Implementation for ArcAI_InProc_Execute (Button)."""

    def onClick(self):
        root = _root()
        runner = os.path.join(root, "runner.py")
        job = os.path.join(root, "inproc_job.json")
        result = os.path.join(root, "inproc_result.json")

        if not os.path.isfile(runner) or not os.path.isfile(job):
            try:
                import pythonaddins
                pythonaddins.MessageBox(
                    u"没有待执行任务。请先在 ArcAI 窗口点击“内运行”。",
                    u"ArcAI InProc", 0)
            except Exception:
                pass
            return

        old_argv = sys.argv
        try:
            sys.argv = [runner, job, result]
            module = imp.load_source("arcai_inproc_runner", runner)
            module.main()
        except Exception:
            try:
                payload = {
                    "ok": False,
                    "stdout": u"",
                    "stderr": u"",
                    "error": u"ArcMap 内执行失败。",
                    "traceback": traceback.format_exc(),
                    "add_layers": [],
                    "map_actions": [],
                    "images": [],
                }
                with open(result, "wb") as handle:
                    handle.write(json.dumps(payload, ensure_ascii=False).encode("utf-8"))
            except Exception:
                pass
        finally:
            sys.argv = old_argv
