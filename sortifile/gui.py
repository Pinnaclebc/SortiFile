"""SortiFile Native Desktop Application.
Built with CustomTkinter for a 100% standalone desktop experience with zero browser dependencies.
"""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import threading
import time
from datetime import datetime
from pathlib import Path
from tkinter import filedialog, messagebox
from typing import Any, Dict, List, Optional

import customtkinter as ctk

# Ensure root is in sys.path
PROJECT_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

from sortifile.brain.context_updater import auto_update_folder_context
from sortifile.config import config
from sortifile.core import engine
from sortifile.executor.mover import get_unique_destination_path
from sortifile.reader.context_manager import (
    get_available_folders,
    load_folder_contexts,
    read_folder_context,
    write_folder_context,
)

# CustomTkinter Theme Setup
ctk.set_appearance_mode("Light")
ctk.set_default_color_theme("blue")


def open_in_file_explorer(path: Path | str) -> bool:
    """Opens directory in Windows File Explorer."""
    p = Path(path).resolve()
    if not p.exists():
        return False
    try:
        if os.name == "nt":
            os.startfile(str(p))
        else:
            subprocess.Popen(["xdg-open", str(p)])
        return True
    except Exception:
        return False


class SortiFileApp(ctk.CTk):
    """Main CustomTkinter Desktop Application Window."""

    def __init__(self):
        super().__init__()

        self.title("SortiFile Agent")
        self.geometry("1180x760")
        self.minsize(980, 620)

        # Start core background engine
        engine.start()

        # Layout: Left Sidebar + Main Viewport
        self.grid_rowconfigure(0, weight=1)
        self.grid_columnconfigure(1, weight=1)

        self._build_sidebar()
        self._build_main_area()

        # Start background polling loop for UI updates
        self._poll_updates()

    def _build_sidebar(self):
        """Builds the slim modern dark sidebar on the left."""
        self.sidebar = ctk.CTkFrame(self, width=80, corner_radius=0, fg_color="#0a0a0a")
        self.sidebar.grid(row=0, column=0, sticky="nsew")
        self.sidebar.grid_rowconfigure(5, weight=1)

        # Brand Logo / Icon
        logo_btn = ctk.CTkButton(
            self.sidebar,
            text="⚡",
            font=ctk.CTkFont(size=20, weight="bold"),
            width=50,
            height=50,
            corner_radius=12,
            fg_color="#ffffff",
            text_color="#000000",
            hover_color="#f1f5f9",
            command=lambda: self.switch_tab("dashboard")
        )
        logo_btn.grid(row=0, column=0, padx=15, pady=(20, 30))

        # Nav Buttons
        self.nav_btns = {}
        tabs = [
            ("dashboard", "📊\nDash", 1),
            ("folders", "📁\nFolders", 2),
            ("history", "📜\nHistory", 3),
            ("settings", "⚙️\nSettings", 4),
        ]

        for tab_id, text, r in tabs:
            btn = ctk.CTkButton(
                self.sidebar,
                text=text,
                font=ctk.CTkFont(size=12, weight="bold"),
                width=56,
                height=56,
                corner_radius=10,
                fg_color="transparent",
                text_color="#94a3b8",
                hover_color="#1e293b",
                command=lambda tid=tab_id: self.switch_tab(tid)
            )
            btn.grid(row=r, column=0, padx=12, pady=8)
            self.nav_btns[tab_id] = btn

        # Highlight default tab
        self.active_tab = "dashboard"
        self._update_nav_highlight()

    def _build_main_area(self):
        """Builds header and view container."""
        self.main_container = ctk.CTkFrame(self, fg_color="#f8f9fa", corner_radius=0)
        self.main_container.grid(row=0, column=1, sticky="nsew", padx=0, pady=0)
        self.main_container.grid_rowconfigure(1, weight=1)
        self.main_container.grid_columnconfigure(0, weight=1)

        # Top Bar
        self.header = ctk.CTkFrame(self.main_container, fg_color="#ffffff", height=70, corner_radius=0)
        self.header.grid(row=0, column=0, sticky="ew", padx=0, pady=(0, 16))
        self.header.grid_columnconfigure(0, weight=1)

        # Left Header Title
        title_box = ctk.CTkFrame(self.header, fg_color="transparent")
        title_box.grid(row=0, column=0, sticky="w", padx=24, pady=14)

        self.lbl_title = ctk.CTkLabel(
            title_box,
            text="SortiFile Agent",
            font=ctk.CTkFont(size=20, weight="bold"),
            text_color="#0f172a"
        )
        self.lbl_title.pack(anchor="w")

        self.lbl_subtitle = ctk.CTkLabel(
            title_box,
            text="Autonomous local file classifier & folder context manager",
            font=ctk.CTkFont(size=11),
            text_color="#64748b"
        )
        self.lbl_subtitle.pack(anchor="w")

        # Right Header Actions
        actions_box = ctk.CTkFrame(self.header, fg_color="transparent")
        actions_box.grid(row=0, column=1, sticky="e", padx=24, pady=14)

        self.btn_status = ctk.CTkLabel(
            actions_box,
            text="● Monitoring Active",
            font=ctk.CTkFont(size=11, weight="bold"),
            text_color="#10b981",
            fg_color="#f1f5f9",
            corner_radius=12,
            padx=12,
            pady=4
        )
        self.btn_status.pack(side="left", padx=6)

        self.btn_ollama = ctk.CTkLabel(
            actions_box,
            text="● Ollama: Checking...",
            font=ctk.CTkFont(size=11, weight="bold"),
            text_color="#f59e0b",
            fg_color="#f1f5f9",
            corner_radius=12,
            padx=12,
            pady=4
        )
        self.btn_ollama.pack(side="left", padx=6)

        self.btn_scan = ctk.CTkButton(
            actions_box,
            text="Scan Watch Folder",
            font=ctk.CTkFont(size=12, weight="bold"),
            width=130,
            height=32,
            fg_color="#0f172a",
            hover_color="#000000",
            command=self.action_scan
        )
        self.btn_scan.pack(side="left", padx=6)

        self.btn_undo = ctk.CTkButton(
            actions_box,
            text="Undo Last",
            font=ctk.CTkFont(size=12, weight="bold"),
            width=90,
            height=32,
            fg_color="#ffffff",
            text_color="#0f172a",
            border_width=1,
            border_color="#cbd5e1",
            hover_color="#f1f5f9",
            command=self.action_undo
        )
        self.btn_undo.pack(side="left", padx=6)

        # Tab Views
        self.views = {}
        self.views["dashboard"] = self._build_dashboard_tab()
        self.views["folders"] = self._build_folders_tab()
        self.views["history"] = self._build_history_tab()
        self.views["settings"] = self._build_settings_tab()

        self.switch_tab("dashboard")

    def _update_nav_highlight(self):
        for tid, btn in self.nav_btns.items():
            if tid == self.active_tab:
                btn.configure(fg_color="#2563eb", text_color="#ffffff")
            else:
                btn.configure(fg_color="transparent", text_color="#94a3b8")

    def switch_tab(self, tab_id: str):
        self.active_tab = tab_id
        self._update_nav_highlight()

        for tid, view in self.views.items():
            if tid == tab_id:
                view.grid(row=1, column=0, sticky="nsew", padx=24, pady=(0, 20))
            else:
                view.grid_forget()

        if tab_id == "folders":
            self.refresh_folders_view()
        elif tab_id == "history":
            self.refresh_history_view()

    # ========================================================
    # TAB 1: DASHBOARD
    # ========================================================
    def _build_dashboard_tab(self) -> ctk.CTkFrame:
        frame = ctk.CTkFrame(self.main_container, fg_color="transparent")
        frame.grid_columnconfigure(0, weight=1)

        # 4 Stat Cards
        stats_frame = ctk.CTkFrame(frame, fg_color="transparent")
        stats_frame.pack(fill="x", pady=(0, 16))
        for i in range(4):
            stats_frame.grid_columnconfigure(i, weight=1, uniform="stat")

        self.stat_cards = {}
        labels = [
            ("total_sorted", "TOTAL SORTED", "0", "Automated moves"),
            ("active_queue", "ACTIVE IN QUEUE", "0", "Pipeline processing"),
            ("avg_conf", "AVG CONFIDENCE", "--%", "AI accuracy"),
            ("folders_count", "TARGET FOLDERS", "3", "Active taxonomies"),
        ]

        for idx, (key, title, val, sub) in enumerate(labels):
            card = ctk.CTkFrame(stats_frame, fg_color="#ffffff", corner_radius=12, border_width=1, border_color="#e2e8f0")
            card.grid(row=0, column=idx, padx=6, sticky="ew")

            t = ctk.CTkLabel(card, text=title, font=ctk.CTkFont(size=10, weight="bold"), text_color="#64748b")
            t.pack(anchor="w", padx=16, pady=(12, 0))

            v = ctk.CTkLabel(card, text=val, font=ctk.CTkFont(size=22, weight="bold"), text_color="#0f172a")
            v.pack(anchor="w", padx=16, pady=(2, 0))
            self.stat_cards[key] = v

            s = ctk.CTkLabel(card, text=sub, font=ctk.CTkFont(size=11), text_color="#94a3b8")
            s.pack(anchor="w", padx=16, pady=(0, 12))

        # Drop & Test Zone
        drop_card = ctk.CTkFrame(frame, fg_color="#ffffff", corner_radius=12, border_width=2, border_color="#cbd5e1")
        drop_card.pack(fill="x", pady=(0, 16))

        drop_btn = ctk.CTkButton(
            drop_card,
            text="📂  Click to Select or Drop Any File to Sort (PDF, Word, Text, Images, Binaries)",
            font=ctk.CTkFont(size=13, weight="bold"),
            fg_color="#eff6ff",
            text_color="#1d4ed8",
            hover_color="#dbeafe",
            height=46,
            corner_radius=8,
            command=self.action_select_file
        )
        drop_btn.pack(fill="x", padx=16, pady=14)

        # Live Queue Card
        queue_box = ctk.CTkFrame(frame, fg_color="#ffffff", corner_radius=12, border_width=1, border_color="#e2e8f0")
        queue_box.pack(fill="x", pady=(0, 16))

        q_header = ctk.CTkFrame(queue_box, fg_color="transparent")
        q_header.pack(fill="x", padx=16, pady=10)

        ctk.CTkLabel(
            q_header,
            text="Live Processing Queue",
            font=ctk.CTkFont(size=14, weight="bold"),
            text_color="#0f172a"
        ).pack(side="left")

        self.lbl_queue_count = ctk.CTkLabel(
            q_header,
            text="0 files",
            font=ctk.CTkFont(size=11, weight="bold"),
            fg_color="#f1f5f9",
            text_color="#0f172a",
            corner_radius=8,
            padx=8,
            pady=2
        )
        self.lbl_queue_count.pack(side="left", padx=8)

        self.queue_scroll = ctk.CTkScrollableFrame(queue_box, fg_color="transparent", height=110)
        self.queue_scroll.pack(fill="x", padx=16, pady=(0, 12))
        self.lbl_empty_queue = ctk.CTkLabel(
            self.queue_scroll,
            text="Queue is empty. Select files above or drop them into Watch_Folder.",
            text_color="#94a3b8",
            font=ctk.CTkFont(size=12)
        )
        self.lbl_empty_queue.pack(pady=20)

        # Recent Activity Card
        act_box = ctk.CTkFrame(frame, fg_color="#ffffff", corner_radius=12, border_width=1, border_color="#e2e8f0")
        act_box.pack(fill="both", expand=True)

        a_header = ctk.CTkFrame(act_box, fg_color="transparent")
        a_header.pack(fill="x", padx=16, pady=10)

        ctk.CTkLabel(
            a_header,
            text="Recent Sort Activity",
            font=ctk.CTkFont(size=14, weight="bold"),
            text_color="#0f172a"
        ).pack(side="left")

        self.act_scroll = ctk.CTkScrollableFrame(act_box, fg_color="transparent")
        self.act_scroll.pack(fill="both", expand=True, padx=16, pady=(0, 12))

        return frame

    # ========================================================
    # TAB 2: FOLDER CONTEXTS & EXPLORER
    # ========================================================
    def _build_folders_tab(self) -> ctk.CTkFrame:
        frame = ctk.CTkFrame(self.main_container, fg_color="transparent")
        frame.grid_columnconfigure(0, weight=1)
        frame.grid_rowconfigure(1, weight=1)

        # Header with Add Folder Button
        top_bar = ctk.CTkFrame(frame, fg_color="transparent")
        top_bar.grid(row=0, column=0, sticky="ew", pady=(0, 12))

        lbl_desc = ctk.CTkLabel(
            top_bar,
            text="Target Folders & Taxonomy (.context.md)",
            font=ctk.CTkFont(size=15, weight="bold"),
            text_color="#0f172a"
        )
        lbl_desc.pack(side="left")

        btn_add = ctk.CTkButton(
            top_bar,
            text="+ Add New Folder",
            font=ctk.CTkFont(size=12, weight="bold"),
            fg_color="#2563eb",
            hover_color="#1d4ed8",
            command=self.action_add_folder
        )
        btn_add.pack(side="right")

        # Scrollable area for folder cards
        self.folders_scroll = ctk.CTkScrollableFrame(frame, fg_color="transparent")
        self.folders_scroll.grid(row=1, column=0, sticky="nsew")

        return frame

    def refresh_folders_view(self):
        """Populates the folder cards with paths, Explorer buttons, and delete buttons."""
        for w in self.folders_scroll.winfo_children():
            w.destroy()

        workspace = engine.workspace_dir
        contexts = load_folder_contexts(workspace, excluded_folders=config.get("excluded_folders", []))

        for name, ctx in contexts.items():
            folder_path = (workspace / name).resolve()
            file_count = 0
            if folder_path.exists():
                file_count = len([f for f in folder_path.iterdir() if f.is_file() and not f.name.startswith(".")])

            card = ctk.CTkFrame(self.folders_scroll, fg_color="#ffffff", corner_radius=12, border_width=1, border_color="#e2e8f0")
            card.pack(fill="x", pady=8, padx=4)

            # Header row: Icon, Name, Count
            hdr = ctk.CTkFrame(card, fg_color="transparent")
            hdr.pack(fill="x", padx=16, pady=(12, 6))

            ctk.CTkLabel(
                hdr,
                text=f"📁  {name}",
                font=ctk.CTkFont(size=15, weight="bold"),
                text_color="#0f172a"
            ).pack(side="left")

            ctk.CTkLabel(
                hdr,
                text=f"{file_count} files",
                font=ctk.CTkFont(size=11, weight="bold"),
                fg_color="#f1f5f9",
                text_color="#64748b",
                corner_radius=6,
                padx=8,
                pady=2
            ).pack(side="left", padx=10)

            # Path Bar (Click to copy)
            path_bar = ctk.CTkFrame(card, fg_color="#f1f5f9", corner_radius=6)
            path_bar.pack(fill="x", padx=16, pady=4)

            lbl_path = ctk.CTkLabel(
                path_bar,
                text=f"📍 {str(folder_path)}",
                font=ctk.CTkFont(size=11, family="Consolas"),
                text_color="#475569"
            )
            lbl_path.pack(side="left", padx=8, pady=4)

            btn_copy = ctk.CTkButton(
                path_bar,
                text="Copy Path",
                width=70,
                height=22,
                font=ctk.CTkFont(size=10, weight="bold"),
                fg_color="#e2e8f0",
                text_color="#0f172a",
                hover_color="#cbd5e1",
                command=lambda p=str(folder_path): self._copy_to_clipboard(p)
            )
            btn_copy.pack(side="right", padx=6, pady=4)

            # Context Text Area
            lbl_ctx_title = ctk.CTkLabel(
                card,
                text=".context.md Description (LLM Taxonomy Instruction):",
                font=ctk.CTkFont(size=11, weight="bold"),
                text_color="#64748b"
            )
            lbl_ctx_title.pack(anchor="w", padx=16, pady=(8, 2))

            txt_context = ctk.CTkTextbox(card, height=65, font=ctk.CTkFont(size=11))
            txt_context.pack(fill="x", padx=16, pady=(0, 8))
            txt_context.insert("1.0", ctx)

            # Action Buttons Row
            btn_row = ctk.CTkFrame(card, fg_color="transparent")
            btn_row.pack(fill="x", padx=16, pady=(0, 12))

            # Open in File Explorer button
            btn_open = ctk.CTkButton(
                btn_row,
                text="📂 Open in Explorer",
                font=ctk.CTkFont(size=11, weight="bold"),
                width=130,
                height=28,
                fg_color="#f1f5f9",
                text_color="#0f172a",
                hover_color="#e2e8f0",
                border_width=1,
                border_color="#cbd5e1",
                command=lambda p=folder_path: open_in_file_explorer(p)
            )
            btn_open.pack(side="left")

            # Save context button
            btn_save = ctk.CTkButton(
                btn_row,
                text="Save Context",
                font=ctk.CTkFont(size=11, weight="bold"),
                width=100,
                height=28,
                fg_color="#0f172a",
                hover_color="#000000",
                command=lambda p=folder_path, tb=txt_context, n=name: self._save_folder_ctx(p, tb.get("1.0", "end-1c"), n)
            )
            btn_save.pack(side="right", padx=(6, 0))

            # Delete Folder button (if not Inbox)
            if name.lower() != "inbox":
                btn_del = ctk.CTkButton(
                    btn_row,
                    text="🗑️ Delete Folder",
                    font=ctk.CTkFont(size=11, weight="bold"),
                    width=110,
                    height=28,
                    fg_color="#fef2f2",
                    text_color="#dc2626",
                    hover_color="#fee2e2",
                    command=lambda n=name, p=folder_path, count=file_count: self.action_delete_folder(n, p, count)
                )
                btn_del.pack(side="right", padx=6)

    def _copy_to_clipboard(self, text: str):
        self.clipboard_clear()
        self.clipboard_append(text)
        messagebox.showinfo("Copied", f"Path copied to clipboard:\n{text}")

    def _save_folder_ctx(self, path: Path, text: str, name: str):
        write_folder_context(path, text)
        messagebox.showinfo("Saved", f"Context for folder '{name}' updated successfully!")

    def action_delete_folder(self, name: str, folder_path: Path, count: int):
        if count > 0:
            msg = f"Folder '{name}' currently contains {count} file(s).\n\nSafely move all files into '/Inbox' and delete folder?"
            if not messagebox.askyesno("Confirm Delete", msg):
                return
            # Relocate files to Inbox
            inbox_path = engine.workspace_dir / "Inbox"
            inbox_path.mkdir(parents=True, exist_ok=True)
            for f in folder_path.iterdir():
                if f.is_file() and not f.name.startswith("."):
                    dest = get_unique_destination_path(inbox_path, f.name)
                    shutil.move(str(f), str(dest))
        else:
            if not messagebox.askyesno("Confirm Delete", f"Delete folder '{name}'?"):
                return

        try:
            shutil.rmtree(str(folder_path))
            messagebox.showinfo("Deleted", f"Folder '{name}' deleted.")
            self.refresh_folders_view()
        except Exception as e:
            messagebox.showerror("Error", f"Failed to delete folder: {e}")

    def action_add_folder(self):
        dialog = ctk.CTkInputDialog(text="Enter new folder name:", title="Create Target Folder")
        name = dialog.get_input()
        if not name or not name.strip():
            return
        name = name.strip().replace("/", "_").replace("\\", "_")
        folder_path = engine.workspace_dir / name
        folder_path.mkdir(parents=True, exist_ok=True)
        write_folder_context(folder_path, f"# {name} Context\n\nFiles and documents related to {name}.\n")
        messagebox.showinfo("Success", f"Folder '{name}' created!")
        self.refresh_folders_view()

    # ========================================================
    # TAB 3: SORT HISTORY
    # ========================================================
    def _build_history_tab(self) -> ctk.CTkFrame:
        frame = ctk.CTkFrame(self.main_container, fg_color="transparent")
        frame.grid_columnconfigure(0, weight=1)
        frame.grid_rowconfigure(1, weight=1)

        hdr = ctk.CTkFrame(frame, fg_color="transparent")
        hdr.grid(row=0, column=0, sticky="ew", pady=(0, 10))

        ctk.CTkLabel(
            hdr,
            text="Comprehensive Sort History (sort_history.json)",
            font=ctk.CTkFont(size=15, weight="bold"),
            text_color="#0f172a"
        ).pack(side="left")

        self.history_scroll = ctk.CTkScrollableFrame(frame, fg_color="#ffffff", corner_radius=12, border_width=1, border_color="#e2e8f0")
        self.history_scroll.grid(row=1, column=0, sticky="nsew")

        return frame

    def refresh_history_view(self):
        for w in self.history_scroll.winfo_children():
            w.destroy()

        history = engine.history.load_history()
        if not history:
            ctk.CTkLabel(self.history_scroll, text="No sort history found.", text_color="#94a3b8").pack(pady=30)
            return

        for item in history:
            row = ctk.CTkFrame(self.history_scroll, fg_color="#f8fafc", corner_radius=8, border_width=1, border_color="#e2e8f0")
            row.pack(fill="x", padx=10, pady=5)

            status = item.get("status", "moved")
            status_color = "#10b981" if status == "moved" else "#94a3b8"

            lbl_f = ctk.CTkLabel(
                row,
                text=f"{item.get('file_name', '')}",
                font=ctk.CTkFont(size=12, weight="bold"),
                text_color="#0f172a"
            )
            lbl_f.pack(side="left", padx=12, pady=8)

            lbl_t = ctk.CTkLabel(
                row,
                text=f"→ {item.get('target_folder', '')}",
                font=ctk.CTkFont(size=11, weight="bold"),
                fg_color="#e2e8f0",
                text_color="#1e293b",
                corner_radius=6,
                padx=8,
                pady=2
            )
            lbl_t.pack(side="left", padx=8)

            lbl_c = ctk.CTkLabel(
                row,
                text=f"{item.get('confidence_score', 0)}% conf",
                font=ctk.CTkFont(size=11),
                text_color="#475569"
            )
            lbl_c.pack(side="left", padx=8)

            lbl_reason = ctk.CTkLabel(
                row,
                text=f"({item.get('reasoning', '')[:40]}...)",
                font=ctk.CTkFont(size=11),
                text_color="#94a3b8"
            )
            lbl_reason.pack(side="left", padx=8)

            if status == "moved":
                btn_u = ctk.CTkButton(
                    row,
                    text="Undo",
                    font=ctk.CTkFont(size=11, weight="bold"),
                    width=60,
                    height=24,
                    fg_color="#ffffff",
                    text_color="#0f172a",
                    border_width=1,
                    border_color="#cbd5e1",
                    hover_color="#f1f5f9",
                    command=lambda aid=item.get("id"): self.action_undo_id(aid)
                )
                btn_u.pack(side="right", padx=10, pady=8)
            else:
                lbl_rev = ctk.CTkLabel(
                    row,
                    text="Undone",
                    font=ctk.CTkFont(size=11, slant="italic"),
                    text_color="#94a3b8"
                )
                lbl_rev.pack(side="right", padx=12, pady=8)

    # ========================================================
    # TAB 4: SETTINGS
    # ========================================================
    def _build_settings_tab(self) -> ctk.CTkFrame:
        frame = ctk.CTkFrame(self.main_container, fg_color="transparent")
        frame.grid_columnconfigure(0, weight=1)

        card = ctk.CTkFrame(frame, fg_color="#ffffff", corner_radius=12, border_width=1, border_color="#e2e8f0")
        card.pack(fill="x", pady=8, padx=4)

        ctk.CTkLabel(card, text="Directory Configuration", font=ctk.CTkFont(size=15, weight="bold"), text_color="#0f172a").pack(anchor="w", padx=20, pady=(16, 10))

        # Watch Folder
        ctk.CTkLabel(card, text="Watch Directory (Incoming files monitored by watchdog):", font=ctk.CTkFont(size=11, weight="bold"), text_color="#64748b").pack(anchor="w", padx=20)
        w_box = ctk.CTkFrame(card, fg_color="transparent")
        w_box.pack(fill="x", padx=20, pady=(2, 10))

        self.txt_watch_dir = ctk.CTkEntry(w_box, height=32, font=ctk.CTkFont(size=12))
        self.txt_watch_dir.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.txt_watch_dir.insert(0, str(config.watch_dir.resolve()))

        btn_w_open = ctk.CTkButton(w_box, text="Open in Explorer", width=120, height=32, fg_color="#f1f5f9", text_color="#0f172a", border_width=1, border_color="#cbd5e1", command=lambda: open_in_file_explorer(self.txt_watch_dir.get()))
        btn_w_open.pack(side="left", padx=4)

        # Workspace Folder
        ctk.CTkLabel(card, text="Workspace Directory (Destination folders Documents, Images, Inbox):", font=ctk.CTkFont(size=11, weight="bold"), text_color="#64748b").pack(anchor="w", padx=20)
        ws_box = ctk.CTkFrame(card, fg_color="transparent")
        ws_box.pack(fill="x", padx=20, pady=(2, 16))

        self.txt_workspace_dir = ctk.CTkEntry(ws_box, height=32, font=ctk.CTkFont(size=12))
        self.txt_workspace_dir.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.txt_workspace_dir.insert(0, str(config.workspace_dir.resolve()))

        btn_ws_open = ctk.CTkButton(ws_box, text="Open in Explorer", width=120, height=32, fg_color="#f1f5f9", text_color="#0f172a", border_width=1, border_color="#cbd5e1", command=lambda: open_in_file_explorer(self.txt_workspace_dir.get()))
        btn_ws_open.pack(side="left", padx=4)

        # AI / LLM Card
        llm_card = ctk.CTkFrame(frame, fg_color="#ffffff", corner_radius=12, border_width=1, border_color="#e2e8f0")
        llm_card.pack(fill="x", pady=8, padx=4)

        ctk.CTkLabel(llm_card, text="Local LLM Engine (Ollama)", font=ctk.CTkFont(size=15, weight="bold"), text_color="#0f172a").pack(anchor="w", padx=20, pady=(16, 10))

        # Host
        ctk.CTkLabel(llm_card, text="Ollama Host URL:", font=ctk.CTkFont(size=11, weight="bold"), text_color="#64748b").pack(anchor="w", padx=20)
        h_box = ctk.CTkFrame(llm_card, fg_color="transparent")
        h_box.pack(fill="x", padx=20, pady=(2, 10))

        self.txt_ollama_host = ctk.CTkEntry(h_box, height=32, font=ctk.CTkFont(size=12))
        self.txt_ollama_host.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.txt_ollama_host.insert(0, config.ollama_host)

        btn_test_ollama = ctk.CTkButton(h_box, text="Test Connection", width=120, height=32, fg_color="#2563eb", hover_color="#1d4ed8", command=self.action_test_ollama)
        btn_test_ollama.pack(side="left", padx=4)

        # Model & Threshold
        row2 = ctk.CTkFrame(llm_card, fg_color="transparent")
        row2.pack(fill="x", padx=20, pady=(0, 16))

        ctk.CTkLabel(row2, text="Model:", font=ctk.CTkFont(size=11, weight="bold"), text_color="#64748b").pack(side="left", padx=(0, 8))
        self.combo_model = ctk.CTkComboBox(row2, values=["llama3:8b", "llama3:latest", "qwen2.5:7b", "qwen:7b", "mistral:latest"], width=160)
        self.combo_model.set(config.ollama_model)
        self.combo_model.pack(side="left", padx=(0, 24))

        ctk.CTkLabel(row2, text="Routing Confidence Threshold:", font=ctk.CTkFont(size=11, weight="bold"), text_color="#64748b").pack(side="left", padx=(0, 8))
        self.slider_thresh = ctk.CTkSlider(row2, from_=50, to=95, number_of_steps=9, width=160, command=self._on_thresh_change)
        self.slider_thresh.set(config.confidence_threshold)
        self.slider_thresh.pack(side="left", padx=(0, 8))

        self.lbl_thresh_val = ctk.CTkLabel(row2, text=f"{config.confidence_threshold}%", font=ctk.CTkFont(size=12, weight="bold"), text_color="#0f172a")
        self.lbl_thresh_val.pack(side="left")

        # Save Settings
        btn_save_all = ctk.CTkButton(
            frame,
            text="Save All Configuration",
            font=ctk.CTkFont(size=13, weight="bold"),
            fg_color="#0f172a",
            hover_color="#000000",
            height=38,
            command=self.action_save_settings
        )
        btn_save_all.pack(pady=12)

        return frame

    def _on_thresh_change(self, val):
        self.lbl_thresh_val.configure(text=f"{int(val)}%")

    def action_save_settings(self):
        w_dir = self.txt_watch_dir.get().strip()
        ws_dir = self.txt_workspace_dir.get().strip()
        o_host = self.txt_ollama_host.get().strip()
        o_model = self.combo_model.get()
        thresh = int(self.slider_thresh.get())

        config.update({
            "watch_dir": w_dir,
            "workspace_dir": ws_dir,
            "ollama_host": o_host,
            "ollama_model": o_model,
            "confidence_threshold": thresh
        })
        engine.watcher.update_watch_dir(w_dir)
        engine.watch_dir = Path(w_dir)
        engine.workspace_dir = Path(ws_dir)
        engine.router.ollama_host = o_host
        engine.router.model_name = o_model
        engine.router.confidence_threshold = thresh

        messagebox.showinfo("Saved", "Settings saved and applied successfully!")

    def action_test_ollama(self):
        st = engine.router.check_ollama_status()
        if st.get("online"):
            messagebox.showinfo("Ollama Connected", f"Successfully connected to Ollama!\nHost: {st.get('host')}\nAvailable Models: {', '.join(st.get('available_models', []))}")
        else:
            messagebox.showwarning("Ollama Offline", f"Could not reach Ollama at {st.get('host')}.\n\nSortiFile will continue operating smoothly in Smart Fallback Mode!")

    # ========================================================
    # ACTIONS & ASYNC POLLING
    # ========================================================
    def action_select_file(self):
        """Allows user to select files from desktop to sort."""
        paths = filedialog.askopenfilenames(title="Select files to sort with SortiFile")
        if not paths:
            return
        count = 0
        for p in paths:
            queued = engine.process_direct_file(p)
            if queued:
                count += 1
        messagebox.showinfo("Queued", f"Enqueued {count} file(s) for sorting!")

    def action_scan(self):
        cnt = engine.scan_watch_directory()
        if cnt > 0:
            messagebox.showinfo("Scan Complete", f"Found and queued {cnt} incoming file(s)!")
        else:
            messagebox.showinfo("Scan Complete", "Watch directory is clear. No new files found.")

    def action_undo(self):
        success, msg, entry = engine.undo_engine.undo_last_action()
        if success:
            messagebox.showinfo("Undone", f"Restored {entry.get('file_name')} to original location.")
            self.refresh_history_view()
        else:
            messagebox.showwarning("Notice", msg)

    def action_undo_id(self, action_id: str):
        success, msg, entry = engine.undo_engine.undo_action_by_id(action_id)
        if success:
            messagebox.showinfo("Undone", f"Restored {entry.get('file_name')} to original location.")
            self.refresh_history_view()
        else:
            messagebox.showwarning("Notice", msg)

    def _poll_updates(self):
        """Periodically refreshes live queue, stats, and Ollama status."""
        try:
            # 1. Update status badges
            ollama_status = engine.router.check_ollama_status()
            if ollama_status.get("online"):
                self.btn_ollama.configure(text=f"● Ollama: {engine.router.model_name}", text_color="#10b981")
            else:
                self.btn_ollama.configure(text="● Smart Fallback Mode", text_color="#f59e0b")

            # 2. Update Stats
            history = engine.history.load_history()
            active_q = [q for q in engine.queue.get_active_items() if q.status not in ("completed", "failed")]

            self.stat_cards["total_sorted"].configure(text=str(len(history)))
            self.stat_cards["active_queue"].configure(text=str(len(active_q)))
            self.lbl_queue_count.configure(text=f"{len(active_q)} files")

            if history:
                avg = int(sum(h.get("confidence_score", 0) for h in history) / len(history))
                self.stat_cards["avg_conf"].configure(text=f"{avg}%")
            else:
                self.stat_cards["avg_conf"].configure(text="--%")

            avail_folders = get_available_folders(engine.workspace_dir, config.get("excluded_folders", []))
            self.stat_cards["folders_count"].configure(text=str(len(avail_folders)))

            # 3. Update Queue List UI
            if active_q:
                self.lbl_empty_queue.pack_forget()
                # Render active items
                for w in self.queue_scroll.winfo_children():
                    if w != self.lbl_empty_queue:
                        w.destroy()

                for item in active_q[:6]:
                    row = ctk.CTkFrame(self.queue_scroll, fg_color="#f8fafc", corner_radius=6)
                    row.pack(fill="x", pady=3)

                    ctk.CTkLabel(row, text=f"📄 {item.file_path.name}", font=ctk.CTkFont(size=12, weight="bold"), text_color="#0f172a").pack(side="left", padx=10, pady=4)
                    ctk.CTkLabel(row, text=f"Stage: {item.status.replace('_', ' ').title()}", font=ctk.CTkFont(size=11), text_color="#2563eb").pack(side="right", padx=10, pady=4)
            else:
                if not self.lbl_empty_queue.winfo_ismapped():
                    self.lbl_empty_queue.pack(pady=20)

            # 4. Update Recent Activity in Dashboard
            if self.active_tab == "dashboard":
                for w in self.act_scroll.winfo_children():
                    w.destroy()

                for item in history[:6]:
                    row = ctk.CTkFrame(self.act_scroll, fg_color="#f8fafc", corner_radius=8, border_width=1, border_color="#e2e8f0")
                    row.pack(fill="x", pady=4, padx=2)

                    ctk.CTkLabel(row, text=f"{item.get('file_name', '')}", font=ctk.CTkFont(size=12, weight="bold"), text_color="#0f172a").pack(side="left", padx=10, pady=6)
                    ctk.CTkLabel(row, text=f"→ {item.get('target_folder', '')}", font=ctk.CTkFont(size=11, weight="bold"), fg_color="#e2e8f0", text_color="#1e293b", corner_radius=6, padx=6, pady=2).pack(side="left", padx=6)
                    ctk.CTkLabel(row, text=f"{item.get('confidence_score', 0)}%", font=ctk.CTkFont(size=11), text_color="#475569").pack(side="left", padx=6)
                    ctk.CTkLabel(row, text=f"({item.get('reasoning', '')[:35]}...)", font=ctk.CTkFont(size=11), text_color="#94a3b8").pack(side="left", padx=6)

                    if item.get("status") == "moved":
                        btn_u = ctk.CTkButton(
                            row,
                            text="Undo",
                            font=ctk.CTkFont(size=11, weight="bold"),
                            width=54,
                            height=22,
                            fg_color="#ffffff",
                            text_color="#0f172a",
                            border_width=1,
                            border_color="#cbd5e1",
                            hover_color="#f1f5f9",
                            command=lambda aid=item.get("id"): self.action_undo_id(aid)
                        )
                        btn_u.pack(side="right", padx=8)

        except Exception as e:
            pass

        # Poll again in 1.5 seconds
        self.after(1500, self._poll_updates)


def main():
    app = SortiFileApp()
    app.mainloop()


if __name__ == "__main__":
    main()

