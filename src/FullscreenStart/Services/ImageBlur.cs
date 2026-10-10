namespace FullscreenStart.Services;

/// <summary>Fast approximate gaussian blur (repeated separable box blur) on 32-bit BGRA pixels.</summary>
public static class ImageBlur
{
    public static void Blur(byte[] bgra, int width, int height, int radius, int passes = 3)
    {
        if (radius <= 0 || width <= 0 || height <= 0) return;
        if (bgra.Length < width * height * 4) throw new ArgumentException("Pixel buffer too small.", nameof(bgra));

        var temp = new byte[width * height * 4];
        for (int p = 0; p < passes; p++)
        {
            BoxPass(bgra, temp, width, height, radius, horizontal: true);
            BoxPass(temp, bgra, width, height, radius, horizontal: false);
        }
    }

    private static void BoxPass(byte[] src, byte[] dst, int width, int height, int radius, bool horizontal)
    {
        int lines = horizontal ? height : width;
        int length = horizontal ? width : height;
        int window = radius * 2 + 1;

        Parallel.For(0, lines, line =>
        {
            int Index(int i) => horizontal ? (line * width + i) * 4 : (i * width + line) * 4;

            // Running sums over a window with clamped edges.
            int s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            for (int k = -radius; k <= radius; k++)
            {
                int idx = Index(Math.Clamp(k, 0, length - 1));
                s0 += src[idx]; s1 += src[idx + 1]; s2 += src[idx + 2]; s3 += src[idx + 3];
            }

            for (int i = 0; i < length; i++)
            {
                int o = Index(i);
                dst[o] = (byte)(s0 / window);
                dst[o + 1] = (byte)(s1 / window);
                dst[o + 2] = (byte)(s2 / window);
                dst[o + 3] = (byte)(s3 / window);

                int add = Index(Math.Min(i + radius + 1, length - 1));
                int sub = Index(Math.Max(i - radius, 0));
                s0 += src[add] - src[sub];
                s1 += src[add + 1] - src[sub + 1];
                s2 += src[add + 2] - src[sub + 2];
                s3 += src[add + 3] - src[sub + 3];
            }
        });
    }
}
