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

    def _stem(self, w: str) -> str:
        w = w.lower()
        if w.endswith("ies") and len(w) > 4: return w[:-3] + "y"
        if w.endswith("es") and len(w) > 3: return w[:-2]
        if w.endswith("s") and not w.endswith("ss") and len(w) > 3: return w[:-1]
        if w.endswith("ing") and len(w) > 5: return w[:-3]
        if w.endswith("ed") and len(w) > 4: return w[:-2]
        return w

    def _words_match(self, w1: str, w2: str) -> bool:
        if w1.lower() == w2.lower(): return True
        if len(w1) >= 2 and len(w2) >= 2:
            s1 = self._stem(w1)
            s2 = self._stem(w2)
            if s1 == s2: return True
            if (s1.startswith(s2) or s2.startswith(s1)) and min(len(s1), len(s2)) >= 3:
                return True
        return False

    def _heuristic_fallback(
        self,
        metadata: Dict[str, Any],
        snippet: str,
        folder_contexts: Dict[str, str],
        category: str
    ) -> Tuple[str, int, str]:
        """Intelligent heuristic router supporting subfolder inheritance, precision scoring, and lazy minimal context."""
        filename = metadata.get("file_name", "").lower()
        ext = metadata.get("extension", "").lower()
        content = (snippet or "").lower()

        stop_words = {
            "and", "the", "for", "with", "this", "that", "from", "into", "over", "more",
            "some", "such", "than", "then", "them", "these", "those", "context", "files",
            "documents", "relevant", "related", "assets", "common", "types", "includes", "general"
        }

        content_words = [
            w for w in set(re.findall(r"\b[a-z0-9]{2,}\b", (filename + " " + content).lower()))
            if w not in stop_words
        ]

        is_video = ext in {".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".m4v"}
        is_audio = ext in {".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a"}
        is_image = ext in {".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tiff"} or category == "Media/Binary"
        is_archive = ext in {".zip", ".tar", ".gz", ".7z", ".rar", ".msi", ".iso"} or category == "Archive/Installer"
        is_doc = ext in {".txt", ".md", ".docx", ".pdf", ".csv", ".tsv", ".json", ".rtf", ".xlsx", ".pptx"} or category == "Text/Doc"

        scores: Dict[str, Dict[str, Any]] = {}

        for folder, ctx in folder_contexts.items():
            if folder in ("Inbox", "Code_Projects"):
                continue

            is_subfolder = "/" in folder
            parent_folder = folder.rsplit("/", 1)[0] if is_subfolder else ""
            self_name = folder.split("/")[-1]

            self_tokens = [t for t in re.split(r"[_ \-\\/]+", self_name.lower()) if len(t) >= 2 and t not in stop_words]
            self_name_match = any(any(self._words_match(st, cw) for cw in content_words) for st in self_tokens)

            inherited_part = ""
            unique_part = ctx or ""
            if "[Subcategory:" in unique_part:
                parts = unique_part.split("[Subcategory:", 1)
                inherited_part = parts[0]
                unique_part = parts[1]

            unique_words = [w for w in set(re.findall(r"\b[a-z0-9]{2,}\b", (self_name + " " + unique_part).lower())) if w not in stop_words]
            inherited_words = [w for w in set(re.findall(r"\b[a-z0-9]{2,}\b", inherited_part.lower())) if w not in stop_words]

            unique_matches = sum(1 for uw in unique_words if any(self._words_match(uw, cw) for cw in content_words))
            inherited_matches = sum(1 for iw in inherited_words if any(self._words_match(iw, cw) for cw in content_words))

            score = 50.0
            reason = ""

            if is_video:
                comb = (self_name + " " + (ctx or "")).lower()
                if any(k in comb for k in ("video", "movie", "recording", "footage", "clip")):
                    score = 90.0 + (4.0 if self_name_match else 0.0) + (unique_matches * 2.0)
                    if "audio" in comb or "podcast" in comb: score -= 50.0
                    reason = f"Matched video file format ({ext}) to {folder}."
            elif is_audio:
                comb = (self_name + " " + (ctx or "")).lower()
                if any(k in comb for k in ("audio", "music", "sound", "podcast", "sfx")):
                    score = 90.0 + (4.0 if self_name_match else 0.0) + (unique_matches * 2.0)
                    if "video" in comb or "movie" in comb: score -= 50.0
                    reason = f"Matched audio file format ({ext}) to {folder}."
            elif is_archive:
                comb = (self_name + " " + (ctx or "")).lower()
                if any(k in comb for k in ("archive", "backup", "zip", "package", "installer")):
                    score = 90.0 + (4.0 if self_name_match else 0.0) + (unique_matches * 2.0)
                    reason = f"Matched compressed archive format ({ext}) to {folder}."
            elif is_image:
                if self_name_match or unique_matches > 0:
                    score = 88.0 + (5.0 if self_name_match else 0.0) + (unique_matches * 3.5)
                    reason = f"Matched image asset context keywords for '{folder}'."
                elif folder == "Images":
                    score = 93.0
                    reason = f"Matched visual image extension ({ext}) to Images folder."
            elif is_doc or content_words:
                is_media_only = any(k in self_name.lower() for k in ("video", "audio", "image", "archive"))
                if self_name_match or unique_matches > 0 or inherited_matches > 0:
                    points = (7.5 if self_name_match else 0.0) + (unique_matches * 4.8) + (inherited_matches * 1.5)
                    score = 80.0 + points
                    if is_subfolder and (self_name_match or unique_matches > 0):
                        score += 4.2
                        reason = f"Matched subfolder '{folder}' with {unique_matches} specific context keywords."
                    else:
                        reason = f"Matched {unique_matches + inherited_matches} taxonomy keywords for '{folder}'."
                elif folder == "Documents" and is_doc:
                    score = 85.0
                    reason = "Default document classification to Documents."

                if is_media_only and unique_matches < 3 and not self_name_match:
                    score = 40.0

            tie_breaker = (unique_matches * 0.17) + (inherited_matches * 0.03) + (0.08 if self_name_match else 0.0)
            final_score = min(98.9, max(50.0, score + tie_breaker))

            scores[folder] = {
                "score": final_score,
                "unique_matches": unique_matches,
                "inherited_matches": inherited_matches,
                "name_match": self_name_match,
                "reason": reason
            }

        best_folder = "Inbox"
        best_score = 50.0
        best_reason = "Unrecognized binary or low confidence format routed to Inbox for review."

        for f, data in scores.items():
            if data["score"] > best_score:
                best_score = data["score"]
                best_folder = f
                best_reason = data["reason"]

        # Subfolder hierarchy check
        root_cand = best_folder.split("/")[0]
        subfolders = [k for k in scores if k.startswith(root_cand + "/")]
        if subfolders:
            winning_sub = None
            winning_score = best_score
            for sub in subfolders:
                s_data = scores[sub]
                if (s_data["unique_matches"] > 0 or s_data["name_match"]) and s_data["score"] >= (best_score - 3.0):
                    eff_score = max(s_data["score"], best_score + 0.5)
                    if eff_score > winning_score or winning_sub is None:
                        winning_score = eff_score
                        winning_sub = sub
                        best_reason = f"Matched specific subfolder '{sub}' (confidence {eff_score:.1f}%) over parent '{root_cand}'."

            if winning_sub:
                best_folder = winning_sub
                best_score = winning_score

        return best_folder, int(round(best_score)), best_reason
