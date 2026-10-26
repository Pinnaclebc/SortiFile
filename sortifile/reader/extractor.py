"""File metadata and text snippet extraction.
Module 2A: For .txt, .md, .csv, .docx, .pdf: Extract only the first 200 words.
Module 2B: Extract file extension, file size, creation date, and metadata.
"""

from __future__ import annotations

import csv
import io
import mimetypes
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional

try:
    from docx import Document
    DOCX_AVAILABLE = True
except ImportError:
    DOCX_AVAILABLE = False

try:
    from pypdf import PdfReader
    PYPDF_AVAILABLE = True
except ImportError:
    PYPDF_AVAILABLE = False

try:
    from PIL import Image
    PIL_AVAILABLE = True
except ImportError:
    PIL_AVAILABLE = False


def format_bytes(size: int) -> str:
    """Formats raw byte count into human-readable representation."""
    for unit in ["B", "KB", "MB", "GB", "TB"]:
        if abs(size) < 1024.0:
            return f"{size:3.1f} {unit}".strip()
        size /= 1024.0
    return f"{size:.1f} PB"


def extract_metadata(file_path: Path | str) -> Dict[str, Any]:
    """Module 2B: Extracts file extension, file size, creation/modification dates, and MIME type."""
    path = Path(file_path)
    stat = path.stat()

    mime_type, _ = mimetypes.guess_type(str(path))
    created_dt = datetime.fromtimestamp(stat.st_ctime)
    modified_dt = datetime.fromtimestamp(stat.st_mtime)

    metadata: Dict[str, Any] = {
        "file_name": path.name,
        "extension": path.suffix.lower(),
        "size_bytes": stat.st_size,
        "size_formatted": format_bytes(stat.st_size),
        "created_at": created_dt.isoformat(),
        "modified_at": modified_dt.isoformat(),
        "mime_type": mime_type or "application/octet-stream",
    }

    # If image, extract dimensions
    if PIL_AVAILABLE and path.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"}:
        try:
            with Image.open(path) as img:
                metadata["image_dimensions"] = f"{img.width}x{img.height}"
                metadata["image_format"] = img.format
        except Exception:
            pass

    return metadata


def _truncate_to_word_limit(text: str, max_words: int = 200) -> str:
    """Limits text to at most max_words tokens."""
    words = text.split()
    if len(words) <= max_words:
        return text.strip()
    return " ".join(words[:max_words]) + "..."


def _extract_plain_text(path: Path, max_words: int) -> str:
    """Extracts text from plain text formats (.txt, .md, .csv, etc.)."""
    encodings = ["utf-8", "utf-8-sig", "latin-1", "cp1252"]
    for enc in encodings:
        try:
            with open(path, "r", encoding=enc, errors="replace") as f:
                content = f.read(15000)  # Read up to 15KB to avoid reading huge files
                return _truncate_to_word_limit(content, max_words)
        except Exception:
            continue
    return ""


def _extract_docx(path: Path, max_words: int) -> str:
    """Extracts text from .docx files using python-docx."""
    if not DOCX_AVAILABLE:
        return "[python-docx not installed]"
    try:
        doc = Document(path)
        paragraphs = []
        word_count = 0
        for p in doc.paragraphs:
            text = p.text.strip()
            if text:
                paragraphs.append(text)
                word_count += len(text.split())
                if word_count >= max_words:
                    break
        combined = "\n".join(paragraphs)
        return _truncate_to_word_limit(combined, max_words)
    except Exception as e:
        return f"[Docx read error: {e}]"


def _extract_pdf(path: Path, max_words: int) -> str:
    """Extracts text from .pdf files using pypdf."""
    if not PYPDF_AVAILABLE:
        return "[pypdf not installed]"
    try:
        reader = PdfReader(path)
        extracted = []
        word_count = 0
        for page in reader.pages[:3]:  # Check up to first 3 pages
            text = page.extract_text() or ""
            if text.strip():
                extracted.append(text.strip())
                word_count += len(text.split())
                if word_count >= max_words:
                    break
        combined = "\n".join(extracted)
        return _truncate_to_word_limit(combined, max_words)
    except Exception as e:
        return f"[PDF read error: {e}]"


def extract_text_snippet(file_path: Path | str, max_words: int = 200) -> str:
    """Module 2A: Extracts up to max_words from supported file formats."""
    path = Path(file_path)
    ext = path.suffix.lower()

    if not path.exists():
        return ""

    if ext in {".txt", ".md", ".markdown", ".json", ".log", ".rtf", ".html", ".xml", ".yaml", ".yml"}:
        return _extract_plain_text(path, max_words)

    if ext in {".csv", ".tsv"}:
        try:
            # Read first few lines of CSV
            with open(path, "r", encoding="utf-8", errors="replace") as f:
                reader = csv.reader(f, delimiter=("\t" if ext == ".tsv" else ","))
                rows = []
                for idx, row in enumerate(reader):
                    if idx >= 15:
                        break
                    rows.append(", ".join(row))
                return _truncate_to_word_limit(" \n".join(rows), max_words)
        except Exception:
            return _extract_plain_text(path, max_words)

    if ext == ".docx":
        return _extract_docx(path, max_words)

    if ext == ".pdf":
        return _extract_pdf(path, max_words)

    # For images / media / binaries, return a descriptive summary of the filename tokens
    stem_words = path.stem.replace("_", " ").replace("-", " ").replace(".", " ")
    return f"Media or binary asset: {path.name} (Tags: {stem_words})"


def read_file_summary(file_path: Path | str, max_words: int = 200) -> Dict[str, Any]:
    """Combines metadata extraction and text snippet into a clean dictionary."""
    path = Path(file_path)
    metadata = extract_metadata(path)
    snippet = extract_text_snippet(path, max_words)
    return {
        "metadata": metadata,
        "snippet": snippet,
    }

