"""向生产导出服务发送真实 HTTP 请求，只写本夹具自己的临时文件。"""
import http.client
import sys
import tempfile
import threading
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'tools'))
from tidegate_dev_server import create_server


def run():
    with tempfile.TemporaryDirectory(prefix='tidegate-export-') as tmp:
        site = Path(tmp) / 'site'
        output = Path(tmp) / 'out'
        site.mkdir()
        output.mkdir()
        (site / 'play.html').write_bytes(b'play')
        with create_server(0, str(site), str(output)) as server:
            port = server.server_port
            host = f'127.0.0.1:{port}'
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()

            def request(method, path, headers, data=None):
                conn = http.client.HTTPConnection('127.0.0.1', port, timeout=5)
                try:
                    conn.request(method, path, data, headers)
                    response = conn.getresponse()
                    return response.status, response.read()
                finally:
                    conn.close()

            try:
                assert request('GET', '/play.html', {}) == (200, b'play')
                file = output / 'tex' / 'layout.json'
                url = '/__save?name=tex%2Flayout.json'
                raw = bytes(range(256))
                assert request('POST', url, {'Origin': 'http://' + host}, raw) == (200, b'256')
                assert file.read_bytes() == raw
                for headers in ({'Origin': f'http://127.0.0.1:{port + 1}'},
                                {'Origin': f'http://localhost:{port}'}, {'Origin': 'null'}, {},
                                {'Host': f'elsewhere.invalid:{port}', 'Origin': f'http://elsewhere.invalid:{port}'}):
                    headers['Content-Type'] = 'text/plain'
                    assert request('POST', url, headers, b'replaced')[0] == 403, headers
                    assert file.read_bytes() == raw, headers
                assert request('GET', '/play.html', {'Host': f'elsewhere.invalid:{port}'})[0] == 403
                assert request('POST', '/__save?name=..%2Foutside.json', {'Origin': 'http://' + host}, b'x')[0] == 400
                assert not (Path(tmp) / 'outside.json').exists()
            finally:
                server.shutdown()
                thread.join()
    print('PASS: same-origin binary export; cross-origin, missing or null origin, foreign Host and traversal rejected.')


if __name__ == '__main__':
    run()
