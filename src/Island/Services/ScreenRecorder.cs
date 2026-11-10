using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Island.Interop;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace Island.Services;

public enum RecordingStopReason { User, TimeLimit, LowDisk, DisplayChanged, CaptureFailed, AppExit }

/// <summary>How a recording ended. <see cref="Path"/> is set only when the MP4 was saved.</summary>
public sealed record RecordingResult(bool Saved, string? Path, TimeSpan Duration, RecordingStopReason Reason, Exception? Error);

/// <summary>
/// Fills a frame: width × height × 4 bytes of BGRA with the bottom row first, like a Windows DIB. Media Foundation reads
/// uncompressed RGB32 bottom-up, so this order needs no flipping. False when no frame could be taken.
/// </summary>
public interface IFrameSource : IDisposable
{
    bool Grab(byte[] destination);
}

/// <summary>Thrown by <see cref="ScreenRecorder.StartAsync"/> when recording cannot begin; the message is for the user.</summary>
public sealed class RecordingStartException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Records a screen region to MP4 (H.264, 30 fps, no sound). A capture thread grabs frames with GDI and draws the
/// cursor; Windows' MediaTranscoder pulls them through a MediaStreamSource and encodes on the GPU when it can.
/// The file is written as "*.mp4.part" and renamed only after the encoder has finalised it, so a failed or killed
/// recording never leaves a broken MP4 behind.
/// </summary>
public sealed class ScreenRecorder
{
    private const int QueueLength = 4;

    private readonly PixelRect _region;
    private readonly string _folder;
    private readonly Func<PixelRect, IFrameSource> _sourceFactory;
    private readonly string _partPath;
    private readonly string _fileName;
    private readonly int _frameBytes;
    private readonly Channel<Frame> _frames;
    private readonly ConcurrentBag<byte[]> _pool = new();
    private readonly Stopwatch _clock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<RecordingResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _captureThread;
    private FileStream? _file;
    private Windows.Storage.Streams.IRandomAccessStream? _stream;
    private RecordingStopReason _reason = RecordingStopReason.User;
    private int _stopRequested;
    private long _framesDelivered;
    private TimeSpan? _firstFrameTime;
    private TimeSpan _lastTimestamp = TimeSpan.MinValue;
    private Exception? _captureError;

    private readonly record struct Frame(byte[] Data, TimeSpan Time);

    private ScreenRecorder(PixelRect region, string folder, DateTime now, Func<PixelRect, IFrameSource> sourceFactory)
    {
        _region = region;
        _folder = folder;
        _sourceFactory = sourceFactory;
        _fileName = RecordingRules.FileName(now);
        _partPath = Path.Combine(folder, _fileName + ".part");
        _frameBytes = region.Width * region.Height * 4;
        _frames = Channel.CreateBounded<Frame>(
            new BoundedChannelOptions(QueueLength) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false, SingleWriter = true },
            dropped => _pool.Add(dropped.Data));
    }

    public PixelRect Region => _region;

    /// <summary>Time since the first frame; stops growing once the recording is stopped.</summary>
    public TimeSpan Elapsed => _clock.Elapsed;

    public long FramesDelivered => Interlocked.Read(ref _framesDelivered);

    /// <summary>Completes once the file is finalised (or removed after a failure); never faults.</summary>
    public Task<RecordingResult> Completion => _done.Task;

    /// <summary>
    /// Starts recording <paramref name="region"/> (physical pixels, even size) into <paramref name="folder"/>. Frames come
    /// from the screen with the cursor, or from <paramref name="sourceFactory"/> when given.
    /// </summary>
    public static Task<ScreenRecorder> StartAsync(PixelRect region, string folder, DateTime now, Func<PixelRect, IFrameSource>? sourceFactory = null) =>
        // Off the UI thread: the media pipeline is then created in the MTA and never needs the dispatcher.
        Task.Run(async () =>
        {
            var recorder = new ScreenRecorder(region, folder, now, sourceFactory ?? (r => new ScreenFrameSource(r)));
            await recorder.BeginAsync().ConfigureAwait(false);
            return recorder;
        });

    public Task<RecordingResult> StopAsync(RecordingStopReason reason = RecordingStopReason.User)
    {
        RequestStop(reason);
        return Completion;
    }

    private void RequestStop(RecordingStopReason reason)
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0) return;
        _reason = reason;
        _stop.Cancel();
    }

    // ---------- start ----------

    private async Task BeginAsync()
    {
        if (_region.IsEmpty || _region.Width % 2 != 0 || _region.Height % 2 != 0)
            throw new ArgumentException("The region must have an even, non-zero size.");

        try
        {
            Directory.CreateDirectory(_folder);
            RemoveStaleParts();
            if (FreeBytes() is { } free && !RecordingRules.HasRoomToStart(free))
                throw new RecordingStartException(Loc.T("Не хватает места на диске"));
            _file = new FileStream(_partPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new RecordingStartException(Loc.T("Нет доступа к папке Видео"), ex);
        }
        catch (IOException ex)
        {
            throw new RecordingStartException(ex.Message, ex);
        }

        // Frames flow from the moment the thread starts; the first one delivered becomes time zero.
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "Island screen capture" };
        _captureThread.Start();

        try
        {
            _stream = _file.AsRandomAccessStream();
            var prepared = await PrepareAsync(hardware: true).ConfigureAwait(false)
                           ?? await PrepareAsync(hardware: false).ConfigureAwait(false);
            if (prepared is null) throw new RecordingStartException(Loc.T("Не удалось начать запись"));
            _ = RunAsync(prepared);
        }
        catch (Exception ex)
        {
            RequestStop(RecordingStopReason.User);
            _captureThread.Join(2000);
            CloseFile();
            TryDelete(_partPath);
            throw ex as RecordingStartException ?? new RecordingStartException(ex.Message, ex);
        }
    }

    private async Task<PrepareTranscodeResult?> PrepareAsync(bool hardware)
    {
        var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_region.Width, (uint)_region.Height);
        input.FrameRate.Numerator = RecordingRules.FrameRate;
        input.FrameRate.Denominator = 1;
        input.PixelAspectRatio.Numerator = 1;
        input.PixelAspectRatio.Denominator = 1;

        var source = new MediaStreamSource(new VideoStreamDescriptor(input)) { BufferTime = TimeSpan.Zero, CanSeek = false };
        source.Starting += (_, args) => args.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += OnSampleRequested;

        var (width, height) = RecordingRules.OutputSize(_region.Width, _region.Height);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null;
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.Bitrate = RecordingRules.Bitrate(width, height);
        profile.Video.FrameRate.Numerator = RecordingRules.FrameRate;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        try
        {
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = hardware, AlwaysReencode = true };
            var result = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, _stream, profile).AsTask().ConfigureAwait(false);
            if (result.CanTranscode) return result;
        }
        catch (Exception ex) when (hardware)
        {
            App.LogError(ex);
        }

        source.SampleRequested -= OnSampleRequested;
        return null;
    }

    // ---------- encoding ----------

    private async Task RunAsync(PrepareTranscodeResult prepared)
    {
        Exception? error = null;
        try
        {
            await prepared.TranscodeAsync().AsTask().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // A failed encoder stops pulling frames; stop capturing too.
        RequestStop(error is null ? _reason : RecordingStopReason.CaptureFailed);
        _captureThread?.Join(5000);
        while (_frames.Reader.TryRead(out var left)) _pool.Add(left.Data);
        CloseFile();

        var duration = _clock.Elapsed;
        error ??= _captureError;
        if (error is null && FramesDelivered > 0)
        {
            try
            {
                var name = RecordingRules.UniqueFileName(_fileName, n => File.Exists(Path.Combine(_folder, n)));
                var path = Path.Combine(_folder, name);
                File.Move(_partPath, path);
                _done.TrySetResult(new RecordingResult(true, path, duration, _reason, null));
                return;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }

        TryDelete(_partPath);
        if (error is not null && IsDiskFull(error)) _reason = RecordingStopReason.LowDisk;
        _done.TrySetResult(new RecordingResult(false, null, duration, _reason, error));
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var request = args.Request;
        if (_frames.Reader.TryRead(out var ready))
        {
            request.Sample = MakeSample(ready);
            return;
        }

        var deferral = request.GetDeferral();
        _ = DeliverLaterAsync(request, deferral);
    }

    private async Task DeliverLaterAsync(MediaStreamSourceSampleRequest request, MediaStreamSourceSampleRequestDeferral deferral)
    {
        try
        {
            while (await _frames.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                if (!_frames.Reader.TryRead(out var frame)) continue;
                request.Sample = MakeSample(frame);
                return;
            }

            // The capture thread completed the channel: a null sample ends the stream and the MP4 gets finalised.
            request.Sample = null;
        }
        catch
        {
            request.Sample = null;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private MediaStreamSample MakeSample(Frame frame)
    {
        _firstFrameTime ??= frame.Time;
        var timestamp = frame.Time - _firstFrameTime.Value;
        if (timestamp <= _lastTimestamp) timestamp = _lastTimestamp + TimeSpan.FromMilliseconds(1);
        _lastTimestamp = timestamp;

        var sample = MediaStreamSample.CreateFromBuffer(frame.Data.AsBuffer(0, _frameBytes), timestamp);
        sample.Duration = RecordingRules.FrameInterval;
        var data = frame.Data;
        sample.Processed += (_, _) => _pool.Add(data);
        Interlocked.Increment(ref _framesDelivered);
        return sample;
    }

    // ---------- capture ----------

    private void CaptureLoop()
    {
        Native.timeBeginPeriod(1);
        IFrameSource? grabber = null;
        try
        {
            grabber = _sourceFactory(_region);
            _clock.Start();
            var token = _stop.Token;
            long index = 0;
            var failures = 0;
            var nextSpaceCheck = TimeSpan.FromSeconds(5);

            while (!token.IsCancellationRequested)
            {
                var due = RecordingRules.FrameInterval * index;
                var now = _clock.Elapsed;
                if (now < due)
                {
                    token.WaitHandle.WaitOne(due - now);
                    continue;
                }

                if (RecordingRules.ReachedLimit(now))
                {
                    RequestStop(RecordingStopReason.TimeLimit);
                    break;
                }

                // Fell behind (a slow frame or a suspended machine): skip ahead instead of bursting.
                if (now - due > RecordingRules.FrameInterval * 3) index = (long)(now.Ticks / RecordingRules.FrameInterval.Ticks);
                index++;

                var buffer = Rent();
                if (grabber.Grab(buffer))
                {
                    failures = 0;
                    if (!_frames.Writer.TryWrite(new Frame(buffer, now))) _pool.Add(buffer);
                }
                else
                {
                    _pool.Add(buffer);
                    // The screen went away (locked session, display reset); a few seconds of failures end the recording.
                    if (++failures > RecordingRules.FrameRate * 3)
                    {
                        RequestStop(RecordingStopReason.CaptureFailed);
                        break;
                    }
                }

                if (now >= nextSpaceCheck)
                {
                    nextSpaceCheck = now + TimeSpan.FromSeconds(5);
                    if (FreeBytes() is { } free && !RecordingRules.HasRoomToContinue(free))
                    {
                        RequestStop(RecordingStopReason.LowDisk);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _captureError = ex;
            RequestStop(RecordingStopReason.CaptureFailed);
        }
        finally
        {
            _clock.Stop();
            grabber?.Dispose();
            Native.timeEndPeriod(1);
            _frames.Writer.TryComplete();
        }
    }

    private byte[] Rent() => _pool.TryTake(out var buffer) ? buffer : new byte[_frameBytes];

    // ---------- files ----------

    private long? FreeBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_folder));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Leftovers of a recording that was killed (power loss, crash): they can never become a valid MP4.</summary>
    private void RemoveStaleParts()
    {
        try
        {
            foreach (var part in Directory.EnumerateFiles(_folder, "Island-*.mp4.part"))
            {
                if (File.GetLastWriteTimeUtc(part) < DateTime.UtcNow.AddMinutes(-1)) TryDelete(part);
            }
        }
        catch
        {
            // Not worth failing a recording over.
        }
    }

    private void CloseFile()
    {
        try { _stream?.Dispose(); } catch { }
        try { _file?.Dispose(); } catch { }
        _stream = null;
        _file = null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static bool IsDiskFull(Exception ex)
    {
        const int DiskFull = unchecked((int)0x80070070), HandleDiskFull = unchecked((int)0x80070027);
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.HResult is DiskFull or HandleDiskFull) return true;
        }

        return false;
    }
}

/// <summary>Copies a screen region into a bottom-up BGRA buffer with GDI and draws the mouse cursor on it.</summary>
public sealed class ScreenFrameSource : IFrameSource
{
    private readonly PixelRect _region;
    private readonly IntPtr _screenDc, _memDc, _bitmap, _oldBitmap, _bits;
    private readonly int _bytes;
    private IntPtr _cursor;
    private int _hotX, _hotY;

    public ScreenFrameSource(PixelRect region)
    {
        _region = region;
        _bytes = region.Width * region.Height * 4;
        _screenDc = Native.GetDC(IntPtr.Zero);
        _memDc = Native.CreateCompatibleDC(_screenDc);
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = region.Width,
            biHeight = region.Height, // positive: bottom-up rows, the order the encoder reads RGB32 in
            biPlanes = 1,
            biBitCount = 32,
        };
        _bitmap = Native.CreateDIBSection(_memDc, ref header, 0, out _bits, IntPtr.Zero, 0);
        if (_screenDc == IntPtr.Zero || _memDc == IntPtr.Zero || _bitmap == IntPtr.Zero || _bits == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("Could not create the capture bitmap.");
        }

        _oldBitmap = Native.SelectObject(_memDc, _bitmap);
    }

    public bool Grab(byte[] destination)
    {
        if (!Native.BitBlt(_memDc, 0, 0, _region.Width, _region.Height, _screenDc, _region.X, _region.Y, Native.SRCCOPY)) return false;
        DrawCursor();
        Native.GdiFlush();
        Marshal.Copy(_bits, destination, 0, _bytes);
        return true;
    }

    private void DrawCursor()
    {
        var info = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref info) || (info.flags & Native.CURSOR_SHOWING) == 0 || info.hCursor == IntPtr.Zero) return;

        if (info.hCursor != _cursor)
        {
            _cursor = info.hCursor;
            _hotX = _hotY = 0;
            if (Native.GetIconInfo(info.hCursor, out var icon))
            {
                _hotX = icon.xHotspot;
                _hotY = icon.yHotspot;
                if (icon.hbmMask != IntPtr.Zero) Native.DeleteObject(icon.hbmMask);
                if (icon.hbmColor != IntPtr.Zero) Native.DeleteObject(icon.hbmColor);
            }
        }

        var x = info.ptScreenPos.X - _hotX - _region.X;
        var y = info.ptScreenPos.Y - _hotY - _region.Y;
        if (x > _region.Width || y > _region.Height || x < -256 || y < -256) return;
        Native.DrawIconEx(_memDc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
    }

    public void Dispose()
    {
        if (_memDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) Native.SelectObject(_memDc, _oldBitmap);
        if (_bitmap != IntPtr.Zero) Native.DeleteObject(_bitmap);
        if (_memDc != IntPtr.Zero) Native.DeleteDC(_memDc);
        if (_screenDc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, _screenDc);
    }
}
