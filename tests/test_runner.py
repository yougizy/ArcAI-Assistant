# -*- coding: utf-8 -*-
"""Runner contract tests. Run with both Python 2.7 (ArcGIS) and Python 3.

    python tests/test_runner.py
    C:\\Python27\\ArcGIS10.8\\python.exe tests/test_runner.py
"""
from __future__ import print_function

import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
RUNNER = os.path.join(HERE, "..", "ArcAI.AddIn", "Python", "runner.py")
FAKE = os.path.join(HERE, "fake_arcpy")


def write_json(path, value):
    text = json.dumps(value, ensure_ascii=False, indent=2)
    with io.open(path, "w", encoding="utf-8") as handle:
        handle.write(text)


def read_json(path):
    with io.open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.mkdtemp(prefix="arcai_runner_")
        self.out = os.path.join(self.work, "out")
        os.makedirs(self.out)
        self.job_path = os.path.join(self.work, "job.json")
        self.result_path = os.path.join(self.work, "result.json")
        self.manifest_path = os.path.join(self.work, "manifest.json")

    def tearDown(self):
        shutil.rmtree(self.work, ignore_errors=True)

    def run_runner(self, code, mxd=None, context=None):
        job = {
            "code": code,
            "context": context or {"map": {"name": "TestMap"}},
            "mxd": mxd,
            "out_dir": self.out,
            "manifest": self.manifest_path,
        }
        write_json(self.job_path, job)
        env = dict(os.environ)
        env["PYTHONPATH"] = FAKE + os.pathsep + env.get("PYTHONPATH", "")
        env["PYTHONIOENCODING"] = "utf-8"
        process = subprocess.Popen(
            [sys.executable, RUNNER, self.job_path, self.result_path],
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
        stdout, stderr = process.communicate()
        self.assertEqual(process.returncode, 0, stderr.decode("utf-8", "replace"))
        self.assertTrue(os.path.isfile(self.result_path), "runner did not write result.json")
        return read_json(self.result_path)

    def test_success_helpers_and_unicode(self):
        code = u"""# -*- coding: utf-8 -*-
arcai_log(u"hello")
print(u"中文输出")
print(arcai_context["map"]["name"])
arcai_add_layer(os.path.join(arcai_out_dir, u"结果.shp"), u"结果")
"""
        result = self.run_runner(code)
        self.assertTrue(result["ok"], result.get("traceback"))
        self.assertIn("hello", result["stdout"])
        self.assertIn(u"中文输出", result["stdout"])
        self.assertIn("TestMap", result["stdout"])
        self.assertIn("fake arcpy message", result["stdout"])
        self.assertEqual(len(result["add_layers"]), 1)
        layer = result["add_layers"][0]
        self.assertTrue(layer["path"].endswith(u"结果.shp"))
        self.assertEqual(layer["name"], u"结果")
        self.assertTrue(os.path.isfile(self.manifest_path))

    def test_clip_raster_fallback(self):
        import imp
        import sys
        import types

        runner = imp.load_source("arcai_runner_clip_test", RUNNER)

        class Desc(object):
            dataType = "Raster Dataset"

        class Raster(object):
            def save(self, path):
                with open(path, "wb") as handle:
                    handle.write(b"ok")

        def fail(*args, **kwargs):
            raise RuntimeError(u"RasterCommander.ImageServer 当前许可不支持影像服务器")

        fake = types.ModuleType("arcpy")
        fake.Clip_management = fail
        fake.Describe = lambda path: Desc()
        fake.AddMessage = lambda message: None
        fake.CheckOutExtension = lambda extension: "Available"
        sa = types.ModuleType("arcpy.sa")
        sa.ExtractByMask = lambda raster, mask: Raster()
        fake.sa = sa

        old_arcpy = sys.modules.get("arcpy")
        old_sa = sys.modules.get("arcpy.sa")
        sys.modules["arcpy"] = fake
        sys.modules["arcpy.sa"] = sa
        try:
            runner._install_clip_fallback(fake)
            out = os.path.join(self.out, "clip.tif")
            fake.Clip_management(in_raster="in.tif", out_raster=out,
                                 in_template_dataset="mask.shp")
            self.assertTrue(os.path.isfile(out))
        finally:
            if old_arcpy is None:
                sys.modules.pop("arcpy", None)
            else:
                sys.modules["arcpy"] = old_arcpy
            if old_sa is None:
                sys.modules.pop("arcpy.sa", None)
            else:
                sys.modules["arcpy.sa"] = old_sa

    def test_error_is_reported(self):
        code = u"""print(u"before")
raise ValueError(u"故意失败")
"""
        result = self.run_runner(code)
        self.assertFalse(result["ok"])
        self.assertIn(u"故意失败", result["error"])
        self.assertIn("ValueError", result["traceback"])
        self.assertIn("before", result["stdout"])

    def test_mxd_is_exposed(self):
        code = u"""print(arcai_mxd)
arcai_add_layers([os.path.join(arcai_out_dir, "a.shp"), os.path.join(arcai_out_dir, "b.shp")])
"""
        result = self.run_runner(code, mxd=r"C:\data\snapshot.mxd")
        self.assertTrue(result["ok"], result.get("traceback"))
        self.assertIn(r"C:\data\snapshot.mxd", result["stdout"])
        self.assertEqual(len(result["add_layers"]), 2)

    def test_arcai_path_helper(self):
        context = {
            "layers": [{"name": u"道路", "full_path": "C:/data/roads.shp"}],
            "data_paths": [{"name": u"行政区", "path": "C:/data/districts.shp"}],
        }
        code = u"""print(arcai_path(u"道路"))
print(arcai_paths[u"行政区"])
"""
        result = self.run_runner(code, context=context)
        self.assertTrue(result["ok"], result.get("traceback"))
        self.assertIn("C:/data/roads.shp", result["stdout"])
        self.assertIn("C:/data/districts.shp", result["stdout"])

    def test_map_actions_and_map_export(self):
        code = u"""arcai_remove_layer(u"旧图层")
arcai_set_layer_visible(u"影像", False)
arcai_zoom_to_layer(u"结果")
arcai_zoom_to_extent(1, 2, 3, 4)
arcai_select_by_attribute(u"道路", u"等级='高速'")
arcai_clear_selection()
arcai_refresh()
shot = arcai_export_map(format="PNG", dpi=120)
print(shot)
"""
        result = self.run_runner(code, mxd=r"C:\data\snapshot.mxd")
        self.assertTrue(result["ok"], result.get("traceback"))
        self.assertEqual(7, len(result["map_actions"]))
        actions = [item["action"] for item in result["map_actions"]]
        self.assertEqual(
            ["remove_layer", "set_layer_visible", "zoom_to_layer",
             "zoom_to_extent", "select_by_attribute", "clear_selection", "refresh"],
            actions)
        self.assertFalse(result["map_actions"][1]["args"]["visible"])
        self.assertEqual(1, len(result["images"]))
        self.assertTrue(os.path.isfile(result["images"][0]))
        self.assertIn(result["images"][0], result["stdout"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
