using System.IO.Compression;

namespace RemoteSupport.SessionAgent;

public static class ScreenFrameProtocol
{
    public const byte CompressedFrame = 2;
    public const byte KeyFrame = 1;
    public const int HeaderBytes = 6;
}

public sealed class ScreenFrameEncoder
{
    private byte[]? _previous;
    private int _width;
    private int _height;

    public byte[]? Encode(CapturedFrame frame, bool forceKeyFrame = false)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var pixels = frame.Pixels;
        if (pixels.Length != checked(frame.Width * frame.Height * 4))
            throw new ArgumentException("Frame pixel length does not match its dimensions.", nameof(frame));

        var dimensionsChanged = frame.Width != _width || frame.Height != _height;
        var keyFrame = forceKeyFrame || dimensionsChanged || _previous is null || _previous.Length != pixels.Length;
        byte[] source;

        if (keyFrame)
        {
            source = pixels;
        }
        else
        {
            source = new byte[pixels.Length];
            var changed = false;
            for (var i = 0; i < pixels.Length; i++)
            {
                var difference = (byte)(pixels[i] ^ _previous![i]);
                source[i] = difference;
                changed |= difference != 0;
            }
            if (!changed) return null;
        }

        _previous = pixels.ToArray();
        _width = frame.Width;
        _height = frame.Height;

        using var output = new MemoryStream(Math.Min(source.Length, 256 * 1024));
        output.WriteByte(ScreenFrameProtocol.CompressedFrame);
        output.WriteByte(keyFrame ? ScreenFrameProtocol.KeyFrame : (byte)0);
        var dimensionBytes = new byte[4];
        var width = checked((ushort)frame.Width);
        var height = checked((ushort)frame.Height);
        dimensionBytes[0] = (byte)width; dimensionBytes[1] = (byte)(width >> 8);
        dimensionBytes[2] = (byte)height; dimensionBytes[3] = (byte)(height >> 8);
        output.Write(dimensionBytes, 0, dimensionBytes.Length);
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(source, 0, source.Length);
        return output.ToArray();
    }
}
