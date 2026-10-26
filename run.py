#!/usr/bin/env python3
"""Runner script for SortiFile."""

import sys
from pathlib import Path

# Add project root to sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent))

from sortifile.main import main

if __name__ == "__main__":
    main()

