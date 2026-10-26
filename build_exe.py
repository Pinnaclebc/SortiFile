"""Script to build SortiFile into a standalone Windows .exe using PyInstaller."""

import subprocess
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent

cmd = [
    sys.executable,
    "-m",
    "PyInstaller",
    "--noconfirm",
    "--onedir",
    "--windowed",
    "--name", "SortiFile",
    "--add-data", f"{PROJECT_ROOT / 'sortifile' / 'server' / 'static'};sortifile/server/static",
    "--hidden-import", "uvicorn.logging",
    "--hidden-import", "uvicorn.loops",
    "--hidden-import", "uvicorn.loops.auto",
    "--hidden-import", "uvicorn.protocols",
    "--hidden-import", "uvicorn.protocols.http",
    "--hidden-import", "uvicorn.protocols.http.auto",
    "--hidden-import", "uvicorn.protocols.websockets",
    "--hidden-import", "uvicorn.protocols.websockets.auto",
    "--hidden-import", "uvicorn.lifespans",
    "--hidden-import", "uvicorn.lifespans.on",
    "--hidden-import", "watchdog.observers.winapi",
    "--hidden-import", "clr_loader",
    "--hidden-import", "pythonnet",
    "--hidden-import", "webview.platforms.winforms",
    str(PROJECT_ROOT / "app_desktop.py")
]

print("Building SortiFile executable...")
print(" ".join(cmd))
res = subprocess.run(cmd, cwd=str(PROJECT_ROOT))
if res.returncode == 0:
    print("\nSUCCESS! Executable created at dist/SortiFile/SortiFile.exe")
else:
    print(f"\nBuild failed with exit code {res.returncode}")
    sys.exit(res.returncode)

