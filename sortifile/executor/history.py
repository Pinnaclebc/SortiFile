"""Sort history manager.
Module 4B: Append every action to local sort_history.json, provide history lookup.
"""

from __future__ import annotations

import json
import logging
import threading
import uuid
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional

from sortifile.config import config

logger = logging.getLogger("sortifile.history")


class HistoryManager:
    """Thread-safe manager for reading and writing sort_history.json."""

    def __init__(self, history_file: Optional[Path | str] = None):
        self.history_file = Path(history_file or config.history_file)
        self._lock = threading.Lock()
        self._ensure_file_exists()

    def _ensure_file_exists(self) -> None:
        with self._lock:
            if not self.history_file.exists():
                try:
                    self.history_file.parent.mkdir(parents=True, exist_ok=True)
                    with open(self.history_file, "w", encoding="utf-8") as f:
                        json.dump([], f, indent=2)
                except Exception as e:
                    logger.error(f"Failed to create history file: {e}")

    def load_history(self) -> List[Dict[str, Any]]:
        with self._lock:
            if not self.history_file.exists():
                return []
            try:
                with open(self.history_file, "r", encoding="utf-8") as f:
                    return json.load(f)
            except Exception as e:
                logger.error(f"Failed to load history: {e}")
                return []

    def record_move(
        self,
        original_path: Path | str,
        destination_path: Path | str,
        category: str,
        routing_result: Dict[str, Any],
        metadata: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        """Module 4B: Appends a move action record to sort_history.json."""
        orig = Path(original_path)
        dest = Path(destination_path)

        record: Dict[str, Any] = {
            "id": str(uuid.uuid4()),
            "timestamp": datetime.now().isoformat(),
            "file_name": orig.name,
            "original_path": str(orig.resolve()),
            "destination_path": str(dest.resolve()),
            "target_folder": dest.parent.name,
            "category": category,
            "confidence_score": routing_result.get("confidence_score", 0),
            "reasoning": routing_result.get("reasoning", ""),
            "engine": routing_result.get("engine", "unknown"),
            "model": routing_result.get("model", ""),
            "status": "moved",  # moved | reverted | failed
            "metadata": metadata or {},
        }

        with self._lock:
            history = []
            if self.history_file.exists():
                try:
                    with open(self.history_file, "r", encoding="utf-8") as f:
                        history = json.load(f)
                except Exception:
                    history = []

            # Prepend newest record
            history.insert(0, record)

            try:
                with open(self.history_file, "w", encoding="utf-8") as f:
                    json.dump(history, f, indent=2)
            except Exception as e:
                logger.error(f"Failed to write history entry: {e}")

        logger.info(f"[History] Recorded move: {orig.name} -> {dest}")
        return record

    def get_last_action(self, filter_moved_only: bool = True) -> Optional[Dict[str, Any]]:
        """Returns the most recent move action eligible for undo."""
        history = self.load_history()
        for item in history:
            if not filter_moved_only or item.get("status") == "moved":
                return item
        return None

    def mark_reverted(self, action_id: str) -> bool:
        """Updates the status of a specific entry to 'reverted'."""
        with self._lock:
            history = []
            if self.history_file.exists():
                try:
                    with open(self.history_file, "r", encoding="utf-8") as f:
                        history = json.load(f)
                except Exception:
                    return False

            updated = False
            for entry in history:
                if entry.get("id") == action_id:
                    entry["status"] = "reverted"
                    entry["reverted_at"] = datetime.now().isoformat()
                    updated = True
                    break

            if updated:
                try:
                    with open(self.history_file, "w", encoding="utf-8") as f:
                        json.dump(history, f, indent=2)
                    return True
                except Exception as e:
                    logger.error(f"Failed to update history: {e}")

        return False

