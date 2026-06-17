namespace ATASMultiSignal.Communication
{
    public class PhaseResult
    {
        public Vote Phase1Vote { get; set; }
        public float Phase1Strength { get; set; }
        public Vote Phase2Vote { get; set; }
        public float Phase2Strength { get; set; }
        public bool IsAligned { get; set; }
    }
}
