namespace Adapters;

public class HtmlEmailRenderer : IDeliveryPlanRenderer
{
    private static readonly string[] ShortMonths =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly string[] LongMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] ShortDays =
        ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private static string ShortDate(LocalDate date) => $"{date.Day} {ShortMonths[date.Month - 1]}";
    private static string ShortDay(LocalDate date) => ShortDays[(int)date.DayOfWeek - 1];

    public string Render(DeliveryPlan plan, LocalDate today)
    {
        var d = plan.Events.ToDictionary(e => e.Label, e => e.Date);
        var daysToRelease = Period.Between(today, d[Milestone.Release], PeriodUnits.Days).Days;

        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html lang="en"><head>
            <meta charset="UTF-8">
            <meta name="color-scheme" content="light dark">
            <meta name="supported-color-schemes" content="light dark">
            </head>
            <body style="margin:0;padding:0;font-family:'Segoe UI',Arial,sans-serif;">
            <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="width:100%;">
            <tr><td align="center" style="padding:24px 12px;">
            <table role="presentation" cellpadding="0" cellspacing="0" bgcolor="#eef1f4" style="max-width:600px;width:100%;background:#eef1f4;">
        """);

        sb.Append(RenderHeader(plan, d, today));
        sb.Append(RenderPhaseGrid(d));
        sb.Append(RenderFooter());

        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static string RenderHeader(DeliveryPlan plan, Dictionary<Milestone, LocalDate> d, LocalDate today)
    {
        var release = d[Milestone.Release];
        var startDev = d[Milestone.StartDev];
        var releaseMonth = LongMonths[release.Month - 1];
        var daysToRelease = Period.Between(today, release, PeriodUnits.Days).Days;

        var totalDays = Period.Between(startDev, release, PeriodUnits.Days).Days;
        var elapsed = Period.Between(startDev, today, PeriodUnits.Days).Days;
        var percent = totalDays <= 0 ? 0 : Math.Clamp((int)Math.Round(100.0 * elapsed / totalDays), 0, 100);

        var ringB64 = Convert.ToBase64String(RingImageGenerator.CreateRingPng(percent));

        var next = NextDelivery(plan, today);
        var nextHtml = next is null ? "" : $"""
          <div style="padding-top:10px;">
            <span style="font-size:11px;color:#6b7772;text-transform:uppercase;letter-spacing:.06em;">Next milestone</span><br>
            <span style="font-size:14px;font-weight:bold;color:#1f2421;">{next.Value.Label}</span>
            <span style="font-size:12px;color:#5f6b64;"> &middot; {ShortDay(next.Value.Date)} {ShortDate(next.Value.Date)}</span>
          </div>
          """;

        var daysHtml = daysToRelease >= 0
            ? $"""<span style="font-size:28px;font-weight:bold;color:#FA4616;">{daysToRelease}</span><span style="font-size:13px;color:#5f6b64;"> days to release &middot; {ShortDate(release)}</span>"""
            : $"""<span style="font-size:16px;font-weight:bold;color:#FA4616;">Released</span><span style="font-size:13px;color:#5f6b64;"> &middot; {ShortDate(release)}</span>""";

        return $"""
        <tr><td style="background:#ffffff;border:1px solid #d3d8dd;border-bottom:none;padding:22px 30px;">
          <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;">
            <tr>
              <td style="vertical-align:top;">
                <div style="font-size:11px;color:#0F6E56;letter-spacing:.14em;text-transform:uppercase;font-weight:bold;">Delivery Plan &middot; GFR</div>
                <div style="font-size:22px;font-weight:bold;color:#1f2421;padding-top:5px;">{releaseMonth} release</div>
                <div style="padding-top:8px;">{daysHtml}</div>
                {nextHtml}
              </td>
              <td style="vertical-align:top;width:122px;text-align:right;">
                <img src="data:image/png;base64,{ringB64}" width="110" height="110" alt="{percent}% of the release timeline elapsed" style="display:block;border:0;margin-left:auto;">
                <div style="font-size:10px;font-weight:bold;color:#5f6b64;letter-spacing:.08em;text-transform:uppercase;text-align:center;width:110px;margin-left:auto;padding-top:5px;">Time elapsed</div>
              </td>
            </tr>
          </table>
        </td></tr>
        <tr><td height="1" style="height:1px;font-size:0;line-height:0;background:#d3d8dd;">&nbsp;</td></tr>
        <tr><td height="1" style="height:1px;font-size:0;line-height:0;background:#e1e5e9;">&nbsp;</td></tr>
        <tr><td height="2" style="height:2px;font-size:0;line-height:0;background:#eef1f4;">&nbsp;</td></tr>
        """;
    }

    private static string RenderPhaseGrid(Dictionary<Milestone, LocalDate> d)
    {
        // "Wed 29 Jul"
        string F(Milestone m) => $"{ShortDay(d[m])} {ShortDate(d[m])}";

        // a row of 3 milestones, each with a label and a date
        string Row(string label, string value) => $"""
        <tr>
          <td style="font-size:13px;color:#1f2421;padding:2px 0;">{label}</td>
          <td style="font-size:13px;color:#1f2421;padding:2px 0;text-align:right;font-weight:bold;">{value}</td>
        </tr>
        """;

        // a box for a milestone, with a label and a date
        string Box(string title, string titleColor, string barColor, string borderColor, string rows, int height) => $"""
            <table role="presentation" cellpadding="0" cellspacing="0" bgcolor="#ffffff" style="width:100%;height:{height}px;background:#ffffff;border:1px solid {borderColor};">
              <tr><td style="border-left:4px solid {barColor};padding:11px 14px;vertical-align:top;">
                <div style="font-size:11px;font-weight:bold;color:{titleColor};letter-spacing:.05em;">{title}</div>
                <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;margin-top:5px;">{rows}</table>
              </td></tr>
            </table>
        """;

        var dev = Box("DEVELOPMENT", "#0C447C", "#378ADD", "#e3eefa",
            Row("Start", F(Milestone.StartDev)) + Row("End", F(Milestone.EndDev)), 118);

        var qa = Box("QA", "#854F0B", "#EF9F27", "#f7ecd7",
            Row("Cut-off", F(Milestone.QaCutoff)) + Row("Regression Start", F(Milestone.StartReg)) + Row("Regression End", F(Milestone.EndReg)), 118);

        var dep = Box("DEPLOYMENT", "#3C3489", "#7F77DD", "#ece9fb",
            Row("QED", F(Milestone.QedDeploy)), 70);

        var rel = Box("RELEASE", "#0F6E56", "#1D9E75", "#d7efe6",
            Row("AMER/UK", F(Milestone.Release)), 70);

        return $"""
        <tr><td style="padding:22px 30px 6px;">
          <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;">
            <tr>
              <td style="width:50%;padding:0 6px 12px 0;vertical-align:top;">{dev}</td>
              <td style="width:50%;padding:0 0 12px 6px;vertical-align:top;">{qa}</td>
            </tr>
            <tr>
              <td style="width:50%;padding:0 6px 0 0;vertical-align:top;">{dep}</td>
              <td style="width:50%;padding:0 0 0 6px;vertical-align:top;">{rel}</td>
            </tr>
          </table>
        </td></tr>
        """;
    }

    private static string RenderFooter()
    {
        return """
        <tr><td style="padding:16px 30px 6px;">
          <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;">
            <tr><td height="46" bgcolor="#ffffff" style="height:46px;background:#ffffff;text-align:center;border:2px solid #FA4616;border-radius:4px;mso-padding-alt:0;">
              <a href="https://dashboard-delivery-plan.example" style="display:block;line-height:46px;font-size:15px;font-weight:bold;color:#FA4616;text-decoration:none;">Open the release dashboard &rarr;</a>
            </td></tr>
          </table>
        </td></tr>
        <tr><td style="padding:10px 30px 4px;">
          <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;">
            <tr><td height="46" bgcolor="#ffffff" style="height:46px;background:#ffffff;text-align:center;border:2px solid #185FA5;border-radius:4px;mso-padding-alt:0;">
              <a href="https://dev.azure.com/tr-tax" style="display:block;line-height:46px;font-size:14px;font-weight:bold;color:#185FA5;text-decoration:none;">View ticket status &rarr;</a>
            </td></tr>
          </table>
        </td></tr>
        <tr><td style="padding:22px 30px 26px;">
          <div style="border-top:1px solid #eceef0;padding-top:14px;font-size:13px;color:#1f2421;">Thanks,<br><b>Mariana Moser</b></div>
          <div style="font-size:11px;color:#5f6b64;padding-top:2px;">Thomson Reuters &middot; Scrum Master &middot; GoFileRoom</div>
        </td></tr>
    """;
    }

    private static (string Label, LocalDate Date)? NextDelivery(DeliveryPlan plan, LocalDate today)
    {
        // the first milestone that is today or in the future is the next one
        foreach (var e in plan.Events)
            if (e.Date >= today)
                return (LabelText(e.Label), e.Date);
        return null; // all milestones have passed
    }

    private static string LabelText(Milestone m) => m switch
    {
        Milestone.StartDev => "Start - Development",
        Milestone.EndDev => "End - Development",
        Milestone.QaCutoff => "QA - Cut-off",
        Milestone.QedDeploy => "QED - Deployment",
        Milestone.StartReg => "Start - Regression",
        Milestone.EndReg => "End - Regression",
        Milestone.Release => "AMER/UK - Release",
        _ => m.ToString()
    };
}