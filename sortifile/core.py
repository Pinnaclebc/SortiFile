"""SortiFile Core Controller and Asynchronous Pipeline Coordinator.
Orchestrates Observer -> Reader -> Brain -> Executor and event broadcasting.
"""

from __future__ import annotations

import asyncio
import logging
import threading
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Set

from sortifile.brain.context_updater import auto_update_folder_context
from sortifile.brain.router import LLMRouter
from sortifile.config import config
from sortifile.executor.history import HistoryManager
from sortifile.executor.mover import SafeFileMover
from sortifile.executor.undo import UndoEngine
from sortifile.observer.classifier import classify_file
from sortifile.observer.file_queue import FileProcessingQueue, QueuedFile, wait_until_unlocked
from sortifile.observer.watcher import DirectoryWatcher
from sortifile.reader.context_manager import (
    get_available_folders,
    initialize_workspace,
    load_folder_contexts,
)
from sortifile.reader.extractor import read_file_summary

logger = logging.getLogger("sortifile.core")


class EventBroadcaster:
    """Manages asynchronous WebSocket subscriber notifications."""

    def __init__(self):
        self._subscribers: Set[asyncio.Queue] = set()
        self._loop: Optional[asyncio.AbstractEventLoop] = None

    def set_loop(self, loop: asyncio.AbstractEventLoop) -> None:
        self._loop = loop

    def register(self, queue: asyncio.Queue) -> None:
        self._subscribers.add(queue)

    def unregister(self, queue: asyncio.Queue) -> None:
        self._subscribers.discard(queue)

    def broadcast(self, event_type: str, data: Dict[str, Any]) -> None:
        """Broadcasts an event message payload to all active WebSockets."""
        if not self._subscribers:
            return

        payload = {
            "type": event_type,
            "data": data,
            "timestamp": datetime.now().isoformat()
        }

        if self._loop and self._loop.is_running():
            try:
                current_loop = None
                try:
                    current_loop = asyncio.get_running_loop()
                except RuntimeError:
                    pass

                if current_loop == self._loop:
                    self._loop.create_task(self._async_broadcast(payload))
                else:
                    asyncio.run_coroutine_threadsafe(self._async_broadcast(payload), self._loop)
            except Exception as e:
                logger.debug(f"Broadcast dispatch error: {e}")

    async def _async_broadcast(self, payload: Dict[str, Any]) -> None:
        for q in list(self._subscribers):
            try:
                await q.put(payload)
            except Exception:
                pass


class SortiFileEngine:
    """Master controller coordinating monitoring, analysis, routing, and UI events."""

    def __init__(self):
        # Initialize folders & workspace
        self.workspace_dir = config.workspace_dir
        self.watch_dir = config.watch_dir
        initialize_workspace(self.workspace_dir, excluded=config.get("excluded_folders", []))
        self.watch_dir.mkdir(parents=True, exist_ok=True)

        # Core subsystems
        self.broadcaster = EventBroadcaster()
        self.queue = FileProcessingQueue()
        self.history = HistoryManager(config.history_file)
        self.mover = SafeFileMover(self.history)
        self.undo_engine = UndoEngine(self.history)
        self.router = LLMRouter(
            ollama_host=config.ollama_host,
            model_name=config.ollama_model,
            confidence_threshold=config.confidence_threshold
        )
        self.watcher = DirectoryWatcher(self.queue, self.watch_dir)

        # Background worker thread
        self._worker_thread: Optional[threading.Thread] = None
        self._stop_event = threading.Event()
        self._thread_pool = ThreadPoolExecutor(max_workers=4)

        # Setup watcher callback
        self.watcher.set_event_callback(self._on_watcher_event)

    def _on_watcher_event(self, event_type: str, path: Path) -> None:
        logger.info(f"Directory event [{event_type}]: {path.name}")
        self.broadcaster.broadcast("file_detected", {
            "event": event_type,
            "file_name": path.name,
            "path": str(path)
        })

    def start(self) -> None:
        """Starts the watcher and worker processing thread."""
        logger.info("Starting SortiFile engine...")
        self._stop_event.clear()

        # Start directory watcher
        self.watcher.start()

        # Start queue worker
        if self._worker_thread is None or not self._worker_thread.is_alive():
            self._worker_thread = threading.Thread(
                target=self._process_queue_loop,
                daemon=True,
                name="SortiFile-Worker"
            )
            self._worker_thread.start()

    def stop(self) -> None:
        """Stops watcher and background workers."""
        logger.info("Stopping SortiFile engine...")
        self._stop_event.set()
        self.watcher.stop()

    def get_status(self) -> Dict[str, Any]:
        """Returns overall system status."""
        ollama_status = self.router.check_ollama_status()
        return {
            "watcher_running": self.watcher.is_running(),
            "watch_dir": str(self.watch_dir),
            "workspace_dir": str(self.workspace_dir),
            "queue_size": self.queue.qsize(),
            "ollama": ollama_status,
            "confidence_threshold": self.router.confidence_threshold,
            "current_model": self.router.model_name,
            "available_folders": get_available_folders(
                self.workspace_dir, config.get("excluded_folders", [])
            ),
        }

    def _process_queue_loop(self) -> None:
        """Continuously pulls files from queue and runs pipeline."""
        while not self._stop_event.is_set():
            try:
                # 0.5s timeout allows checking stop_event
                item = self.queue.get(block=True, timeout=0.5)
            except Exception:
                continue

            try:
                self._execute_file_pipeline(item)
            except Exception as e:
                logger.error(f"Unexpected error processing {item.file_path.name}: {e}", exc_info=True)
                self.queue.update_status(item, "failed", error_message=str(e))
                self.broadcaster.broadcast("file_status", item.to_dict())
            finally:
                self.queue.task_done()

    def _execute_file_pipeline(self, item: QueuedFile) -> None:
        """Executes full sorting pipeline on a single queued file."""
        file_path = item.file_path
        file_name = file_path.name

        # 1. OS Lock check
        self.queue.update_status(item, "checking_lock")
        self.broadcaster.broadcast("file_status", item.to_dict())

        unlocked = wait_until_unlocked(
            file_path,
            max_retries=config.get("lock_check_retries", 5),
            delay_sec=config.get("lock_check_delay_sec", 1.0)
        )

        if not unlocked:
            err = f"File is locked by OS or still writing: {file_name}"
            self.queue.update_status(item, "failed", error_message=err)
            self.broadcaster.broadcast("file_status", item.to_dict())
            return

        # 2. Reading metadata & text extraction
        self.queue.update_status(item, "reading")
        self.broadcaster.broadcast("file_status", item.to_dict())

        try:
            summary = read_file_summary(file_path, max_words=config.get("max_words_extract", 200))
            metadata = summary["metadata"]
            snippet = summary["snippet"]
            item.metadata = metadata
        except Exception as e:
            err = f"Failed to extract metadata/text: {e}"
            self.queue.update_status(item, "failed", error_message=err)
            self.broadcaster.broadcast("file_status", item.to_dict())
            return

        # 3. Brain: LLM Routing
        self.queue.update_status(item, "routing")
        self.broadcaster.broadcast("file_status", item.to_dict())

        folder_contexts = load_folder_contexts(
            self.workspace_dir,
            excluded_folders=config.get("excluded_folders", [])
        )

        routing_result = self.router.route_file(
            metadata=metadata,
            snippet=snippet,
            folder_contexts=folder_contexts,
            category=item.category
        )
        item.routing_result = routing_result

        # 4. Executor: Safe Move
        self.queue.update_status(item, "moving")
        self.broadcaster.broadcast("file_status", item.to_dict())

        target_folder_name = routing_result["target_folder"]
        target_dir = self.workspace_dir / target_folder_name

        success, dest_path, err = self.mover.move_file(
            source_path=file_path,
            target_dir=target_dir,
            category=item.category,
            routing_result=routing_result,
            metadata=metadata
        )

        if success and dest_path:
            self.queue.update_status(item, "completed", destination_path=dest_path)
            self.broadcaster.broadcast("file_status", item.to_dict())
            self.broadcaster.broadcast("file_sorted", {
                "file_name": file_name,
                "target_folder": target_folder_name,
                "confidence_score": routing_result["confidence_score"],
                "reasoning": routing_result["reasoning"],
                "destination_path": str(dest_path),
                "timestamp": datetime.now().isoformat()
            })

            # 5. Background folder context auto-update
            self._thread_pool.submit(auto_update_folder_context, target_dir)
        else:
            self.queue.update_status(item, "failed", error_message=err or "Move failed")
            self.broadcaster.broadcast("file_status", item.to_dict())

    def process_direct_file(self, file_path: Path | str) -> Optional[QueuedFile]:
        """Manually submits a file to the processing queue (e.g. from UI drop zone)."""
        path = Path(file_path).resolve()
        if not path.exists():
            return None
        return self.queue.put(path)

    def scan_watch_directory(self) -> int:
        """Manually triggers a scan of all files in watch folder."""
        return self.watcher.scan_existing_files()


# Global engine singleton
engine = SortiFileEngine()
