using System.Collections.Generic;
using System.Linq;

namespace ATASMultiSignal.Communication
{
    public static class PhaseAggregator
    {
        public static PhaseResult Aggregate(Dictionary<string, IndicatorState> states)
        {
            var phase1 = states.Values.Where(s => s.IndicatorId.StartsWith("P1_")).ToList();
            var phase2 = states.Values.Where(s => s.IndicatorId.StartsWith("P2_")).ToList();

            var (p1Vote, p1Strength) = ComputeVote(phase1);
            var (p2Vote, p2Strength) = ComputeVote(phase2);

            return new PhaseResult
            {
                Phase1Vote = p1Vote,
                Phase1Strength = p1Strength,
                Phase2Vote = p2Vote,
                Phase2Strength = p2Strength,
                IsAligned = p1Vote == p2Vote && p1Vote != Vote.Neutral
            };
        }

        private static (Vote vote, float strength) ComputeVote(List<IndicatorState> states)
        {
            if (states.Count == 0)
                return (Vote.Neutral, 0f);

            int bulls = states.Count(s => s.Vote == Vote.Bull);
            int bears = states.Count(s => s.Vote == Vote.Bear);
            int total = states.Count;

            if (bulls > bears)
                return (Vote.Bull, (float)bulls / total);
            if (bears > bulls)
                return (Vote.Bear, (float)bears / total);

            return (Vote.Neutral, 0f);
        }
    }
}
