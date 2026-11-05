#!/usr/bin/env python3
"""
Phase 4: 公网页面入口（单文件白名单静态服务）

只暴露 test_webrtc.html 一个文件，绑定 127.0.0.1，供 Tailscale Funnel 反代到公网：
    tailscale funnel --bg --yes --https=10000 --set-path=/ http://127.0.0.1:8090
    tailscale funnel --bg --yes --https=10000 --set-path=/ws http://127.0.0.1:8766

与 8082 的全目录静态服务的区别：
  - 8082 服务整个工程目录（Assets/Packages/Logs...），仅限局域网使用；
  - 本服务是公网入口，白名单只有页面本身，工程其余内容一律 404。

路由：
  GET  /  /test_webrtc.html  /index.html → 页面（每次从磁盘读，无副本漂移）
  POST /stats                            → 204（页面统计上报的空接收端）
  其余                                   → 404
"""
import argparse
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.dirname(os.path.abspath(__file__))
PAGE = os.path.join(ROOT, 'test_webrtc.html')


class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'
    server_version = 'Phase4Page/1.0'

    def _send(self, code, ctype=None, body=b''):
        self.send_response(code)
        if ctype:
            self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _page_body(self):
        with open(PAGE, 'rb') as f:
            return f.read()

    def do_GET(self):
        path = self.path.split('?', 1)[0]
        if path in ('/', '/test_webrtc.html', '/index.html'):
            try:
                body = self._page_body()
            except OSError:
                self._send(500, 'text/plain; charset=utf-8', b'page missing')
                return
            self._send(200, 'text/html; charset=utf-8', body)
        else:
            self._send(404, 'text/plain; charset=utf-8', b'not found')

    def do_POST(self):
        path = self.path.split('?', 1)[0]
        length = int(self.headers.get('Content-Length') or 0)
        if length:
            self.rfile.read(length)
        if path == '/stats':
            self._send(204)
        else:
            self._send(404, 'text/plain; charset=utf-8', b'not found')

    def log_message(self, fmt, *args):
        pass  # 公网入口不落访问日志，避免泄露访问者信息


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Phase 4 public page server (single file)')
    parser.add_argument('--host', default='127.0.0.1', help='bind host (keep loopback for funnel)')
    parser.add_argument('--port', type=int, default=8090, help='bind port')
    args = parser.parse_args()
    print(f'phase4 page server on http://{args.host}:{args.port} (page: {PAGE})')
    ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()
