"""FastAPI Web Server and WebSocket Broadcaster.
Module 5A: Lightweight local server bridging Python queues to frontend via WebSockets.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import shutil
import subprocess
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any, Dict, List, Optional

from fastapi import FastAPI, File, HTTPException, UploadFile, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

from sortifile.config import config
from sortifile.core import engine
from sortifile.reader.context_manager import (
    get_available_folders,
    load_folder_contexts,
    read_folder_context,
    write_folder_context,
)

logger = logging.getLogger("sortifile.server")


def open_in_file_explorer(target_path: Path | str) -> bool:
    """Opens the directory in Windows File Explorer or native OS file manager."""
    path = Path(target_path).resolve()
    if not path.exists():
        return False
    try:
        if os.name == "nt":
            os.startfile(str(path))
        else:
            subprocess.Popen(["xdg-open", str(path)])
        return True
    except Exception as e:
        logger.error(f"Error opening file explorer for {path}: {e}")
        return False


# Base paths (supports PyInstaller frozen execution)
import sys
if getattr(sys, "frozen", False) and hasattr(sys, "_MEIPASS"):
    SERVER_DIR = Path(sys._MEIPASS) / "sortifile" / "server"
else:
    SERVER_DIR = Path(__file__).resolve().parent
STATIC_DIR = SERVER_DIR / "static"


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Starts the engine and binds the async loop to event broadcaster."""
    loop = asyncio.get_running_loop()
    engine.broadcaster.set_loop(loop)
    engine.start()
    logger.info("SortiFile server started successfully.")
    yield
    engine.stop()
    logger.info("SortiFile server stopped.")


app = FastAPI(title="SortiFile Dashboard", version="0.1.0", lifespan=lifespan)

# Enable CORS for local development
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


# Pydantic models for request bodies
class SettingsUpdate(BaseModel):
    watch_dir: Optional[str] = None
    workspace_dir: Optional[str] = None
    confidence_threshold: Optional[int] = None
    ollama_host: Optional[str] = None
    ollama_model: Optional[str] = None
    theme: Optional[Dict[str, str]] = None


class FolderContextUpdate(BaseModel):
    context: str


class CreateFolderRequest(BaseModel):
    name: str
    context: Optional[str] = ""


class UndoRequest(BaseModel):
    action_id: Optional[str] = None


class OpenPathRequest(BaseModel):
    path: str





# WebSocket for real-time live events
@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    subscriber_queue = asyncio.Queue()
    engine.broadcaster.register(subscriber_queue)

    # Send initial status and queue dump on connection
    try:
        await websocket.send_json({
            "type": "init",
            "data": {
                "status": engine.get_status(),
                "queue": [q.to_dict() for q in engine.queue.get_active_items()],
                "history": engine.history.load_history()[:25],
                "theme": config.theme,
            }
        })

        async def listen_to_broadcasts():
            while True:
                msg = await subscriber_queue.get()
                await websocket.send_json(msg)

        async def listen_to_client():
            while True:
                data = await websocket.receive_text()
                # Handle client ping or control messages
                try:
                    payload = json.loads(data)
                    action = payload.get("action")
                    if action == "ping":
                        await websocket.send_json({"type": "pong"})
                except Exception:
                    pass

        done, pending = await asyncio.wait(
            [
                asyncio.create_task(listen_to_broadcasts()),
                asyncio.create_task(listen_to_client())
            ],
            return_when=asyncio.FIRST_COMPLETED
        )
        for task in pending:
            task.cancel()

    except (WebSocketDisconnect, asyncio.CancelledError):
        pass
    finally:
        engine.broadcaster.unregister(subscriber_queue)


# API Routes
@app.get("/api/status")
def get_status():
    return engine.get_status()


@app.get("/api/queue")
def get_queue():
    return {
        "items": [item.to_dict() for item in engine.queue.get_active_items()],
        "queue_size": engine.queue.qsize(),
    }


@app.get("/api/history")
def get_history(limit: int = 50):
    return {
        "history": engine.history.load_history()[:limit]
    }


@app.post("/api/undo")
def undo_action(req: Optional[UndoRequest] = None):
    action_id = req.action_id if req else None
    if action_id:
        success, msg, entry = engine.undo_engine.undo_action_by_id(action_id)
    else:
        success, msg, entry = engine.undo_engine.undo_last_action()

    if not success:
        raise HTTPException(status_code=400, detail=msg)

    # Broadcast updated history
    engine.broadcaster.broadcast("history_updated", {"action": "undo", "entry": entry})
    return {"success": True, "message": msg, "entry": entry}


@app.get("/api/folders")
def get_folders():
    workspace = engine.workspace_dir
    contexts = load_folder_contexts(workspace, excluded_folders=config.get("excluded_folders", []))
    folder_list = []
    for name, ctx in contexts.items():
        folder_path = workspace / name
        file_count = 0
        if folder_path.exists():
            file_count = len([f for f in folder_path.iterdir() if f.is_file() and not f.name.startswith(".")])
        folder_list.append({
            "name": name,
            "path": str(folder_path.resolve()),
            "context": ctx,
            "file_count": file_count,
            "is_inbox": name.lower() == "inbox",
            "is_deletable": name.lower() not in ("inbox",)
        })
    return {"folders": folder_list}


@app.post("/api/folders/{name}/open")
def open_folder(name: str):
    folder_path = engine.workspace_dir / name
    if not folder_path.exists():
        raise HTTPException(status_code=404, detail=f"Folder '{name}' does not exist.")
    success = open_in_file_explorer(folder_path)
    if not success:
        raise HTTPException(status_code=500, detail=f"Failed to open explorer for '{folder_path}'.")
    return {"success": True, "path": str(folder_path.resolve())}


@app.delete("/api/folders/{name}")
def delete_folder(name: str, force: bool = False):
    if name.lower() in ("inbox",):
        raise HTTPException(status_code=400, detail="Cannot delete default Inbox folder.")

    folder_path = engine.workspace_dir / name
    if not folder_path.exists():
        raise HTTPException(status_code=404, detail=f"Folder '{name}' does not exist.")

    # Check files inside (ignoring hidden files like .context.md)
    files = [f for f in folder_path.iterdir() if f.is_file() and not f.name.startswith(".")]
    if files and not force:
        raise HTTPException(
            status_code=400,
            detail=f"Folder contains {len(files)} file(s). Set force=true to move remaining files to Inbox and delete."
        )

    # If force=true and files exist, safely relocate them to Inbox
    if files:
        inbox_dir = engine.workspace_dir / "Inbox"
        inbox_dir.mkdir(parents=True, exist_ok=True)
        from sortifile.executor.mover import get_unique_destination_path
        for f in files:
            try:
                dest = get_unique_destination_path(inbox_dir, f.name)
                shutil.move(str(f), str(dest))
            except Exception as e:
                logger.error(f"Failed to relocate {f.name} to Inbox: {e}")

    # Remove the directory
    try:
        shutil.rmtree(str(folder_path))
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to delete folder: {e}")

    engine.broadcaster.broadcast("folder_deleted", {"name": name})
    return {"success": True, "name": name, "message": f"Folder '{name}' deleted successfully."}


@app.post("/api/open-path")
def open_custom_path(req: OpenPathRequest):
    target = Path(req.path).resolve()
    if not target.exists():
        raise HTTPException(status_code=404, detail=f"Path '{target}' does not exist.")
    success = open_in_file_explorer(target)
    if not success:
        raise HTTPException(status_code=500, detail=f"Failed to open '{target}'.")
    return {"success": True, "path": str(target)}


@app.put("/api/folders/{name}")
def update_folder(name: str, body: FolderContextUpdate):
    folder_path = engine.workspace_dir / name
    if not folder_path.exists():
        raise HTTPException(status_code=404, detail=f"Folder {name} does not exist.")

    success = write_folder_context(folder_path, body.context)
    if not success:
        raise HTTPException(status_code=500, detail="Failed to save folder context.")

    return {"success": True, "name": name, "context": body.context}


@app.post("/api/folders")
def create_folder(req: CreateFolderRequest):
    folder_name = req.name.strip().replace("/", "_").replace("\\", "_")
    if not folder_name:
        raise HTTPException(status_code=400, detail="Folder name is required.")

    folder_path = engine.workspace_dir / folder_name
    folder_path.mkdir(parents=True, exist_ok=True)
    initial_context = req.context or f"Files and documents related to {folder_name}."
    write_folder_context(folder_path, initial_context)

    return {"success": True, "name": folder_name, "context": initial_context}


@app.post("/api/scan")
def trigger_scan():
    count = engine.scan_watch_directory()
    return {"success": True, "queued_files_count": count}


@app.post("/api/upload")
async def upload_files(file: UploadFile = File(...)):
    """Uploads a file directly into the watch directory to trigger sorting."""
    watch_dir = engine.watch_dir
    watch_dir.mkdir(parents=True, exist_ok=True)
    destination = watch_dir / file.filename

    with open(destination, "wb") as buffer:
        shutil.copyfileobj(file.file, buffer)

    # Let the watcher or direct queue pick it up
    queued = engine.process_direct_file(destination)
    return {
        "success": True,
        "filename": file.filename,
        "path": str(destination),
        "item": queued.to_dict() if queued else None
    }


@app.get("/api/settings")
def get_settings():
    return {
        "watch_dir": str(config.watch_dir),
        "workspace_dir": str(config.workspace_dir),
        "confidence_threshold": config.confidence_threshold,
        "ollama_host": config.ollama_host,
        "ollama_model": config.ollama_model,
        "alternative_models": config.get("alternative_models", []),
        "theme": config.theme,
    }


@app.post("/api/settings")
def update_settings(settings: SettingsUpdate):
    updates = {}
    if settings.watch_dir is not None:
        updates["watch_dir"] = settings.watch_dir
        engine.watcher.update_watch_dir(settings.watch_dir)
        engine.watch_dir = Path(settings.watch_dir)

    if settings.workspace_dir is not None:
        updates["workspace_dir"] = settings.workspace_dir
        engine.workspace_dir = Path(settings.workspace_dir)

    if settings.confidence_threshold is not None:
        updates["confidence_threshold"] = settings.confidence_threshold
        engine.router.confidence_threshold = settings.confidence_threshold

    if settings.ollama_host is not None:
        updates["ollama_host"] = settings.ollama_host
        engine.router.ollama_host = settings.ollama_host.rstrip("/")

    if settings.ollama_model is not None:
        updates["ollama_model"] = settings.ollama_model
        engine.router.model_name = settings.ollama_model

    if settings.theme is not None:
        current_theme = dict(config.theme)
        current_theme.update(settings.theme)
        updates["theme"] = current_theme

    config.update(updates)
    engine.broadcaster.broadcast("settings_updated", config.data)
    return {"success": True, "settings": config.data}


@app.get("/api/test-ollama")
def test_ollama():
    return engine.router.check_ollama_status()


# Mount static files & root page
if STATIC_DIR.exists():
    app.mount("/static", StaticFiles(directory=str(STATIC_DIR)), name="static")


@app.get("/", response_class=HTMLResponse)
def serve_index():
    index_file = STATIC_DIR / "index.html"
    if index_file.exists():
        return FileResponse(str(index_file))
    return HTMLResponse("<h1>SortiFile UI is loading...</h1>")
