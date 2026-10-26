"""SortiFile Native Desktop Application Entrypoint.
Runs the SortiFile FastAPI backend in a background thread and opens a native
Edge WebView2 desktop window via pywebview.
"""

from __future__ import annotations

import os
import socket
import sys
import threading
import time
from pathlib import Path

# Add project root to sys.path
PROJECT_ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(PROJECT_ROOT))

import uvicorn
from sortifile.config import config
from sortifile.server.app import app


def find_free_port(default_port: int = 8000) -> int:
    """Checks if default_port is free, otherwise finds an available port."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        if s.connect_ex(("127.0.0.1", default_port)) != 0:
            return default_port

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def run_server(host: str, port: int):
    """Runs uvicorn server in a background thread."""
    uvicorn.run(app, host=host, port=port, log_level="warning")


def main():
    host = "127.0.0.1"
    port = find_free_port(8000)

    # Start FastAPI server in background daemon thread
    server_thread = threading.Thread(
        target=run_server,
        args=(host, port),
        daemon=True,
        name="SortiFile-ServerThread"
    )
    server_thread.start()

    # Wait briefly for server startup
    time.sleep(0.8)

    url = f"http://{host}:{port}"
    print(f"[SortiFile Desktop] Backend running at {url}")

    # Check if webview is available
    try:
        import webview
        print("[SortiFile Desktop] Launching native window...")
        window = webview.create_window(
            title="SortiFile Agent",
            url=url,
            width=1280,
            height=850,
            min_size=(960, 640),
            background_color="#f8f9fa",
        )
        webview.start(debug=False)
    except Exception as e:
        print(f"[SortiFile Desktop] PyWebView window launch notice: {e}")
        print(f"[SortiFile Desktop] Opening default browser at {url}...")
        import webbrowser
        webbrowser.open(url)
        # Keep running
        try:
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            print("[SortiFile] Exiting...")


if __name__ == "__main__":
    main()

