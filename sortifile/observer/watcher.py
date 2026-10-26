"""Directory event monitor using watchdog.
Module 1A: Implement watchdog to monitor a specific target directory.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Callable, Optional

from watchdog.events import FileSystemEvent, FileSystemEventHandler
from watchdog.observers import Observer

from sortifile.observer.file_queue import FileProcessingQueue

logger = logging.getLogger("sortifile.watcher")


class WatchDirectoryHandler(FileSystemEventHandler):
    """Handles file system events in the target watch directory."""

    def __init__(self, queue: FileProcessingQueue, on_event_callback: Optional[Callable[[str, Path], None]] = None):
        super().__init__()
        self.queue = queue
        self.on_event_callback = on_event_callback

    def on_created(self, event: FileSystemEvent) -> None:
        if event.is_directory:
            return
        path = Path(event.src_path)
        logger.info(f"[Watcher] File created: {path.name}")
        if self.on_event_callback:
            self.on_event_callback("created", path)
        self.queue.put(path)

    def on_moved(self, event: FileSystemEvent) -> None:
        if event.is_directory:
            return
        dest_path = Path(event.dest_path)
        logger.info(f"[Watcher] File moved/renamed to: {dest_path.name}")
        if self.on_event_callback:
            self.on_event_callback("moved", dest_path)
        self.queue.put(dest_path)


class DirectoryWatcher:
    """Manages the watchdog Observer lifecycle."""

    def __init__(self, queue: FileProcessingQueue, watch_dir: Path | str):
        self.queue = queue
        self.watch_dir = Path(watch_dir)
        self.observer: Optional[Observer] = None
        self._is_running = False
        self._event_callback: Optional[Callable[[str, Path], None]] = None

    def set_event_callback(self, callback: Callable[[str, Path], None]) -> None:
        self._event_callback = callback

    def start(self) -> bool:
        """Starts monitoring the target directory."""
        if self._is_running:
            return True

        self.watch_dir.mkdir(parents=True, exist_ok=True)
        handler = WatchDirectoryHandler(self.queue, self._event_callback)
        self.observer = Observer()
        self.observer.schedule(handler, str(self.watch_dir), recursive=False)
        self.observer.start()
        self._is_running = True
        logger.info(f"[Watcher] Started observing directory: {self.watch_dir}")
        return True

    def stop(self) -> None:
        """Stops the directory observer."""
        if self.observer and self._is_running:
            self.observer.stop()
            self.observer.join(timeout=2.0)
            self._is_running = False
            logger.info("[Watcher] Stopped observing directory.")

    def is_running(self) -> bool:
        return self._is_running and self.observer is not None and self.observer.is_alive()

    def update_watch_dir(self, new_dir: Path | str) -> None:
        """Updates the directory being watched."""
        was_running = self.is_running()
        if was_running:
            self.stop()
        self.watch_dir = Path(new_dir)
        if was_running:
            self.start()

    def scan_existing_files(self) -> int:
        """Enqueues any existing files currently in the watch directory."""
        if not self.watch_dir.exists():
            return 0

        count = 0
        for entry in self.watch_dir.iterdir():
            if entry.is_file():
                if self.queue.put(entry):
                    count += 1
        return count

