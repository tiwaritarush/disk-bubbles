using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiskBubbles
{
    internal static class AuroraBackdrop
    {
        public static BitmapSource Create(Color tint)
        {
            const int width = 720;
            const int height = 420;
            var pixels = new byte[width * height * 4];
            for (int x = 0; x < width; x++)
            {
                double u = x / (width - 1.0);
                double sweep = 0.19 + 0.49 * u + 0.098 * Math.Sin(5.7 * u - 0.3);
                double fold = 0.77 - 0.24 * u + 0.066 * Math.Sin(7.2 * u - 0.64);
                double echo = sweep + 0.16 + 0.025 * Math.Sin(8 * u);
                double widthScale = 0.9 + 0.23 * Math.Sin(2.9 * u);
                double veil = EdgeFade(u, 0.23) * (0.79 + 0.17 * Math.Sin(3.6 * u + 0.3));

                for (int y = 0; y < height; y++)
                {
                    double v = y / (height - 1.0);
                    double light = 0.125 * Band(v - sweep, 0.023 * widthScale)
                        + 0.123 * Band(v - sweep + 0.028, 0.093 * widthScale)
                        + 0.091 * Band(v - fold, 0.037)
                        + 0.105 * Band(v - fold + 0.028, 0.077)
                        + 0.055 * Band(v - echo, 0.049);
                    double dither = ((x * 7 + y * 13) & 15) / 16.0;
                    int index = (y * width + x) * 4;
                    pixels[index] = tint.B;
                    pixels[index + 1] = tint.G;
                    pixels[index + 2] = tint.R;
                    pixels[index + 3] = (byte)(255 * light * veil * EdgeFade(v, 0.16) + dither);
                }
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        private static double Band(double distance, double width)
        {
            double offset = distance / width;
            return Math.Exp(-offset * offset);
        }

        private static double EdgeFade(double position, double falloff)
        {
            double step = Math.Min(1, Math.Min(position, 1 - position) / falloff);
            return step * step * (3 - 2 * step);
        }
    }
}
