﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;

namespace Obsidian.Elements;

public class FilterButterworth<S> where S : unmanaged, IAudioSample<S>
{
    /// resonance amount goes from sqrt(2) to ~ 0.1

    private float c, a1, a2, a3, b1, b2;

    /// <summary>
    /// Array of input values, latest are in front
    /// </summary>
    private S[] inputHistory = new S[2];

    /// <summary>
    /// Array of output values, latest are in front
    /// </summary>
    private S[] outputHistory = new S[3];

    public void UpdateCoefficients(float frequency, int sampleRate, PassType passType, float resonance)
    {
        switch (passType)
        {
            case PassType.Lowpass:
                c = 1.0f / (float)Math.Tan(Math.PI * frequency / sampleRate);
                a1 = 1.0f / (1.0f + resonance * c + c * c);
                a2 = 2f * a1;
                a3 = a1;
                b1 = 2.0f * (1.0f - c * c) * a1;
                b2 = (1.0f - resonance * c + c * c) * a1;
                break;
            case PassType.Highpass:
                c = (float)Math.Tan(Math.PI * frequency / sampleRate);
                a1 = 1.0f / (1.0f + resonance * c + c * c);
                a2 = -2f * a1;
                a3 = a1;
                b1 = 2.0f * (c * c - 1.0f) * a1;
                b2 = (1.0f - resonance * c + c * c) * a1;
                break;
        }
    }

    public enum PassType
    {
        Highpass,
        Lowpass,
    }

    public void Update(ref S newInput)
    {
        S first = newInput.Multiply(a1);
        S second = this.inputHistory[0].Multiply(a2);
        S third = this.inputHistory[1].Multiply(a3);
        S fourth = this.outputHistory[0].Multiply(b1);
        S fifth = this.outputHistory[1].Multiply(b2);
        S final = first.Add(second).Add(third).Subtract(fourth).Subtract(fifth);

        for (int i = 0; i < final.ChannelCount; i++)
        {
            if (final[i] > 1f) final = final.SetChannel(i, 1f);
            else if (final[i] < -1f) final = final.SetChannel(i, -1f);
        }

        this.inputHistory[1] = this.inputHistory[0];
        this.inputHistory[0] = newInput;

        this.outputHistory[2] = this.outputHistory[1];
        this.outputHistory[1] = this.outputHistory[0];
        this.outputHistory[0] = final;

        newInput = final;
    }

    public S Value
    {
        get { return this.outputHistory[0]; }
    }
}

public class ButterworthFilterController
{
    private Dictionary<Type, object> filters = new();

    public void Clear()
    {
        filters.Clear();
    }

    public void Process<S>(Span<S> buffer, bool lowPass, float freq, float resonance) where S : unmanaged, IAudioSample<S>
    {
        // avoid dividing by zero
        if (freq == 0f)
        {
            buffer.Fill(default(S));
            Clear();
            return;
        }

        if (!filters.TryGetValue(typeof(S), out object filter))
        {
            filter = new FilterButterworth<S>();
            filters.Add(typeof(S), filter);
        }

        ((FilterButterworth<S>)filter).UpdateCoefficients(freq, Engine.Current.AudioSystem.SampleRate, lowPass ? FilterButterworth<S>.PassType.Lowpass : FilterButterworth<S>.PassType.Highpass, resonance);

        for (int i = 0; i < buffer.Length; i++)
        {
            ((FilterButterworth<S>)filter).Update(ref buffer[i]);
        }
    }
}

public class BandPassFilterController
{
    private Dictionary<Type, object> lowFilters = new();
    private Dictionary<Type, object> highFilters = new();

    public void Clear()
    {
        lowFilters.Clear();
        highFilters.Clear();
    }

    public void Process<S>(Span<S> buffer, float lowFreq, float highFreq, float resonance) where S : unmanaged, IAudioSample<S>
    {
        // avoid dividing by zero
        if (lowFreq == 0f || highFreq == 0f)
        {
            buffer.Fill(default(S));
            Clear();
            return;
        }

        if (!lowFilters.TryGetValue(typeof(S), out object lowFilter))
        {
            lowFilter = new FilterButterworth<S>();
            lowFilters.Add(typeof(S), lowFilter);
        }
        if (!highFilters.TryGetValue(typeof(S), out object highFilter))
        {
            highFilter = new FilterButterworth<S>();
            highFilters.Add(typeof(S), highFilter);
        }

        ((FilterButterworth<S>)lowFilter).UpdateCoefficients(highFreq, Engine.Current.AudioSystem.SampleRate, FilterButterworth<S>.PassType.Lowpass, resonance);
        ((FilterButterworth<S>)highFilter).UpdateCoefficients(lowFreq, Engine.Current.AudioSystem.SampleRate, FilterButterworth<S>.PassType.Highpass, resonance);

        for (int i = 0; i < buffer.Length; i++)
        {
            ((FilterButterworth<S>)lowFilter).Update(ref buffer[i]);
            ((FilterButterworth<S>)highFilter).Update(ref buffer[i]);
        }
    }
}

public static class Algorithms
{
    public static void SineShapedRingModulation<S>(Span<S> buffer, Span<S> input1, Span<S> input2, float modulationIndex, int channelCount) where S : unmanaged, IAudioSample<S>
    {
        // Apply sine-shaped ring modulation
        for (int i = 0; i < buffer.Length; i++)
        {
            for (int j = 0; j < channelCount; j++)
            {
                float carrierValue = input1[i][j];
                float modulatorValue = input2[i][j];

                float modulatedValue = (float)(carrierValue * Math.Sin(2 * Math.PI * modulationIndex * modulatorValue));

                buffer[i] = buffer[i].SetChannel(j, modulatedValue);

                if (buffer[i][j] > 1f) buffer[i] = buffer[i].SetChannel(j, 1f);
                if (buffer[i][j] < -1f) buffer[i] = buffer[i].SetChannel(j, -1f);
            }
        }
    }

    /// <summary>
    /// Calculates instantaneous phase of a signal using a simple Hilbert transform approximation
    /// </summary>
    //private static double[] CalculateInstantaneousPhase<S>(Span<S> buffer) where S : unmanaged, IAudioSample<S>
    //{
    //    int length = buffer.Length;
    //    double[] phase = new double[length];
    //    double[] avgAmplitudes = new double[length];

    //    for (int i = 1; i < length - 1; i++)
    //    {
    //        for (int j = 0; j < buffer[i].ChannelCount; j++)
    //        {
    //            avgAmplitudes[i] += buffer[i][j];
    //        }
    //        avgAmplitudes[i] /= buffer[i].ChannelCount;
    //    }

    //    // Simple 3-point derivative for phase approximation
    //    for (int i = 1; i < length - 1; i++)
    //    {
    //        double derivative = (avgAmplitudes[i + 1] - avgAmplitudes[i - 1]) / 2.0;
    //        double hilbertApprox = avgAmplitudes[i] / Math.Sqrt(avgAmplitudes[i] * avgAmplitudes[i] + derivative * derivative);
    //        phase[i] = Math.Acos(hilbertApprox);

    //        // Correct phase quadrant based on derivative sign
    //        if (derivative < 0)
    //            phase[i] = 2 * Math.PI - phase[i];
    //    }

    //    // Handle edge cases
    //    phase[0] = phase[1];
    //    phase[length - 1] = phase[length - 2];

    //    return phase;
    //}

    //public static void PhaseModulation<S>(Span<S> buffer, Span<S> input1, Span<S> input2, float modulationIndex, int channelCount) where S : unmanaged, IAudioSample<S>
    //{
    //    double[] carrierPhase = CalculateInstantaneousPhase(input1);

    //    // Apply phase modulation
    //    for (int i = 0; i < buffer.Length; i++)
    //    {
    //        for (int j = 0; j < channelCount; j++)
    //        {
    //            double modifiedPhase = carrierPhase[i] + (modulationIndex * input2[i][j]);

    //            // Calculate amplitude using original carrier amplitude
    //            float amplitude = input1[i][j];

    //            // Generate output sample
    //            buffer[i] = buffer[i].SetChannel(j, amplitude * (float)Math.Sin(modifiedPhase));

    //            if (buffer[i][j] > 1f) buffer[i] = buffer[i].SetChannel(j, 1f);
    //            if (buffer[i][j] < -1f) buffer[i] = buffer[i].SetChannel(j, -1f);
    //        }
    //    }
    //}

    /// <summary>
    /// Calculates instantaneous phase of a signal using a more robust Hilbert transform approximation
    /// </summary>
    private static double[] CalculateInstantaneousPhase<S>(Span<S> buffer) where S : unmanaged, IAudioSample<S>
    {
        int length = buffer.Length;
        double[] phase = new double[length];
        double[] avgAmplitudes = new double[length];

        // Calculate average amplitudes across channels
        for (int i = 0; i < length; i++)
        {
            double sum = 0;
            for (int j = 0; j < buffer[i].ChannelCount; j++)
            {
                sum += buffer[i][j];
            }
            avgAmplitudes[i] = sum / buffer[i].ChannelCount;
        }

        // Use a wider window for derivative calculation to reduce noise
        const int windowSize = 5;
        const double epsilon = 1e-10; // Small value to prevent division by zero

        for (int i = windowSize; i < length - windowSize; i++)
        {
            // Calculate smoothed derivative using a wider window
            double derivative = 0;
            for (int j = 1; j <= windowSize; j++)
            {
                derivative += (avgAmplitudes[i + j] - avgAmplitudes[i - j]) / (2.0 * j);
            }
            derivative /= windowSize;

            // Calculate analytic signal magnitude with protection against zero
            double magnitude = Math.Sqrt(avgAmplitudes[i] * avgAmplitudes[i] + derivative * derivative + epsilon);

            // Normalize with smoothing to prevent discontinuities
            double normalizedSignal = avgAmplitudes[i] / magnitude;

            // Clamp to valid arccos range to prevent NaN
            normalizedSignal = Math.Max(-1.0, Math.Min(1.0, normalizedSignal));

            // Calculate phase
            phase[i] = Math.Acos(normalizedSignal);

            // Correct phase quadrant based on derivative sign
            if (derivative < 0)
                phase[i] = 2 * Math.PI - phase[i];
        }

        // Smooth out edge cases using linear interpolation
        for (int i = 0; i < windowSize; i++)
        {
            phase[i] = phase[windowSize];
        }
        for (int i = length - windowSize; i < length; i++)
        {
            phase[i] = phase[length - windowSize - 1];
        }

        return phase;
    }

    public static void PhaseModulation<S>(Span<S> buffer, Span<S> input1, Span<S> input2, float modulationIndex, int channelCount) where S : unmanaged, IAudioSample<S>
    {
        double[] carrierPhase = CalculateInstantaneousPhase(input1);

        // Apply phase modulation with improved amplitude handling
        for (int i = 0; i < buffer.Length; i++)
        {
            // Get carrier amplitude for envelope
            float carrierAmplitude = 0;
            for (int j = 0; j < channelCount; j++)
            {
                carrierAmplitude += Math.Abs(input1[i][j]);
            }
            carrierAmplitude /= channelCount;

            for (int j = 0; j < channelCount; j++)
            {
                // Apply modulation with smooth amplitude envelope
                double modifiedPhase = carrierPhase[i] + (modulationIndex * input2[i][j]);

                // Generate output sample with envelope following
                float outputSample = carrierAmplitude * (float)Math.Sin(modifiedPhase);

                // Soft clip instead of hard limiting
                if (Math.Abs(outputSample) > 1f)
                {
                    outputSample = Math.Sign(outputSample) * (1f - 1f / (Math.Abs(outputSample) + 1f));
                }

                buffer[i] = buffer[i].SetChannel(j, outputSample);
            }
        }
    }

    public static void RingModulation<S>(Span<S> buffer, Span<S> input1, Span<S> input2, float modulationIndex, int channelCount) where S : unmanaged, IAudioSample<S>
    {
        // Apply ring modulation
        for (int i = 0; i < buffer.Length; i++)
        {
            for (int j = 0; j < channelCount; j++)
            {
                float carrierValue = input1[i][j];
                float modulatorValue = input2[i][j];

                float modulatedValue = (float)(carrierValue * modulatorValue * modulationIndex);

                buffer[i] = buffer[i].SetChannel(j, modulatedValue);

                if (buffer[i][j] > 1f) buffer[i] = buffer[i].SetChannel(j, 1f);
                if (buffer[i][j] < -1f) buffer[i] = buffer[i].SetChannel(j, -1f);
            }
        }
    }

    // smoothingFactor is between 0.0 (no smoothing) and 0.9999.. (almost smoothing to DC) - *kind* of the inverse of cutoff frequency
    public static void EMAIIRSmoothSignal<S>(ref Span<S> input, int N, float smoothingFactor = 0.8f) where S : unmanaged, IAudioSample<S>
    {
        // forward EMA IIR
        S acc = input[0];
        for (int i = 0; i < N; ++i)
        {
            acc = input[i].LerpTo(acc, smoothingFactor);
            input[i] = acc;
        }

        // backward EMA IIR - required only if we need to preserve the phase (aka make the filter symetric) - we usually want this
        acc = input[N - 1];
        for (int i = N - 1; i >= 0; --i)
        {
            acc = input[i].LerpTo(acc, smoothingFactor);
            input[i] = acc;
        }
    }
}