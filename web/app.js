// BackTrack Premium Client Logic

document.addEventListener('DOMContentLoaded', () => {
    // API Endpoints
    const API = {
        history: '/api/history',
        restore: '/api/restore',
        privateMode: '/api/private-mode',
        snapshots: '/api/snapshots',
        snapshotsCreate: '/api/snapshots/create',
        snapshotsRestore: '/api/snapshots/restore',
        snapshotsRestoreMerged: '/api/snapshots/restore-merged',
        snapshotsCreateFromWindows: '/api/snapshots/create-from-windows',
        snapshotsDelete: '/api/snapshots/delete',
        clipboard: '/api/clipboard',
        clipboardCopy: '/api/clipboard/copy',
        clipboardDelete: '/api/clipboard/delete',
        snapshotsAddItems: '/api/snapshots/add-items',
        snapshotsItemDelete: '/api/snapshots/item',
        windows: '/api/windows',
        windowsArrange: '/api/windows/arrange',
        windowsTileAll: '/api/windows/tile-all',
        windowsMerge: '/api/windows/merge',
        fsList: '/api/fs/list',
        fsOpen: '/api/fs/open',
        fsPick: '/api/fs/pick-folder'
    };

    // State Variables
    let isPrivateMode = false;
    let historyData = [];
    let clipboardData = [];
    let snapshotsData = [];
    let windowsData = [];
    let windowsJson = '';
    let selectedWindows = new Set();
    
    let activeTab = 'history-tab';
    let historyFilter = 'all';
    let historySearch = '';
    let clipboardSearch = '';

    // DOM Elements
    const navItems = document.querySelectorAll('.nav-item');
    const tabContents = document.querySelectorAll('.tab-content');
    
    // Private Mode
    const privateModeToggle = document.getElementById('privateModeToggle');
    const privateOverlay = document.getElementById('privateOverlay');
    const privateIcon = document.getElementById('privateIcon');
    const privateDesc = document.getElementById('privateDesc');
    const disablePrivateBtn = document.getElementById('disablePrivateBtn');

    // History Tab
    const historyList = document.getElementById('historyList');
    const historyEmptyState = document.getElementById('historyEmptyState');
    const historySearchInput = document.getElementById('historySearch');
    const historyFilterTabs = document.querySelectorAll('#history-tab .filter-tab');
    const clearHistoryBtn = document.getElementById('clearHistoryBtn');

    // Clipboard Tab
    const clipboardList = document.getElementById('clipboardList');
    const clipboardEmptyState = document.getElementById('clipboardEmptyState');
    const clipboardSearchInput = document.getElementById('clipboardSearch');
    const clearClipboardBtn = document.getElementById('clearClipboardBtn');

    // Snapshots Tab
    const snapshotsList = document.getElementById('snapshotsList');
    const snapshotsEmptyState = document.getElementById('snapshotsEmptyState');
    const saveSnapshotBtn = document.getElementById('saveSnapshotBtn');
    
    // Snapshot Modal
    const snapshotModal = document.getElementById('snapshotModal');
    const snapshotNameInput = document.getElementById('snapshotNameInput');
    const cancelSnapshotBtn = document.getElementById('cancelSnapshotBtn');
    const confirmSnapshotBtn = document.getElementById('confirmSnapshotBtn');

    // Toast Notification Container
    const toastContainer = document.getElementById('toastContainer');

    // --- INITIALIZATION ---
    syncPrivateMode();
    fetchData();

    // Poll the status periodically (every 1.5 seconds)
    setInterval(() => {
        syncPrivateMode().then(() => {
            if (!isPrivateMode) {
                fetchData();
            }
        });
    }, 1500);

    // --- NAVIGATION CONTROLLER ---
    navItems.forEach(item => {
        item.addEventListener('click', () => {
            navItems.forEach(i => i.classList.remove('active'));
            item.classList.add('active');
            
            activeTab = item.getAttribute('data-tab');
            tabContents.forEach(tab => {
                tab.classList.remove('active');
                if (tab.id === activeTab) {
                    tab.classList.add('active');
                }
            });
            fetchData();
        });
    });

    // --- PRIVATE MODE SYNC ---
    async function syncPrivateMode() {
        try {
            const response = await fetch(API.privateMode);
            if (response.ok) {
                const data = await response.json();
                if (isPrivateMode !== data.enabled) {
                    isPrivateMode = data.enabled;
                    updatePrivateModeUI();
                }
            }
        } catch (error) {
            console.error('Failed to sync private mode:', error);
        }
    }

    async function togglePrivateMode(enabled) {
        try {
            const response = await fetch(`${API.privateMode}?enabled=${enabled}`, { method: 'POST' });
            if (response.ok) {
                isPrivateMode = enabled;
                updatePrivateModeUI();
                showToast(enabled ? 'מצב פרטי הופעל. הניטור מושהה.' : 'מצב פרטי כבוי. הניטור הופעל מחדש.');
            }
        } catch (error) {
            console.error('Failed to toggle private mode:', error);
            showToast('שגיאה בשינוי מצב פרטי.');
        }
    }

    function updatePrivateModeUI() {
        privateModeToggle.checked = isPrivateMode;
        if (isPrivateMode) {
            privateOverlay.style.display = 'flex';
            privateIcon.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="#f87171" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="18" height="11" x="3" y="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 9.9-1"/></svg>`; // Unlocked or warning lock
            privateDesc.textContent = 'הניטור מושהה כעת';
            privateDesc.style.color = '#f87171';
        } else {
            privateOverlay.style.display = 'none';
            privateIcon.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="18" height="11" x="3" y="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>`; // Standard Lock
            privateDesc.textContent = 'הניטור פועל כסדרו';
            privateDesc.style.color = '';
        }
    }

    privateModeToggle.addEventListener('change', (e) => {
        togglePrivateMode(e.target.checked);
    });

    disablePrivateBtn.addEventListener('click', () => {
        togglePrivateMode(false);
    });

    // --- DATA FETCHER ---
    async function fetchData() {
        if (isPrivateMode) return;

        if (activeTab === 'history-tab') {
            fetchHistory();
        } else if (activeTab === 'clipboard-tab') {
            fetchClipboard();
        } else if (activeTab === 'snapshots-tab') {
            fetchSnapshots();
        } else if (activeTab === 'windows-tab') {
            fetchWindows();
        } else if (activeTab === 'foldertabs-tab') {
            ensureFolderTabsInit();
        }
    }

    // --- 1. HISTORY MANAGEMENT ---
    async function fetchHistory() {
        try {
            const response = await fetch(API.history);
            if (response.ok) {
                const data = await response.json();
                if (JSON.stringify(data) !== JSON.stringify(historyData)) {
                    historyData = data;
                    renderHistory();
                } else {
                    updateRelativeTimes();
                }
            }
        } catch (error) {
            console.error('Failed to fetch history:', error);
        }
    }

    function renderHistory() {
        const filtered = historyData.filter(item => {
            if (historyFilter !== 'all' && item.Type !== historyFilter) return false;
            if (historySearch) {
                const search = historySearch.toLowerCase();
                return item.Name.toLowerCase().includes(search) || item.Path.toLowerCase().includes(search);
            }
            return true;
        });

        if (filtered.length === 0) {
            historyList.style.display = 'none';
            historyEmptyState.style.display = 'flex';
            return;
        }

        historyList.style.display = 'flex';
        historyEmptyState.style.display = 'none';
        historyList.innerHTML = '';

        filtered.forEach(item => {
            const card = document.createElement('div');
            card.className = 'history-card';
            card.setAttribute('data-id', item.Id);

            const iconInfo = getIconDetails(item);

            card.innerHTML = `
                <div class="card-details">
                    <div class="card-icon ${iconInfo.class}" title="${iconInfo.title}">
                        ${iconInfo.svg}
                    </div>
                    <div class="card-info">
                        <div class="card-title-row">
                            <span class="card-name" title="${item.Name}">${item.Name}</span>
                            <span class="card-time" data-timestamp="${item.Timestamp}">${getRelativeTime(item.Timestamp)}</span>
                        </div>
                        <div class="card-path" title="לחץ להעתקת הנתיב">${item.Path}</div>
                    </div>
                </div>
                <div class="card-actions">
                    <button class="btn btn-secondary btn-sm btn-restore-item">שחזר</button>
                    <button class="btn-card-action btn-delete-card" title="מחק מההיסטוריה">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/>
                        </svg>
                    </button>
                </div>
            `;

            // Bind actions
            card.querySelector('.btn-restore-item').addEventListener('click', () => restoreHistoryItem(item));
            card.querySelector('.btn-delete-card').addEventListener('click', () => deleteHistoryItem(item.Id, card));
            card.querySelector('.card-path').addEventListener('click', () => {
                navigator.clipboard.writeText(item.Path);
                showToast(`הנתיב הועתק ללוח: ${item.Path}`);
            });

            historyList.appendChild(card);
        });
    }

    async function restoreHistoryItem(item) {
        try {
            const response = await fetch(`${API.restore}?id=${encodeURIComponent(item.Id)}`, { method: 'POST' });
            if (response.ok) {
                const res = await response.json();
                if (res.success) {
                    const card = historyList.querySelector(`[data-id="${item.Id}"]`);
                    if (card) {
                        card.classList.add('removing');
                        setTimeout(() => {
                            historyData = historyData.filter(x => x.Id !== item.Id);
                            renderHistory();
                        }, 250);
                    }
                    
                    let typeLabel = 'התיקייה';
                    if (item.Type === 'file') typeLabel = 'הקובץ';
                    else if (item.Type === 'app') typeLabel = 'האפליקציה';
                    
                    showToast(`${typeLabel} "${item.Name}" נפתח/ה בהצלחה.`);
                } else {
                    showToast('שגיאה בשחזור הפריט. ייתכן והקובץ או התיקייה אינם קיימים עוד.');
                }
            }
        } catch (error) {
            console.error('Restore error:', error);
            showToast('שגיאה בשחזור.');
        }
    }

    async function deleteHistoryItem(id, cardElement) {
        try {
            const response = await fetch(`${API.history}/delete?id=${encodeURIComponent(id)}`, { method: 'DELETE' });
            if (response.ok) {
                cardElement.classList.add('removing');
                setTimeout(() => {
                    historyData = historyData.filter(x => x.Id !== id);
                    renderHistory();
                }, 250);
            }
        } catch (error) {
            console.error('Delete history item error:', error);
        }
    }

    async function clearAllHistory() {
        try {
            const response = await fetch(API.history, { method: 'DELETE' });
            if (response.ok) {
                historyData = [];
                renderHistory();
                showToast('היסטוריית הסגירות נמחקה.');
            }
        } catch (error) {
            console.error('Clear history error:', error);
        }
    }

    // Bind History Filters & Search
    historySearchInput.addEventListener('input', (e) => {
        historySearch = e.target.value.trim();
        renderHistory();
    });

    historyFilterTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            historyFilterTabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            historyFilter = tab.getAttribute('data-filter');
            renderHistory();
        });
    });

    clearHistoryBtn.addEventListener('click', () => {
        if (confirm('האם אתה בטוח שברצונך למחוק את כל היסטוריית הסגירות?')) {
            clearAllHistory();
        }
    });

    // --- 2. CLIPBOARD HISTORY MANAGEMENT ---
    async function fetchClipboard() {
        try {
            const response = await fetch(API.clipboard);
            if (response.ok) {
                const data = await response.json();
                if (JSON.stringify(data) !== JSON.stringify(clipboardData)) {
                    clipboardData = data;
                    renderClipboard();
                }
            }
        } catch (error) {
            console.error('Failed to fetch clipboard history:', error);
        }
    }

    function renderClipboard() {
        const filtered = clipboardData.filter(item => {
            if (clipboardSearch) {
                return item.Content.toLowerCase().includes(clipboardSearch.toLowerCase());
            }
            return true;
        });

        if (filtered.length === 0) {
            clipboardList.style.display = 'none';
            clipboardEmptyState.style.display = 'flex';
            return;
        }

        clipboardList.style.display = 'grid';
        clipboardEmptyState.style.display = 'none';
        clipboardList.innerHTML = '';

        filtered.forEach(item => {
            const card = document.createElement('div');
            card.className = 'clipboard-card';
            card.setAttribute('data-id', item.Id);

            // Escape HTML characters to prevent XSS
            const escapedContent = escapeHtml(item.Content);

            card.innerHTML = `
                <div class="clip-content" title="לחץ להעתקה מהירה">${escapedContent}</div>
                <div class="clip-footer">
                    <span class="clip-time">${getRelativeTime(item.Timestamp)}</span>
                    <div class="clip-actions">
                        <button class="btn-card-action btn-copy-clip" title="העתק מחדש ללוח">
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <rect width="14" height="14" x="8" y="8" rx="2" ry="2"/>
                                <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>
                            </svg>
                        </button>
                        <button class="btn-card-action btn-delete-clip" title="מחק מההיסטוריה">
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/>
                            </svg>
                        </button>
                    </div>
                </div>
            `;

            // Clicking card content or copy button copies it back
            const performCopy = () => copyClipItem(item.Id, item.Content);
            card.querySelector('.clip-content').addEventListener('click', performCopy);
            card.querySelector('.btn-copy-clip').addEventListener('click', (e) => {
                e.stopPropagation();
                performCopy();
            });

            card.querySelector('.btn-delete-clip').addEventListener('click', (e) => {
                e.stopPropagation();
                deleteClipItem(item.Id, card);
            });

            clipboardList.appendChild(card);
        });
    }

    async function copyClipItem(id, text) {
        try {
            const response = await fetch(`${API.clipboardCopy}?id=${encodeURIComponent(id)}`, { method: 'POST' });
            if (response.ok) {
                const res = await response.json();
                if (res.success) {
                    showToast('הטקסט הועתק מחדש ללוח ההעתקה במחשב!');
                    
                    // Put it on the top of local array and re-render
                    fetchClipboard();
                }
            }
        } catch (error) {
            console.error('Copy clip item error:', error);
        }
    }

    async function deleteClipItem(id, cardElement) {
        try {
            const response = await fetch(`${API.clipboardDelete}?id=${encodeURIComponent(id)}`, { method: 'DELETE' });
            if (response.ok) {
                cardElement.classList.add('removing');
                setTimeout(() => {
                    clipboardData = clipboardData.filter(x => x.Id !== id);
                    renderClipboard();
                }, 250);
            }
        } catch (error) {
            console.error('Delete clip error:', error);
        }
    }

    async function clearAllClipboard() {
        try {
            const response = await fetch(API.clipboard, { method: 'DELETE' });
            if (response.ok) {
                clipboardData = [];
                renderClipboard();
                showToast('היסטוריית לוח ההעתקה נמחקה.');
            }
        } catch (error) {
            console.error('Clear clipboard error:', error);
        }
    }

    // Bind Clipboard Search & Clear
    clipboardSearchInput.addEventListener('input', (e) => {
        clipboardSearch = e.target.value.trim();
        renderClipboard();
    });

    clearClipboardBtn.addEventListener('click', () => {
        if (confirm('האם אתה בטוח שברצונך למחוק את כל היסטוריית לוח ההעתקה?')) {
            clearAllClipboard();
        }
    });

    // --- 3. WORKSPACE SNAPSHOTS MANAGEMENT ---
    async function fetchSnapshots() {
        try {
            const response = await fetch(API.snapshots);
            if (response.ok) {
                const data = await response.json();
                if (JSON.stringify(data) !== JSON.stringify(snapshotsData)) {
                    snapshotsData = data;
                    renderSnapshots();
                }
            }
        } catch (error) {
            console.error('Failed to fetch snapshots:', error);
        }
    }

    function renderSnapshots() {
        if (snapshotsData.length === 0) {
            snapshotsList.style.display = 'none';
            snapshotsEmptyState.style.display = 'flex';
            return;
        }

        snapshotsList.style.display = 'flex';
        snapshotsEmptyState.style.display = 'none';
        snapshotsList.innerHTML = '';

        snapshotsData.forEach(item => {
            const card = document.createElement('div');
            card.className = 'snapshot-card collapsed';

            const items = (item.Items && item.Items.length)
                ? item.Items
                : (item.Paths || []).map(p => ({ Path: p, Name: p, Type: 'folder' }));

            const counts = { folder: 0, app: 0, file: 0 };
            items.forEach(it => { counts[it.Type] = (counts[it.Type] || 0) + 1; });
            const metaParts = [];
            if (counts.app) metaParts.push(`${counts.app} תוכנות`);
            if (counts.folder) metaParts.push(`${counts.folder} תיקיות`);
            if (counts.file) metaParts.push(`${counts.file} מסמכים`);
            const metaText = (metaParts.join(' • ') || '0 פריטים') + ` • נשמר ב-${formatDateTime(item.Timestamp)}`;

            const itemsHtml = items.map(it => {
                const ic = getIconDetails({ Type: it.Type, Path: it.Path, Name: it.Name });
                return `<div class="snapshot-item-row" data-path="${escapeHtml(it.Path)}">
                    <span class="snapshot-item-icon ${ic.class}">${ic.svg}</span>
                    <span class="snapshot-item-name" title="${escapeHtml(it.Path)}">${escapeHtml(it.Name || it.Path)}</span>
                    <button class="btn-card-action btn-remove-item" title="הסר מהקבוצה">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>
                    </button>
                </div>`;
            }).join('');

            card.innerHTML = `
                <div class="snapshot-rowhead">
                    <span class="snap-chevron">▸</span>
                    <span class="snapshot-name">${escapeHtml(item.Name)}</span>
                    <span class="snapshot-count">${items.length} פריטים</span>
                </div>
                <div class="snapshot-body">
                    <div class="snapshot-meta-row">${metaText}</div>
                    <div class="card-actions">
                        <button class="btn btn-primary btn-sm btn-restore-merged">פתח כלשוניות</button>
                        <button class="btn btn-secondary btn-sm btn-restore-snapshot">חלונות נפרדים</button>
                        <button class="btn btn-secondary btn-sm btn-add-docs">הוסף מסמכים</button>
                        <button class="btn-card-action btn-delete-snapshot" title="מחק קבוצה">
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/>
                            </svg>
                        </button>
                    </div>
                    <div class="snapshot-paths">${itemsHtml}</div>
                </div>
            `;

            const head = card.querySelector('.snapshot-rowhead');
            head.addEventListener('click', () => {
                const exp = card.classList.toggle('expanded');
                card.classList.toggle('collapsed', !exp);
            });
            card.querySelector('.btn-restore-merged').addEventListener('click', (e) => { e.stopPropagation(); restoreMerged(item.Id, item.Name); });
            card.querySelector('.btn-restore-snapshot').addEventListener('click', (e) => { e.stopPropagation(); restoreSnapshot(item.Id, item.Name); });
            card.querySelector('.btn-delete-snapshot').addEventListener('click', (e) => { e.stopPropagation(); deleteSnapshot(item.Id, card); });
            card.querySelector('.btn-add-docs').addEventListener('click', (e) => { e.stopPropagation(); openAddDocsModal(item.Id); });
            card.querySelectorAll('.btn-remove-item').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const row = btn.closest('.snapshot-item-row');
                    if (row) removeSnapshotItem(item.Id, row.getAttribute('data-path'));
                });
            });

            snapshotsList.appendChild(card);
        });
    }

    async function restoreMerged(id, name) {
        try {
            const response = await fetch(`${API.snapshotsRestoreMerged}?id=${encodeURIComponent(id)}`, { method: 'POST' });
            if (response.ok) {
                const res = await response.json();
                showToast(res.success
                    ? `פותח את "${name}" כקבוצת לשוניות... (החלונות ייפתחו ויתאחדו)`
                    : 'הקבוצה ריקה.');
            }
        } catch (e) { console.error('restore merged', e); showToast('שגיאה בפתיחת הקבוצה.'); }
    }

    async function restoreSnapshot(id, name) {
        try {
            const response = await fetch(`${API.snapshotsRestore}?id=${encodeURIComponent(id)}`, { method: 'POST' });
            if (response.ok) {
                const res = await response.json();
                if (res.success) {
                    showToast(`כל הפריטים בקבוצה "${name}" נפתחו במחשב!`);
                } else {
                    showToast('שגיאה בפתיחת קבוצת התיקיות.');
                }
            }
        } catch (error) {
            console.error('Restore snapshot error:', error);
        }
    }

    async function deleteSnapshot(id, cardElement) {
        try {
            const response = await fetch(`${API.snapshotsDelete}?id=${encodeURIComponent(id)}`, { method: 'DELETE' });
            if (response.ok) {
                cardElement.style.opacity = 0;
                cardElement.style.transform = 'translateY(10px)';
                cardElement.style.transition = 'all 0.25s ease-out';
                setTimeout(() => {
                    snapshotsData = snapshotsData.filter(x => x.Id !== id);
                    renderSnapshots();
                }, 250);
            }
        } catch (error) {
            console.error('Delete snapshot error:', error);
        }
    }

    // Modal Prompts for Snapshot
    saveSnapshotBtn.addEventListener('click', () => {
        snapshotNameInput.value = '';
        snapshotModal.style.display = 'flex';
        snapshotNameInput.focus();
    });

    cancelSnapshotBtn.addEventListener('click', () => {
        snapshotModal.style.display = 'none';
    });

    confirmSnapshotBtn.addEventListener('click', () => {
        const name = snapshotNameInput.value.trim();
        createSnapshot(name);
        snapshotModal.style.display = 'none';
    });

    snapshotNameInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            const name = snapshotNameInput.value.trim();
            createSnapshot(name);
            snapshotModal.style.display = 'none';
        } else if (e.key === 'Escape') {
            snapshotModal.style.display = 'none';
        }
    });

    async function createSnapshot(name) {
        try {
            // Encode named parameter for query URL (Hebrew Support)
            const queryName = name ? `?name=${encodeURIComponent(name)}` : '';
            const response = await fetch(`${API.snapshotsCreate}${queryName}`, { method: 'POST' });
            if (response.ok) {
                const res = await response.json();
                if (res.success) {
                    showToast(name ? `קבוצת התיקיות "${name}" נשמרה בהצלחה!` : 'קבוצת התיקיות הנוכחית נשמרה בהצלחה!');
                    fetchSnapshots();
                } else {
                    showToast('לא נמצאו תיקיות פתוחות לשמירה כקבוצה.');
                }
            }
        } catch (error) {
            console.error('Create snapshot error:', error);
        }
    }

    // --- HELPER UTILITIES ---

    // Get icon class and SVG code
    function getIconDetails(item) {
        if (item.Type === 'folder') {
            return {
                class: 'icon-folder',
                title: 'תיקייה',
                svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/></svg>`
            };
        }
        if (item.Type === 'app') {
            return {
                class: 'icon-app',
                title: 'אפליקציה',
                svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="16" x="2" y="3" rx="2"/><path d="M12 17v4"/><path d="M8 21h8"/></svg>`
            };
        }

        // File: Determine by extension
        const ext = item.Path.split('.').pop().toLowerCase();

        if (ext === 'pdf') {
            return {
                class: 'icon-file',
                title: 'קובץ PDF',
                svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/><line x1="9" y1="15" x2="15" y2="15"/></svg>`
            };
        }
        if (['xlsx', 'xls', 'csv'].includes(ext)) {
            return {
                class: 'icon-file',
                title: 'גיליון נתונים (Excel)',
                svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="18" height="18" x="3" y="3" rx="2" ry="2"/><line x1="9" y1="3" x2="9" y2="21"/><line x1="15" y1="3" x2="15" y2="21"/><line x1="3" y1="9" x2="21" y2="9"/><line x1="3" y1="15" x2="21" y2="15"/></svg>`
            };
        }
        if (['png', 'jpg', 'jpeg', 'gif', 'svg', 'webp', 'bmp'].includes(ext)) {
            return {
                class: 'icon-file',
                title: 'קובץ תמונה',
                svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="18" height="18" x="3" y="3" rx="2" ry="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/></svg>`
            };
        }
        if (['cs', 'html', 'css', 'js', 'py', 'json', 'xml', 'cpp', 'h'].includes(ext)) {
            return {
                class: 'icon-file',
                title: 'קובץ קוד מקור',
                svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m18 16 4-4-4-4"/><path d="m6 8-4 4 4 4"/><path d="m14.5 4-5 16"/></svg>`
            };
        }
        if (['zip', 'rar', '7z', 'tar', 'gz'].includes(ext)) {
            return {
                class: 'icon-file',
                title: 'קובץ כיווץ (ארכיון)',
                svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="12" x="2" y="6" rx="2"/><path d="M12 12V6"/><path d="M12 18v-2"/></svg>`
            };
        }

        // Generic File
        return {
            class: 'icon-file',
            title: 'קובץ/מסמך',
            svg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/></svg>`
        };
    }

    // Update timestamps on cards dynamically
    function updateRelativeTimes() {
        document.querySelectorAll('.card-time').forEach(el => {
            const timestamp = el.getAttribute('data-timestamp');
            if (timestamp) {
                el.textContent = getRelativeTime(timestamp);
            }
        });
        document.querySelectorAll('.clip-time').forEach(el => {
            const timestamp = el.getAttribute('data-timestamp');
            if (timestamp) {
                el.textContent = getRelativeTime(timestamp);
            }
        });
    }

    // Relative time in Hebrew
    function getRelativeTime(timestampStr) {
        const now = new Date();
        const date = new Date(timestampStr);
        const diffMs = now - date;
        const diffSec = Math.floor(diffMs / 1000);
        const diffMin = Math.floor(diffSec / 60);
        const diffHr = Math.floor(diffMin / 60);
        const diffDay = Math.floor(diffHr / 24);

        if (diffSec < 5) return 'כרגע';
        if (diffSec < 60) return `לפני ${diffSec} שניות`;
        if (diffMin === 1) return 'לפני דקה';
        if (diffMin === 2) return 'לפני שתי דקות';
        if (diffMin < 60) return `לפני ${diffMin} דקות`;
        if (diffHr === 1) return 'לפני שעה';
        if (diffHr === 2) return 'לפני שעתיים';
        if (diffHr < 24) return `לפני ${diffHr} שעות`;
        if (diffDay === 1) return 'אתמול';
        if (diffDay === 2) return 'שלשום';
        return `לפני ${diffDay} ימים`;
    }

    function formatDateTime(timestampStr) {
        const d = new Date(timestampStr);
        const hours = String(d.getHours()).padStart(2, '0');
        const minutes = String(d.getMinutes()).padStart(2, '0');
        return `${d.getDate()}/${d.getMonth() + 1}/${d.getFullYear()} בשעה ${hours}:${minutes}`;
    }

    function escapeHtml(text) {
        return text
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    // Custom Toast Notification System
    function showToast(message) {
        const toast = document.createElement('div');
        toast.className = 'toast';
        toast.innerHTML = `
            <div class="toast-icon">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>
            </div>
            <div class="toast-message">${message}</div>
        `;
        toastContainer.appendChild(toast);

        // Auto remove toast after 3s
        setTimeout(() => {
            toast.classList.add('removing');
            setTimeout(() => {
                toast.remove();
            }, 250);
        }, 3000);
    }

    // --- 4. WINDOW ARRANGE (SNAP) MANAGEMENT ---
    const windowsList = document.getElementById('windowsList');
    const windowsEmptyState = document.getElementById('windowsEmptyState');
    const refreshWindowsBtn = document.getElementById('refreshWindowsBtn');
    const tileAllBtn = document.getElementById('tileAllBtn');
    const layoutBtns = document.querySelectorAll('.layout-btn');

    async function fetchWindows() {
        try {
            const r = await fetch(API.windows);
            if (!r.ok) return;
            const data = await r.json();
            const j = JSON.stringify(data);
            if (j !== windowsJson) {
                windowsJson = j;
                windowsData = data;
                renderWindows();
            }
        } catch (e) { console.error('Failed to fetch windows:', e); }
    }

    function renderWindows() {
        if (!windowsList) return;
        const present = new Set(windowsData.map(w => w.Handle));
        Array.from(selectedWindows).forEach(h => { if (!present.has(h)) selectedWindows.delete(h); });

        if (windowsData.length === 0) {
            windowsList.style.display = 'none';
            if (windowsEmptyState) windowsEmptyState.style.display = 'flex';
            return;
        }
        windowsList.style.display = 'flex';
        if (windowsEmptyState) windowsEmptyState.style.display = 'none';
        windowsList.innerHTML = '';

        windowsData.forEach(w => {
            const card = document.createElement('div');
            card.className = 'history-card window-card' + (selectedWindows.has(w.Handle) ? ' selected' : '');
            card.innerHTML = `
                <div class="card-details">
                    <span class="window-check"><input type="checkbox" ${selectedWindows.has(w.Handle) ? 'checked' : ''}></span>
                    <div class="card-icon icon-app">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="16" x="2" y="3" rx="2"/><path d="M12 17v4"/><path d="M8 21h8"/></svg>
                    </div>
                    <div class="card-info">
                        <div class="card-title-row">
                            <span class="card-name" title="${escapeHtml(w.Title)}">${escapeHtml(w.Title)}</span>
                        </div>
                        <div class="card-path">${escapeHtml(w.ProcessName)}</div>
                    </div>
                </div>
            `;
            const cb = card.querySelector('input');
            const toggle = () => {
                if (selectedWindows.has(w.Handle)) { selectedWindows.delete(w.Handle); card.classList.remove('selected'); cb.checked = false; }
                else { selectedWindows.add(w.Handle); card.classList.add('selected'); cb.checked = true; }
            };
            cb.addEventListener('click', (e) => { e.stopPropagation(); toggle(); });
            card.addEventListener('click', toggle);
            windowsList.appendChild(card);
        });
    }

    async function arrangeWindows(layout) {
        const handles = Array.from(selectedWindows);
        if (handles.length < 1) { showToast('יש לסמן לפחות חלון אחד מהרשימה.'); return; }
        try {
            const r = await fetch(`${API.windowsArrange}?layout=${encodeURIComponent(layout)}&handles=${handles.join(',')}`, { method: 'POST' });
            if (r.ok) {
                const res = await r.json();
                showToast(res.success ? `סודרו ${res.placed} חלונות זה לצד זה.` : 'לא ניתן היה לסדר את החלונות.');
            }
        } catch (e) { console.error('arrange error', e); showToast('שגיאה בסידור החלונות.'); }
    }

    async function tileAllWindows() {
        try {
            const r = await fetch(`${API.windowsTileAll}?layout=grid`, { method: 'POST' });
            if (r.ok) {
                const res = await r.json();
                showToast(res.success ? `סודרו ${res.placed} חלונות אוטומטית.` : 'אין חלונות פתוחים לסידור.');
            }
        } catch (e) { console.error('tile all error', e); }
    }

    if (refreshWindowsBtn) refreshWindowsBtn.addEventListener('click', fetchWindows);
    if (tileAllBtn) tileAllBtn.addEventListener('click', tileAllWindows);
    layoutBtns.forEach(b => b.addEventListener('click', () => arrangeWindows(b.getAttribute('data-layout'))));

    // --- ADD DOCUMENTS / REMOVE ITEMS FOR GROUPS ---
    const addDocsModal = document.getElementById('addDocsModal');
    const addDocsList = document.getElementById('addDocsList');
    const cancelAddDocsBtn = document.getElementById('cancelAddDocsBtn');
    const confirmAddDocsBtn = document.getElementById('confirmAddDocsBtn');
    let addDocsTargetId = null;
    let addDocsSelected = new Set();

    async function openAddDocsModal(groupId) {
        addDocsTargetId = groupId;
        addDocsSelected = new Set();
        if (!addDocsModal) return;
        addDocsList.innerHTML = '<p style="opacity:.7;padding:8px">טוען מסמכים אחרונים...</p>';
        addDocsModal.style.display = 'flex';
        try {
            const r = await fetch(API.history);
            const data = r.ok ? await r.json() : [];
            const files = data.filter(x => x.Type === 'file');
            if (files.length === 0) {
                addDocsList.innerHTML = '<p style="opacity:.7;padding:8px">לא נמצאו מסמכים אחרונים. פתח/י מסמך כלשהו (Word, PDF וכו\') והוא יופיע כאן.</p>';
                return;
            }
            addDocsList.innerHTML = '';
            files.forEach(f => {
                const row = document.createElement('label');
                row.className = 'add-doc-row';
                row.innerHTML = `<input type="checkbox"><span class="add-doc-name">${escapeHtml(f.Name)}</span><span class="add-doc-path">${escapeHtml(f.Path)}</span>`;
                const key = JSON.stringify({ Path: f.Path, Name: f.Name, Type: 'file' });
                row.querySelector('input').addEventListener('change', (e) => {
                    if (e.target.checked) addDocsSelected.add(key); else addDocsSelected.delete(key);
                });
                addDocsList.appendChild(row);
            });
        } catch (e) { addDocsList.innerHTML = '<p style="padding:8px">שגיאה בטעינת מסמכים.</p>'; }
    }

    async function confirmAddDocs() {
        if (!addDocsTargetId) { addDocsModal.style.display = 'none'; return; }
        const items = Array.from(addDocsSelected).map(s => JSON.parse(s));
        if (items.length === 0) { addDocsModal.style.display = 'none'; return; }
        try {
            const r = await fetch(`${API.snapshotsAddItems}?id=${encodeURIComponent(addDocsTargetId)}`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(items)
            });
            if (r.ok) { showToast('המסמכים נוספו לקבוצה בהצלחה.'); fetchSnapshots(); }
        } catch (e) { console.error('add docs error', e); }
        addDocsModal.style.display = 'none';
    }

    async function removeSnapshotItem(groupId, path) {
        try {
            const r = await fetch(`${API.snapshotsItemDelete}?id=${encodeURIComponent(groupId)}&path=${encodeURIComponent(path)}`, { method: 'DELETE' });
            if (r.ok) fetchSnapshots();
        } catch (e) { console.error('remove item error', e); }
    }

    if (cancelAddDocsBtn) cancelAddDocsBtn.addEventListener('click', () => { addDocsModal.style.display = 'none'; });
    if (confirmAddDocsBtn) confirmAddDocsBtn.addEventListener('click', confirmAddDocs);


    // --- EXPERIMENTAL: merge windows into one tabbed window ---
    const mergeWindowsBtn = document.getElementById('mergeWindowsBtn');
    async function mergeWindows() {
        const handles = Array.from(selectedWindows);
        if (handles.length < 1) { showToast('בחר/י לפחות חלון אחד. אפשר להתחיל מאחד ולהוסיף עוד עם ➕ בתוך הקבוצה.'); return; }
        try {
            const r = await fetch(`${API.windowsMerge}?handles=${handles.join(',')}`, { method: 'POST' });
            if (r.ok) {
                const res = await r.json();
                showToast(res.success ? `מוזגו ${res.merged} חלונות לחלון אחד עם לשוניות.` : 'לא ניתן היה למזג את החלונות.');
            }
        } catch (e) { console.error('merge error', e); showToast('שגיאה במיזוג החלונות.'); }
    }
    if (mergeWindowsBtn) mergeWindowsBtn.addEventListener('click', mergeWindows);

    const saveSelectionBtn = document.getElementById('saveSelectionBtn');
    async function saveSelectionAsGroup() {
        const handles = Array.from(selectedWindows);
        if (handles.length < 1) { showToast('סמן/י לפחות חלון אחד כדי לשמור כקבוצה.'); return; }
        const name = window.prompt('שם לקבוצה החדשה:', '');
        if (name === null) return;
        try {
            const r = await fetch(`${API.snapshotsCreateFromWindows}?name=${encodeURIComponent(name.trim())}&handles=${handles.join(',')}`, { method: 'POST' });
            if (r.ok) {
                const res = await r.json();
                showToast(res.success ? 'הקבוצה נשמרה! מופיעה בלשונית "קבוצות עבודה".' : 'לא ניתן היה לשמור את הקבוצה.');
            }
        } catch (e) { console.error('save selection', e); showToast('שגיאה בשמירת הקבוצה.'); }
    }
    if (saveSelectionBtn) saveSelectionBtn.addEventListener('click', saveSelectionAsGroup);


    // --- FOLDER TABS (browser-like tabs for folders) ---
    const folderTabsBar = document.getElementById('folderTabsBar');
    const folderListing = document.getElementById('folderListing');
    const folderTabsEmpty = document.getElementById('folderTabsEmpty');
    const folderToolbar = document.getElementById('folderToolbar');
    const folderBreadcrumb = document.getElementById('folderBreadcrumb');
    const folderUpBtn = document.getElementById('folderUpBtn');
    const pickFolderBtn = document.getElementById('pickFolderBtn');

    let folderTabs = [];          // [{id, path, name}]
    let activeFolderId = null;
    let folderActiveParent = null;
    let folderInitialized = false;
    let folderUid = 1;

    function loadFolderTabs() {
        try {
            const raw = localStorage.getItem('bt_folder_tabs');
            const act = localStorage.getItem('bt_folder_active');
            if (raw) folderTabs = JSON.parse(raw) || [];
            activeFolderId = act || (folderTabs[0] && folderTabs[0].id) || null;
            folderTabs.forEach(t => { const n = parseInt(String(t.id).replace(/\D/g, ''), 10); if (n >= folderUid) folderUid = n + 1; });
        } catch (e) { folderTabs = []; activeFolderId = null; }
    }
    function saveFolderTabs() {
        try {
            localStorage.setItem('bt_folder_tabs', JSON.stringify(folderTabs));
            if (activeFolderId) localStorage.setItem('bt_folder_active', activeFolderId);
        } catch (e) {}
    }

    function ensureFolderTabsInit() {
        if (folderInitialized) return;
        folderInitialized = true;
        loadFolderTabs();
        renderFolderTabsBar();
        const active = folderTabs.find(t => t.id === activeFolderId);
        if (active) loadFolderListing(active.path);
        else showFolderEmpty(true);
    }

    function showFolderEmpty(on) {
        if (folderTabsEmpty) folderTabsEmpty.style.display = on ? 'flex' : 'none';
        if (folderListing) folderListing.style.display = on ? 'none' : 'flex';
        if (folderToolbar) folderToolbar.style.display = on ? 'none' : 'flex';
    }

    function renderFolderTabsBar() {
        if (!folderTabsBar) return;
        folderTabsBar.innerHTML = '';
        folderTabs.forEach(t => {
            const tab = document.createElement('div');
            tab.className = 'folder-tab' + (t.id === activeFolderId ? ' active' : '');
            tab.innerHTML = `<span class="folder-tab-name">${escapeHtml(t.name || 'תיקייה')}</span><span class="folder-tab-close" title="סגור לשונית">✕</span>`;
            tab.querySelector('.folder-tab-name').addEventListener('click', () => setActiveFolderTab(t.id));
            tab.querySelector('.folder-tab-close').addEventListener('click', (e) => { e.stopPropagation(); closeFolderTab(t.id); });
            folderTabsBar.appendChild(tab);
        });
        const plus = document.createElement('button');
        plus.className = 'folder-add-btn';
        plus.title = 'לשונית חדשה';
        plus.textContent = '+';
        plus.addEventListener('click', () => addFolderTab(''));
        folderTabsBar.appendChild(plus);
    }

    function addFolderTab(path) {
        const id = 'ft' + (folderUid++);
        const tab = { id, path: path || '', name: path ? path.replace(/[\\/]+$/,'').split(/[\\/]/).pop() || path : 'המחשב שלי' };
        folderTabs.push(tab);
        activeFolderId = id;
        saveFolderTabs();
        renderFolderTabsBar();
        loadFolderListing(tab.path);
    }

    function setActiveFolderTab(id) {
        activeFolderId = id;
        saveFolderTabs();
        renderFolderTabsBar();
        const t = folderTabs.find(x => x.id === id);
        if (t) loadFolderListing(t.path);
    }

    function closeFolderTab(id) {
        const idx = folderTabs.findIndex(t => t.id === id);
        if (idx === -1) return;
        folderTabs.splice(idx, 1);
        if (activeFolderId === id) activeFolderId = folderTabs.length ? folderTabs[Math.max(0, idx - 1)].id : null;
        saveFolderTabs();
        renderFolderTabsBar();
        const active = folderTabs.find(t => t.id === activeFolderId);
        if (active) loadFolderListing(active.path);
        else showFolderEmpty(true);
    }

    async function loadFolderListing(path) {
        showFolderEmpty(false);
        if (folderListing) folderListing.innerHTML = '<p style="opacity:.6;padding:10px">טוען…</p>';
        try {
            const r = await fetch(`${API.fsList}?path=${encodeURIComponent(path || '')}`);
            const data = r.ok ? await r.json() : null;
            if (!data) { folderListing.innerHTML = '<p style="padding:10px">שגיאה בטעינת התיקייה.</p>'; return; }

            folderActiveParent = data.Parent;
            // update active tab path/name
            const t = folderTabs.find(x => x.id === activeFolderId);
            if (t) { t.path = data.Path || ''; t.name = data.Name || (data.Path ? data.Path : 'המחשב שלי'); saveFolderTabs(); renderFolderTabsBar(); }

            renderBreadcrumb(data.Path || '');
            if (folderUpBtn) folderUpBtn.style.display = (data.Path && data.Path.length) ? 'inline-flex' : 'none';

            if (!data.Ok) { folderListing.innerHTML = '<p style="padding:10px">אין הרשאת גישה לתיקייה זו.</p>'; return; }

            const entries = data.Entries || [];
            if (entries.length === 0) { folderListing.innerHTML = '<p style="opacity:.6;padding:10px">התיקייה ריקה.</p>'; return; }

            folderListing.innerHTML = '';
            entries.forEach(en => {
                const row = document.createElement('div');
                row.className = 'folder-row ' + (en.IsDir ? 'is-dir' : 'is-file');
                const icon = en.IsDir
                    ? '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/></svg>'
                    : '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/></svg>';
                row.innerHTML = `<span class="folder-row-icon">${icon}</span><span class="folder-row-name">${escapeHtml(en.Name)}</span>`;
                row.addEventListener('dblclick', () => en.IsDir ? loadFolderListing(en.Path) : openFsItem(en.Path));
                row.addEventListener('click', () => { if (en.IsDir) loadFolderListing(en.Path); });
                folderListing.appendChild(row);
            });
        } catch (e) {
            folderListing.innerHTML = '<p style="padding:10px">שגיאה בטעינת התיקייה.</p>';
        }
    }

    function renderBreadcrumb(path) {
        if (!folderBreadcrumb) return;
        folderBreadcrumb.innerHTML = '';
        const mkCrumb = (label, p) => {
            const c = document.createElement('span');
            c.className = 'folder-crumb';
            c.textContent = label;
            c.addEventListener('click', () => loadFolderListing(p));
            folderBreadcrumb.appendChild(c);
        };
        mkCrumb('המחשב שלי', '');
        if (path) {
            const parts = path.split(/[\\/]/).filter(Boolean);
            parts.forEach((seg, i) => {
                const sep = document.createElement('span'); sep.className = 'folder-crumb-sep'; sep.textContent = '‹'; folderBreadcrumb.appendChild(sep);
                let segPath = parts.slice(0, i + 1).join('\\');
                if (i === 0) segPath += '\\';
                mkCrumb(seg, segPath);
            });
        }
    }

    async function openFsItem(path) {
        try {
            const r = await fetch(`${API.fsOpen}?path=${encodeURIComponent(path)}`, { method: 'POST' });
            if (r.ok) { const res = await r.json(); showToast(res.success ? 'נפתח.' : 'לא ניתן לפתוח את הקובץ.'); }
        } catch (e) { console.error('open fs', e); }
    }

    if (folderUpBtn) folderUpBtn.addEventListener('click', () => loadFolderListing(folderActiveParent || ''));
    if (pickFolderBtn) pickFolderBtn.addEventListener('click', async () => {
        try {
            const r = await fetch(API.fsPick);
            const data = r.ok ? await r.json() : null;
            if (data && data.path) addFolderTab(data.path);
        } catch (e) { console.error('pick folder', e); }
    });

});
