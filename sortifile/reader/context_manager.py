"""Folder context management.
Module 2C: Read .context.md files of all target folders and format into a dictionary for prompts.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Dict, List, Optional

logger = logging.getLogger("sortifile.context_manager")

CONTEXT_FILENAME = ".context.md"

DEFAULT_FOLDER_CONTEXTS: Dict[str, str] = {
    "Documents": (
        "General written documents, reports, text files, articles, PDF papers, "
        "meeting notes, essays, and educational or business documentation."
    ),
    "Images": (
        "Photographs, graphic designs, screenshots, illustrations, wallpapers, "
        "and visual media assets in PNG, JPG, WEBP, or SVG formats."
    ),
    "Inbox": (
        "Default staging area for unclassified files, low confidence routing, "
        "or unknown binary formats requiring manual review."
    ),
    "Code_Projects": (
        "Excluded from automated sorting. Contains programming source code, "
        "repositories, scripts, and software build artifacts."
    ),
}


def initialize_workspace(workspace_dir: Path | str, excluded: Optional[List[str]] = None) -> None:
    """Ensures default workspace directories and their initial .context.md files exist."""
    base = Path(workspace_dir)
    base.mkdir(parents=True, exist_ok=True)

    for folder_name, context_desc in DEFAULT_FOLDER_CONTEXTS.items():
        folder_path = base / folder_name
        folder_path.mkdir(parents=True, exist_ok=True)

        # Write .context.md if it does not exist
        ctx_file = folder_path / CONTEXT_FILENAME
        if not ctx_file.exists():
            try:
                with open(ctx_file, "w", encoding="utf-8") as f:
                    f.write(f"# {folder_name} Context\n\n{context_desc}\n")
            except Exception as e:
                logger.error(f"Failed to write initial context for {folder_name}: {e}")


def read_folder_context(folder_path: Path) -> str:
    """Reads the content of .context.md in a folder, or returns default fallback description."""
    ctx_file = folder_path / CONTEXT_FILENAME
    if ctx_file.exists():
        try:
            with open(ctx_file, "r", encoding="utf-8", errors="replace") as f:
                content = f.read().strip()
                if content:
                    return content
        except Exception as e:
            logger.warning(f"Error reading context from {ctx_file}: {e}")

    # Fallback to default known contexts or name
    folder_name = folder_path.name
    return DEFAULT_FOLDER_CONTEXTS.get(folder_name, f"Files associated with {folder_name}.")


def write_folder_context(folder_path: Path, context_text: str) -> bool:
    """Writes or updates .context.md in a target directory."""
    try:
        folder_path.mkdir(parents=True, exist_ok=True)
        ctx_file = folder_path / CONTEXT_FILENAME
        with open(ctx_file, "w", encoding="utf-8") as f:
            f.write(context_text.strip() + "\n")
        return True
    except Exception as e:
        logger.error(f"Failed to write context to {folder_path}: {e}")
        return False


def get_available_folders(workspace_dir: Path | str, excluded_folders: Optional[List[str]] = None) -> List[str]:
    """Lists all valid target folders and subfolders in the workspace, excluding blacklisted directories."""
    base = Path(workspace_dir)
    if not base.exists():
        return []

    excluded = set(excluded_folders or ["Code_Projects"])
    folders: List[str] = []

    def _scan(current: Path) -> None:
        for entry in current.iterdir():
            if entry.is_dir() and not entry.name.startswith("."):
                rel = entry.relative_to(base).as_posix()
                top_name = rel.split("/")[0]
                if top_name in excluded or entry.name in excluded:
                    continue
                folders.append(rel)
                _scan(entry)

    _scan(base)

    if "Inbox" not in folders and (base / "Inbox").exists():
        folders.append("Inbox")

    return sorted(folders, key=lambda p: (p.count("/"), p))


def load_folder_contexts(workspace_dir: Path | str, excluded_folders: Optional[List[str]] = None) -> Dict[str, str]:
    """Module 2C: Reads .context.md for all target folders and applies hierarchical context inheritance."""
    base = Path(workspace_dir)
    folders = get_available_folders(base, excluded_folders=excluded_folders)
    raw_contexts: Dict[str, str] = {}

    for folder_name in folders:
        folder_path = base / folder_name
        raw_contexts[folder_name] = read_folder_context(folder_path)

    # Compute dynamic context inheritance (parents resolve before subfolders)
    effective_contexts: Dict[str, str] = {}
    for folder_name in folders:
        raw_ctx = raw_contexts.get(folder_name, "")
        if "/" in folder_name:
            parent_name = folder_name.rsplit("/", 1)[0]
            parent_ctx = effective_contexts.get(parent_name, "")
            sub_name = folder_name.split("/")[-1]
            if not raw_ctx or raw_ctx == f"Files related to {sub_name}.":
                effective_contexts[folder_name] = f"{parent_ctx}\n[Subcategory: {sub_name}]"
            else:
                effective_contexts[folder_name] = f"{parent_ctx}\n[Subcategory: {sub_name}] Specific context: {raw_ctx}"
        else:
            effective_contexts[folder_name] = raw_ctx

    return effective_contexts

