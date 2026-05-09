const canvas = document.querySelector("#visualizer");
const ctx = canvas.getContext("2d", { alpha: false });
const audio = document.querySelector("#audio");
const fileInput = document.querySelector("#audio-file");
const playButton = document.querySelector("#play");
const captureButton = document.querySelector("#capture");
const demoButton = document.querySelector("#demo");
const statusText = document.querySelector("#status");
const peakReadout = document.querySelector("#peak-readout");
const rmsReadout = document.querySelector("#rms-readout");
const centroidReadout = document.querySelector("#centroid-readout");

const CONFIG = {
  fftSize: 8192,
  minHz: 24,
  maxHz: 18000,
  minDb: -90,
  maxDb: -12,
  bands: 176,
  attack: 46,
  release: 10,
  peakRelease: 0.42,
};

const TEXT = {
  play: "\u64ad\u653e",
  pause: "\u6682\u505c",
  capture: "\u6355\u6349\u7cfb\u7edf\u97f3\u9891",
  stopCapture: "\u505c\u6b62\u6355\u6349",
  selectedFile: "\u5df2\u9009\u62e9\u672c\u5730\u97f3\u4e50\u6587\u4ef6\u3002",
  usingFile: "\u6b63\u5728\u4f7f\u7528\u672c\u5730\u97f3\u4e50\u6587\u4ef6\u3002",
  chooseShare: "\u8bf7\u9009\u62e9\u5e26\u97f3\u9891\u7684\u5c4f\u5e55\u3001\u7a97\u53e3\u6216\u6807\u7b7e\u9875\uff0c\u5e76\u5f00\u542f\u5171\u4eab\u97f3\u9891\u3002",
  capturing: "\u6b63\u5728\u6355\u6349\u7cfb\u7edf\u97f3\u9891\u3002",
  noAudio: "\u6ca1\u6709\u62ff\u5230\u97f3\u9891\u8f68\u9053\u3002\u91cd\u65b0\u70b9\u51fb\u65f6\u8bf7\u9009\u62e9\u5171\u4eab\u97f3\u9891\u3002",
  unsupported: "\u5f53\u524d\u6d4f\u89c8\u5668\u4e0d\u652f\u6301\u7cfb\u7edf\u97f3\u9891\u6355\u6349\u3002",
  stopped: "\u7cfb\u7edf\u97f3\u9891\u6355\u6349\u5df2\u505c\u6b62\u3002",
  cancelled: "\u6355\u6349\u88ab\u53d6\u6d88\u3002\u6d4f\u89c8\u5668\u9700\u8981\u4f60\u6388\u6743\u5171\u4eab\u97f3\u9891\u3002",
};

let audioContext;
let analyser;
let fileSource;
let activeSource;
let captureStream;
let floatFrequencyData;
let timeData;
let bands = [];
let audioUrl;
let inputMode = "demo";
let demoPulse = 0;
let width = 1;
let height = 1;
let dpr = 1;
let lastFrame = performance.now();

function resize() {
  const rect = canvas.getBoundingClientRect();
  dpr = Math.min(window.devicePixelRatio || 1, 2);
  width = Math.max(1, Math.floor(rect.width));
  height = Math.max(1, Math.floor(rect.height));
  canvas.width = Math.floor(width * dpr);
  canvas.height = Math.floor(height * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function createBands() {
  bands = Array.from({ length: CONFIG.bands }, (_, index) => ({
    index,
    lowHz: logHz(index / CONFIG.bands),
    highHz: logHz((index + 1) / CONFIG.bands),
    value: 0,
    target: 0,
    peak: 0,
    db: CONFIG.minDb,
  }));
}

function logHz(t) {
  return CONFIG.minHz * (CONFIG.maxHz / CONFIG.minHz) ** t;
}

function ensureAnalyzer() {
  if (audioContext) return;

  audioContext = new AudioContext();
  analyser = audioContext.createAnalyser();
  analyser.fftSize = CONFIG.fftSize;
  analyser.minDecibels = -120;
  analyser.maxDecibels = -6;
  analyser.smoothingTimeConstant = 0;
  floatFrequencyData = new Float32Array(analyser.frequencyBinCount);
  timeData = new Uint8Array(analyser.fftSize);
}

function disconnectInput() {
  if (activeSource) {
    activeSource.disconnect();
    activeSource = null;
  }

  if (captureStream) {
    for (const track of captureStream.getTracks()) {
      track.stop();
    }
    captureStream = null;
  }

  audio.pause();
}

function useFileInput() {
  ensureAnalyzer();
  disconnectInput();

  if (!fileSource) {
    fileSource = audioContext.createMediaElementSource(audio);
  }

  activeSource = fileSource;
  activeSource.connect(analyser);
  analyser.disconnect();
  analyser.connect(audioContext.destination);
  inputMode = "file";
  captureButton.textContent = TEXT.capture;
  statusText.textContent = TEXT.usingFile;
}

async function useSystemAudio() {
  if (!navigator.mediaDevices?.getDisplayMedia) {
    statusText.textContent = TEXT.unsupported;
    return;
  }

  ensureAnalyzer();
  await audioContext.resume();
  statusText.textContent = TEXT.chooseShare;

  const stream = await navigator.mediaDevices.getDisplayMedia({
    video: true,
    audio: {
      echoCancellation: false,
      noiseSuppression: false,
      autoGainControl: false,
    },
  });

  if (!stream.getAudioTracks().length) {
    for (const track of stream.getTracks()) {
      track.stop();
    }
    statusText.textContent = TEXT.noAudio;
    return;
  }

  disconnectInput();
  captureStream = stream;
  activeSource = audioContext.createMediaStreamSource(captureStream);
  activeSource.connect(analyser);
  analyser.disconnect();
  inputMode = "capture";
  playButton.textContent = TEXT.play;
  captureButton.textContent = TEXT.stopCapture;
  statusText.textContent = TEXT.capturing;

  for (const track of captureStream.getTracks()) {
    track.addEventListener("ended", stopSystemAudio);
  }
}

function stopSystemAudio() {
  if (inputMode !== "capture" && !captureStream) return;
  disconnectInput();
  inputMode = "demo";
  captureButton.textContent = TEXT.capture;
  statusText.textContent = TEXT.stopped;
}

function readAudioFrame(dt) {
  if (analyser && inputMode !== "demo") {
    analyser.getFloatFrequencyData(floatFrequencyData);
    analyser.getByteTimeDomainData(timeData);
    updateBandsFromAnalyzer(dt);
    updateReadouts();
    return;
  }

  updateDemoBands(dt);
  peakReadout.textContent = "-- dB";
  rmsReadout.textContent = "-- dB";
  centroidReadout.textContent = "-- Hz";
}

function updateBandsFromAnalyzer(dt) {
  const nyquist = audioContext.sampleRate / 2;
  for (const band of bands) {
    const start = Math.max(0, Math.floor((band.lowHz / nyquist) * floatFrequencyData.length));
    const end = Math.min(floatFrequencyData.length - 1, Math.ceil((band.highHz / nyquist) * floatFrequencyData.length));
    let linearSum = 0;
    let peakDb = -160;
    let count = 0;

    for (let i = start; i <= end; i += 1) {
      const db = floatFrequencyData[i];
      linearSum += dbToLinear(db) ** 2;
      peakDb = Math.max(peakDb, db);
      count += 1;
    }

    const rmsDb = 20 * Math.log10(Math.sqrt(linearSum / Math.max(1, count)) || 1e-8);
    band.db = Math.max(rmsDb, peakDb - 4);
    band.target = dbToDisplay(band.db);
    smoothBand(band, dt);
  }
}

function updateDemoBands(dt) {
  demoPulse = Math.max(0, demoPulse - dt * 1.4);
  const time = performance.now() * 0.001;

  for (const band of bands) {
    const t = band.index / (bands.length - 1);
    const bass = Math.max(0, 1 - Math.abs(t - 0.18) * 7);
    const mids = Math.max(0, 1 - Math.abs(t - 0.47) * 6);
    const air = Math.sin(time * 9 + t * 40) ** 2 * Math.max(0, t - 0.58);
    band.target = 0.015 + bass * (0.2 + demoPulse * 0.55) + mids * 0.12 + air * 0.12;
    band.db = CONFIG.minDb + band.target * (CONFIG.maxDb - CONFIG.minDb);
    smoothBand(band, dt);
  }
}

function smoothBand(band, dt) {
  const speed = band.target > band.value ? CONFIG.attack : CONFIG.release;
  band.value += (band.target - band.value) * (1 - Math.exp(-speed * dt));
  band.value = clamp(band.value, 0, 1.2);
  band.peak = Math.max(band.value, band.peak - CONFIG.peakRelease * dt);
}

function dbToLinear(db) {
  return 10 ** (db / 20);
}

function dbToDisplay(db) {
  return clamp((db - CONFIG.minDb) / (CONFIG.maxDb - CONFIG.minDb), 0, 1);
}

function updateReadouts() {
  let peakDb = -160;
  let linearSum = 0;
  let weightedHz = 0;
  let magnitudeSum = 0;

  for (let i = 0; i < floatFrequencyData.length; i += 1) {
    const db = floatFrequencyData[i];
    const magnitude = dbToLinear(db);
    const hz = (i / floatFrequencyData.length) * (audioContext.sampleRate / 2);
    peakDb = Math.max(peakDb, db);
    linearSum += magnitude * magnitude;
    weightedHz += hz * magnitude;
    magnitudeSum += magnitude;
  }

  const rmsDb = 20 * Math.log10(Math.sqrt(linearSum / floatFrequencyData.length) || 1e-8);
  const centroid = weightedHz / Math.max(1e-8, magnitudeSum);
  peakReadout.textContent = `${formatDb(peakDb)} dB`;
  rmsReadout.textContent = `${formatDb(rmsDb)} dB`;
  centroidReadout.textContent = formatHz(centroid);
}

function formatDb(db) {
  if (!Number.isFinite(db) || db < -140) return "-inf";
  return db.toFixed(1);
}

function formatHz(hz) {
  if (!Number.isFinite(hz)) return "-- Hz";
  return hz >= 1000 ? `${(hz / 1000).toFixed(2)} kHz` : `${Math.round(hz)} Hz`;
}

function draw() {
  ctx.fillStyle = "rgba(4, 5, 7, 0.34)";
  ctx.fillRect(0, 0, width, height);

  const left = 56;
  const right = 28;
  const top = 72;
  const bottom = 58;
  const plotWidth = Math.max(1, width - left - right);
  const plotHeight = Math.max(1, height - top - bottom);
  const baseY = top + plotHeight;

  drawGrid(left, top, plotWidth, plotHeight, baseY);
  drawWaveform(left, plotWidth);
  drawBars(left, plotWidth, baseY, plotHeight);
  drawCurve(left, plotWidth, baseY, plotHeight);
  drawHeader(left);
}

function drawGrid(left, top, plotWidth, plotHeight, baseY) {
  ctx.save();
  ctx.font = "11px Inter, Segoe UI, sans-serif";
  ctx.textBaseline = "middle";
  ctx.lineWidth = 1;
  ctx.strokeStyle = "rgba(150, 157, 168, 0.16)";
  ctx.fillStyle = "rgba(150, 157, 168, 0.78)";

  const dbLines = [-90, -78, -66, -54, -42, -30, -18, -12];
  for (const db of dbLines) {
    const y = baseY - dbToDisplay(db) * plotHeight;
    ctx.beginPath();
    ctx.moveTo(left, y);
    ctx.lineTo(left + plotWidth, y);
    ctx.stroke();
    ctx.fillText(`${db} dB`, 10, y);
  }

  ctx.textAlign = "center";
  const freqLabels = [31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
  for (const hz of freqLabels) {
    const x = hzToX(hz, left, plotWidth);
    ctx.strokeStyle = hz === 1000 ? "rgba(240, 184, 79, 0.28)" : "rgba(150, 157, 168, 0.14)";
    ctx.beginPath();
    ctx.moveTo(x, top);
    ctx.lineTo(x, baseY + 8);
    ctx.stroke();
    ctx.fillText(hz >= 1000 ? `${hz / 1000}k` : `${hz}`, x, height - 24);
  }

  ctx.restore();
}

function hzToX(hz, left, plotWidth) {
  const t = Math.log(hz / CONFIG.minHz) / Math.log(CONFIG.maxHz / CONFIG.minHz);
  return left + clamp(t, 0, 1) * plotWidth;
}

function drawWaveform(left, plotWidth) {
  if (!timeData) return;

  ctx.save();
  ctx.beginPath();
  for (let i = 0; i < timeData.length; i += 8) {
    const x = left + (i / (timeData.length - 1)) * plotWidth;
    const y = 38 + ((timeData[i] - 128) / 128) * 22;
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.strokeStyle = "rgba(240, 242, 244, 0.34)";
  ctx.lineWidth = 1.2;
  ctx.stroke();
  ctx.restore();
}

function drawBars(left, plotWidth, baseY, plotHeight) {
  const slot = plotWidth / bands.length;
  const barWidth = Math.max(2, slot - 1.2);

  ctx.save();
  ctx.globalCompositeOperation = "source-over";
  for (const band of bands) {
    const x = left + band.index * slot + (slot - barWidth) * 0.5;
    const value = Math.pow(band.value, 1.18);
    const peak = Math.pow(band.peak, 1.08);
    const barHeight = Math.max(1, value * plotHeight);
    const hue = 28 + band.index / (bands.length - 1) * 178;
    const gradient = ctx.createLinearGradient(x, baseY, x, baseY - barHeight);
    gradient.addColorStop(0, `hsla(${hue}, 90%, 42%, 0.42)`);
    gradient.addColorStop(0.55, `hsla(${hue + 18}, 90%, 54%, 0.88)`);
    gradient.addColorStop(1, `hsla(${hue + 60}, 92%, 68%, 0.98)`);

    ctx.fillStyle = gradient;
    ctx.fillRect(x, baseY - barHeight, barWidth, barHeight);

    ctx.fillStyle = `hsla(${hue + 50}, 95%, 70%, 0.78)`;
    ctx.fillRect(x, baseY - peak * plotHeight, barWidth, 2);
  }
  ctx.restore();
}

function drawCurve(left, plotWidth, baseY, plotHeight) {
  ctx.save();
  ctx.beginPath();
  for (const band of bands) {
    const x = left + (band.index / (bands.length - 1)) * plotWidth;
    const y = baseY - Math.pow(band.value, 1.05) * plotHeight;
    if (band.index === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.strokeStyle = "rgba(241, 243, 245, 0.72)";
  ctx.lineWidth = 1.2;
  ctx.stroke();
  ctx.restore();
}

function drawHeader(left) {
  ctx.save();
  ctx.textAlign = "left";
  ctx.fillStyle = "rgba(241, 243, 245, 0.92)";
  ctx.font = "13px Inter, Segoe UI, sans-serif";
  ctx.fillText("LOG FREQUENCY SPECTRUM", left, 24);
  ctx.fillStyle = "rgba(152, 161, 173, 0.78)";
  ctx.font = "11px Inter, Segoe UI, sans-serif";
  ctx.fillText(`${CONFIG.fftSize} FFT  ${CONFIG.minDb}..${CONFIG.maxDb} dBFS  ${CONFIG.bands} bands`, left, 44);
  ctx.restore();
}

function loop(now) {
  const dt = Math.min(0.033, (now - lastFrame) / 1000 || 0.016);
  lastFrame = now;
  readAudioFrame(dt);
  draw();
  requestAnimationFrame(loop);
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

fileInput.addEventListener("change", () => {
  const file = fileInput.files?.[0];
  if (!file) return;
  if (audioUrl) URL.revokeObjectURL(audioUrl);
  audioUrl = URL.createObjectURL(file);
  audio.src = audioUrl;
  inputMode = "file";
  playButton.textContent = TEXT.play;
  statusText.textContent = TEXT.selectedFile;
});

playButton.addEventListener("click", async () => {
  if (!audio.src) {
    demoPulse = 1;
    return;
  }

  useFileInput();
  await audioContext.resume();
  if (audio.paused) {
    await audio.play();
    playButton.textContent = TEXT.pause;
  } else {
    audio.pause();
    playButton.textContent = TEXT.play;
  }
});

audio.addEventListener("ended", () => {
  playButton.textContent = TEXT.play;
});

captureButton.addEventListener("click", async () => {
  try {
    if (inputMode === "capture") {
      stopSystemAudio();
    } else {
      await useSystemAudio();
    }
  } catch (error) {
    statusText.textContent = error.name === "NotAllowedError"
      ? TEXT.cancelled
      : `Capture failed: ${error.message}`;
  }
});

demoButton.addEventListener("click", () => {
  inputMode = "demo";
  demoPulse = 1;
  statusText.textContent = "Demo signal.";
});

window.addEventListener("resize", resize);
createBands();
resize();
requestAnimationFrame(loop);
