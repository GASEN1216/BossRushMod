"""潮闸镇 three.js 场景的本地开发服务：静态文件一律不缓存（浏览器会死抓旧的 ES 模块），
另收 POST /__save?name=<文件名> 把页面导出的文件写进指定目录（只允许一层子目录、不许越界）。

用法：python tools/tidegate_dev_server.py <端口> <站点根目录> [导出目录]
对应 .claude/launch.json 的 tidegate-v2；导出流程见 ArtSource/Tidegate/README.md。
"""
import http.server, socketserver, sys, functools, os, re, urllib.parse
port = int(sys.argv[1]); root = sys.argv[2]
EXPORT_DIR = sys.argv[3] if len(sys.argv) > 3 else None
NAME_OK = re.compile(r'^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)?\.[A-Za-z0-9]+$')

class H(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store, must-revalidate'); super().end_headers()
    def log_message(self, *a): pass
    def do_POST(self):
        u = urllib.parse.urlparse(self.path); q = urllib.parse.parse_qs(u.query)
        name = (q.get('name') or [''])[0]
        if u.path != '/__save' or not EXPORT_DIR or not NAME_OK.match(name):
            self.send_response(400); self.end_headers(); self.wfile.write(b'bad request'); return
        n = int(self.headers.get('Content-Length') or 0); data = self.rfile.read(n)
        dest = os.path.realpath(os.path.join(EXPORT_DIR, name))
        if not dest.startswith(os.path.realpath(EXPORT_DIR) + os.sep):
            self.send_response(400); self.end_headers(); self.wfile.write(b'bad path'); return
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, 'wb') as f: f.write(data)
        self.send_response(200); self.end_headers(); self.wfile.write(str(len(data)).encode())

socketserver.ThreadingTCPServer.allow_reuse_address = True
with socketserver.ThreadingTCPServer(('127.0.0.1', port), functools.partial(H, directory=root)) as s:
    s.serve_forever()
