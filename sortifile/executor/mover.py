"""Safe file mover and transfer execution.
Module 4A: Use shutil.move() to transfer the file to the AI-determined target folder.
"""

from __future__ import annotations

import logging
import shutil
from pathlib import Path
from typing import Any, Dict, Optional, Tuple

from sortifile.executor.history import HistoryManager

logger = logging.getLogger("sortifile.mover")


def get_unique_destination_path(target_dir: Path, filename: str) -> Path:
    """Generates a non-colliding destination path if filename already exists."""
    dest = target_dir / filename
    if not dest.exists():
        return dest

    stem = Path(filename).stem
    suffix = Path(filename).suffix
    counter = 1

    while True:
        new_name = f"{stem} ({counter}){suffix}"
        candidate = target_dir / new_name
        if not candidate.exists():
            return candidate
        counter += 1


class SafeFileMover:
    """Executes safe moves with logging and collision avoidance."""

    def __init__(self, history_manager: Optional[HistoryManager] = None):
        self.history = history_manager or HistoryManager()

    def move_file(
        self,
        source_path: Path | str,
        target_dir: Path | str,
        category: str,
        routing_result: Dict[str, Any],
        metadata: Optional[Dict[str, Any]] = None
    ) -> Tuple[bool, Optional[Path], Optional[str]]:
        """Module 4A: Transfers the file using shutil.move() and appends to sort_history.json."""
        src = Path(source_path)
        dest_dir = Path(target_dir)

        if not src.exists() or not src.is_file():
            return False, None, f"Source file does not exist: {src}"

        try:
            dest_dir.mkdir(parents=True, exist_ok=True)
            dest_path = get_unique_destination_path(dest_dir, src.name)

            # Perform the atomic file move
            shutil.move(str(src), str(dest_path))

            # Record in history
            self.history.record_move(
                original_path=src,
                destination_path=dest_path,
                category=category,
                routing_result=routing_result,
                metadata=metadata
            )

            logger.info(f"Successfully moved '{src.name}' -> '{dest_path}'")
            return True, dest_path, None

        except Exception as e:
            err = f"Move failed for '{src.name}': {e}"
            logger.error(err)
            return False, None, err

