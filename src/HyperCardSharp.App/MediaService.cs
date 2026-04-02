using LibVLCSharp.Shared;

namespace HyperCardSharp.App;

/// <summary>
/// Thin LibVLCSharp wrapper for audio and video playback.
/// WAV bytes are written to a temp file and played via LibVLC so the application
/// does not require any additional OS media infrastructure.
///
/// Initialization is deferred to first use and failures are caught silently —
/// if LibVLC native libraries are unavailable the player degrades gracefully.
/// </summary>
public sealed class MediaService : IDisposable
{
    private LibVLC? _libVLC;
    private MediaPlayer? _player;
    private string? _currentTempFile;
    private bool _initialized;

    /// <summary>Initialize LibVLC on first use. Returns false if initialization fails.</summary>
    private bool TryInit()
    {
        if (_initialized)
            return _libVLC != null;

        _initialized = true;
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVLC = new LibVLC(enableDebugLogs: false);
            _player = new MediaPlayer(_libVLC);
            return true;
        }
        catch
        {
            _libVLC  = null;
            _player  = null;
            return false;
        }
    }

    /// <summary>
    /// Play WAV bytes. The bytes are written to a temp file and played immediately.
    /// Any previous playback is stopped first.
    /// </summary>
    public void PlayWav(byte[] wavBytes)
    {
        if (!TryInit() || _libVLC == null || _player == null)
            return;

        Stop();

        try
        {
            var tmp = Path.ChangeExtension(Path.GetTempFileName(), ".wav");
            File.WriteAllBytes(tmp, wavBytes);
            _currentTempFile = tmp;

            using var media = new Media(_libVLC, new Uri(tmp));
            _player.Play(media);
        }
        catch
        {
            // Gracefully degrade — audio just won't play.
        }
    }

    /// <summary>Stop any currently playing media.</summary>
    public void Stop()
    {
        if (_player == null) return;
        try { _player.Stop(); } catch { }

        if (_currentTempFile != null)
        {
            try { File.Delete(_currentTempFile); } catch { }
            _currentTempFile = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _player?.Dispose();
        _libVLC?.Dispose();
        _player = null;
        _libVLC = null;
    }
}
