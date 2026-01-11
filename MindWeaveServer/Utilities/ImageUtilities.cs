using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

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
            try
            {
                using (var ms = new MemoryStream(originalBytes))
                using (var original = Image.FromStream(ms))
                {
                    int newW = original.Width;
                    int newH = original.Height;
                    if (original.Width > MAX_SIZE || original.Height > MAX_SIZE)
                    {
                        double ratio = Math.Min((double)MAX_SIZE / original.Width, (double)MAX_SIZE / original.Height);
                        newW = (int)(original.Width * ratio);
                        newH = (int)(original.Height * ratio);
                    }
                    newW = Math.Max(newW, 1);
                    newH = Math.Max(newH, 1);
                    using (var resized = new Bitmap(newW, newH))
                    {
                        resized.SetResolution(72, 72);
                        using (var g = Graphics.FromImage(resized))
                        {
                            g.CompositingQuality = CompositingQuality.HighSpeed;
                            g.InterpolationMode = InterpolationMode.Low;
                            g.SmoothingMode = SmoothingMode.HighSpeed;
                            g.DrawImage(original, 0, 0, newW, newH);
                        }
                        var jpegCodec = getEncoder(ImageFormat.Jpeg);
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, QUALITY);
                        using (var outMs = new MemoryStream())
                        {
                            resized.Save(outMs, jpegCodec, encoderParams);
                            return outMs.ToArray();
                        }
                    }
                }
            }
            catch (ArgumentException)
            {
                return Array.Empty<byte>();
            }
            catch (ExternalException)
            {
                return Array.Empty<byte>();
            }
        }

        public static byte[] optimizeImageAsPng(byte[] originalBytes)
        {
            try
            {
                using (var ms = new MemoryStream(originalBytes))
                using (var original = Image.FromStream(ms))
                {
                    int newW = original.Width;
                    int newH = original.Height;

                    if (original.Width > MAX_SIZE || original.Height > MAX_SIZE)
                    {
                        double ratio = Math.Min((double)MAX_SIZE / original.Width, (double)MAX_SIZE / original.Height);
                        newW = (int)(original.Width * ratio);
                        newH = (int)(original.Height * ratio);
                    }

                    newW = Math.Max(newW, 1);
                    newH = Math.Max(newH, 1);

                    using (var resized = new Bitmap(newW, newH, PixelFormat.Format32bppArgb))
                    {
                        resized.SetResolution(72, 72);
                        using (var g = Graphics.FromImage(resized))
                        {
                            g.CompositingQuality = CompositingQuality.HighSpeed;
                            g.InterpolationMode = InterpolationMode.Low;
                            g.SmoothingMode = SmoothingMode.HighSpeed;
                            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                            g.Clear(Color.Transparent);
                            g.DrawImage(original, 0, 0, newW, newH);
                        }

                        using (var outMs = new MemoryStream())
                        {
                            resized.Save(outMs, ImageFormat.Png);
                            return outMs.ToArray();
                        }
                    }
                }
            }
            catch (ArgumentException)
            {
                return Array.Empty<byte>();
            }
            catch (ExternalException)
            {
                return Array.Empty<byte>();
            }
        }

        public static byte[] extractPieceWithMask(
            Image sourceImage,
            GraphicsPath clipPath,
            int sourceX,
            int sourceY,
            int width,
            int height)
        {
            using (var pieceBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                pieceBitmap.SetResolution(96, 96);

                using (var g = Graphics.FromImage(pieceBitmap))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.HighSpeed;
                    g.InterpolationMode = InterpolationMode.Low;
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    g.CompositingMode = CompositingMode.SourceOver;

                    g.SetClip(clipPath);

                    int srcX = Math.Max(0, sourceX);
                    int srcY = Math.Max(0, sourceY);
                    int destX = sourceX < 0 ? -sourceX : 0;
                    int destY = sourceY < 0 ? -sourceY : 0;

                    int availableWidth = sourceImage.Width - srcX;
                    int availableHeight = sourceImage.Height - srcY;
                    int drawWidth = Math.Min(width - destX, availableWidth);
                    int drawHeight = Math.Min(height - destY, availableHeight);

                    if (drawWidth > 0 && drawHeight > 0)
                    {
                        var srcRect = new Rectangle(srcX, srcY, drawWidth, drawHeight);
                        var destRect = new Rectangle(destX, destY, drawWidth, drawHeight);
                        g.DrawImage(sourceImage, destRect, srcRect, GraphicsUnit.Pixel);
                    }
                }

                using (var ms = new MemoryStream())
                {
                    pieceBitmap.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        public static byte[] generateSilhouetteImage(
            int puzzleWidth,
            int puzzleHeight,
            System.Collections.Generic.List<SilhouetteData> silhouettes)
        {
            using (var bitmap = new Bitmap(puzzleWidth, puzzleHeight, PixelFormat.Format32bppArgb))
            {
                bitmap.SetResolution(96, 96);

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.FromArgb(255, 26, 26, 26));
                    g.SmoothingMode = SmoothingMode.HighSpeed;
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                    g.CompositingQuality = CompositingQuality.HighSpeed;

                    using (var fillBrush = new SolidBrush(Color.FromArgb(255, 40, 40, 40)))
                    using (var borderPen = new Pen(Color.FromArgb(255, 55, 55, 55), 1f))
                    {
                        foreach (var sil in silhouettes)
                        {
                            using (var translatedPath = (GraphicsPath)sil.Path.Clone())
                            {
                                var matrix = new Matrix();
                                matrix.Translate((float)sil.X, (float)sil.Y);
                                translatedPath.Transform(matrix);

                                g.FillPath(fillBrush, translatedPath);
                                g.DrawPath(borderPen, translatedPath);
                            }
                        }
                    }
                }

                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }
    }

    public class SilhouetteData
    {
        public GraphicsPath Path { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }
}