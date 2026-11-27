"""Serve published RecallDeck assets on loopback only; no development browser is opened."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import sys
import tempfile
from urllib.parse import urlsplit


def asset_path(root, request_path):
    path = urlsplit(request_path).path
    # SimpleHTTPRequestHandler still normalizes/contains the final filesystem path.
    return "/index.html" if not Path(path).suffix and not (root / path.lstrip("/")).is_file() else request_path


class PublishedHandler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, ".wasm": "application/wasm", ".webmanifest": "application/manifest+json"}

    def do_GET(self):
        self.path = asset_path(Path(self.directory), self.path)
        super().do_GET()

    def do_HEAD(self):
        self.path = asset_path(Path(self.directory), self.path)
        super().do_HEAD()

    def end_headers(self):
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()


def self_test():
    with tempfile.TemporaryDirectory(prefix="RecallDeck-server-check-") as directory:
        root = Path(directory)
        (root / "index.html").write_text("fixture", encoding="utf-8")
        assert asset_path(root, "/decks/123?test=1") == "/index.html"
        assert asset_path(root, "/missing.js") == "/missing.js"
        assert asset_path(root, "/index.html") == "/index.html"
        assert PublishedHandler.extensions_map[".wasm"] == "application/wasm"
    print("PASS: SPA fallback preserves missing asset errors and WebAssembly MIME")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path)
    parser.add_argument("--port", type=int, default=5189)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if args.root is None or not (args.root.resolve() / "index.html").is_file():
        parser.error("root must contain the published index.html")
    if not 1 <= args.port <= 65535:
        parser.error("port must be between 1 and 65535")
    root = args.root.resolve()
    server = ThreadingHTTPServer(("127.0.0.1", args.port), partial(PublishedHandler, directory=str(root)))
    print(f"Serving {root} at http://127.0.0.1:{args.port}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
