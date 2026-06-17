using System;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Phase3_Execution.ExitStrategy
{
    public sealed class ExitMacdCalculator
    {
        public int FastPeriod { get; set; } = 12;
        public int SlowPeriod { get; set; } = 26;
        public int SignalPeriod { get; set; } = 9;

        private float[]? _fastEma, _slowEma, _signalEma, _histogram;
        private bool _initialized;
        private float _alphaFast, _alphaSlow, _alphaSignal;

        public float GetHistogram(int bar, Func<int, IndicatorCandle> getCandle)
        {
            int minBars = SlowPeriod + SignalPeriod;
            if (bar < minBars) return 0f;

            int size = bar + 2;
            if (_fastEma == null || _fastEma.Length < size)
            {
                _fastEma = new float[size + 64];
                _slowEma = new float[size + 64];
                _signalEma = new float[size + 64];
                _histogram = new float[size + 64];
                _initialized = false;
            }

            _alphaFast = 2f / (FastPeriod + 1f);
            _alphaSlow = 2f / (SlowPeriod + 1f);
            _alphaSignal = 2f / (SignalPeriod + 1f);

            float close = (float)getCandle(bar).Close;

            if (!_initialized && bar == minBars)
            {
                float sumF = 0f, sumS = 0f;
                for (int i = 0; i < FastPeriod; i++) sumF += (float)getCandle(bar - SlowPeriod - SignalPeriod + i).Close;
                _fastEma![bar - SlowPeriod - SignalPeriod + FastPeriod - 1] = sumF / FastPeriod;
                for (int i = 0; i < SlowPeriod; i++) sumS += (float)getCandle(bar - SignalPeriod - (SlowPeriod - 1) + i).Close;
                _slowEma![bar - SignalPeriod] = sumS / SlowPeriod;
                for (int i = bar - SlowPeriod - SignalPeriod + FastPeriod; i <= bar - SignalPeriod; i++)
                    _fastEma[i] = _fastEma[i - 1] + _alphaFast * ((float)getCandle(i).Close - _fastEma[i - 1]);
                for (int i = bar - SignalPeriod + 1; i <= bar; i++)
                {
                    _fastEma[i] = _fastEma[i - 1] + _alphaFast * ((float)getCandle(i).Close - _fastEma[i - 1]);
                    _slowEma![i] = _slowEma[i - 1] + _alphaSlow * ((float)getCandle(i).Close - _slowEma[i - 1]);
                }
                float sumSig = 0f;
                for (int i = 0; i < SignalPeriod; i++) { int idx = bar - SignalPeriod + 1 + i; sumSig += _fastEma[idx] - _slowEma![idx]; }
                _signalEma![bar] = sumSig / SignalPeriod;
                _histogram![bar] = (_fastEma[bar] - _slowEma![bar]) - _signalEma[bar];
                _initialized = true;
            }
            else if (_initialized && bar > 0)
            {
                _fastEma![bar] = _fastEma[bar - 1] + _alphaFast * (close - _fastEma[bar - 1]);
                _slowEma![bar] = _slowEma[bar - 1] + _alphaSlow * (close - _slowEma[bar - 1]);
                float macdLine = _fastEma[bar] - _slowEma[bar];
                _signalEma![bar] = _signalEma[bar - 1] + _alphaSignal * (macdLine - _signalEma[bar - 1]);
                _histogram![bar] = macdLine - _signalEma[bar];
            }

            return _histogram?[bar] ?? 0f;
        }

        public bool ShouldExit(int bar, SignalDirection position, Func<int, IndicatorCandle> getCandle)
        {
            if (bar < SlowPeriod + SignalPeriod + 1) return false;
            float prev = GetHistogram(bar - 1, getCandle);
            float curr = GetHistogram(bar, getCandle);
            if (position == SignalDirection.Buy) return curr < 0f && prev >= 0f;
            if (position == SignalDirection.Sell) return curr > 0f && prev <= 0f;
            return false;
        }
    }
}
