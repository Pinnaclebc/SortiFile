"""SortiFile Main Entrypoint.
Coordinates watchdog monitor, LLM routing pipeline, and FastAPI dashboard.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

# Ensure root package is in sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import uvicorn
from sortifile.config import config
from sortifile.server.app import app


def parse_arguments():
    parser = argparse.ArgumentParser(description="SortiFile - Autonomous Local File Sorting Agent")
    parser.add_argument("--host", type=str, default="127.0.0.1", help="Server host IP (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8000, help="Server port (default: 8000)")
    parser.add_argument("--watch-dir", type=str, default=None, help="Custom directory to watch for incoming files")
    parser.add_argument("--workspace-dir", type=str, default=None, help="Custom workspace destination directory")
    parser.add_argument("--reload", action="store_true", help="Enable uvicorn auto-reload")
    return parser.parse_args()


def main():
    args = parse_arguments()

    if args.watch_dir:
        config.set("watch_dir", str(Path(args.watch_dir).resolve()))

    if args.workspace_dir:
        config.set("workspace_dir", str(Path(args.workspace_dir).resolve()))

    print(f"""
===================================================================
  SortiFile Agent - Autonomous Local File Sorting System
===================================================================
  Dashboard URL : http://{args.host}:{args.port}
  Watch Folder  : {config.watch_dir}
  Workspace     : {config.workspace_dir}
  Ollama Host   : {config.ollama_host} (Model: {config.ollama_model})
===================================================================
""")

    uvicorn.run(
        app,
        host=args.host,
        port=args.port,
        reload=args.reload,
        log_level="info"
    )


if __name__ == "__main__":
    main()

