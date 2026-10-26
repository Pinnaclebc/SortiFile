"""Compiles SortiFile into a single standalone SortiFile.exe directly in project root."""

import os
import shutil
import subprocess
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent

cmd = [
    sys.executable,
    "-m",
    "PyInstaller",
    "--noconfirm",
    "--onefile",
    "--windowed",
    "--name", "SortiFile",
    "--distpath", str(PROJECT_ROOT),
    "--collect-all", "customtkinter",
    "--hidden-import", "watchdog.observers.winapi",
    "--hidden-import", "PIL",
    "--hidden-import", "PIL.Image",
    "--hidden-import", "pypdf",
    "--hidden-import", "docx",
    str(PROJECT_ROOT / "main_desktop.py")
]

print("Building standalone SortiFile.exe in project root...")
print(" ".join(cmd))
res = subprocess.run(cmd, cwd=str(PROJECT_ROOT))
if res.returncode == 0:
    target_exe = PROJECT_ROOT / "SortiFile.exe"
    print(f"\nSUCCESS! Executable is ready at: {target_exe}")
    print(f"File size: {target_exe.stat().st_size / (1024*1024):.2f} MB")
else:
    print(f"\nBuild failed with exit code {res.returncode}")
    sys.exit(res.returncode)

