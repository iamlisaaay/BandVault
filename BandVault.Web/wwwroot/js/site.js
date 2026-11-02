// =====================================================================
// F6 + C15: АУДІО-ВІЗУАЛІЗАТОР (Web Audio API + Canvas) з налаштуваннями
// Клік по візуалізатору або ⚙ відкриває панель: стиль, кольори, чутливість,
// кількість смуг, плавність, світіння, пресети. Збереження - localStorage.
// =====================================================================
(function () {
    const STORAGE_KEY = 'bandvault_visualizer';
    const BAR_OPTIONS = [32, 64, 128, 256]; // fftSize; смуг = fftSize / 2
    const DEFAULTS = { style: 'bars', colorMode: 'solid', color1: '#ffffff', color2: '#ff2850', sensitivity: 1, bars: 1, smoothing: 0.8, glow: false };
    const PRESETS = {
        classic: { style: 'bars', colorMode: 'solid', color1: '#ffffff', color2: '#888888', sensitivity: 1, bars: 1, smoothing: 0.8, glow: false },
        neon: { style: 'mirror', colorMode: 'gradient', color1: '#00f0ff', color2: '#ff00d4', sensitivity: 1.3, bars: 2, smoothing: 0.75, glow: true },
        blood: { style: 'bars', colorMode: 'reactive', color1: '#ff1e3c', color2: '#3a0008', sensitivity: 1.2, bars: 1, smoothing: 0.7, glow: true },
        ocean: { style: 'wave', colorMode: 'gradient', color1: '#0dcaf0', color2: '#198754', sensitivity: 1.1, bars: 2, smoothing: 0.85, glow: false },
        spectrum: { style: 'dots', colorMode: 'rainbow', color1: '#ffffff', color2: '#ffffff', sensitivity: 1.4, bars: 2, smoothing: 0.6, glow: true }
    };

    function loadSettings() {
        try {
            return Object.assign({}, DEFAULTS, JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}'));
        } catch {
            return Object.assign({}, DEFAULTS);
        }
    }

    function init() {
        const audio = document.getElementById('main-audio');
        const canvas = document.getElementById('visualizer');
        const panel = document.getElementById('vizPanel');
        const settingsBtn = document.getElementById('vizSettingsBtn');
        if (!audio || !canvas || !panel || window.visualizerInitialized) return;
        window.visualizerInitialized = true;

        const ctx = canvas.getContext('2d');
        let settings = loadSettings();
        let audioContext = null;
        let analyser = null;
        let frameId = null;
        let idlePhase = 0;

        // ---------- Web Audio ----------
        function ensureAudioGraph() {
            if (audioContext) return;
            audioContext = new (window.AudioContext || window.webkitAudioContext)();
            analyser = audioContext.createAnalyser();
            applyAnalyserSettings();
            const source = audioContext.createMediaElementSource(audio);
            source.connect(analyser);
            analyser.connect(audioContext.destination);
        }

        function applyAnalyserSettings() {
            if (!analyser) return;
            analyser.fftSize = BAR_OPTIONS[settings.bars] || 64;
            analyser.smoothingTimeConstant = settings.smoothing;
        }

        // Дані для кадру: справжній спектр під час відтворення або "дихання" на паузі (щоб було видно зміни налаштувань)
        function getFrequencyData() {
            if (analyser && !audio.paused) {
                const live = new Uint8Array(analyser.frequencyBinCount);
                analyser.getByteFrequencyData(live);
                return live;
            }
            const count = (BAR_OPTIONS[settings.bars] || 64) / 2;
            const data = new Uint8Array(count);
            idlePhase += 0.04;
            for (let i = 0; i < count; i++) {
                data[i] = 40 + 30 * Math.sin(idlePhase + i * 0.35) * Math.cos(idlePhase * 0.5 + i * 0.1);
            }
            return data;
        }

        // ---------- Кольори ----------
        function hexToRgb(hex) {
            const n = parseInt(hex.slice(1), 16);
            return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
        }

        function mix(a, b, t) {
            const ca = hexToRgb(a), cb = hexToRgb(b);
            return `rgb(${Math.round(ca[0] + (cb[0] - ca[0]) * t)}, ${Math.round(ca[1] + (cb[1] - ca[1]) * t)}, ${Math.round(ca[2] + (cb[2] - ca[2]) * t)})`;
        }

        function colorFor(index, total, level) {
            switch (settings.colorMode) {
                case 'gradient': return mix(settings.color1, settings.color2, total > 1 ? index / (total - 1) : 0);
                case 'rainbow': return `hsl(${Math.round((index / total) * 300)}, 90%, ${45 + level * 20}%)`;
                case 'reactive': return mix(settings.color2, settings.color1, Math.min(1, level));
                default: return settings.color1;
            }
        }

        // ---------- Малювання ----------
        function resizeCanvas() {
            const ratio = window.devicePixelRatio || 1;
            const w = Math.max(1, Math.round(canvas.clientWidth * ratio));
            const h = Math.max(1, Math.round(canvas.clientHeight * ratio));
            if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
        }

        function draw() {
            resizeCanvas();
            const data = getFrequencyData();
            const w = canvas.width, h = canvas.height;
            const n = data.length;
            ctx.clearRect(0, 0, w, h);
            ctx.shadowBlur = settings.glow ? 8 * (window.devicePixelRatio || 1) : 0;

            const levelOf = v => Math.min(1, (v / 255) * settings.sensitivity);

            if (settings.style === 'wave') {
                ctx.lineWidth = 2 * (window.devicePixelRatio || 1);
                ctx.lineJoin = 'round';
                for (let i = 0; i < n - 1; i++) {
                    const l1 = levelOf(data[i]), l2 = levelOf(data[i + 1]);
                    const x1 = (i / (n - 1)) * w, x2 = ((i + 1) / (n - 1)) * w;
                    const y1 = h / 2 - (l1 * h / 2) * Math.sin(i * 0.9);
                    const y2 = h / 2 - (l2 * h / 2) * Math.sin((i + 1) * 0.9);
                    ctx.strokeStyle = ctx.shadowColor = colorFor(i, n, l1);
                    ctx.beginPath();
                    ctx.moveTo(x1, y1);
                    ctx.lineTo(x2, y2);
                    ctx.stroke();
                }
                return;
            }

            const slot = w / n;
            const barWidth = Math.max(1, slot * 0.7);
            for (let i = 0; i < n; i++) {
                const level = levelOf(data[i]);
                const color = colorFor(i, n, level);
                ctx.fillStyle = ctx.shadowColor = color;
                const x = i * slot + (slot - barWidth) / 2;

                if (settings.style === 'mirror') {
                    const barH = Math.max(1, level * h);
                    ctx.fillRect(x, (h - barH) / 2, barWidth, barH);
                } else if (settings.style === 'dots') {
                    const r = Math.max(1, Math.min(slot, h) * 0.35);
                    const y = h - r - level * (h - 2 * r);
                    ctx.beginPath();
                    ctx.arc(x + barWidth / 2, y, r, 0, Math.PI * 2);
                    ctx.fill();
                } else {
                    const barH = Math.max(1, level * h);
                    ctx.fillRect(x, h - barH, barWidth, barH);
                }
            }
        }

        // Анімація крутиться лише коли грає музика або відкрита панель (не вантажимо CPU на паузі)
        function loop() {
            draw();
            if (!audio.paused || !panel.classList.contains('d-none')) {
                frameId = requestAnimationFrame(loop);
            } else {
                frameId = null;
                ctx.clearRect(0, 0, canvas.width, canvas.height);
            }
        }

        function startLoop() {
            if (frameId === null) frameId = requestAnimationFrame(loop);
        }

        audio.addEventListener('play', () => {
            ensureAudioGraph();
            if (audioContext.state === 'suspended') audioContext.resume();
            startLoop();
        });

        // ---------- Панель налаштувань ----------
        const els = {
            color1: document.getElementById('vizColor1'),
            color2: document.getElementById('vizColor2'),
            color2Wrap: document.getElementById('vizColor2Wrap'),
            sensitivity: document.getElementById('vizSensitivity'),
            sensitivityVal: document.getElementById('vizSensitivityVal'),
            bars: document.getElementById('vizBars'),
            barsVal: document.getElementById('vizBarsVal'),
            smoothing: document.getElementById('vizSmoothing'),
            smoothingVal: document.getElementById('vizSmoothingVal'),
            glow: document.getElementById('vizGlow')
        };

        function syncPanel() {
            panel.querySelectorAll('[data-viz-group]').forEach(group => {
                group.querySelectorAll('.viz-chip').forEach(b => b.classList.toggle('active', b.dataset.value === settings[group.dataset.vizGroup]));
            });
            panel.querySelectorAll('[data-preset]').forEach(b => {
                const p = PRESETS[b.dataset.preset];
                b.classList.toggle('active', Object.keys(p).every(k => p[k] === settings[k]));
            });
            els.color1.value = settings.color1;
            els.color2.value = settings.color2;
            // Другий колір має сенс лише для градієнта та режиму "від гучності"
            els.color2Wrap.classList.toggle('opacity-25', !['gradient', 'reactive'].includes(settings.colorMode));
            els.color1.disabled = settings.colorMode === 'rainbow';
            els.sensitivity.value = settings.sensitivity;
            els.sensitivityVal.textContent = '×' + Number(settings.sensitivity).toFixed(1);
            els.bars.value = settings.bars;
            els.barsVal.textContent = (BAR_OPTIONS[settings.bars] / 2);
            els.smoothing.value = settings.smoothing;
            els.smoothingVal.textContent = Math.round(settings.smoothing * 100) + '%';
            els.glow.checked = settings.glow;
        }

        function update(changes) {
            settings = Object.assign({}, settings, changes);
            try { localStorage.setItem(STORAGE_KEY, JSON.stringify(settings)); } catch { }
            applyAnalyserSettings();
            syncPanel();
            startLoop();
        }

        function setPanelOpen(open) {
            panel.classList.toggle('d-none', !open);
            settingsBtn.classList.toggle('active', open);
            settingsBtn.setAttribute('aria-expanded', String(open));
            if (open) { syncPanel(); startLoop(); }
        }

        settingsBtn.addEventListener('click', e => { e.stopPropagation(); setPanelOpen(panel.classList.contains('d-none')); });
        canvas.addEventListener('click', e => { e.stopPropagation(); setPanelOpen(true); });
        panel.querySelector('[data-viz-close]').addEventListener('click', () => setPanelOpen(false));
        panel.addEventListener('click', e => e.stopPropagation());
        document.addEventListener('click', () => { if (!panel.classList.contains('d-none')) setPanelOpen(false); });
        document.addEventListener('keydown', e => { if (e.key === 'Escape') setPanelOpen(false); });

        panel.querySelectorAll('[data-viz-group]').forEach(group => {
            group.querySelectorAll('.viz-chip').forEach(b => b.addEventListener('click', () => update({ [group.dataset.vizGroup]: b.dataset.value })));
        });
        panel.querySelectorAll('[data-preset]').forEach(b => b.addEventListener('click', () => update(PRESETS[b.dataset.preset])));
        els.color1.addEventListener('input', () => update({ color1: els.color1.value, colorMode: settings.colorMode === 'rainbow' ? 'solid' : settings.colorMode }));
        els.color2.addEventListener('input', () => update({ color2: els.color2.value }));
        els.sensitivity.addEventListener('input', () => update({ sensitivity: Number(els.sensitivity.value) }));
        els.bars.addEventListener('input', () => update({ bars: Number(els.bars.value) }));
        els.smoothing.addEventListener('input', () => update({ smoothing: Number(els.smoothing.value) }));
        els.glow.addEventListener('change', () => update({ glow: els.glow.checked }));
        document.getElementById('vizReset').addEventListener('click', () => update(DEFAULTS));
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();

// C12: чернетку поста прибираємо, лише коли сервер прийняв пост і перенаправив зі сторінки редактора.
// Якщо збереження не вдалося (помилка, відмова в доступі), чернетка в localStorage зберігається.
(function () {
    const key = sessionStorage.getItem('bandvault_pending_draft_clear');
    if (key && /^\/Posts\/?$/i.test(location.pathname)) { // після успіху сервер веде на стрічку /Posts
        localStorage.removeItem(key);
        sessionStorage.removeItem('bandvault_pending_draft_clear');
    }
})();

//запуск треків

// Кнопки "▶ Слухати" описують трек через data-атрибути (без вбудованого JS із рядками - безпечно для назв з лапками)
document.addEventListener('click', e => {
    const btn = e.target.closest('[data-play-track]');
    if (!btn) return;
    window.playTrack(btn.dataset.audio, btn.dataset.title, btn.dataset.artist, btn.dataset.cover, btn.dataset.releaseUrl, btn.dataset.playTrack);
});

window.playTrack = function (audioUrl, trackTitle, trackArtist, coverUrl, releaseUrl, trackId) {
    // F1: лічильник прослуховувань (для графіка в адмінці)
    if (trackId) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]');
        fetch(`/Releases/RegisterPlay/${trackId}`, { method: 'POST', headers: token ? { 'RequestVerificationToken': token.value } : {} }).catch(() => { });
    }

    const audio = document.getElementById('main-audio'); 
    
    const titleEl = document.getElementById('player-track-title');
    const artistEl = document.getElementById('player-track-artist');
    const coverEl = document.getElementById('player-cover');

    if (titleEl) titleEl.innerText = trackTitle;
    

    if (artistEl) {
        artistEl.innerText = trackArtist;
        if (releaseUrl) {
            artistEl.href = releaseUrl;
            artistEl.style.pointerEvents = 'auto'; 
        } else {
            artistEl.removeAttribute('href');
            artistEl.style.pointerEvents = 'none';
        }
    }
    
    if (coverEl && coverUrl) coverEl.src = coverUrl;

    if (audio) {
        // Файли з Blob Storage/CDN - з іншого домену: без CORS-режиму Web Audio API заглушить звук
        audio.crossOrigin = 'anonymous';
        audio.src = audioUrl;
        audio.load(); 
        
        let playPromise = audio.play();
        if (playPromise !== undefined) {
            playPromise.catch(error => {
                console.log("Автозапуск заблоковано.", error);
            });
        }
    }
    
   
    if (typeof gtag === 'function') {
        gtag('event', 'play_track', { track_title: trackTitle, artist: trackArtist });
    }
};