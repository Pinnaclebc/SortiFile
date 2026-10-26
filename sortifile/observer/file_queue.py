"""File Processing Queue and OS Lock Detection.
Module 1B: Thread-safe queue to prevent OS lock conflicts.
Module 1C: Check if a file is locked by OS (e.g. downloading/copying), retry mechanism.
"""

from __future__ import annotations

import os
import queue
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, Dict, Optional

from sortifile.observer.classifier import classify_file, is_temporary_or_partial_file


@dataclass
class QueuedFile:
    file_path: Path
    category: str = "Unknown"
    status: str = "queued"  # queued, checking_lock, reading, routing, moving, completed, failed
    error_message: Optional[str] = None
    created_at: datetime = field(default_factory=datetime.now)
    updated_at: datetime = field(default_factory=datetime.now)
    metadata: Dict[str, Any] = field(default_factory=dict)
    routing_result: Dict[str, Any] = field(default_factory=dict)
    destination_path: Optional[Path] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "file_name": self.file_path.name,
            "file_path": str(self.file_path),
            "category": self.category,
            "status": self.status,
            "error_message": self.error_message,
            "created_at": self.created_at.isoformat(),
            "updated_at": self.updated_at.isoformat(),
            "metadata": self.metadata,
            "routing_result": self.routing_result,
            "destination_path": str(self.destination_path) if self.destination_path else None,
        }


def is_file_locked(file_path: Path | str) -> bool:
    """Checks if a file is currently locked by the OS or another process.
    Attempts to acquire exclusive read/write access to test lock status.
    """
    path = Path(file_path)
    if not path.exists():
        return True

    # Check for temporary download extensions
    if is_temporary_or_partial_file(path):
        return True

    try:
        # On Windows, opening with 'r+b' or testing rename/exclusive open tests lock
        # Testing opening in append or read mode with write sharing
        if path.stat().st_size == 0:
            # Empty file might be freshly created, check read
            with open(path, "rb"):
                pass
            return False

        with open(path, "r+b"):
            pass
        return False
    except (PermissionError, OSError):
        return True


def wait_until_unlocked(
    file_path: Path | str,
    max_retries: int = 5,
    delay_sec: float = 1.0,
    progress_callback: Optional[Callable[[int, int], None]] = None
) -> bool:
    """Polls the file until it is unlocked and its size is stable, or timeout.
    Returns True if file is unlocked and ready for processing.
    """
    path = Path(file_path)
    if not path.exists():
        return False

    prev_size = -1
    for attempt in range(1, max_retries + 1):
        if progress_callback:
            progress_callback(attempt, max_retries)

        if not is_file_locked(path):
            try:
                curr_size = path.stat().st_size
                # If size hasn't changed between checks (or on first check if non-zero), consider ready
                if prev_size != -1 and curr_size == prev_size:
                    return True
                prev_size = curr_size
            except OSError:
                pass

        time.sleep(delay_sec)

    # Final check
    return not is_file_locked(path)


class FileProcessingQueue:
    """Thread-safe queue with priority handling and listener notifications."""

    def __init__(self):
        self._queue: queue.Queue[QueuedFile] = queue.Queue()
        self._active_files: Dict[str, QueuedFile] = {}

    def put(self, file_path: Path | str) -> Optional[QueuedFile]:
        """Validates and enqueues a file for processing."""
        path = Path(file_path).resolve()
        if not path.exists() or not path.is_file():
            return None

        # Ignore temporary partial files immediately
        if is_temporary_or_partial_file(path):
            return None

        # Avoid duplicate queuing if already pending
        if str(path) in self._active_files:
            existing = self._active_files[str(path)]
            if existing.status in ("queued", "checking_lock", "reading", "routing", "moving"):
                return existing

        category = classify_file(path)
        item = QueuedFile(file_path=path, category=category)
        self._active_files[str(path)] = item
        self._queue.put(item)
        return item

    def get(self, block: bool = True, timeout: Optional[float] = None) -> QueuedFile:
        return self._queue.get(block=block, timeout=timeout)

    def task_done(self) -> None:
        self._queue.task_done()

    def qsize(self) -> int:
        return self._queue.qsize()

    def get_active_items(self) -> list[QueuedFile]:
        return list(self._active_files.values())

    def update_status(
        self,
        item: QueuedFile,
        status: str,
        error_message: Optional[str] = None,
        destination_path: Optional[Path] = None,
        routing_result: Optional[Dict[str, Any]] = None,
    ) -> None:
        item.status = status
        item.updated_at = datetime.now()
        if error_message:
            item.error_message = error_message
        if destination_path:
            item.destination_path = destination_path
        if routing_result:
            item.routing_result = routing_result

