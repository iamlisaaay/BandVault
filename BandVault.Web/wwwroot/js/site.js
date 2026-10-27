
// Логіка Аудіо-візуалізатора (Web Audio API)

document.addEventListener('DOMContentLoaded', () => {
    const audio = document.getElementById('main-audio');
    const canvas = document.getElementById('visualizer');
    
 
    if (!audio || !canvas) return;

    const ctx = canvas.getContext('2d');

    if (window.audioContextInitialized) return;

    let audioContext;
    let analyser;
    let source;
    
 
    let accentMode = false;
    canvas.style.cursor = 'pointer';
    canvas.title = "Клікніть, щоб змінити колір";
    
    canvas.addEventListener('click', () => {
        accentMode = !accentMode;
    });

    audio.addEventListener('play', () => {
        if (!audioContext) {
            window.audioContextInitialized = true;
            

            audioContext = new (window.AudioContext || window.webkitAudioContext)();
            analyser = audioContext.createAnalyser();
            analyser.fftSize = 128; 

            source = audioContext.createMediaElementSource(audio);
            source.connect(analyser);
            analyser.connect(audioContext.destination);

            drawVisualizer();
        }
        
        
        if (audioContext.state === 'suspended') {
            audioContext.resume();
        }
    });

    function drawVisualizer() {
        requestAnimationFrame(drawVisualizer);

        const bufferLength = analyser.frequencyBinCount;
        const dataArray = new Uint8Array(bufferLength);
        
        analyser.getByteFrequencyData(dataArray);

        ctx.clearRect(0, 0, canvas.width, canvas.height);

        const barWidth = (canvas.width / bufferLength) * 2.5;
        let barHeight;
        let x = 0;

        for (let i = 0; i < bufferLength; i++) {
            barHeight = dataArray[i];
            const colorVal = Math.min(255, barHeight + 80); 
            
           
            if (accentMode) {
                ctx.fillStyle = `rgb(${colorVal}, 40, 60)`;
            } else {
                ctx.fillStyle = `rgb(${colorVal}, ${colorVal}, ${colorVal})`; 
            }
            
            const y = (canvas.height - (barHeight / 3)) / 2;
            ctx.fillRect(x, y, barWidth - 1, barHeight / 3);

            x += barWidth;
        }
    }
});


//запуск треків

window.playTrack = function (audioUrl, trackTitle, trackArtist, coverUrl, releaseUrl) {
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