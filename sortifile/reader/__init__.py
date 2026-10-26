"""Reader module for metadata and text extraction."""

from sortifile.reader.context_manager import (
    get_available_folders,
    initialize_workspace,
    load_folder_contexts,
    read_folder_context,
    write_folder_context,
)
from sortifile.reader.extractor import (
    extract_metadata,
    extract_text_snippet,
    format_bytes,
    read_file_summary,
)

__all__ = [
    "extract_metadata",
    "extract_text_snippet",
    "format_bytes",
    "read_file_summary",
    "initialize_workspace",
    "read_folder_context",
    "write_folder_context",
    "get_available_folders",
    "load_folder_contexts",
]

