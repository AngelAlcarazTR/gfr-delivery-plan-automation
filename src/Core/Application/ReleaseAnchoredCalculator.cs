namespace Core.Application;

// Builds a delivery plan backward from the calendar anchors (QED deploy, and the
// production Release for Prod plans), reverse-engineered from 25 real GFR plans
// (2024-2026) cross-referenced with the production release calendar and holidays.
//
// Deterministic backbone (business-day offsets, weekends only — NOT holiday-shifted,
// because the real markers stay on their weekday even when a holiday lands on them):
//   Start Regression = QED
//   QA Cut-off       = QED - 1 business day   (the Friday before the QED Monday)
//   End Development   = QED - 4 business days   (the Tuesday of the prior week)
//   End Regression    = Release - 3 business days               (Prod only)
//   Release          = calendar anchor                          (Prod only)
//
// Start Development is the one planned marker: nominal offset, rolled off holidays.
//   Prod    -> QED - 15 business days
//   QedOnly -> QED - 20 business days
public static class ReleaseAnchoredCalculator
{
    private const int QaCutoffOffset = -1;
    private const int EndDevOffset = -4;
    private const int EndRegOffset = -3;
    private const int StartDevOffsetProd = -15;
    private const int StartDevOffsetQedOnly = -20;

    public static DeliveryPlan Compute(ReleaseSchedule schedule, HolidayCalendar? holidays = null)
    {
        holidays ??= HolidayCalendar.None;
        var qed = schedule.QedDeploy;

        var startReg = qed;
        var qaCutoff = BusinessDayCalculator.AddBusinessDays(qed, QaCutoffOffset);
        var endDev = BusinessDayCalculator.AddBusinessDays(qed, EndDevOffset);

        // Start Development — planned marker: nominal offset rolled off weekends/holidays.
        var nominalStartDev = BusinessDayCalculator.AddBusinessDays(
            qed,
            schedule.Kind == PlanKind.Prod ? StartDevOffsetProd : StartDevOffsetQedOnly);
        var startDev = holidays.RollForwardToBusinessDay(nominalStartDev);
        var startDevAdjusted = startDev != nominalStartDev;

        var events = new List<PlanEvent>
        {
            new(Milestone.StartDev, startDev, startDevAdjusted, startDevAdjusted ? nominalStartDev : null),
            new(Milestone.EndDev, endDev, false, null),
            new(Milestone.QaCutoff, qaCutoff, false, null),
            new(Milestone.QedDeploy, qed, false, null),
            new(Milestone.StartReg, startReg, false, null),
        };

        if (schedule.Kind == PlanKind.Prod)
        {
            var release = schedule.Release
                ?? throw new ArgumentException("Release date is required for Prod plans.", nameof(schedule));
            var endReg = BusinessDayCalculator.AddBusinessDays(release, EndRegOffset);
            events.Add(new PlanEvent(Milestone.EndReg, endReg, false, null));
            events.Add(new PlanEvent(Milestone.Release, release, false, null));
        }

        events.Sort((a, b) => a.Date.CompareTo(b.Date));

        var sprint = new Sprint(startDev, schedule.PlanName);
        return new DeliveryPlan(sprint, events);
    }
}
