// =====================================================================
// БЛОКОВИЙ РЕДАКТОР ПОСТІВ (C12 + F3) зі спільним редагуванням (C13 + C14)
//  - блоки (текст EasyMDE / медіа), drag-n-drop сортування (SortableJS)
//  - автозбереження чернетки в localStorage
//  - синхронізація між вкладками/пристроями через SignalR (/editorHub)
//  - підсвітка блоку, який редагує інший користувач, та анімація переміщень
// Налаштування: window.PostEditorConfig = { storageKey, docId, initialContent }
// =====================================================================
(function () {
    const config = window.PostEditorConfig;
    const BLOCK_SEPARATOR = '<!-- block -->'; // збігається з MarkdownRenderer.BlockSeparator
    const editorContainer = document.getElementById('editor-blocks');
    const editorsMap = {}; // екземпляри EasyMDE за ID блоку
    const peerColors = ['#ffc107', '#0dcaf0', '#d63384', '#20c997', '#fd7e14', '#6f42c1'];
    const peers = {}; // connectionId -> { name, color, blockId }

    let applyingRemote = false;
    let syncTimer = null;
    let connection = null;
    let receivedInitialState = false;

    // ---------- Блоки ----------
    function newBlockId() {
        return 'block_' + Date.now() + '_' + Math.random().toString(36).slice(2, 6);
    }

    function createBlockShell(blockId, type) {
        const block = document.createElement('div');
        block.className = 'block-item';
        block.id = blockId;
        block.dataset.type = type;
        block.innerHTML = `
            <div class="drag-handle">⠿</div>
            <div class="block-content"></div>
            <button type="button" class="btn btn-sm btn-outline-danger border-0">✕</button>
            <div class="peer-label d-none"></div>`;
        block.querySelector('button').addEventListener('click', () => removeBlock(blockId));
        return block;
    }

    function addTextBlock(content = "", blockId = null, beforeEl = null) {
        blockId = blockId || newBlockId();
        const block = createBlockShell(blockId, 'text');

        // Текст кладемо через .value, а не в HTML-розмітку - інакше "</textarea>" у тексті ламає редактор
        const textarea = document.createElement('textarea');
        textarea.className = 'block-textarea';
        textarea.value = content;
        block.querySelector('.block-content').appendChild(textarea);
        editorContainer.insertBefore(block, beforeEl);

        const mde = new EasyMDE({
            element: textarea,
            spellChecker: false,
            status: false, // ховаємо статус-бар для компактності
            toolbar: ["bold", "italic", "heading", "|", "quote", "unordered-list", "ordered-list", "|", "link", "image", "preview"],
            uploadImage: true,
            imageAccept: "image/*", // Текстовий блок приймає тільки картинки через Drag-n-Drop
            imageUploadFunction: function (file, onSuccess, onError) {
                if (file.size > 20 * 1024 * 1024) { onError("До 20 МБ."); return; }
                const formData = new FormData(); formData.append("file", file);
                fetch('/Posts/UploadMedia', { method: 'POST', body: formData })
                    .then(res => { if (!res.ok) throw new Error(res.status); return res.json(); })
                    .then(data => onSuccess(data.url))
                    .catch(err => onError(err.toString()));
            }
        });

        // Зберігаємо чернетку при кожній зміні тексту
        mde.codemirror.on("change", onLocalChange);
        mde.codemirror.on("focus", () => sendFocus(blockId));
        editorsMap[blockId] = mde;
        onLocalChange();
        return block;
    }

    function addMediaBlock(markdownCode, previewHtml, blockId = null, beforeEl = null) {
        blockId = blockId || newBlockId();
        const block = createBlockShell(blockId, 'media');
        const hidden = document.createElement('input');
        hidden.type = 'hidden';
        hidden.className = 'media-markdown';
        hidden.value = markdownCode;
        const preview = document.createElement('div');
        preview.className = 'media-preview';
        preview.innerHTML = previewHtml;
        block.querySelector('.block-content').append(hidden, preview);
        editorContainer.insertBefore(block, beforeEl);
        onLocalChange();
        return block;
    }

    function removeBlock(blockId) {
        if (editorsMap[blockId]) {
            editorsMap[blockId].toTextArea();
            delete editorsMap[blockId];
        }
        const el = document.getElementById(blockId);
        if (el) el.remove();
        onLocalChange();
    }

    function uploadMedia(input) {
        if (!input.files || input.files.length === 0) return;
        const file = input.files[0];
        if (file.size > 20 * 1024 * 1024) { alert("Файл занадто великий! Дозволено до 20 МБ."); return; }

        const formData = new FormData();
        formData.append("file", file);

        fetch('/Posts/UploadMedia', { method: 'POST', body: formData })
            .then(res => { if (!res.ok) throw new Error('HTTP ' + res.status); return res.json(); })
            .then(data => {
                let markdown = "";
                if (data.type === 'video') {
                    markdown = `\n<video controls style="width:100%; max-height: 400px; border-radius:8px; margin: 10px 0;"><source src="${data.url}"></video>\n`;
                } else if (data.type === 'audio') {
                    markdown = `\n<audio controls class="w-100" style="margin: 10px 0;"><source src="${data.url}"></audio>\n`;
                }
                if (markdown !== "") addMediaBlock(markdown, markdown);
                input.value = "";
            })
            .catch(err => alert("Помилка завантаження: " + err));
    }

    // ---------- Стан документа ----------
    function getState() {
        const blocks = [];
        Array.from(editorContainer.children).forEach(block => {
            if (block.dataset.type === 'text' && editorsMap[block.id]) {
                blocks.push({ id: block.id, type: 'text', content: editorsMap[block.id].value() });
            } else if (block.dataset.type === 'media') {
                blocks.push({
                    id: block.id,
                    type: 'media',
                    content: block.querySelector('.media-markdown').value,
                    preview: block.querySelector('.media-preview').innerHTML
                });
            }
        });
        return {
            title: document.getElementById('Title')?.value ?? '',
            tier: document.getElementById('RequiredTierLevel')?.value ?? '0',
            blocks
        };
    }

    function saveDraft() {
        localStorage.setItem(config.storageKey, JSON.stringify(getState().blocks));
    }

    function onLocalChange() {
        if (applyingRemote) return;
        saveDraft();
        scheduleSync();
    }

    function scheduleSync() {
        if (!connection || connection.state !== 'Connected') return;
        clearTimeout(syncTimer);
        syncTimer = setTimeout(() => connection.invoke('Update', config.docId, getState()).catch(console.error), 120);
    }

    // C14: FLIP-анімація - блоки плавно "переїжджають" на нові місця після зміни порядку іншим користувачем
    function animateReorder(mutate) {
        const before = {};
        Array.from(editorContainer.children).forEach(el => before[el.id] = el.getBoundingClientRect().top);
        mutate();
        Array.from(editorContainer.children).forEach(el => {
            if (before[el.id] === undefined) {
                el.animate([{ opacity: 0, transform: 'translateY(-8px)' }, { opacity: 1, transform: 'none' }], { duration: 300, easing: 'ease-out' });
                return;
            }
            const delta = before[el.id] - el.getBoundingClientRect().top;
            if (Math.abs(delta) > 1) {
                el.animate([{ transform: `translateY(${delta}px)` }, { transform: 'none' }], { duration: 350, easing: 'cubic-bezier(.2,.8,.2,1)' });
            }
        });
    }

    function flashBlock(el, color) {
        el.animate([{ boxShadow: `0 0 0 2px ${color}` }, { boxShadow: '0 0 0 0 transparent' }], { duration: 900, easing: 'ease-out' });
    }

    // Застосовуємо стан, отриманий від іншого пристрою
    function applyRemoteState(state, peerColor) {
        applyingRemote = true;
        try {
            const titleInput = document.getElementById('Title');
            if (titleInput && document.activeElement !== titleInput && titleInput.value !== state.title) titleInput.value = state.title;
            const tierSelect = document.getElementById('RequiredTierLevel');
            if (tierSelect && tierSelect.value !== String(state.tier)) tierSelect.value = String(state.tier);

            const remoteIds = new Set(state.blocks.map(b => b.id));
            animateReorder(() => {
                // Видалені іншим користувачем блоки
                Array.from(editorContainer.children).forEach(el => {
                    if (!remoteIds.has(el.id)) {
                        if (editorsMap[el.id]) { editorsMap[el.id].toTextArea(); delete editorsMap[el.id]; }
                        el.remove();
                    }
                });

                // Нові / змінені блоки + порядок
                state.blocks.forEach((b, index) => {
                    let el = document.getElementById(b.id);
                    const expectedAtIndex = editorContainer.children[index] || null;
                    if (!el) {
                        el = b.type === 'text'
                            ? addTextBlock(b.content, b.id, expectedAtIndex)
                            : addMediaBlock(b.content, b.preview, b.id, expectedAtIndex);
                        return;
                    }
                    if (el !== expectedAtIndex) editorContainer.insertBefore(el, expectedAtIndex);

                    if (b.type === 'text' && editorsMap[b.id] && editorsMap[b.id].value() !== b.content) {
                        const cm = editorsMap[b.id].codemirror;
                        const cursor = cm.hasFocus() ? cm.getCursor() : null;
                        editorsMap[b.id].value(b.content);
                        if (cursor) cm.setCursor(cursor);
                        if (peerColor) flashBlock(el, peerColor);
                    }
                });
            });
            saveDraft();
        } finally {
            applyingRemote = false;
        }
        renderPeerHighlights();
    }

    // ---------- Присутність (C14) ----------
    function colorFor(connectionId) {
        let hash = 0;
        for (const ch of connectionId) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
        return peerColors[hash % peerColors.length];
    }

    function sendFocus(blockId) {
        if (connection && connection.state === 'Connected') connection.invoke('Focus', config.docId, blockId).catch(() => { });
    }

    function renderPeerHighlights() {
        editorContainer.querySelectorAll('.block-item').forEach(el => {
            el.classList.remove('peer-editing');
            el.style.removeProperty('--peer-color');
            const label = el.querySelector('.peer-label');
            if (label) label.classList.add('d-none');
        });
        Object.values(peers).forEach(p => {
            if (!p.blockId) return;
            const el = document.getElementById(p.blockId);
            if (!el) return;
            el.classList.add('peer-editing');
            el.style.setProperty('--peer-color', p.color);
            const label = el.querySelector('.peer-label');
            label.textContent = p.name + ' редагує';
            label.classList.remove('d-none');
        });
        const status = document.getElementById('collabStatus');
        if (status) {
            const names = Object.values(peers).map(p => `<span style="color:${p.color}">●</span> ${escapeHTML(p.name)}`);
            status.innerHTML = names.length ? 'РАЗОМ З ВАМИ: ' + names.join(', ') : 'ВИ РЕДАГУЄТЕ САМІ';
        }
    }

    function escapeHTML(str) {
        return String(str).replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    }

    function startCollaboration() {
        if (typeof signalR === 'undefined') return;
        connection = new signalR.HubConnectionBuilder().withUrl('/editorHub').withAutomaticReconnect().build();

        connection.on('PeerJoined', (id, name) => {
            peers[id] = { name, color: colorFor(id), blockId: null };
            renderPeerHighlights();
            connection.invoke('SendStateTo', id, getState()).catch(console.error);
        });
        connection.on('InitialState', (id, name, state) => {
            peers[id] = peers[id] || { name, color: colorFor(id), blockId: null };
            // Новачок бере стан того, хто вже редагує документ
            if (!receivedInitialState) {
                receivedInitialState = true;
                applyRemoteState(state, null);
            }
            renderPeerHighlights();
        });
        connection.on('State', (id, name, state) => {
            peers[id] = peers[id] || { name, color: colorFor(id), blockId: null };
            applyRemoteState(state, peers[id].color);
        });
        connection.on('PeerFocus', (id, name, blockId) => {
            peers[id] = peers[id] || { name, color: colorFor(id), blockId: null };
            peers[id].blockId = blockId;
            renderPeerHighlights();
        });
        connection.on('PeerLeft', id => { delete peers[id]; renderPeerHighlights(); });

        const join = () => connection.invoke('Join', config.docId).then(renderPeerHighlights);
        connection.onreconnected(join);
        connection.start().then(join).catch(err => {
            console.error(err);
            const status = document.getElementById('collabStatus');
            if (status) status.innerText = 'СИНХРОНІЗАЦІЯ НЕДОСТУПНА';
        });
    }

    // ---------- Збереження ----------
    function saveAndSubmit() {
        const form = document.getElementById('postForm');
        if (!form.reportValidity()) return;

        // Блоки розділяємо невидимим HTML-коментарем: так при редагуванні структура відновлюється точно,
        // навіть якщо всередині блоку є списки чи код з порожніми рядками
        const finalMarkdown = getState().blocks.map(b => b.content.trim()).filter(Boolean).join(`\n\n${BLOCK_SEPARATOR}\n\n`);

        document.getElementById('ContentMarkdown').value = finalMarkdown;
        // Чернетку видаляємо лише після успішного збереження (див. site.js: сторінка, куди веде редірект)
        sessionStorage.setItem('bandvault_pending_draft_clear', config.storageKey);
        if (connection) connection.stop();
        // requestSubmit: форму відправляє Turbo без перезавантаження - музика в плеєрі не перериватиметься
        form.requestSubmit();
    }

    // ---------- Ініціалізація ----------
    new Sortable(editorContainer, {
        animation: 150,
        handle: '.drag-handle',
        ghostClass: 'bg-dark',
        onEnd: onLocalChange
    });

    function loadInitial() {
        const savedDraft = localStorage.getItem(config.storageKey);
        if (savedDraft) {
            JSON.parse(savedDraft).forEach(b => {
                if (b.type === 'text') addTextBlock(b.content, b.id);
                else if (b.type === 'media') addMediaBlock(b.content, b.preview, b.id);
            });
        } else if (config.initialContent && config.initialContent.trim() !== "") {
            // Нові пости зберігають межі блоків маркером; старі - розбиваємо по порожніх рядках
            const parts = config.initialContent.includes(BLOCK_SEPARATOR)
                ? config.initialContent.split(BLOCK_SEPARATOR)
                : config.initialContent.split(/\n\s*\n/);
            parts.forEach(part => {
                const trimmed = part.trim();
                if (!trimmed) return;
                if (trimmed.startsWith('<video') || trimmed.startsWith('<audio')) addMediaBlock(trimmed, trimmed);
                else addTextBlock(trimmed);
            });
        }
        if (editorContainer.children.length === 0) addTextBlock();
    }

    editorContainer.addEventListener('focusin', e => {
        const block = e.target.closest('.block-item');
        if (block) sendFocus(block.id);
    });
    ['Title', 'RequiredTierLevel'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.addEventListener('input', scheduleSync);
        if (el) el.addEventListener('change', scheduleSync);
    });

    window.addTextBlock = addTextBlock;
    window.uploadMedia = uploadMedia;
    window.saveAndSubmit = saveAndSubmit;

    loadInitial();
    startCollaboration();

    // Turbo-перехід на іншу сторінку: закриваємо з'єднання синхронізації цього документа
    document.addEventListener('turbo:before-render', () => { if (connection) connection.stop(); }, { once: true });
})();
