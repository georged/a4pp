from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import sys

class Handler(BaseHTTPRequestHandler):
    def _dump_request(self):
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length) if length else b""

        print("\n" + "=" * 80)
        print(f"{self.command} {self.path} {self.request_version}")
        print(f"Client: {self.client_address[0]}:{self.client_address[1]}")
        print("- Headers -")
        for k, v in self.headers.items():
            print(f"{k}: {v}")
        print("- Body -")
        try:
            print(body.decode("utf-8"))
        except UnicodeDecodeError:
            print(body)
        print("=" * 80 + "\n")

    def _ok(self):
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.end_headers()
        self.wfile.write(b"ok\n")

    def do_GET(self): self._dump_request(); self._ok()
    def do_POST(self): self._dump_request(); self._ok()
    def do_PUT(self): self._dump_request(); self._ok()
    def do_PATCH(self): self._dump_request(); self._ok()
    def do_DELETE(self): self._dump_request(); self._ok()

if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    server = ThreadingHTTPServer(("0.0.0.0", port), Handler)
    print(f"Listening on http://localhost:{port}")
    server.serve_forever()