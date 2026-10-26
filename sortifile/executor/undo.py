"""Undo execution engine.
Module 4C: Build an Undo script that reads the last log entry and reverses the move.
"""

from __future__ import annotations

import logging
import shutil
from pathlib import Path
from typing import Any, Dict, Optional, Tuple

from sortifile.executor.history import HistoryManager
from sortifile.executor.mover import get_unique_destination_path

logger = logging.getLogger("sortifile.undo")


class UndoEngine:
    """Handles reversing file move actions recorded in sort_history.json."""

    def __init__(self, history_manager: Optional[HistoryManager] = None):
        self.history = history_manager or HistoryManager()

    def undo_last_action(self) -> Tuple[bool, str, Optional[Dict[str, Any]]]:
        """Reads the most recent move action and reverses the file move."""
        last_action = self.history.get_last_action(filter_moved_only=True)
        if not last_action:
            return False, "No active move action found to undo.", None

        return self.undo_action_by_id(last_action["id"])

    def undo_action_by_id(self, action_id: str) -> Tuple[bool, str, Optional[Dict[str, Any]]]:
        """Reverses a specific move action by its ID."""
        history = self.history.load_history()
        target_entry = None
        for item in history:
            if item.get("id") == action_id:
                target_entry = item
                break

        if not target_entry:
            return False, f"Action ID '{action_id}' not found in history.", None

        if target_entry.get("status") == "reverted":
            return False, f"Action '{action_id}' has already been undone.", target_entry

        current_path = Path(target_entry["destination_path"])
        orig_path = Path(target_entry["original_path"])

        if not current_path.exists():
            return False, f"File no longer exists at destination: {current_path}", target_entry

        try:
            # Ensure original directory exists
            orig_path.parent.mkdir(parents=True, exist_ok=True)

            # Avoid overwriting if a file was created at the original path
            final_orig_path = get_unique_destination_path(orig_path.parent, orig_path.name)

            shutil.move(str(current_path), str(final_orig_path))
            self.history.mark_reverted(action_id)

            msg = f"Successfully restored '{current_path.name}' back to '{final_orig_path.parent}'."
            logger.info(msg)
            return True, msg, target_entry

        except Exception as e:
            err = f"Failed to revert action '{action_id}': {e}"
            logger.error(err)
            return False, err, target_entry

