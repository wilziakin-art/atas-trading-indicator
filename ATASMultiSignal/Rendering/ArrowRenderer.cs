using System.Drawing;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Rendering
{
    /// <summary>
    /// Configures the appearance of the buy/sell arrow <see cref="ValueDataSeries"/>.
    /// The actual arrow values are written by <see cref="Indicators.MultiSignalIndicator"/>;
    /// this class centralises all visual configuration so it can be changed in one place.
    /// </summary>
    public sealed class ArrowRenderer
    {
        private readonly ValueDataSeries _buyArrows;
        private readonly ValueDataSeries _sellArrows;

        public ArrowRenderer(ValueDataSeries buyArrows, ValueDataSeries sellArrows)
        {
            _buyArrows = buyArrows;
            _sellArrows = sellArrows;
            Configure();
        }

        private void Configure()
        {
            // Buy arrows — green, pointing up, displayed below the bar
            _buyArrows.Name = "Buy Signal";
            _buyArrows.Color = Color.FromArgb(255, 0, 200, 83);     // bright green
            _buyArrows.VisualType = VisualMode.UpArrow;
            _buyArrows.ShowZeroLine = false;
            _buyArrows.Width = 2;

            // Sell arrows — red, pointing down, displayed above the bar
            _sellArrows.Name = "Sell Signal";
            _sellArrows.Color = Color.FromArgb(255, 229, 57, 53);   // bright red
            _sellArrows.VisualType = VisualMode.DownArrow;
            _sellArrows.ShowZeroLine = false;
            _sellArrows.Width = 2;
        }

        /// <summary>
        /// Updates the buy series' colour (e.g. to reflect signal strength).
        /// </summary>
        public void SetBuyColor(Color color) => _buyArrows.Color = color;

        /// <summary>
        /// Updates the sell series' colour.
        /// </summary>
        public void SetSellColor(Color color) => _sellArrows.Color = color;
    }
}
