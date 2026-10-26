"""SortiFile Configuration module.
Centralized settings for directory monitoring, LLM, routing thresholds, and UI theming.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any, Dict

# Base project root (supports PyInstaller frozen execution)
if getattr(sys, "frozen", False):
    PROJECT_ROOT = Path(sys.executable).resolve().parent
else:
    PROJECT_ROOT = Path(__file__).resolve().parent.parent

# Default configuration paths
DEFAULT_WORKSPACE_DIR = PROJECT_ROOT / "SortiFile_Workspace"
DEFAULT_WATCH_DIR = PROJECT_ROOT / "Watch_Folder"
DEFAULT_HISTORY_FILE = PROJECT_ROOT / "sort_history.json"
DEFAULT_CONFIG_FILE = PROJECT_ROOT / "config.json"

DEFAULT_SETTINGS: Dict[str, Any] = {
    "watch_dir": str(DEFAULT_WATCH_DIR),
    "workspace_dir": str(DEFAULT_WORKSPACE_DIR),
    "history_file": str(DEFAULT_HISTORY_FILE),
    "confidence_threshold": 75,
    "fallback_folder": "Inbox",
    "excluded_folders": ["Code_Projects", "Inbox"],
    "ollama_host": "http://127.0.0.1:11434",
    "ollama_model": "llama3:8b",
    "alternative_models": ["llama3:8b", "llama3:latest", "qwen2.5:7b", "qwen:7b", "mistral:latest"],
    "max_words_extract": 200,
    "lock_check_retries": 5,
    "lock_check_delay_sec": 1.0,
    "theme": {
        "bg_color": "#f8f9fa",
        "sidebar_color": "#0a0a0a",
        "card_bg": "#ffffff",
        "accent_color": "#2563eb",
        "text_primary": "#0f172a",
        "text_secondary": "#64748b",
        "border_color": "#e2e8f0"
    }
}


class Config:
    """Manages application runtime configuration and persistence."""

    def __init__(self, config_file: Path | str = DEFAULT_CONFIG_FILE):
        self.config_file = Path(config_file)
        self.data: Dict[str, Any] = dict(DEFAULT_SETTINGS)
        self.load()

    def load(self) -> None:
        """Loads configuration from JSON file if present."""
        if self.config_file.exists():
            try:
                with open(self.config_file, "r", encoding="utf-8") as f:
                    loaded = json.load(f)
                    self.data.update(loaded)
            except Exception as e:
                print(f"[Config] Error loading config {self.config_file}: {e}")

    def save(self) -> None:
        """Persists configuration to JSON file."""
        try:
            self.config_file.parent.mkdir(parents=True, exist_ok=True)
            with open(self.config_file, "w", encoding="utf-8") as f:
                json.dump(self.data, f, indent=2)
        except Exception as e:
            print(f"[Config] Error saving config {self.config_file}: {e}")

    def get(self, key: str, default: Any = None) -> Any:
        return self.data.get(key, default)

    def set(self, key: str, value: Any) -> None:
        self.data[key] = value
        self.save()

    def update(self, updates: Dict[str, Any]) -> None:
        self.data.update(updates)
        self.save()

    @property
    def watch_dir(self) -> Path:
        return Path(self.data.get("watch_dir", str(DEFAULT_WATCH_DIR)))

    @property
    def workspace_dir(self) -> Path:
        return Path(self.data.get("workspace_dir", str(DEFAULT_WORKSPACE_DIR)))

    @property
    def history_file(self) -> Path:
        return Path(self.data.get("history_file", str(DEFAULT_HISTORY_FILE)))

    @property
    def confidence_threshold(self) -> int:
        return int(self.data.get("confidence_threshold", 75))

    @property
    def ollama_host(self) -> str:
        return str(self.data.get("ollama_host", "http://127.0.0.1:11434"))

    @property
    def ollama_model(self) -> str:
        return str(self.data.get("ollama_model", "llama3:8b"))

    @property
    def theme(self) -> Dict[str, str]:
        return self.data.get("theme", DEFAULT_SETTINGS["theme"])


# Singleton global config instance
config = Config()

