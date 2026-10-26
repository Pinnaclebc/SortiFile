#!/usr/bin/env python3
"""SortiFile Undo CLI Utility.
Module 4C: Build an Undo script that reads the last log entry and reverses the move.
"""

import argparse
import sys
from pathlib import Path

# Configure stdout utf-8 if supported
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

# Add project root to sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent))

from sortifile.executor.undo import UndoEngine
from sortifile.executor.history import HistoryManager


def main():
    parser = argparse.ArgumentParser(
        description="SortiFile Undo Utility - Reverses previous file sorting actions."
    )
    parser.add_argument(
        "--id",
        type=str,
        default=None,
        help="Specific action ID to undo. If omitted, undoes the most recent move."
    )
    parser.add_argument(
        "--list",
        action="store_true",
        help="List recent sort history entries."
    )

    args = parser.parse_args()
    history = HistoryManager()
    engine = UndoEngine(history)

    if args.list:
        records = history.load_history()
        if not records:
            print("No sort history found.")
            return

        print("\n=== Recent SortiFile Actions ===")
        for r in records[:10]:
            status_icon = "[MOVED]" if r.get("status") == "moved" else "[UNDONE]"
            print(f"{status_icon} ID: {r.get('id')[:8]}... | {r.get('file_name')} -> {r.get('target_folder')} "
                  f"({r.get('confidence_score')}% conf)")
        print("================================")
        return

    if args.id:
        print(f"Attempting to revert action ID: {args.id}")
        success, message, record = engine.undo_action_by_id(args.id)
    else:
        print("Attempting to revert the most recent move action...")
        success, message, record = engine.undo_last_action()

    if success:
        print(f"SUCCESS: {message}")
        if record:
            print(f"File: {record.get('file_name')}")
            print(f"From: {record.get('destination_path')}")
            print(f"Back to: {record.get('original_path')}")
    else:
        print(f"FAILED: {message}")
        sys.exit(1)


if __name__ == "__main__":
    main()
