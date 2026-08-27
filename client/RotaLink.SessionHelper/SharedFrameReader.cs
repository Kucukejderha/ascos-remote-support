using System.IO.MemoryMappedFiles;
using RemoteSupport.Protocol;

namespace RotaLink.SessionHelper;

internal sealed class SharedFrameReader : IDisposable
{
    private const int HeaderBytes = 64;
    private const uint Magic = 0x4D465452;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly EventWaitHandle _ready;
    private readonly int _capacity;

    private SharedFrameReader(MemoryMappedFile mapping, MemoryMappedViewAccessor view, EventWaitHandle ready)
    {
        _mapping = mapping;
        _view = view;
        _ready = ready;
        if (_view.ReadUInt32(0) != Magic || _view.ReadUInt16(4) != 1 || _view.ReadUInt16(6) != HeaderBytes)
            throw new InvalidDataException("The native capture mapping has an unsupported header.");
        _capacity = checked((int)_view.ReadUInt32(8));
        if (_capacity is <= 0 or > VideoPacketCodec.MaximumPayloadBytes)
            throw new InvalidDataException("The native capture mapping capacity is invalid.");
    }

    public static SharedFrameReader Open(uint sessionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Environment.TickCount;
        Exception? lastError = null;
        while (unchecked(Environment.TickCount - started) < (int)timeout.TotalMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MemoryMappedFile? mapping = null;
            MemoryMappedViewAccessor? view = null;
            EventWaitHandle? ready = null;
            try
            {
                mapping = MemoryMappedFile.OpenExisting("Global\\RotaLink.FrameMap." + sessionId, MemoryMappedFileRights.Read);
                view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                ready = EventWaitHandle.OpenExisting("Global\\RotaLink.FrameReady." + sessionId);
                var result = new SharedFrameReader(mapping, view, ready);
                mapping = null;
                view = null;
                ready = null;
                return result;
            }
            catch (WaitHandleCannotBeOpenedException exception) { lastError = exception; }
            catch (FileNotFoundException exception) { lastError = exception; }
            finally
            {
                ready?.Dispose();
                view?.Dispose();
                mapping?.Dispose();
            }
            cancellationToken.WaitHandle.WaitOne(100);
        }
        throw new TimeoutException("Native capture shared memory did not appear.", lastError);
    }

    public SharedVideoFrame ReadLatest(CancellationToken cancellationToken)
    {
        var signaled = WaitHandle.WaitAny(new WaitHandle[] { _ready, cancellationToken.WaitHandle });
        if (signaled == 1) throw new OperationCanceledException(cancellationToken);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var sequenceBefore = _view.ReadInt64(32);
            Thread.MemoryBarrier();
            var length = checked((int)_view.ReadUInt32(12));
            var width = checked((int)_view.ReadUInt32(16));
            var height = checked((int)_view.ReadUInt32(20));
            var codec = (VideoCodec)_view.ReadUInt32(24);
            var flags = _view.ReadUInt32(28);
            var timestamp = _view.ReadInt64(40);
            if (sequenceBefore <= 0 || (sequenceBefore & 1) != 0 || length is <= 0 || length > _capacity || width <= 0 || height <= 0 || codec != VideoCodec.H264AnnexB)
                throw new InvalidDataException("Native capture published an invalid frame header.");
            var payload = new byte[length];
            _view.ReadArray(HeaderBytes, payload, 0, length);
            Thread.MemoryBarrier();
            if (_view.ReadInt64(32) == sequenceBefore)
                return new SharedVideoFrame(sequenceBefore, timestamp, width, height, (flags & 1) != 0, payload);
        }
        throw new IOException("Native frame changed repeatedly while being copied.");
    }

    public void Dispose()
    {
        _ready.Dispose();
        _view.Dispose();
        _mapping.Dispose();
    }
}

internal sealed class SharedVideoFrame
{
    public SharedVideoFrame(long sequence, long timestamp100Nanoseconds, int width, int height, bool keyFrame, byte[] payload)
    {
        Sequence = sequence;
        Timestamp100Nanoseconds = timestamp100Nanoseconds;
        Width = width;
        Height = height;
        KeyFrame = keyFrame;
        Payload = payload;
    }

    public long Sequence { get; }
    public long Timestamp100Nanoseconds { get; }
    public int Width { get; }
    public int Height { get; }
    public bool KeyFrame { get; }
    public byte[] Payload { get; }
}
