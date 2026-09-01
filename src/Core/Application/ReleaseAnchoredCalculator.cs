namespace Core.Application;

// Builds a whole delivery plan backward from a SINGLE calendar anchor, reverse-engineered
// from 25 real GFR plans (2024-2026) cross-referenced with the production release calendar
// and holidays. The engine takes one date (plus the plan kind) and derives everything:
//   Prod    -> Anchor is the AMER/UK production Release. The QED deploy is derived from it
//              (Release - 2 weeks, on that week's Monday; December uses the first business
//              day of December, its year-end compression QED). Every other marker follows.
//   QedOnly -> Anchor is the QED deploy itself (busy-season months have no Release).
//
// Deterministic backbone (business-day offsets, weekends only — NOT holiday-shifted,
// because the real markers stay on their weekday even when a holiday lands on them):
//   Start Regression = QED                                              (Prod only)
//   QA Cut-off       = QED - 1 business day   (the Friday before the QED Monday)
//   End Development   = QED - 4 business days   (the Tuesday of the prior week)
//   End Regression    = (Monday of the Release week) - 3 business days   (Prod only)
//   Release          = the anchor                                        (Prod only)
//
// The regression window (Start Regression -> End Regression -> Release) only exists
// for production months. Busy-season QedOnly plans stop at the QED deploy, so they
// emit no StartReg/EndReg/Release (that matches the real ADO plans exactly).
//
// Start Development is a suggested/editable marker: weekends only (NOT holiday-shifted).
// The real GFR plans place StartDev on its nominal weekday even when a US holiday lands
// on it (e.g. Feb 2026 StartDev = MLK Day, Jun 2026 StartDev = Memorial Day), so no
// roll-forward is applied. Occasionally the planners nudge StartDev OFF a holiday instead
// (e.g. Mar 2026 nominal = President's Day Feb 16, real plan = Feb 19) — an editable,
// non-derivable choice, which is why StartDev is a suggestion and carries a wider tolerance.
//   Prod    -> (Monday of the Release week) - 25 business days.
//              The Release is the anchor and everything derives from it. Anchoring on the
//              week's Monday (not the raw, possibly holiday-rolled Release) keeps Tuesday
//              releases (May/Oct) aligned and naturally handles December's wider
//              QED<->Release gap with no special case. On normal months this equals QED-15.
//   QedOnly -> QED - 20 business days (busy-season months have no production Release).
// The residual StartDev misses (e.g. Apr-2025 Holy Week pull-in, Jun-2026 compressed cycle,
// Mar-2026 President's Day nudge) are human planning adjustments, not calendar-derivable, and
// stay within tolerance; every deterministic marker (QED/QaCutoff/EndDev/EndReg/Release) is exact.
public static class ReleaseAnchoredCalculator
{
    private const int QaCutoffOffset = -1;
    private const int EndDevOffset = -4;
    private const int EndRegOffset = -3;
    private const int StartDevOffsetProdFromReleaseMonday = -25;
    private const int StartDevOffsetQedOnly = -20;
    private const int DecemberMonth = 12;

    public static DeliveryPlan Compute(ReleaseSchedule schedule, HolidayCalendar? holidays = null)
    {
        holidays ??= HolidayCalendar.None;
        var anchor = schedule.Anchor;
        var isProd = schedule.Kind == PlanKind.Prod;

        // From the single anchor, recover the QED deploy and (for Prod) the Release week.
        LocalDate qed;
        LocalDate? release = null;
        LocalDate releaseWeekMonday = default;
        if (isProd)
        {
            release = anchor;
            // The Monday of the Release week — the true anchor for both StartDev and EndReg.
            // When the Release rolls to a Tuesday (e.g. May/Oct land the day after Memorial Day
            // / the 4th Monday), counting from the raw Release would drift the windows +1;
            // counting from the week's Monday keeps them stable.
            releaseWeekMonday = MondayOfWeek(anchor);
            qed = QedFromRelease(anchor, holidays);
        }
        else
        {
            qed = anchor;
        }

        var qaCutoff = BusinessDayCalculator.AddBusinessDays(qed, QaCutoffOffset);
        var endDev = BusinessDayCalculator.AddBusinessDays(qed, EndDevOffset);

        // Start Development — Release-anchored for Prod (everything derives from the Release);
        // QED-anchored only for the busy-season QedOnly months that have no Release.
        var startDev = isProd
            ? BusinessDayCalculator.AddBusinessDays(releaseWeekMonday, StartDevOffsetProdFromReleaseMonday)
            : BusinessDayCalculator.AddBusinessDays(qed, StartDevOffsetQedOnly);

        var events = new List<PlanEvent>
        {
            new(Milestone.StartDev, startDev, false, null),
            new(Milestone.EndDev, endDev, false, null),
            new(Milestone.QaCutoff, qaCutoff, false, null),
            new(Milestone.QedDeploy, qed, false, null),
        };

        // Regression window (Start Regression = QED, then End Regression, then Release)
        // exists ONLY for production months. Busy-season QedOnly plans have no Release,
        // so the plan ends at the QED deploy — no StartReg/EndReg/Release is emitted.
        if (isProd)
        {
            var endReg = BusinessDayCalculator.AddBusinessDays(releaseWeekMonday, EndRegOffset);
            events.Add(new PlanEvent(Milestone.StartReg, qed, false, null));
            events.Add(new PlanEvent(Milestone.EndReg, endReg, false, null));
            events.Add(new PlanEvent(Milestone.Release, release!.Value, false, null));
        }

        events.Sort((a, b) => a.Date.CompareTo(b.Date));

        var sprint = new Sprint(startDev, schedule.PlanName);
        return new DeliveryPlan(sprint, events);
    }

    // Derives the QED deploy from the production Release (Prod anchor).
    //   Normal months: Release - 2 weeks, stepped back to that week's Monday. The QED is
    //   always a Monday and is kept even if it lands on a US holiday (e.g. Oct-2025 QED =
    //   Columbus Day).
    //   December: year-end compression — the QED is the first business day of December,
    //   which is NOT Release - 2 weeks, so it is computed directly from the calendar.
    private static LocalDate QedFromRelease(LocalDate release, HolidayCalendar holidays)
    {
        if (release.Month == DecemberMonth)
            return holidays.RollForwardToBusinessDay(new LocalDate(release.Year, DecemberMonth, 1));

        var qed = release.PlusWeeks(-2);
        while (qed.DayOfWeek != IsoDayOfWeek.Monday)
            qed = qed.PlusDays(-1);
        return qed;
    }

    private static LocalDate MondayOfWeek(LocalDate date)
    {
        var d = date;
        while (d.DayOfWeek != IsoDayOfWeek.Monday)
            d = d.PlusDays(-1);
        return d;
    }
}
