using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Island.Services;

public sealed class MediaSnapshot
{
    public bool HasSession { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public bool IsPlaying { get; init; }
    public bool CanPrev { get; init; }
    public bool CanNext { get; init; }
    public BitmapImage? Art { get; init; }
}

/// <summary>Reads and controls whatever Windows currently considers the active media session.</summary>
public sealed class MediaService
{
    private readonly Dispatcher _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _refreshId;

    public MediaService(Dispatcher ui) => _ui = ui;

    public MediaSnapshot State { get; private set; } = new();

    public event Action<MediaSnapshot>? Changed;

    public async Task InitAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            _manager.CurrentSessionChanged += (_, _) => Rebind();
            Rebind();
        }
        catch
        {
            // Media sessions unavailable: the home card simply stays empty.
        }
    }

    private void Rebind()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnSessionChanged;
            _session.PlaybackInfoChanged -= OnSessionChanged;
        }

        _session = _manager?.GetCurrentSession();
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
        }

        _ = RefreshAsync();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession s, object args) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var id = ++_refreshId;
        var session = _session;
        if (session is null)
        {
            Publish(new MediaSnapshot(), id);
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync().AsTask();
            var info = session.GetPlaybackInfo();
            byte[]? art = null;
            if (props.Thumbnail is { } thumb)
            {
                try
                {
                    using var stream = await thumb.OpenReadAsync().AsTask();
                    var bytes = new byte[stream.Size];
                    using var reader = new DataReader(stream);
                    await reader.LoadAsync((uint)stream.Size).AsTask();
                    reader.ReadBytes(bytes);
                    art = bytes;
                }
                catch
                {
                    art = null;
                }
            }

            if (id != _refreshId) return;

            _ui.Invoke(() =>
            {
                BitmapImage? image = null;
                if (art is { Length: > 0 })
                {
                    try
                    {
                        image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = new MemoryStream(art);
                        image.EndInit();
                        image.Freeze();
                    }
                    catch
                    {
                        image = null;
                    }
                }

                var next = new MediaSnapshot
                {
                    HasSession = true,
                    Title = props.Title ?? "",
                    Artist = props.Artist ?? "",
                    IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    CanPrev = info.Controls.IsPreviousEnabled,
                    CanNext = info.Controls.IsNextEnabled,
                    Art = image,
                };
                if (id != _refreshId) return;
                State = next;
                Changed?.Invoke(next);
            });
        }
        catch
        {
            Publish(new MediaSnapshot(), id);
        }
    }

    private void Publish(MediaSnapshot state, int id)
    {
        _ui.Invoke(() =>
        {
            if (id != _refreshId) return;
            State = state;
            Changed?.Invoke(state);
        });
    }

    /// <summary>Current playback position and track length; length is zero when unknown.</summary>
    public (TimeSpan Position, TimeSpan Duration) GetTimeline()
    {
        try
        {
            if (_session is null) return (TimeSpan.Zero, TimeSpan.Zero);
            var tl = _session.GetTimelineProperties();
            var duration = tl.EndTime - tl.StartTime;
            var position = tl.Position;
            if (State.IsPlaying) position += DateTimeOffset.Now - tl.LastUpdatedTime;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && position > duration) position = duration;
            return (position, duration);
        }
        catch
        {
            return (TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync());

    public Task NextAsync() => Run(s => s.TrySkipNextAsync());

    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync());

    private async Task Run(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> action)
    {
        try
        {
            if (_session is not null) await action(_session).AsTask();
        }
        catch
        {
            // The player refused or went away; the next state event corrects the UI.
        }
    }
}
