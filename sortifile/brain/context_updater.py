"""Background folder context updater.
Module 3C: Background prompt to auto-generate or update a folder's .context.md when files are placed in it.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import List, Optional

import httpx

from sortifile.config import config
from sortifile.reader.context_manager import CONTEXT_FILENAME, write_folder_context

logger = logging.getLogger("sortifile.brain.context_updater")


def summarize_folder_contents(folder_path: Path | str) -> List[str]:
    """Gathers sample file names and extensions from a directory for summarization."""
    path = Path(folder_path)
    if not path.exists():
        return []

    items = []
    for p in path.iterdir():
        if p.is_file() and p.name != CONTEXT_FILENAME and not p.name.startswith("."):
            items.append(f"{p.name} ({p.suffix.lower()})")
        if len(items) >= 20:  # Sample up to 20 files
            break
    return items


def auto_update_folder_context(
    folder_path: Path | str,
    ollama_host: Optional[str] = None,
    model_name: Optional[str] = None
) -> str:
    """Updates a folder's .context.md file using LLM summary or smart heuristic."""
    path = Path(folder_path)
    if not path.exists():
        return ""

    file_samples = summarize_folder_contents(path)
    if not file_samples:
        return ""

    folder_name = path.name
    host = (ollama_host or config.ollama_host).rstrip("/")
    model = model_name or config.ollama_model

    # Try Ollama prompt
    prompt = (
        f"You are a folder taxonomy assistant. A folder named '{folder_name}' contains these files:\n"
        + "\n".join([f"- {s}" for s in file_samples])
        + "\n\nWrite a concise 2-sentence description of the folder's purpose and contents for automated file sorting. "
        "Do not include preamble, just the description."
    )

    summary_text = None
    try:
        timeout_cfg = httpx.Timeout(3.0, connect=0.5)
        with httpx.Client(timeout=timeout_cfg) as client:
            res = client.post(
                f"{host}/api/generate",
                json={"model": model, "prompt": prompt, "stream": False}
            )
            if res.status_code == 200:
                summary_text = res.json().get("response", "").strip()
    except Exception as e:
        logger.debug(f"LLM context update skipped/failed: {e}")

    if not summary_text:
        # Heuristic fallback summary
        exts = sorted(list(set(Path(s.split()[0]).suffix.lower() for s in file_samples if "." in s)))
        summary_text = (
            f"Repository for {folder_name.lower()} assets and documents. "
            f"Common types include: {', '.join(exts[:6])}."
        )

    full_context_doc = (
        f"# {folder_name} Context\n\n"
        f"{summary_text}\n\n"
        f"*(Auto-updated based on {len(file_samples)} sample file(s))*\n"
    )

    write_folder_context(path, full_context_doc)
    logger.info(f"Updated context for folder {folder_name}")
    return summary_text
