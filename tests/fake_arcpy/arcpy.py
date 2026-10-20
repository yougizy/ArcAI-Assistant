# -*- coding: utf-8 -*-
"""Minimal fake arcpy module used by the runner tests."""


class _Env(object):
    def __init__(self):
        self.overwriteOutput = False
        self.workspace = None
        self.scratchWorkspace = None


class _DataFrame(object):
    pass


class _MapDocument(object):
    pass


class _Mapping(object):
    def _write(self, path):
        with open(path, "wb") as handle:
            handle.write(b"TEST")

    def MapDocument(self, path):
        return _MapDocument()

    def ListDataFrames(self, mxd):
        return [_DataFrame()]

    def ExportToPNG(self, mxd, path, data_frame=None, **kwargs):
        self._write(path)

    def ExportToJPEG(self, mxd, path, data_frame=None, **kwargs):
        self._write(path)

    def ExportToTIFF(self, mxd, path, data_frame=None, **kwargs):
        self._write(path)

    def ExportToPDF(self, mxd, path, data_frame=None, **kwargs):
        self._write(path)


env = _Env()
mapping = _Mapping()


def GetMessages():
    return u"fake arcpy message"


def AddMessage(message):
    return None
