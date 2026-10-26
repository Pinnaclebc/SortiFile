/**
 * SortiFile Dashboard Frontend Application
 * WebSocket listener, live queue tracker, folder context editor, dynamic theming engine.
 */

// Application State
const state = {
    currentTab: 'dashboard',
    ws: null,
    queue: [],
    history: [],
    folders: [],
    status: {},
    settings: {},
    theme: {}
};

// Preset themes
const THEME_PRESETS = {
    mono: {
        bg_color: '#f8f9fa',
        sidebar_color: '#0a0a0a',
        card_bg: '#ffffff',
        accent_color: '#2563eb',
        text_primary: '#0f172a',
        text_secondary: '#64748b',
        border_color: '#e2e8f0'
    },
    midnight: {
        bg_color: '#0f172a',
        sidebar_color: '#020617',
        card_bg: '#1e293b',
        accent_color: '#38bdf8',
        text_primary: '#f8fafc',
        text_secondary: '#94a3b8',
        border_color: '#334155'
    },
    emerald: {
        bg_color: '#f0fdf4',
        sidebar_color: '#064e3b',
        card_bg: '#ffffff',
        accent_color: '#059669',
        text_primary: '#064e3b',
        text_secondary: '#047857',
        border_color: '#bbf7d0'
    },
    graphite: {
        bg_color: '#18181b',
        sidebar_color: '#09090b',
        card_bg: '#27272a',
        accent_color: '#a855f7',
        text_primary: '#fafafa',
        text_secondary: '#a1a1aa',
        border_color: '#3f3f46'
    }
};

// DOM Initialization
document.addEventListener('DOMContentLoaded', () => {
    initNavigation();
    initDropZone();
    initWebSocket();
    loadFolders();
    loadSettings();
    loadHistory();
});

// Toast notification helper
function showToast(message, type = 'info') {
    const container = document.getElementById('toast-container');
    const toast = document.createElement('div');
    toast.className = 'toast';
    toast.innerHTML = `<span>${message}</span>`;
    container.appendChild(toast);
    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transform = 'translateY(10px)';
        setTimeout(() => toast.remove(), 200);
    }, 3500);
}

// Navigation between views
function initNavigation() {
    const navItems = document.querySelectorAll('.nav-item[data-tab]');
    navItems.forEach(item => {
        item.addEventListener('click', () => {
            const tab = item.dataset.tab;
            switchTab(tab);
        });
    });
}

function switchTab(tabId) {
    state.currentTab = tabId;
    document.querySelectorAll('.nav-item[data-tab]').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tab === tabId);
    });

    document.querySelectorAll('.view-tab').forEach(view => {
        view.style.display = view.id === `view-${tabId}` ? 'block' : 'none';
    });

    if (tabId === 'folders') loadFolders();
    if (tabId === 'history') loadHistory();
    if (tabId === 'settings') loadSettings();
}

// ==========================================
// Dynamic Theming Engine (Module 5D)
// ==========================================
function applyTheme(theme) {
    if (!theme) return;
    state.theme = theme;
    const root = document.documentElement;
    if (theme.bg_color) root.style.setProperty('--bg-color', theme.bg_color);
    if (theme.sidebar_color) root.style.setProperty('--sidebar-color', theme.sidebar_color);
    if (theme.card_bg) root.style.setProperty('--card-bg', theme.card_bg);
    if (theme.accent_color) root.style.setProperty('--accent-color', theme.accent_color);
    if (theme.text_primary) root.style.setProperty('--text-primary', theme.text_primary);
    if (theme.text_secondary) root.style.setProperty('--text-secondary', theme.text_secondary);
    if (theme.border_color) root.style.setProperty('--border-color', theme.border_color);

    // Sync input color values if settings tab is visible
    syncThemeInputs(theme);
}

function syncThemeInputs(theme) {
    for (const [key, val] of Object.entries(theme)) {
        const input = document.getElementById(`theme-${key}`);
        if (input && val) {
            input.value = val;
        }
    }
}

function selectThemePreset(presetKey) {
    const preset = THEME_PRESETS[presetKey];
    if (preset) {
        applyTheme(preset);
        saveTheme(preset);
        showToast(`Theme preset '${presetKey}' applied`);
    }
}

async function saveTheme(theme) {
    try {
        await fetch('/api/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ theme })
        });
    } catch (e) {
        console.error('Failed to persist theme:', e);
    }
}

// ==========================================
// WebSocket Listener (Module 5A & 5C)
// ==========================================
function initWebSocket() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${window.location.host}/ws`;

    state.ws = new WebSocket(wsUrl);

    state.ws.onopen = () => {
        console.log('[WS] Connected to SortiFile engine');
    };

    state.ws.onmessage = (event) => {
        try {
            const msg = JSON.parse(event.data);
            handleWebSocketMessage(msg);
        } catch (e) {
            console.error('[WS] Parse error:', e);
        }
    };

    state.ws.onclose = () => {
        console.log('[WS] Disconnected, retrying in 2s...');
        setTimeout(initWebSocket, 2000);
    };
}

function handleWebSocketMessage(msg) {
    const type = msg.type;
    const data = msg.data;

    if (type === 'init') {
        state.status = data.status || {};
        state.queue = data.queue || [];
        state.history = data.history || [];
        applyTheme(data.theme);
        updateStatusHeader();
        renderQueue();
        renderRecentActivity();
        updateStats();
    } else if (type === 'file_detected') {
        showToast(`New file detected: ${data.file_name}`);
    } else if (type === 'file_status') {
        updateQueueItem(data);
    } else if (type === 'file_sorted') {
        showToast(`Sorted: ${data.file_name} -> ${data.target_folder} (${data.confidence_score}%)`);
        loadHistory();
        loadFolders();
        updateStats();
    } else if (type === 'engine_status') {
        state.status = data;
        updateStatusHeader();
        updateStats();
    } else if (type === 'history_updated') {
        loadHistory();
        loadFolders();
        updateStats();
    } else if (type === 'settings_updated') {
        if (data.theme) applyTheme(data.theme);
    }
}

function updateStatusHeader() {
    const watcherDot = document.getElementById('watcher-pulse');
    const watcherText = document.getElementById('watcher-status-text');
    const ollamaPill = document.getElementById('ollama-pill-text');
    const ollamaDot = document.getElementById('ollama-pulse');

    if (state.status.watcher_running) {
        watcherDot.className = 'pulse-dot';
        watcherText.textContent = 'Monitoring Active';
    } else {
        watcherDot.className = 'pulse-dot amber';
        watcherText.textContent = 'Monitoring Paused';
    }

    const ollama = state.status.ollama || {};
    if (ollama.online) {
        ollamaDot.className = 'pulse-dot';
        ollamaPill.textContent = `Ollama: ${ollama.current_model || 'online'}`;
    } else {
        ollamaDot.className = 'pulse-dot amber';
        ollamaPill.textContent = 'Ollama: Offline (Smart Fallback Mode)';
    }
}

// ==========================================
// Live Queue Rendering (Module 5C)
// ==========================================
function updateQueueItem(itemData) {
    const index = state.queue.findIndex(q => q.file_path === itemData.file_path);
    if (index >= 0) {
        if (itemData.status === 'completed' || itemData.status === 'failed') {
            // Keep completed briefly or remove
            state.queue[index] = itemData;
            setTimeout(() => {
                state.queue = state.queue.filter(q => q.file_path !== itemData.file_path);
                renderQueue();
            }, 3000);
        } else {
            state.queue[index] = itemData;
        }
    } else if (itemData.status !== 'completed') {
        state.queue.unshift(itemData);
    }
    renderQueue();
    updateStats();
}

function renderQueue() {
    const container = document.getElementById('queue-list');
    const countBadge = document.getElementById('queue-badge-count');
    const activeItems = state.queue.filter(q => q.status !== 'completed' && q.status !== 'failed');
    countBadge.textContent = activeItems.length;

    if (state.queue.length === 0) {
        container.innerHTML = `
            <div class="queue-empty">
                <p>Queue is empty. Drop files above or place them in the Watch Folder to sort.</p>
            </div>
        `;
        return;
    }

    container.innerHTML = state.queue.map(item => {
        const ext = item.file_name ? item.file_name.split('.').pop() : 'FILE';
        const stages = ['checking_lock', 'reading', 'routing', 'moving'];
        const currentStageIdx = stages.indexOf(item.status);

        const stageLabels = [
            { key: 'checking_lock', label: '1. Lock Check' },
            { key: 'reading', label: '2. Text Extract' },
            { key: 'routing', label: '3. AI Routing' },
            { key: 'moving', label: '4. Moving' },
        ];

        const stepperHtml = stageLabels.map((st, idx) => {
            let cls = 'stage-step';
            if (item.status === 'completed') cls += ' done';
            else if (st.key === item.status) cls += ' active';
            else if (currentStageIdx > idx) cls += ' done';
            return `<div class="${cls}">${st.label}</div>`;
        }).join('');

        let statusBadge = '';
        if (item.status === 'completed') {
            statusBadge = `<span class="tag-badge" style="background:#f0fdf4; color:#16a34a;">✓ Completed</span>`;
        } else if (item.status === 'failed') {
            statusBadge = `<span class="tag-badge" style="background:#fef2f2; color:#dc2626;">✕ Error: ${item.error_message || 'Failed'}</span>`;
        }

        return `
            <div class="queue-card">
                <div class="queue-card-left">
                    <div class="file-type-icon">${ext}</div>
                    <div>
                        <div class="file-info-name" title="${item.file_name}">${item.file_name}</div>
                        <div class="file-info-sub">${item.category} • ${item.status.replace('_', ' ')}</div>
                    </div>
                </div>
                <div style="display:flex; align-items:center; gap:16px;">
                    <div class="pipeline-stages">${stepperHtml}</div>
                    ${statusBadge}
                </div>
            </div>
        `;
    }).join('');
}

// ==========================================
// Recent Activity & History
// ==========================================
async function loadHistory() {
    try {
        const res = await fetch('/api/history?limit=50');
        const data = await res.json();
        state.history = data.history || [];
        renderRecentActivity();
        renderFullHistory();
        updateStats();
    } catch (e) {
        console.error('Failed to load history:', e);
    }
}

function renderRecentActivity() {
    const tbody = document.getElementById('recent-activity-tbody');
    if (!tbody) return;

    const recent = state.history.slice(0, 8);
    if (recent.length === 0) {
        tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; padding:32px; color:var(--text-secondary);">No sorted files yet.</td></tr>`;
        return;
    }

    tbody.innerHTML = recent.map(row => createHistoryRowHtml(row, false)).join('');
}

function renderFullHistory() {
    const tbody = document.getElementById('full-history-tbody');
    if (!tbody) return;

    if (state.history.length === 0) {
        tbody.innerHTML = `<tr><td colspan="7" style="text-align:center; padding:32px; color:var(--text-secondary);">No history records found.</td></tr>`;
        return;
    }

    tbody.innerHTML = state.history.map(row => createHistoryRowHtml(row, true)).join('');
}

function createHistoryRowHtml(row, showFullDetails = false) {
    const isInbox = row.target_folder === 'Inbox';
    const folderClass = isInbox ? 'tag-badge inbox' : 'tag-badge folder';
    const conf = row.confidence_score || 0;
    const barClass = conf >= 75 ? 'bar-fill' : conf >= 50 ? 'bar-fill amber' : 'bar-fill red';

    const isReverted = row.status === 'reverted';
    const undoButton = isReverted
        ? `<span style="font-size:12px; color:var(--text-secondary); font-style:italic;">Undone</span>`
        : `<button class="btn btn-outline btn-sm" onclick="triggerUndo('${row.id}')">Undo</button>`;

    const timeFormatted = new Date(row.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

    return `
        <tr>
            <td style="font-weight:600;">${row.file_name}</td>
            <td><span class="${folderClass}">${row.target_folder}</span></td>
            <td>
                <div class="confidence-bar">
                    <div class="bar-bg"><div class="${barClass}" style="width: ${conf}%;"></div></div>
                    <span style="font-weight:600; font-size:12px;">${conf}%</span>
                </div>
            </td>
            <td style="max-width: 240px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color:var(--text-secondary);" title="${row.reasoning}">
                ${row.reasoning || '-'}
            </td>
            <td style="color:var(--text-secondary); font-size:12px;">${timeFormatted}</td>
            <td>${undoButton}</td>
        </tr>
    `;
}

// ==========================================
// Stats Calculation
// ==========================================
function updateStats() {
    const totalSortedEl = document.getElementById('stat-total-sorted');
    const activeQueueEl = document.getElementById('stat-active-queue');
    const accuracyEl = document.getElementById('stat-avg-confidence');
    const foldersCountEl = document.getElementById('stat-folders-count');

    if (totalSortedEl) totalSortedEl.textContent = state.history.length;
    if (activeQueueEl) activeQueueEl.textContent = state.queue.filter(q => q.status !== 'completed' && q.status !== 'failed').length;

    if (accuracyEl) {
        if (state.history.length > 0) {
            const sum = state.history.reduce((acc, h) => acc + (h.confidence_score || 0), 0);
            const avg = Math.round(sum / state.history.length);
            accuracyEl.textContent = `${avg}%`;
        } else {
            accuracyEl.textContent = '--%';
        }
    }

    if (foldersCountEl) foldersCountEl.textContent = state.folders.length || 3;
}

// ==========================================
// Folders & Context Editor (Module 2C & 3C)
// ==========================================
async function loadFolders() {
    try {
        const res = await fetch('/api/folders');
        const data = await res.json();
        state.folders = data.folders || [];
        renderFolders();
        updateStats();
    } catch (e) {
        console.error('Failed to load folders:', e);
    }
}

function renderFolders() {
    const grid = document.getElementById('folders-grid');
    if (!grid) return;

    grid.innerHTML = state.folders.map(f => {
        const escapedPath = f.path.replace(/\\/g, '\\\\');
        const deleteBtn = f.is_deletable ? `
            <button class="btn btn-outline btn-sm btn-danger-hover" onclick="deleteFolder('${f.name}', ${f.file_count})" title="Delete folder">
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                Delete
            </button>
        ` : '';

        return `
            <div class="folder-card">
                <div class="folder-header">
                    <div class="folder-name">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path></svg>
                        ${f.name}
                    </div>
                    <span class="badge-count">${f.file_count} files</span>
                </div>

                <div class="folder-path-bar" title="Click to copy full path" onclick="copyPathToClipboard('${escapedPath}')">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
                    <span class="folder-path-text">${f.path}</span>
                    <span class="copy-tag">Copy</span>
                </div>

                <div style="font-size:11px; text-transform:uppercase; font-weight:600; color:var(--text-secondary); margin: 10px 0 6px;">
                    .context.md definition
                </div>
                <textarea class="context-textarea" id="context-text-${f.name}">${f.context || ''}</textarea>
                
                <div style="display:flex; justify-content:space-between; align-items:center; gap:8px; margin-top:4px;">
                    <button class="btn btn-secondary btn-sm" onclick="openFolderInExplorer('${f.name}')" title="Open directory in Windows File Explorer">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><polyline points="15 3 21 3 21 9"></polyline><line x1="10" y1="14" x2="21" y2="3"></line></svg>
                        Open in Explorer
                    </button>
                    <div style="display:flex; gap:6px;">
                        ${deleteBtn}
                        <button class="btn btn-primary btn-sm" onclick="saveFolderContext('${f.name}')">Save Context</button>
                    </div>
                </div>
            </div>
        `;
    }).join('');
}

function copyPathToClipboard(text) {
    if (navigator.clipboard) {
        navigator.clipboard.writeText(text).then(() => {
            showToast('Path copied to clipboard!');
        });
    } else {
        // Fallback
        const el = document.createElement('textarea');
        el.value = text;
        document.body.appendChild(el);
        el.select();
        document.execCommand('copy');
        document.body.removeChild(el);
        showToast('Path copied to clipboard!');
    }
}

async function openFolderInExplorer(folderName) {
    try {
        showToast(`Opening '${folderName}' in File Explorer...`);
        const res = await fetch(`/api/folders/${encodeURIComponent(folderName)}/open`, { method: 'POST' });
        const data = await res.json();
        if (res.ok) {
            showToast(`Opened: ${folderName}`);
        } else {
            showToast(data.detail || 'Could not open folder', 'error');
        }
    } catch (e) {
        showToast(`Error: ${e.message}`, 'error');
    }
}

async function openPathInExplorer(path) {
    try {
        const res = await fetch('/api/open-path', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ path })
        });
        const data = await res.json();
        if (res.ok) {
            showToast('Opened in File Explorer');
        } else {
            showToast(data.detail || 'Could not open path', 'error');
        }
    } catch (e) {
        showToast(`Error: ${e.message}`, 'error');
    }
}

async function deleteFolder(folderName, fileCount) {
    let force = false;
    if (fileCount > 0) {
        const proceed = confirm(`Folder '${folderName}' contains ${fileCount} file(s).\n\nDo you want to safely move all remaining files into the Inbox and delete this folder?`);
        if (!proceed) return;
        force = true;
    } else {
        const proceed = confirm(`Are you sure you want to delete the folder '${folderName}'?`);
        if (!proceed) return;
    }

    try {
        const res = await fetch(`/api/folders/${encodeURIComponent(folderName)}?force=${force}`, {
            method: 'DELETE'
        });
        const data = await res.json();
        if (res.ok) {
            showToast(`Folder '${folderName}' deleted successfully.`);
            loadFolders();
        } else {
            showToast(data.detail || 'Failed to delete folder', 'error');
        }
    } catch (e) {
        showToast(`Error: ${e.message}`, 'error');
    }
}

async function saveFolderContext(folderName) {
    const textarea = document.getElementById(`context-text-${folderName}`);
    if (!textarea) return;

    try {
        const res = await fetch(`/api/folders/${encodeURIComponent(folderName)}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ context: textarea.value })
        });
        if (res.ok) {
            showToast(`Context for '${folderName}' updated!`);
            loadFolders();
        } else {
            showToast(`Failed to update context`, 'error');
        }
    } catch (e) {
        showToast(`Error: ${e.message}`, 'error');
    }
}

function openCreateFolderModal() {
    document.getElementById('create-folder-modal').style.display = 'flex';
}

function closeCreateFolderModal() {
    document.getElementById('create-folder-modal').style.display = 'none';
}

async function submitCreateFolder() {
    const nameInput = document.getElementById('new-folder-name');
    const descInput = document.getElementById('new-folder-desc');
    const name = nameInput.value.trim();
    if (!name) return;

    try {
        const res = await fetch('/api/folders', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name, context: descInput.value.trim() })
        });
        if (res.ok) {
            showToast(`Folder '${name}' created!`);
            nameInput.value = '';
            descInput.value = '';
            closeCreateFolderModal();
            loadFolders();
        } else {
            const err = await res.json();
            showToast(err.detail || 'Failed to create folder', 'error');
        }
    } catch (e) {
        showToast(`Error: ${e.message}`, 'error');
    }
}

// ==========================================
// Drop Zone & Manual Upload
// ==========================================
function initDropZone() {
    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('file-upload-input');
    if (!dropZone || !fileInput) return;

    ['dragenter', 'dragover'].forEach(eventName => {
        dropZone.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.add('dragover');
        });
    });

    ['dragleave', 'drop'].forEach(eventName => {
        dropZone.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.remove('dragover');
        });
    });

    dropZone.addEventListener('drop', (e) => {
        const files = e.dataTransfer.files;
        if (files.length > 0) {
            handleUploadFiles(files);
        }
    });

    dropZone.addEventListener('click', () => {
        fileInput.click();
    });

    fileInput.addEventListener('change', () => {
        if (fileInput.files.length > 0) {
            handleUploadFiles(fileInput.files);
        }
    });
}

async function handleUploadFiles(files) {
    for (const file of files) {
        const formData = new FormData();
        formData.append('file', file);
        try {
            showToast(`Uploading ${file.name} to sorting pipeline...`);
            const res = await fetch('/api/upload', {
                method: 'POST',
                body: formData
            });
            const data = await res.json();
            if (data.success) {
                showToast(`File ${file.name} queued for AI sorting`);
            }
        } catch (e) {
            showToast(`Upload failed: ${e.message}`, 'error');
        }
    }
}

// ==========================================
// Operations: Undo & Scan
// ==========================================
async function triggerUndo(actionId = null) {
    try {
        const res = await fetch('/api/undo', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ action_id: actionId })
        });
        const data = await res.json();
        if (res.ok) {
            showToast(`Restored: ${data.entry.file_name} back to original folder`);
            loadHistory();
            loadFolders();
        } else {
            showToast(data.detail || 'Undo failed', 'error');
        }
    } catch (e) {
        showToast(`Undo error: ${e.message}`, 'error');
    }
}

async function triggerScan() {
    try {
        showToast('Scanning watch folder...');
        const res = await fetch('/api/scan', { method: 'POST' });
        const data = await res.json();
        if (data.queued_files_count > 0) {
            showToast(`Found and queued ${data.queued_files_count} file(s)!`);
        } else {
            showToast('Watch folder scanned: no new files found.');
        }
    } catch (e) {
        showToast(`Scan failed: ${e.message}`, 'error');
    }
}

// ==========================================
// Settings Management
// ==========================================
async function loadSettings() {
    try {
        const res = await fetch('/api/settings');
        const data = await res.json();
        state.settings = data;

        document.getElementById('setting-watch-dir').value = data.watch_dir || '';
        document.getElementById('setting-workspace-dir').value = data.workspace_dir || '';
        document.getElementById('setting-ollama-host').value = data.ollama_host || '';
        document.getElementById('setting-threshold').value = data.confidence_threshold || 75;
        document.getElementById('threshold-display').textContent = `${data.confidence_threshold || 75}%`;

        // Populate Ollama models
        const modelSelect = document.getElementById('setting-ollama-model');
        const models = data.alternative_models || ['llama3:8b', 'qwen2.5:7b'];
        modelSelect.innerHTML = models.map(m => `
            <option value="${m}" ${m === data.ollama_model ? 'selected' : ''}>${m}</option>
        `).join('');

        if (data.theme) {
            syncThemeInputs(data.theme);
        }
    } catch (e) {
        console.error('Failed to load settings:', e);
    }
}

async function saveGeneralSettings() {
    const watch_dir = document.getElementById('setting-watch-dir').value.trim();
    const workspace_dir = document.getElementById('setting-workspace-dir').value.trim();
    const ollama_host = document.getElementById('setting-ollama-host').value.trim();
    const ollama_model = document.getElementById('setting-ollama-model').value;
    const confidence_threshold = parseInt(document.getElementById('setting-threshold').value, 10);

    const payload = {
        watch_dir,
        workspace_dir,
        ollama_host,
        ollama_model,
        confidence_threshold
    };

    try {
        const res = await fetch('/api/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (res.ok) {
            showToast('Settings saved successfully!');
        } else {
            showToast('Failed to save settings', 'error');
        }
    } catch (e) {
        showToast(`Error: ${e.message}`, 'error');
    }
}

function updateThresholdDisplay(val) {
    document.getElementById('threshold-display').textContent = `${val}%`;
}

function onCustomColorChange(colorKey, value) {
    const updated = { ...state.theme, [colorKey]: value };
    applyTheme(updated);
    saveTheme(updated);
}

async function testOllamaConnection() {
    showToast('Testing Ollama endpoint...');
    try {
        const res = await fetch('/api/test-ollama');
        const data = await res.json();
        if (data.online) {
            showToast(`Ollama Connected! Detected ${data.available_models.length} model(s).`);
        } else {
            showToast(`Ollama offline at ${data.host}. Using heuristic fallback.`, 'warning');
        }
    } catch (e) {
        showToast(`Connection failed: ${e.message}`, 'error');
    }
}

