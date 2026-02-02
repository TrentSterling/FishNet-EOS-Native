/****************************************************************************
*
* NAME: SMBPitchShifter.cs
* VERSION: 1.3 (Instance-based adaptation for EOS Voice)
* ORIGINAL: http://www.dspdimension.com
*
* SYNOPSIS: Routine for doing pitch shifting while maintaining
* duration using the Short Time Fourier Transform.
*
* DESCRIPTION: The routine takes a pitchShift factor value which is between 0.5
* (one octave down) and 2. (one octave up). A value of exactly 1 does not change
* the pitch.
*
* COPYRIGHT 1999-2006 Stephan M. Bernsee <smb [AT] dspdimension [DOT] com>
*
* The Wide Open License (WOL)
*
* Permission to use, copy, modify, distribute and sell this software and its
* documentation for any purpose is hereby granted without fee, provided that
* the above copyright notice and this license appear in all source copies.
* THIS SOFTWARE IS PROVIDED "AS IS" WITHOUT EXPRESS OR IMPLIED WARRANTY OF
* ANY KIND. See http://www.dspguru.com/wol.htm for more information.
*
* C# conversion by Michael Knight (madmik3 at gmail dot com)
* Instance-based adaptation for FishNet-EOS-Native voice chat
*
*****************************************************************************/

using System;

namespace FishNet.Transport.EOSNative.Voice
{
    /// <summary>
    /// Instance-based STFT pitch shifter for voice processing.
    /// Each voice participant should have their own instance.
    ///
    /// pitchShift values:
    /// - 0.5 = one octave down
    /// - 1.0 = no change
    /// - 2.0 = one octave up
    /// </summary>
    public class SMBPitchShifter
    {
        #region Constants

        /// <summary>
        /// Maximum frame length for FFT processing.
        /// </summary>
        public const int MAX_FRAME_LENGTH = 16000;

        /// <summary>
        /// Default FFT frame size (must be power of 2).
        /// </summary>
        public const int DEFAULT_FFT_FRAME_SIZE = 2048;

        /// <summary>
        /// Default oversampling factor. Higher = better quality but more CPU.
        /// Recommended: 4 for speed, 10 for balance, 32 for best quality.
        /// </summary>
        public const int DEFAULT_OSAMP = 10;

        #endregion

        #region Instance Fields

        private readonly float[] _inFIFO;
        private readonly float[] _outFIFO;
        private readonly float[] _fftWorkspace;
        private readonly float[] _lastPhase;
        private readonly float[] _sumPhase;
        private readonly float[] _outputAccum;
        private readonly float[] _anaFreq;
        private readonly float[] _anaMagn;
        private readonly float[] _synFreq;
        private readonly float[] _synMagn;
        private long _rover;

        #endregion

        #region Constructor

        /// <summary>
        /// Create a new pitch shifter instance with its own buffers.
        /// </summary>
        public SMBPitchShifter()
        {
            _inFIFO = new float[MAX_FRAME_LENGTH];
            _outFIFO = new float[MAX_FRAME_LENGTH];
            _fftWorkspace = new float[2 * MAX_FRAME_LENGTH];
            _lastPhase = new float[MAX_FRAME_LENGTH / 2 + 1];
            _sumPhase = new float[MAX_FRAME_LENGTH / 2 + 1];
            _outputAccum = new float[2 * MAX_FRAME_LENGTH];
            _anaFreq = new float[MAX_FRAME_LENGTH];
            _anaMagn = new float[MAX_FRAME_LENGTH];
            _synFreq = new float[MAX_FRAME_LENGTH];
            _synMagn = new float[MAX_FRAME_LENGTH];
            _rover = 0;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Reset the pitch shifter state. Call when switching participants or after silence.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_inFIFO, 0, _inFIFO.Length);
            Array.Clear(_outFIFO, 0, _outFIFO.Length);
            Array.Clear(_fftWorkspace, 0, _fftWorkspace.Length);
            Array.Clear(_lastPhase, 0, _lastPhase.Length);
            Array.Clear(_sumPhase, 0, _sumPhase.Length);
            Array.Clear(_outputAccum, 0, _outputAccum.Length);
            Array.Clear(_anaFreq, 0, _anaFreq.Length);
            Array.Clear(_anaMagn, 0, _anaMagn.Length);
            Array.Clear(_synFreq, 0, _synFreq.Length);
            Array.Clear(_synMagn, 0, _synMagn.Length);
            _rover = 0;
        }

        /// <summary>
        /// Process audio data in-place with pitch shifting.
        /// </summary>
        /// <param name="pitchShift">Pitch factor (0.5 = octave down, 1.0 = normal, 2.0 = octave up)</param>
        /// <param name="sampleRate">Sample rate in Hz (e.g., 48000)</param>
        /// <param name="data">Audio data to process in-place</param>
        public void Process(float pitchShift, float sampleRate, float[] data)
        {
            Process(pitchShift, data.Length, DEFAULT_FFT_FRAME_SIZE, DEFAULT_OSAMP, sampleRate, data);
        }

        /// <summary>
        /// Process audio data in-place with pitch shifting and custom FFT settings.
        /// </summary>
        /// <param name="pitchShift">Pitch factor (0.5 = octave down, 1.0 = normal, 2.0 = octave up)</param>
        /// <param name="numSamples">Number of samples to process</param>
        /// <param name="fftFrameSize">FFT frame size (must be power of 2, e.g., 1024, 2048, 4096)</param>
        /// <param name="osamp">Oversampling factor (4 = fast, 10 = balanced, 32 = best quality)</param>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <param name="data">Audio data to process in-place</param>
        public void Process(float pitchShift, long numSamples, long fftFrameSize, long osamp, float sampleRate, float[] data)
        {
            // Skip processing if pitch is essentially 1.0
            if (Math.Abs(pitchShift - 1.0f) < 0.001f)
                return;

            double magn, phase, tmp, window, real, imag;
            double freqPerBin, expct;
            long i, k, qpd, index, inFifoLatency, stepSize, fftFrameSize2;

            // Set up variables
            fftFrameSize2 = fftFrameSize / 2;
            stepSize = fftFrameSize / osamp;
            freqPerBin = sampleRate / (double)fftFrameSize;
            expct = 2.0 * Math.PI * (double)stepSize / (double)fftFrameSize;
            inFifoLatency = fftFrameSize - stepSize;
            if (_rover == 0) _rover = inFifoLatency;

            // Main processing loop
            for (i = 0; i < numSamples; i++)
            {
                // Collect data into FIFO
                _inFIFO[_rover] = data[i];
                data[i] = _outFIFO[_rover - inFifoLatency];
                _rover++;

                // Process when we have enough data
                if (_rover >= fftFrameSize)
                {
                    _rover = inFifoLatency;

                    // Windowing and interleave
                    for (k = 0; k < fftFrameSize; k++)
                    {
                        window = -0.5 * Math.Cos(2.0 * Math.PI * (double)k / (double)fftFrameSize) + 0.5;
                        _fftWorkspace[2 * k] = (float)(_inFIFO[k] * window);
                        _fftWorkspace[2 * k + 1] = 0.0f;
                    }

                    // ANALYSIS: Forward FFT
                    ShortTimeFourierTransform(_fftWorkspace, fftFrameSize, -1);

                    // Analysis step
                    for (k = 0; k <= fftFrameSize2; k++)
                    {
                        real = _fftWorkspace[2 * k];
                        imag = _fftWorkspace[2 * k + 1];

                        magn = 2.0 * Math.Sqrt(real * real + imag * imag);
                        phase = Math.Atan2(imag, real);

                        tmp = phase - _lastPhase[k];
                        _lastPhase[k] = (float)phase;

                        tmp -= (double)k * expct;

                        qpd = (long)(tmp / Math.PI);
                        if (qpd >= 0) qpd += qpd & 1;
                        else qpd -= qpd & 1;
                        tmp -= Math.PI * (double)qpd;

                        tmp = osamp * tmp / (2.0 * Math.PI);
                        tmp = (double)k * freqPerBin + tmp * freqPerBin;

                        _anaMagn[k] = (float)magn;
                        _anaFreq[k] = (float)tmp;
                    }

                    // PROCESSING: Pitch shifting
                    for (int zero = 0; zero < fftFrameSize; zero++)
                    {
                        _synMagn[zero] = 0;
                        _synFreq[zero] = 0;
                    }

                    for (k = 0; k <= fftFrameSize2; k++)
                    {
                        index = (long)(k * pitchShift);
                        if (index <= fftFrameSize2)
                        {
                            _synMagn[index] += _anaMagn[k];
                            _synFreq[index] = _anaFreq[k] * pitchShift;
                        }
                    }

                    // SYNTHESIS: Inverse FFT
                    for (k = 0; k <= fftFrameSize2; k++)
                    {
                        magn = _synMagn[k];
                        tmp = _synFreq[k];

                        tmp -= (double)k * freqPerBin;
                        tmp /= freqPerBin;
                        tmp = 2.0 * Math.PI * tmp / osamp;
                        tmp += (double)k * expct;

                        _sumPhase[k] += (float)tmp;
                        phase = _sumPhase[k];

                        _fftWorkspace[2 * k] = (float)(magn * Math.Cos(phase));
                        _fftWorkspace[2 * k + 1] = (float)(magn * Math.Sin(phase));
                    }

                    // Zero negative frequencies
                    for (k = fftFrameSize + 2; k < 2 * fftFrameSize; k++)
                        _fftWorkspace[k] = 0.0f;

                    // Inverse transform
                    ShortTimeFourierTransform(_fftWorkspace, fftFrameSize, 1);

                    // Windowing and accumulate
                    for (k = 0; k < fftFrameSize; k++)
                    {
                        window = -0.5 * Math.Cos(2.0 * Math.PI * (double)k / (double)fftFrameSize) + 0.5;
                        _outputAccum[k] += (float)(2.0 * window * _fftWorkspace[2 * k] / (fftFrameSize2 * osamp));
                    }

                    for (k = 0; k < stepSize; k++)
                        _outFIFO[k] = _outputAccum[k];

                    // Shift accumulator
                    for (k = 0; k < fftFrameSize; k++)
                        _outputAccum[k] = _outputAccum[k + stepSize];

                    // Move input FIFO
                    for (k = 0; k < inFifoLatency; k++)
                        _inFIFO[k] = _inFIFO[k + stepSize];
                }
            }
        }

        #endregion

        #region Private Methods

        private void ShortTimeFourierTransform(float[] fftBuffer, long fftFrameSize, long sign)
        {
            float wr, wi, arg, temp;
            float tr, ti, ur, ui;
            long i, bitm, j, le, le2, k;

            for (i = 2; i < 2 * fftFrameSize - 2; i += 2)
            {
                for (bitm = 2, j = 0; bitm < 2 * fftFrameSize; bitm <<= 1)
                {
                    if ((i & bitm) != 0) j++;
                    j <<= 1;
                }
                if (i < j)
                {
                    temp = fftBuffer[i];
                    fftBuffer[i] = fftBuffer[j];
                    fftBuffer[j] = temp;
                    temp = fftBuffer[i + 1];
                    fftBuffer[i + 1] = fftBuffer[j + 1];
                    fftBuffer[j + 1] = temp;
                }
            }

            long max = (long)(Math.Log(fftFrameSize) / Math.Log(2.0) + 0.5);
            for (k = 0, le = 2; k < max; k++)
            {
                le <<= 1;
                le2 = le >> 1;
                ur = 1.0f;
                ui = 0.0f;
                arg = (float)Math.PI / (le2 >> 1);
                wr = (float)Math.Cos(arg);
                wi = (float)(sign * Math.Sin(arg));

                for (j = 0; j < le2; j += 2)
                {
                    for (i = j; i < 2 * fftFrameSize; i += le)
                    {
                        tr = fftBuffer[i + le2] * ur - fftBuffer[i + le2 + 1] * ui;
                        ti = fftBuffer[i + le2] * ui + fftBuffer[i + le2 + 1] * ur;
                        fftBuffer[i + le2] = fftBuffer[i] - tr;
                        fftBuffer[i + le2 + 1] = fftBuffer[i + 1] - ti;
                        fftBuffer[i] += tr;
                        fftBuffer[i + 1] += ti;
                    }
                    tr = ur * wr - ui * wi;
                    ui = ur * wi + ui * wr;
                    ur = tr;
                }
            }
        }

        #endregion
    }
}
