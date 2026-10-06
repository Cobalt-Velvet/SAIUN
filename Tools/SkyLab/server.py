# 시안 페이지를 내보내고, 페이지가 올린 PNG를 out/에 저장한다(localhost 전용).
import http.server, os, urllib.parse
ROOT = os.path.dirname(os.path.abspath(__file__))
class H(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k): super().__init__(*a, directory=ROOT, **k)
    def do_POST(self):
        q = urllib.parse.urlparse(self.path)
        name = urllib.parse.parse_qs(q.query).get('name', ['shot'])[0]
        name = ''.join(ch for ch in name if ch.isalnum() or ch in '_-')
        n = int(self.headers.get('Content-Length', 0))
        data = self.rfile.read(n)
        with open(os.path.join(ROOT, 'out', name + '.png'), 'wb') as f: f.write(data)
        self.send_response(200); self.send_header('Access-Control-Allow-Origin', '*'); self.end_headers(); self.wfile.write(b'ok')
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store'); super().end_headers()
    def log_message(self, *a): pass
http.server.ThreadingHTTPServer(('127.0.0.1', 8765), H).serve_forever()
