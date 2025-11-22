using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace MindWeaveServer.Utilities
{
    public static class ImageUtilities
    {
        private const int MAX_SIZE = 1024;
        private const long QUALITY = 70L;

        public static ImageCodecInfo getEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid) return codec;
            }
            return null;
        }

        public static byte[] optimizeImage(byte[] originalBytes)
        {
            using (var ms = new MemoryStream(originalBytes))
            using (var original = Image.FromStream(ms))
            {
                int newW = original.Width;
                int newH = original.Height;

                if (original.Width > MAX_SIZE || original.Height > MAX_SIZE)
                {
                    double ratio = System.Math.Min((double)MAX_SIZE / original.Width, (double)MAX_SIZE / original.Height);
                    newW = (int)(original.Width * ratio);
                    newH = (int)(original.Height * ratio);
                }

                using (var resized = new Bitmap(newW, newH))
                {
                    resized.SetResolution(72, 72);

                    using (var g = Graphics.FromImage(resized))
                    {
                        g.CompositingQuality = CompositingQuality.HighSpeed;
                        g.InterpolationMode = InterpolationMode.Bicubic;
                        g.SmoothingMode = SmoothingMode.HighSpeed;
                        g.DrawImage(original, 0, 0, newW, newH);
                    }

                    var jpegCodec = getEncoder(ImageFormat.Jpeg);
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, QUALITY);

                    using (var outMs = new MemoryStream())
                    {
                        resized.Save(outMs, jpegCodec, encoderParams);
                        return outMs.ToArray();
                    }
                }
            }
        }
    }
}