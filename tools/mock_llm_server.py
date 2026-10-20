# -*- coding: utf-8 -*-
"""Tiny OpenAI-compatible mock server for ArcAI Assistant.

Run:  python tools/mock_llm_server.py
Then configure the add-in with:
  Base URL: http://127.0.0.1:8765/v1
  Model:    mock
  API Key:  (empty)

It returns a harmless Python 2.7 / arcpy snippet so the full pipeline
(request -> code preview -> standalone Python execution -> result) can be
tested without a real API key.
"""
from __future__ import print_function

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HOST = "127.0.0.1"
PORT = 8765

CODE = u'''# -*- coding: utf-8 -*-
import arcpy
print(u"ArcAI mock: hello from Python 2.7")
arcpy.AddMessage(u"ArcAI mock message")
'''


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        print("[mock] " + (fmt % args))

    def do_GET(self):
        if self.path.rstrip("/") in ("", "/health"):
            body = b"ok"
            self.send_response(200)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        self.send_error(404)

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            request = json.loads(raw.decode("utf-8"))
        except Exception:
            request = {}
        print("[mock] request model=" + str(request.get("model")))

        content = u"这是本地 mock 响应。\n\n```python\n" + CODE + u"```"
        payload = {
            "id": "chatcmpl-mock",
            "object": "chat.completion",
            "model": request.get("model") or "mock",
            "choices": [{
                "index": 0,
                "message": {"role": "assistant", "content": content},
                "finish_reason": "stop",
            }],
        }
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    print("ArcAI mock server listening on http://%s:%d/v1" % (HOST, PORT))
    print("Set the add-in Base URL to http://%s:%d/v1, model 'mock', empty API key." % (HOST, PORT))
    ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()
