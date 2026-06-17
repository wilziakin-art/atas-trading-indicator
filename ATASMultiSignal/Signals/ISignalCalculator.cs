using ATASMultiSignal.Models;

namespace ATASMultiSignal.Signals
{
    /// <summary>
    /// Contract for a single sub-signal calculator.
    /// </summary>
    public interface ISignalCalculator
    {
        /// <summary>
        /// Calculates a <see cref="SignalScore"/> for the given bar index.
        /// Implementations must return a score with Value == 0 when bar &lt; minRequiredBars.
        /// </summary>
        SignalScore Calculate(int bar);
    }
}
