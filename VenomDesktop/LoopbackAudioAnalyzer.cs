using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace VenomDesktop;

public sealed class LoopbackAudioAnalyzer : IDisposable
{
    private const int FftSize = 2048;
    private const int BandCount = 96;
    private const double MinHz = 28;
    private const double MaxHz = 18000;
    private const double MinDb = -86;
    private const double MaxDb = -10;

    private readonly object _gate = new();
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];
    private readonly float[] _smoothedBands = new float[BandCount];
    private readonly Complex[] _fft = new Complex[FftSize];
    private WasapiLoopbackCapture? _capture;
    private int _writeIndex;
    private int _sampleRate = 48000;
    private double _rms;
    private double _peak;
    private double _bass;
    private double _mid;
    private double _air;
    private double _impact;
    private double _previousBass;
    private bool _hasSignal;

    public string Status { get; private set; } = "Starting audio capture...";

    public void Start()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    Status = args.Exception.Message;
                }
            };
            _capture.StartRecording();
            Status = "Capturing system audio";
        }
        catch (Exception ex)
        {
            Status = $"Audio capture unavailable: {ex.Message}";
        }
    }

    public AudioSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new AudioSnapshot
            {
                Bands = _smoothedBands.ToArray(),
                Rms = _rms,
                Peak = _peak,
                Bass = _bass,
                Mid = _mid,
                Air = _air,
                Impact = _impact,
                HasSignal = _hasSignal,
            };
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null) return;

        var format = _capture.WaveFormat;
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = format.BitsPerSample / 8;
        var frameSize = bytesPerSample * channels;
        if (frameSize <= 0) return;

        lock (_gate)
        {
            for (var offset = 0; offset + frameSize <= e.BytesRecorded; offset += frameSize)
            {
                var mixed = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sampleOffset = offset + channel * bytesPerSample;
                    mixed += ReadSample(e.Buffer, sampleOffset, format);
                }

                mixed /= channels;
                _samples[_writeIndex] = mixed;
                _writeIndex = (_writeIndex + 1) % FftSize;
            }

            Analyze();
        }
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        if (format.BitsPerSample == 16)
        {
            return BitConverter.ToInt16(buffer, offset) / 32768f;
        }

        if (format.BitsPerSample == 24)
        {
            var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000);
            return value / 8388608f;
        }

        if (format.BitsPerSample == 32)
        {
            return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        }

        return 0;
    }

    private void Analyze()
    {
        var linearSum = 0d;
        var peak = 0d;

        for (var i = 0; i < FftSize; i++)
        {
            var sample = _samples[(_writeIndex + i) % FftSize];
            linearSum += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));

            var window = FastFourierTransform.HannWindow(i, FftSize);
            _fft[i].X = (float)(sample * window);
            _fft[i].Y = 0;
        }

        FastFourierTransform.FFT(true, (int)Math.Log2(FftSize), _fft);
        Array.Clear(_bands);

        for (var band = 0; band < BandCount; band++)
        {
            var lowHz = LogHz((double)band / BandCount);
            var highHz = LogHz((double)(band + 1) / BandCount);
            var start = Math.Max(1, (int)Math.Floor(lowHz / _sampleRate * FftSize));
            var end = Math.Min(FftSize / 2 - 1, (int)Math.Ceiling(highHz / _sampleRate * FftSize));
            var sum = 0d;
            var count = 0;

            for (var bin = start; bin <= end; bin++)
            {
                var real = _fft[bin].X;
                var imaginary = _fft[bin].Y;
                var magnitude = Math.Sqrt(real * real + imaginary * imaginary);
                sum += magnitude * magnitude;
                count++;
            }

            var rms = Math.Sqrt(sum / Math.Max(1, count));
            var db = 20 * Math.Log10(rms + 1e-9);
            _bands[band] = (float)Clamp((db - MinDb) / (MaxDb - MinDb), 0, 1);
        }

        for (var i = 0; i < BandCount; i++)
        {
            var target = _bands[i];
            var speed = target > _smoothedBands[i] ? 0.45f : 0.12f;
            _smoothedBands[i] += (target - _smoothedBands[i]) * speed;
        }

        _rms = Math.Sqrt(linearSum / FftSize);
        _peak = peak;
        _bass = AverageBands(0.05, 0.22);
        _mid = AverageBands(0.23, 0.62);
        _air = AverageBands(0.68, 1.0);
        _impact = Math.Max(_impact * 0.82, Math.Max(0, _bass - _previousBass) * 6.5);
        _previousBass = _bass;
        _hasSignal = _rms > 0.0008;
    }

    private double AverageBands(double from, double to)
    {
        var start = Math.Clamp((int)Math.Floor(from * BandCount), 0, BandCount - 1);
        var end = Math.Clamp((int)Math.Ceiling(to * BandCount), start + 1, BandCount);
        var sum = 0d;
        for (var i = start; i < end; i++)
        {
            sum += _smoothedBands[i];
        }

        return sum / Math.Max(1, end - start);
    }

    private static double LogHz(double t) => MinHz * Math.Pow(MaxHz / MinHz, t);

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

    public void Dispose()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
    }
}
