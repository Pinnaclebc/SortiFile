"""Standalone Desktop GUI entrypoint for SortiFile."""

import sys
from pathlib import Path

# Add project root to sys.path
PROJECT_ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(PROJECT_ROOT))

from sortifile.gui import main

if __name__ == "__main__":
    main()

