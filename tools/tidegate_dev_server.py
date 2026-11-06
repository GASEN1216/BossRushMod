"""潮闸镇 three.js 场景的本地开发服务：静态文件一律不缓存（浏览器会死抓旧的 ES 模块），
同源页面可 POST /__save?name=<文件名> 将导出写进指定目录（只允许一层子目录、不许越界）。

用法：python tools/tidegate_dev_server.py <端口> <站点根目录> [导出目录]
对应 .claude/launch.json 的 tidegate-v2；导出流程见 ArtSource/Tidegate/README.md。
"""
import functools
import http.server
import os
import re
import sys
import urllib.parse

NAME_OK = re.compile(r'^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)?\.[A-Za-z0-9]+$')


class ExportHandler(http.server.SimpleHTTPRequestHandler):
    def parse_request(self):
        if not super().parse_request():
            return False
        # 不从任意 Host 推导可信 Origin：防止其它域名解析到回环地址后读页面或写导出。
        port = self.server.server_port
        hosts = {f'{host}:{port}' for host in ('127.0.0.1', 'localhost')}
        if port == 80:
            hosts.update(('127.0.0.1', 'localhost'))
        values = self.headers.get_all('Host', [])
        if len(values) != 1 or values[0].lower() not in hosts:
            self.send_error(403, 'Local host required')
            return False
        return True

    def end_headers(self):
        self.send_header('Cache-Control', 'no-store, must-revalidate')
        super().end_headers()

    def log_message(self, *args):
        pass

    def do_POST(self):
        # 浏览器跨源表单也能发送简单 POST，不能依靠 CORS 限制读取响应来保护写入。
        authority = self.headers['Host'].lower()
        if self.server.server_port == 80:
            authority = authority.removesuffix(':80')
        if self.headers.get_all('Origin', []) != ['http://' + authority]:
            self.send_error(403, 'Same-origin export required')
            return
        u = urllib.parse.urlsplit(self.path)
        name = urllib.parse.parse_qs(u.query).get('name', [''])[0]
        export_dir = self.server.export_dir
        if u.path != '/__save' or not export_dir or not NAME_OK.fullmatch(name):
            self.send_error(400, 'Bad export request')
            return
        dest = os.path.realpath(os.path.join(export_dir, name))
        if not os.path.normcase(dest).startswith(os.path.normcase(os.path.join(export_dir, ''))):
            self.send_error(400, 'Bad export path')
            return
        length = self.headers.get('Content-Length', '')
        if not re.fullmatch(r'[0-9]+', length):
            self.send_error(400, 'Content-Length required')
            return
        size = int(length)
        data = self.rfile.read(size)
        if len(data) != size:
            self.send_error(400, 'Incomplete export')
            return
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, 'wb') as f:
            f.write(data)
        self.send_response(200)
        self.end_headers()
        self.wfile.write(str(len(data)).encode())


def create_server(port, root, export_dir=None):
    server = http.server.ThreadingHTTPServer(('127.0.0.1', port), functools.partial(ExportHandler, directory=root))
    server.export_dir = os.path.realpath(export_dir) if export_dir else None
    return server


if __name__ == '__main__':
    with create_server(int(sys.argv[1]), sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None) as server:
        server.serve_forever()
