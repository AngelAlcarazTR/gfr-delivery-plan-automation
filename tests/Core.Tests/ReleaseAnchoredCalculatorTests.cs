namespace Core.Tests;

// Ground-truth regression suite: 25 real GFR delivery plans (2024-2026) pulled from
// Azure DevOps. Each plan feeds its calendar anchors (QED deploy + production Release)
// into the engine, and every milestone the engine computes is checked against the
// plan's real ADO markers, to the engine's documented precision (business-day tolerance).
public class ReleaseAnchoredCalculatorTests
{
    public record PlanFixture(
        string Name,
        PlanKind Kind,
        LocalDate Qed,
        LocalDate? Release,
        IReadOnlyDictionary<Milestone, LocalDate> Expected);

    // Documented engine precision per milestone (max business-day deviation vs. real plans).
    // Deterministic backbone stays tight; Start Development is the one planned marker.
    private static readonly IReadOnlyDictionary<Milestone, int> Tolerance =
        new Dictionary<Milestone, int>
        {
            [Milestone.QedDeploy] = 0,
            [Milestone.StartReg]  = 0,
            [Milestone.Release]   = 0,
            [Milestone.EndReg]    = 1,
            [Milestone.QaCutoff]  = 2,
            [Milestone.EndDev]    = 2,
            [Milestone.StartDev]  = 5,
        };

    private static readonly HolidayCalendar Holidays =
        CompanyHolidays.Calendar(2023, 2024, 2025, 2026, 2027);

    public static readonly IReadOnlyList<PlanFixture> All = new List<PlanFixture>
    {
        new(
            "[GFR][2026][Delivery Plan] - August 24th Release",
            PlanKind.Prod,
            new LocalDate(2026, 8, 10),
            new LocalDate(2026, 8, 24),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2026, 7, 20),
            [Milestone.EndDev] = new LocalDate(2026, 8, 4),
            [Milestone.QaCutoff] = new LocalDate(2026, 8, 7),
            [Milestone.QedDeploy] = new LocalDate(2026, 8, 10),
            [Milestone.StartReg] = new LocalDate(2026, 8, 10),
            [Milestone.EndReg] = new LocalDate(2026, 8, 18),
            [Milestone.Release] = new LocalDate(2026, 8, 24),
            }),
        new(
            "[GFR][2025][Delivery Plan] - August 25th Release",
            PlanKind.Prod,
            new LocalDate(2025, 8, 11),
            new LocalDate(2025, 8, 25),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 7, 21),
            [Milestone.EndDev] = new LocalDate(2025, 8, 5),
            [Milestone.QaCutoff] = new LocalDate(2025, 8, 8),
            [Milestone.QedDeploy] = new LocalDate(2025, 8, 11),
            [Milestone.StartReg] = new LocalDate(2025, 8, 11),
            [Milestone.EndReg] = new LocalDate(2025, 8, 20),
            [Milestone.Release] = new LocalDate(2025, 8, 25),
            }),
        new(
            "[GFR][2026][Delivery Plan] - April 27th Release",
            PlanKind.Prod,
            new LocalDate(2026, 4, 13),
            new LocalDate(2026, 4, 27),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2026, 3, 23),
            [Milestone.EndDev] = new LocalDate(2026, 4, 7),
            [Milestone.QaCutoff] = new LocalDate(2026, 4, 10),
            [Milestone.QedDeploy] = new LocalDate(2026, 4, 13),
            [Milestone.StartReg] = new LocalDate(2026, 4, 13),
            [Milestone.EndReg] = new LocalDate(2026, 4, 22),
            [Milestone.Release] = new LocalDate(2026, 4, 27),
            }),
        new(
            "[GFR][2025][Delivery Plan] - September 15th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2025, 9, 15),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 8, 18),
            [Milestone.EndDev] = new LocalDate(2025, 9, 9),
            [Milestone.QaCutoff] = new LocalDate(2025, 9, 12),
            [Milestone.QedDeploy] = new LocalDate(2025, 9, 15),
            [Milestone.StartReg] = new LocalDate(2025, 9, 15),
            [Milestone.EndReg] = new LocalDate(2025, 9, 24),
            }),
        new(
            "[GFR][2024][Delivery Plan] - December 16th PROD Release",
            PlanKind.Prod,
            new LocalDate(2024, 12, 2),
            new LocalDate(2024, 12, 16),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2024, 11, 11),
            [Milestone.EndDev] = new LocalDate(2024, 11, 26),
            [Milestone.QaCutoff] = new LocalDate(2024, 11, 29),
            [Milestone.QedDeploy] = new LocalDate(2024, 12, 2),
            [Milestone.StartReg] = new LocalDate(2024, 12, 2),
            [Milestone.EndReg] = new LocalDate(2024, 12, 11),
            [Milestone.Release] = new LocalDate(2024, 12, 16),
            }),
        new(
            "[GFR][2025][Delivery Plan] - October 28th Release",
            PlanKind.Prod,
            new LocalDate(2025, 10, 13),
            new LocalDate(2025, 10, 28),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 9, 22),
            [Milestone.EndDev] = new LocalDate(2025, 10, 7),
            [Milestone.QaCutoff] = new LocalDate(2025, 10, 10),
            [Milestone.QedDeploy] = new LocalDate(2025, 10, 13),
            [Milestone.StartReg] = new LocalDate(2025, 10, 13),
            [Milestone.EndReg] = new LocalDate(2025, 10, 22),
            [Milestone.Release] = new LocalDate(2025, 10, 28),
            }),
        new(
            "[GFR][2025][Delivery Plan] - June 23th Release",
            PlanKind.Prod,
            new LocalDate(2025, 6, 9),
            new LocalDate(2025, 6, 23),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 5, 19),
            [Milestone.EndDev] = new LocalDate(2025, 6, 3),
            [Milestone.QaCutoff] = new LocalDate(2025, 6, 6),
            [Milestone.QedDeploy] = new LocalDate(2025, 6, 9),
            [Milestone.StartReg] = new LocalDate(2025, 6, 9),
            [Milestone.EndReg] = new LocalDate(2025, 6, 18),
            [Milestone.Release] = new LocalDate(2025, 6, 23),
            }),
        new(
            "[GFR][2025][Delivery Plan] - May 27th Release",
            PlanKind.Prod,
            new LocalDate(2025, 5, 12),
            new LocalDate(2025, 5, 27),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 4, 21),
            [Milestone.EndDev] = new LocalDate(2025, 5, 6),
            [Milestone.QaCutoff] = new LocalDate(2025, 5, 9),
            [Milestone.QedDeploy] = new LocalDate(2025, 5, 12),
            [Milestone.StartReg] = new LocalDate(2025, 5, 12),
            [Milestone.EndReg] = new LocalDate(2025, 5, 21),
            [Milestone.Release] = new LocalDate(2025, 5, 27),
            }),
        new(
            "[GFR][2026][Delivery Plan] - June 22th Release",
            PlanKind.Prod,
            new LocalDate(2026, 6, 8),
            new LocalDate(2026, 6, 22),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2026, 5, 25),
            [Milestone.EndDev] = new LocalDate(2026, 6, 2),
            [Milestone.QaCutoff] = new LocalDate(2026, 6, 5),
            [Milestone.QedDeploy] = new LocalDate(2026, 6, 8),
            [Milestone.StartReg] = new LocalDate(2026, 6, 8),
            [Milestone.EndReg] = new LocalDate(2026, 6, 17),
            [Milestone.Release] = new LocalDate(2026, 6, 22),
            }),
        new(
            "[GFR][2024][Delivery Plan] - November 25th PROD Release",
            PlanKind.Prod,
            new LocalDate(2024, 11, 11),
            new LocalDate(2024, 11, 25),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2024, 10, 14),
            [Milestone.EndDev] = new LocalDate(2024, 11, 6),
            [Milestone.QaCutoff] = new LocalDate(2024, 11, 8),
            [Milestone.QedDeploy] = new LocalDate(2024, 11, 11),
            [Milestone.StartReg] = new LocalDate(2024, 11, 11),
            [Milestone.EndReg] = new LocalDate(2024, 11, 20),
            [Milestone.Release] = new LocalDate(2024, 11, 25),
            }),
        new(
            "[GFR][2025][Delivery Plan] - April 28th Release",
            PlanKind.Prod,
            new LocalDate(2025, 4, 10),
            new LocalDate(2025, 4, 28),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 3, 17),
            [Milestone.EndDev] = new LocalDate(2025, 4, 8),
            [Milestone.QaCutoff] = new LocalDate(2025, 4, 9),
            [Milestone.QedDeploy] = new LocalDate(2025, 4, 10),
            [Milestone.StartReg] = new LocalDate(2025, 4, 10),
            [Milestone.EndReg] = new LocalDate(2025, 4, 23),
            [Milestone.Release] = new LocalDate(2025, 4, 28),
            }),
        new(
            "[GFR][2025][Delivery Plan] - January 20th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2025, 1, 20),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2024, 12, 23),
            [Milestone.EndDev] = new LocalDate(2025, 1, 14),
            [Milestone.QaCutoff] = new LocalDate(2025, 1, 17),
            [Milestone.QedDeploy] = new LocalDate(2025, 1, 20),
            }),
        new(
            "[GFR][2025][Delivery Plan] - February 17th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2025, 2, 17),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 1, 20),
            [Milestone.EndDev] = new LocalDate(2025, 2, 10),
            [Milestone.QaCutoff] = new LocalDate(2025, 2, 14),
            [Milestone.QedDeploy] = new LocalDate(2025, 2, 17),
            }),
        new(
            "[GFR][2024][Delivery Plan] - Sep 23th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2024, 9, 23),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2024, 8, 21),
            [Milestone.EndDev] = new LocalDate(2024, 9, 19),
            [Milestone.QaCutoff] = new LocalDate(2024, 9, 20),
            [Milestone.QedDeploy] = new LocalDate(2024, 9, 23),
            [Milestone.StartReg] = new LocalDate(2024, 9, 23),
            [Milestone.EndReg] = new LocalDate(2024, 9, 27),
            }),
        new(
            "[GFR][2026][Delivery Plan] - July 27th Release",
            PlanKind.Prod,
            new LocalDate(2026, 7, 13),
            new LocalDate(2026, 7, 27),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2026, 6, 22),
            [Milestone.EndDev] = new LocalDate(2026, 7, 7),
            [Milestone.QaCutoff] = new LocalDate(2026, 7, 10),
            [Milestone.QedDeploy] = new LocalDate(2026, 7, 13),
            [Milestone.StartReg] = new LocalDate(2026, 7, 13),
            [Milestone.EndReg] = new LocalDate(2026, 7, 21),
            [Milestone.Release] = new LocalDate(2026, 7, 27),
            }),
        new(
            "[GFR][2026][Delivery Plan] - September 14th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2026, 9, 14),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2026, 8, 24),
            [Milestone.EndDev] = new LocalDate(2026, 9, 8),
            [Milestone.QaCutoff] = new LocalDate(2026, 9, 11),
            [Milestone.QedDeploy] = new LocalDate(2026, 9, 14),
            }),
        new(
            "[GFR][2026][Delivery Plan] - January 19th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2026, 1, 19),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 12, 22),
            [Milestone.EndDev] = new LocalDate(2026, 1, 13),
            [Milestone.QaCutoff] = new LocalDate(2026, 1, 16),
            [Milestone.QedDeploy] = new LocalDate(2026, 1, 19),
            }),
        new(
            "[GFR][2026][Delivery Plan] - February 16th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2026, 2, 16),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2026, 1, 19),
            [Milestone.EndDev] = new LocalDate(2026, 2, 10),
            [Milestone.QaCutoff] = new LocalDate(2026, 2, 13),
            [Milestone.QedDeploy] = new LocalDate(2026, 2, 16),
            }),
        new(
            "[GFR][2026][Delivery Plan] - May 26th Release",
            PlanKind.Prod,
            new LocalDate(2026, 5, 11),
            new LocalDate(2026, 5, 26),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2026, 4, 20),
            [Milestone.EndDev] = new LocalDate(2026, 5, 5),
            [Milestone.QaCutoff] = new LocalDate(2026, 5, 8),
            [Milestone.QedDeploy] = new LocalDate(2026, 5, 11),
            [Milestone.StartReg] = new LocalDate(2026, 5, 11),
            [Milestone.EndReg] = new LocalDate(2026, 5, 20),
            [Milestone.Release] = new LocalDate(2026, 5, 26),
            }),
        new(
            "[GFR][2025][Delivery Plan] - November 24th Release",
            PlanKind.Prod,
            new LocalDate(2025, 11, 10),
            new LocalDate(2025, 11, 24),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 10, 20),
            [Milestone.EndDev] = new LocalDate(2025, 11, 4),
            [Milestone.QaCutoff] = new LocalDate(2025, 11, 7),
            [Milestone.QedDeploy] = new LocalDate(2025, 11, 10),
            [Milestone.StartReg] = new LocalDate(2025, 11, 10),
            [Milestone.EndReg] = new LocalDate(2025, 11, 19),
            [Milestone.Release] = new LocalDate(2025, 11, 24),
            }),
        new(
            "[GFR][2025][Delivery Plan] - July 28th Release",
            PlanKind.Prod,
            new LocalDate(2025, 7, 14),
            new LocalDate(2025, 7, 28),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 6, 23),
            [Milestone.EndDev] = new LocalDate(2025, 7, 8),
            [Milestone.QaCutoff] = new LocalDate(2025, 7, 11),
            [Milestone.QedDeploy] = new LocalDate(2025, 7, 14),
            [Milestone.StartReg] = new LocalDate(2025, 7, 14),
            [Milestone.EndReg] = new LocalDate(2025, 7, 23),
            [Milestone.Release] = new LocalDate(2025, 7, 28),
            }),
        new(
            "[GFR][2025][Delivery Plan] - December 15th Release",
            PlanKind.Prod,
            new LocalDate(2025, 12, 3),
            new LocalDate(2025, 12, 15),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 11, 10),
            [Milestone.EndDev] = new LocalDate(2025, 11, 25),
            [Milestone.QaCutoff] = new LocalDate(2025, 11, 28),
            [Milestone.QedDeploy] = new LocalDate(2025, 12, 3),
            [Milestone.StartReg] = new LocalDate(2025, 12, 3),
            [Milestone.EndReg] = new LocalDate(2025, 12, 10),
            [Milestone.Release] = new LocalDate(2025, 12, 15),
            }),
        new(
            "[GFR][2025][Delivery Plan] - March 17th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2025, 3, 17),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2025, 2, 17),
            [Milestone.EndDev] = new LocalDate(2025, 3, 11),
            [Milestone.QaCutoff] = new LocalDate(2025, 3, 14),
            [Milestone.QedDeploy] = new LocalDate(2025, 3, 17),
            }),
        new(
            "[GFR][2024][Delivery Plan] - October 21th PROD Release",
            PlanKind.Prod,
            new LocalDate(2024, 10, 7),
            new LocalDate(2024, 10, 21),
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2024, 9, 16),
            [Milestone.EndDev] = new LocalDate(2024, 10, 2),
            [Milestone.QaCutoff] = new LocalDate(2024, 10, 4),
            [Milestone.QedDeploy] = new LocalDate(2024, 10, 7),
            [Milestone.StartReg] = new LocalDate(2024, 10, 7),
            [Milestone.EndReg] = new LocalDate(2024, 10, 16),
            [Milestone.Release] = new LocalDate(2024, 10, 21),
            }),
        new(
            "[GFR][2026][Delivery Plan] - March 16th QED Release",
            PlanKind.QedOnly,
            new LocalDate(2026, 3, 16),
            null,
            new Dictionary<Milestone, LocalDate>
            {
            [Milestone.StartDev] = new LocalDate(2026, 2, 19),
            [Milestone.EndDev] = new LocalDate(2026, 3, 10),
            [Milestone.QaCutoff] = new LocalDate(2026, 3, 13),
            [Milestone.QedDeploy] = new LocalDate(2026, 3, 16),
            }),
    };

    public static IEnumerable<object[]> PlanNames() => All.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(PlanNames))]
    public void Engine_Reproduces_RealPlan_WithinPrecision(string planName)
    {
        var fx = All.Single(p => p.Name == planName);
        var schedule = new ReleaseSchedule(fx.Kind, fx.Qed, fx.Release, fx.Name);

        var plan = ReleaseAnchoredCalculator.Compute(schedule, Holidays);

        foreach (var ev in plan.Events)
        {
            if (!fx.Expected.TryGetValue(ev.Label, out var actual))
                continue; // engine produced a marker this plan does not record

            var deviation = BusinessDayCalculator.BusinessDaysBetween(ev.Date, actual);
            var tol = Tolerance[ev.Label];
            Assert.True(
                deviation <= tol,
                $"{fx.Name}: {ev.Label} computed {ev.Date} vs actual {actual} " +
                $"= {deviation} business days (tolerance {tol}).");
        }
    }

    [Fact]
    public void Engine_MeetsDocumentedExactMatchPrecision_AcrossAllPlans()
    {
        var present = new Dictionary<Milestone, int>();
        var exact = new Dictionary<Milestone, int>();

        foreach (var fx in All)
        {
            var schedule = new ReleaseSchedule(fx.Kind, fx.Qed, fx.Release, fx.Name);
            var plan = ReleaseAnchoredCalculator.Compute(schedule, Holidays);

            foreach (var ev in plan.Events)
            {
                if (!fx.Expected.TryGetValue(ev.Label, out var actual))
                    continue;

                present[ev.Label] = present.GetValueOrDefault(ev.Label) + 1;
                if (ev.Date == actual)
                    exact[ev.Label] = exact.GetValueOrDefault(ev.Label) + 1;
            }
        }

        int Ex(Milestone m) => exact.GetValueOrDefault(m);
        int Pr(Milestone m) => present.GetValueOrDefault(m);

        // Anchors and StartReg are exact by construction (100%).
        Assert.Equal(Pr(Milestone.QedDeploy), Ex(Milestone.QedDeploy));
        Assert.Equal(Pr(Milestone.StartReg), Ex(Milestone.StartReg));
        Assert.Equal(Pr(Milestone.Release), Ex(Milestone.Release));

        // Deterministic backbone: high exact-match floors observed across 2024-2026.
        Assert.True(Ex(Milestone.QaCutoff) >= 24, $"QaCutoff exact {Ex(Milestone.QaCutoff)}/{Pr(Milestone.QaCutoff)}");
        Assert.True(Ex(Milestone.EndDev) >= 19, $"EndDev exact {Ex(Milestone.EndDev)}/{Pr(Milestone.EndDev)}");
        Assert.True(Ex(Milestone.EndReg) >= 11, $"EndReg exact {Ex(Milestone.EndReg)}/{Pr(Milestone.EndReg)}");

        // Start Development is a human-planned marker; the nominal prediction still
        // reproduces the majority exactly and every case stays within tolerance.
        Assert.True(Ex(Milestone.StartDev) >= 15, $"StartDev exact {Ex(Milestone.StartDev)}/{Pr(Milestone.StartDev)}");

        var totalExact = exact.Values.Sum();
        var totalPresent = present.Values.Sum();
        Assert.True(totalExact >= 128, $"Total exact {totalExact}/{totalPresent}");
    }
}
