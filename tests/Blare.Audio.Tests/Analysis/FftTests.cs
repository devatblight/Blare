using Blight.Blare.Audio.Analysis;

namespace Blare.Audio.Tests.Analysis;

public class FftTests
{
    [Fact]
    public void DcSignal_PutsAllEnergyInBinZero()
    {
        var real = Enumerable.Repeat(1.0, 16).ToArray();
        var imaginary = new double[16];

        Fft.Transform(real, imaginary);

        Assert.Equal(16.0, real[0], precision: 6);
        for (var i = 1; i < 16; i++)
        {
            Assert.Equal(0.0, Magnitude(real[i], imaginary[i]), precision: 6);
        }
    }

    [Fact]
    public void PureSineAtBinK_PeaksAtBinK()
    {
        const int n = 64;
        const int k = 8;

        var real = new double[n];
        var imaginary = new double[n];
        for (var i = 0; i < n; i++)
        {
            real[i] = Math.Sin(2.0 * Math.PI * k * i / n);
        }

        Fft.Transform(real, imaginary);

        var peakBin = 0;
        double peak = 0;
        for (var i = 1; i < n / 2; i++)
        {
            var magnitude = Magnitude(real[i], imaginary[i]);
            if (magnitude > peak)
            {
                peak = magnitude;
                peakBin = i;
            }
        }

        Assert.Equal(k, peakBin);
    }

    [Fact]
    public void Transform_IsInvertibleViaConjugateTrick()
    {
        const int n = 32;
        var original = new double[n];
        for (var i = 0; i < n; i++)
        {
            original[i] = Math.Sin(i * 0.3) + 0.5 * Math.Cos(i * 1.1);
        }

        var real = (double[])original.Clone();
        var imaginary = new double[n];

        Fft.Transform(real, imaginary);

        // Inverse via conjugate: conj -> forward -> conj -> scale by 1/n.
        for (var i = 0; i < n; i++)
        {
            imaginary[i] = -imaginary[i];
        }

        Fft.Transform(real, imaginary);

        for (var i = 0; i < n; i++)
        {
            Assert.Equal(original[i], real[i] / n, precision: 6);
        }
    }

    [Fact]
    public void NonPowerOfTwoLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => Fft.Transform(new double[6], new double[6]));
    }

    [Fact]
    public void MismatchedLengths_Throw()
    {
        Assert.Throws<ArgumentException>(() => Fft.Transform(new double[8], new double[4]));
    }

    private static double Magnitude(double real, double imaginary) =>
        Math.Sqrt(real * real + imaginary * imaginary);
}
