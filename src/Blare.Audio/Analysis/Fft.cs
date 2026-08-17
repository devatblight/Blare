namespace Blight.Blare.Audio.Analysis;

/// <summary>
/// In-place iterative radix-2 Cooley-Tukey FFT. Length must be a power of
/// two. Written out rather than pulled from a DSP library because this is
/// the only transform the app needs and it keeps Blare.Audio dependency-free.
/// </summary>
public static class Fft
{
    public static void Transform(Span<double> real, Span<double> imaginary)
    {
        var n = real.Length;

        if (n != imaginary.Length)
        {
            throw new ArgumentException("Real and imaginary spans must be the same length.");
        }

        if (n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("Length must be a positive power of two.", nameof(real));
        }

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        // Butterfly stages.
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2.0 * Math.PI / len;
            var wReal = Math.Cos(angle);
            var wImaginary = Math.Sin(angle);

            for (var i = 0; i < n; i += len)
            {
                double currentReal = 1.0, currentImaginary = 0.0;

                for (var k = 0; k < len / 2; k++)
                {
                    var evenReal = real[i + k];
                    var evenImaginary = imaginary[i + k];

                    var oddReal = real[i + k + len / 2] * currentReal - imaginary[i + k + len / 2] * currentImaginary;
                    var oddImaginary = real[i + k + len / 2] * currentImaginary + imaginary[i + k + len / 2] * currentReal;

                    real[i + k] = evenReal + oddReal;
                    imaginary[i + k] = evenImaginary + oddImaginary;
                    real[i + k + len / 2] = evenReal - oddReal;
                    imaginary[i + k + len / 2] = evenImaginary - oddImaginary;

                    var nextReal = currentReal * wReal - currentImaginary * wImaginary;
                    currentImaginary = currentReal * wImaginary + currentImaginary * wReal;
                    currentReal = nextReal;
                }
            }
        }
    }
}
