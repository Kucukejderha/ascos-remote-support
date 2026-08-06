using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteSupport.SessionAgent;

public static class ScreenFrameProtocol
{
    public const byte RawFrame = 1;
    public const byte CompressedFrame = 2;
    public const byte JpegFrame = 3;
}

public sealed class ScreenFrameEncoder
{
    private byte[]? _previous;
    private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders()
        .Single(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    public byte[]? Encode(CapturedFrame frame, bool forceKeyFrame = false)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var pixels = frame.Pixels;
        if (pixels.Length != checked(frame.Width * frame.Height * 4))
            throw new ArgumentException("Frame pixel length does not match its dimensions.", nameof(frame));
        if (!forceKeyFrame && _previous is not null && AreEqual(pixels, _previous)) return null;
        _previous = pixels.ToArray();

        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var area = new Rectangle(0, 0, frame.Width, frame.Height);
        var data = bitmap.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride == frame.Width * 4)
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            else
                for (var y = 0; y < frame.Height; y++)
                    Marshal.Copy(pixels, y * frame.Width * 4, IntPtr.Add(data.Scan0, y * data.Stride), frame.Width * 4);
        }
        finally { bitmap.UnlockBits(data); }

        using var jpeg = new MemoryStream(96 * 1024);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 68L);
        bitmap.Save(jpeg, JpegCodec, parameters);
        var image = jpeg.ToArray();
        var packet = new byte[5 + image.Length];
        packet[0] = ScreenFrameProtocol.JpegFrame;
        packet[1] = (byte)frame.Width; packet[2] = (byte)(frame.Width >> 8);
        packet[3] = (byte)frame.Height; packet[4] = (byte)(frame.Height >> 8);
        Buffer.BlockCopy(image, 0, packet, 5, image.Length);
        return packet;
    }

    private static bool AreEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
            if (left[i] != right[i]) return false;
        return true;
    }
}
