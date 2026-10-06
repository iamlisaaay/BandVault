// =========================================
// Логіка Аудіо-візуалізатора (Web Audio API)
// =========================================
document.addEventListener('DOMContentLoaded', () => {
    const audio = document.getElementById('main-audio');
    const canvas = document.getElementById('visualizer');
    
    // Якщо елементів немає на сторінці — припиняємо роботу
    if (!audio || !canvas) return;

    const ctx = canvas.getContext('2d');
    
    // Перевіряємо, чи ми вже не ініціалізували AudioContext 
    // (важливо для роботи з Turbo, щоб не було дублювання)
    if (window.audioContextInitialized) return;

    let audioContext;
    let analyser;
    let source;

    audio.addEventListener('play', () => {
        if (!audioContext) {
            window.audioContextInitialized = true;
            
            // Створюємо аудіо-контекст
            audioContext = new (window.AudioContext || window.webkitAudioContext)();
            analyser = audioContext.createAnalyser();
            analyser.fftSize = 128; // Роздільна здатність спектра

            source = audioContext.createMediaElementSource(audio);
            source.connect(analyser);
            analyser.connect(audioContext.destination);

            drawVisualizer();
        }
    });

    function drawVisualizer() {
        requestAnimationFrame(drawVisualizer);

        const bufferLength = analyser.frequencyBinCount;
        const dataArray = new Uint8Array(bufferLength);
        
        analyser.getByteFrequencyData(dataArray);

        // Очищаємо Canvas перед кожним новим кадром
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        const barWidth = (canvas.width / bufferLength) * 2.5;
        let barHeight;
        let x = 0;

        for (let i = 0; i < bufferLength; i++) {
            barHeight = dataArray[i];

            // ЧОРНО-БІЛИЙ ГРАДІЄНТ (чим гучніше, тим біліше)
            const colorVal = Math.min(255, barHeight + 80); 
            ctx.fillStyle = `rgb(${colorVal}, ${colorVal}, ${colorVal})`;
            
            const y = (canvas.height - (barHeight / 3)) / 2;
            ctx.fillRect(x, y, barWidth - 1, barHeight / 3);

            x += barWidth;
        }
    }
});

// =========================================
// Глобальна функція для запуску треків
// =========================================
// Додали 5-й параметр: releaseUrl
window.playTrack = function (audioUrl, trackTitle, trackArtist, coverUrl, releaseUrl) {
    const audio = document.getElementById('main-audio'); 
    
    const titleEl = document.getElementById('player-track-title');
    const artistEl = document.getElementById('player-track-artist');
    const coverEl = document.getElementById('player-cover');

    if (titleEl) titleEl.innerText = trackTitle;
    
    // Оновлюємо текст і посилання (href)
    if (artistEl) {
        artistEl.innerText = trackArtist;
        if (releaseUrl) {
            artistEl.href = releaseUrl;
            artistEl.style.pointerEvents = 'auto'; // Робимо клікабельним
        } else {
            artistEl.removeAttribute('href');
            artistEl.style.pointerEvents = 'none'; // Вимикаємо клік, якщо посилання немає
        }
    }
    
    if (coverEl && coverUrl) coverEl.src = coverUrl;

    if (audio) {
        audio.src = audioUrl;
        audio.load(); 
        
        let playPromise = audio.play();
        if (playPromise !== undefined) {
            playPromise.catch(error => {
                console.log("Автозапуск заблоковано.", error);
            });
        }
    }
};