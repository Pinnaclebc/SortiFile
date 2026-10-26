"""File category classifier.
Module 1D: Categorize incoming files into Text/Doc, Media/Binary, or Archive/Installer.
"""

from __future__ import annotations

import mimetypes
from pathlib import Path
from typing import Literal

CategoryType = Literal["Text/Doc", "Media/Binary", "Archive/Installer", "Unknown"]

TEXT_EXTENSIONS = {
    ".txt", ".md", ".markdown", ".csv", ".tsv", ".docx", ".doc",
    ".pdf", ".json", ".xml", ".html", ".htm", ".rtf", ".log",
    ".yaml", ".yml", ".ini", ".cfg", ".sql", ".tex"
}

MEDIA_BINARY_EXTENSIONS = {
    # Images
    ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".tiff", ".tif", ".ico", ".raw", ".heic",
    # Audio
    ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma",
    # Video
    ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv",
    # Executables & Binaries
    ".exe", ".dll", ".bin", ".dat", ".apk", ".app"
}

ARCHIVE_INSTALLER_EXTENSIONS = {
    # Archives
    ".zip", ".tar", ".gz", ".tgz", ".7z", ".rar", ".bz2", ".xz",
    # Installers & Disk Images
    ".msi", ".pkg", ".deb", ".rpm", ".iso", ".img", ".dmg"
}

TEMPORARY_EXTENSIONS = {
    ".crdownload", ".tmp", ".part", ".download", ".partial", ".filepart"
}


def is_temporary_or_partial_file(file_path: Path | str) -> bool:
    """Checks whether the file is a partial browser download or temp file."""
    path = Path(file_path)
    ext = path.suffix.lower()
    name = path.name.lower()
    if ext in TEMPORARY_EXTENSIONS:
        return True
    if name.startswith("~$") or name.startswith(".~"):  # Office temp lock files
        return True
    return False


def classify_file(file_path: Path | str) -> CategoryType:
    """Categorizes incoming files into Text/Doc, Media/Binary, or Archive/Installer."""
    path = Path(file_path)
    ext = path.suffix.lower()

    if ext in TEXT_EXTENSIONS:
        return "Text/Doc"

    if ext in ARCHIVE_INSTALLER_EXTENSIONS:
        return "Archive/Installer"

    if ext in MEDIA_BINARY_EXTENSIONS:
        return "Media/Binary"

    # Fallback to mimetype inspection
    mime_type, _ = mimetypes.guess_type(str(path))
    if mime_type:
        if mime_type.startswith("text/") or mime_type in (
            "application/pdf",
            "application/json",
            "application/rtf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        ):
            return "Text/Doc"
        if mime_type.startswith("image/") or mime_type.startswith("video/") or mime_type.startswith("audio/"):
            return "Media/Binary"
        if (
            mime_type.startswith("application/zip")
            or "archive" in mime_type
            or "tar" in mime_type
            or "gzip" in mime_type
        ):
            return "Archive/Installer"

    return "Unknown"

