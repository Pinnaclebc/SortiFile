"""LLM Routing Engine.
Module 3A: Send extracted data and folder contexts to the local LLM via Ollama API. Force JSON output.
Module 3B: If confidence < 75%, route to /Inbox.
"""

from __future__ import annotations

import json
import logging
import re
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import httpx

from sortifile.config import config

logger = logging.getLogger("sortifile.brain.router")

ROUTING_SYSTEM_PROMPT = (
    "You are a local file sorting agent. You will receive a file's metadata and a snippet of its text, "
    "along with a list of available folders and their context descriptions. "
    "Output strictly valid JSON identifying the best target folder. If unsure, output 'Inbox'. "
    "Your output format MUST be: "
    '{"target_folder": "<Folder_Name>", "confidence_score": <0-100 integer>, "reasoning": "<short explanation>"}'
)


class LLMRouter:
    """Connects to local Ollama LLM to classify and route files."""

    def __init__(
        self,
        ollama_host: Optional[str] = None,
        model_name: Optional[str] = None,
        confidence_threshold: int = 75,
        timeout_sec: float = 12.0
    ):
        self.ollama_host = (ollama_host or config.ollama_host).rstrip("/")
        self.model_name = model_name or config.ollama_model
        self.confidence_threshold = confidence_threshold
        self.timeout_sec = timeout_sec

    def check_ollama_status(self) -> Dict[str, Any]:
        """Checks if Ollama service is reachable and queries installed models."""
        try:
            timeout_cfg = httpx.Timeout(1.0, connect=0.5)
            with httpx.Client(timeout=timeout_cfg) as client:
                res = client.get(f"{self.ollama_host}/api/tags")
                if res.status_code == 200:
                    data = res.json()
                    models = [m.get("name") for m in data.get("models", [])]
                    return {
                        "online": True,
                        "host": self.ollama_host,
                        "current_model": self.model_name,
                        "available_models": models,
                        "status": "connected"
                    }
        except Exception as e:
            logger.debug(f"Ollama health check failed: {e}")

        return {
            "online": False,
            "host": self.ollama_host,
            "current_model": self.model_name,
            "available_models": [],
            "status": "offline",
            "fallback_active": True
        }

    def _call_ollama(self, system_prompt: str, user_prompt: str) -> Optional[Dict[str, Any]]:
        """Makes a direct HTTP JSON call to Ollama /api/chat with format='json'."""
        endpoint = f"{self.ollama_host}/api/chat"
        payload = {
            "model": self.model_name,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt}
            ],
            "format": "json",
            "stream": False,
            "options": {
                "temperature": 0.1,  # Low temperature for deterministic classification
            }
        }

        try:
            timeout_cfg = httpx.Timeout(self.timeout_sec, connect=1.0)
            with httpx.Client(timeout=timeout_cfg) as client:
                response = client.post(endpoint, json=payload)
                if response.status_code == 200:
                    data = response.json()
                    content = data.get("message", {}).get("content", "").strip()
                    # Parse JSON from content
                    parsed = self._extract_json(content)
                    return parsed
                else:
                    logger.warning(f"Ollama returned HTTP {response.status_code}: {response.text}")
        except Exception as e:
            logger.warning(f"Ollama request error: {e}")

        return None

    def _extract_json(self, text: str) -> Optional[Dict[str, Any]]:
        """Extracts and parses JSON object from model response string."""
        if not text:
            return None

        # Clean markdown fences if present
        text = text.strip()
        if text.startswith("```json"):
            text = text[7:]
        elif text.startswith("```"):
            text = text[3:]
        if text.endswith("```"):
            text = text[:-3]
        text = text.strip()

        try:
            return json.loads(text)
        except json.JSONDecodeError:
            # Try regex to locate first { ... }
            match = re.search(r"\{.*\}", text, re.DOTALL)
            if match:
                try:
                    return json.loads(match.group(0))
                except Exception:
                    pass

        return None

    def route_file(
        self,
        metadata: Dict[str, Any],
        snippet: str,
        folder_contexts: Dict[str, str],
        category: str = "Unknown"
    ) -> Dict[str, Any]:
        """Module 3A & 3B: Routes a file to the best target folder based on LLM or heuristic fallback."""
        available_folders = list(folder_contexts.keys())
        if "Inbox" not in available_folders:
            available_folders.append("Inbox")

        user_prompt = self._build_prompt(metadata, snippet, folder_contexts)

        # Attempt LLM routing via Ollama
        llm_decision = self._call_ollama(ROUTING_SYSTEM_PROMPT, user_prompt)

        used_engine = "ollama"
        if llm_decision and "target_folder" in llm_decision:
            target_folder = str(llm_decision.get("target_folder", "Inbox")).strip()
            # Normalize folder name matching (case-insensitive)
            matched_folder = self._match_folder_name(target_folder, available_folders)
            raw_confidence = llm_decision.get("confidence_score", 50)
            try:
                confidence = int(raw_confidence)
            except (ValueError, TypeError):
                confidence = 50
            reasoning = llm_decision.get("reasoning", "Decided by LLM")
        else:
            # Fallback heuristic engine
            used_engine = "heuristic_fallback"
            matched_folder, confidence, reasoning = self._heuristic_fallback(
                metadata, snippet, folder_contexts, category
            )

        # Module 3B: If confidence < 75%, route to /Inbox
        effective_folder = matched_folder
        threshold = self.confidence_threshold
        if confidence < threshold:
            effective_folder = "Inbox"
            reasoning = f"Confidence ({confidence}%) is below threshold ({threshold}%). Routed to Inbox. Original candidate: {matched_folder}. {reasoning}"

        return {
            "target_folder": effective_folder,
            "candidate_folder": matched_folder,
            "confidence_score": confidence,
            "reasoning": reasoning,
            "engine": used_engine,
            "model": self.model_name if used_engine == "ollama" else "heuristic-rule-engine",
            "threshold": threshold,
        }

    def _match_folder_name(self, name: str, available_folders: List[str]) -> str:
        """Finds closest matching folder name from available folders."""
        for folder in available_folders:
            if folder.lower() == name.lower():
                return folder
            # Strip trailing slash or path components
            clean_name = Path(name).name
            if folder.lower() == clean_name.lower():
                return folder

        # Search substring
        for folder in available_folders:
            if folder.lower() in name.lower() or name.lower() in folder.lower():
                return folder

        return "Inbox"

    def _build_prompt(
        self,
        metadata: Dict[str, Any],
        snippet: str,
        folder_contexts: Dict[str, str]
    ) -> str:
        """Constructs the prompt containing metadata, snippet, and folder contexts."""
        folders_str = "\n".join(
            [f"- **{name}**: {ctx}" for name, ctx in folder_contexts.items()]
        )

        return (
            f"### Available Target Folders & Contexts:\n"
            f"{folders_str}\n\n"
            f"### File Under Consideration:\n"
            f"- File Name: {metadata.get('file_name', '')}\n"
            f"- Extension: {metadata.get('extension', '')}\n"
            f"- Size: {metadata.get('size_formatted', '')}\n"
            f"- MIME Type: {metadata.get('mime_type', '')}\n"
            f"- Created At: {metadata.get('created_at', '')}\n\n"
            f"### Extracted Text / Content Snippet (first 200 words):\n"
            f"\"\"\"\n{snippet}\n\"\"\"\n\n"
            f"Analyze the file name, type, and snippet content against the folder descriptions. "
            f"Determine the single best matching target folder and your confidence score (0 to 100). "
            f"Output strictly valid JSON with keys: 'target_folder', 'confidence_score', and 'reasoning'."
        )

    def _heuristic_fallback(
        self,
        metadata: Dict[str, Any],
        snippet: str,
        folder_contexts: Dict[str, str],
        category: str
    ) -> Tuple[str, int, str]:
        """Intelligent heuristic router when local LLM is offline or uninstalled."""
        filename = metadata.get("file_name", "").lower()
        ext = metadata.get("extension", "").lower()
        content = (snippet or "").lower()

        # Check for images / media
        image_extensions = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg"}
        if ext in image_extensions or category == "Media/Binary":
            if "Images" in folder_contexts:
                return "Images", 92, f"Matched image file extension ({ext}) to Images folder."

        # Check for documents / text
        doc_extensions = {".txt", ".md", ".docx", ".pdf", ".csv", ".tsv", ".json", ".rtf"}
        if ext in doc_extensions or category == "Text/Doc":
            # Check keywords for specific context matches
            best_folder = "Documents" if "Documents" in folder_contexts else "Inbox"
            score = 88
            reason = f"Matched document extension ({ext}) to {best_folder}."

            # Check if text strongly matches another available folder's description
            for folder, ctx in folder_contexts.items():
                if folder in ("Inbox", "Code_Projects"):
                    continue
                folder_lower = folder.lower()
                ctx_lower = ctx.lower()
                if folder_lower in filename or folder_lower in content:
                    best_folder = folder
                    score = 94
                    reason = f"File name or content matches keyword '{folder}'."
                    break

            return best_folder, score, reason

        # Check archives
        archive_extensions = {".zip", ".tar", ".gz", ".7z", ".rar", ".msi", ".iso"}
        if ext in archive_extensions or category == "Archive/Installer":
            for folder in folder_contexts:
                if "archive" in folder.lower() or "installer" in folder.lower():
                    return folder, 85, f"Matched archive extension ({ext}) to {folder}."
            return "Inbox", 70, f"Archive file ({ext}) routed to Inbox for review."

        # Default fallback
        return "Inbox", 60, "Unrecognized file format or category; routed to Inbox."
