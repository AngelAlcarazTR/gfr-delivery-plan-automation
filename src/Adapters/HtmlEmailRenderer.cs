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

    public string Render(DeliveryPlan plan)
    {
        var d = plan.Events.ToDictionary(e => e.Label, e => e.Date);

        var sb = new StringBuilder();
        sb.Append("""
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="UTF-8"></head>
        <body style="margin:0;background:#cfd4d8;padding:24px 12px;font-family:'Segoe UI',Arial,sans-serif;">
        <table role="presentation" cellpadding="0" cellspacing="0" style="max-width:600px;margin:0 auto;width:100%;background:#ffffff;">
        """);

        sb.Append(RenderHeader(plan, d));
        sb.Append(RenderPhaseGrid(d));
        sb.Append(RenderFooter());

        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static string RenderHeader(DeliveryPlan plan, Dictionary<Milestone, LocalDate> d)
    {
        var release = d[Milestone.Release];
        var releaseMonth = LongMonths[release.Month - 1];

        return $"""
        <tr><td style="background:#0A3D2E;padding:24px 30px;">
          <div style="font-size:11px;color:#6fa891;letter-spacing:.14em;text-transform:uppercase;">Delivery Plan &middot; GFR</div>
          <div style="font-size:22px;font-weight:bold;color:#ffffff;padding-top:5px;">{releaseMonth} release</div>
          <div style="padding-top:8px;">
            <span style="font-size:13px;color:#c9e0d6;">Release &middot; </span>
            <span style="font-size:22px;font-weight:bold;color:#FA4616;">{ShortDay(release)} {ShortDate(release)}</span>
          </div>
        </td></tr>
        """;
    }

    private static string RenderPhaseGrid(Dictionary<Milestone, LocalDate> d)
    {
        return $"""
        <tr><td style="padding:22px 24px 6px;">
          <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;">
            <tr>
              <td style="width:50%;padding:0 6px 12px 0;vertical-align:top;">
                <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border:1px solid #e3eefa;">
                  <tr><td style="border-left:4px solid #378ADD;padding:11px 14px;">
                    <div style="font-size:11px;font-weight:bold;color:#0C447C;letter-spacing:.05em;">DEVELOPMENT</div>
                    <div style="font-size:13px;color:#1f2421;padding-top:7px;line-height:1.8;">Start<b style="float:right;">{ShortDate(d[Milestone.StartDev])}</b><br>End<b style="float:right;">{ShortDate(d[Milestone.EndDev])}</b></div>
                  </td></tr>
                </table>
              </td>
              <td style="width:50%;padding:0 0 12px 6px;vertical-align:top;">
                <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border:1px solid #f7ecd7;">
                  <tr><td style="border-left:4px solid #EF9F27;padding:11px 14px;">
                    <div style="font-size:11px;font-weight:bold;color:#854F0B;letter-spacing:.05em;">QA</div>
                    <div style="font-size:13px;color:#1f2421;padding-top:7px;line-height:1.8;">Cut-off<b style="float:right;">{ShortDate(d[Milestone.QaCutoff])}</b><br>Regression<b style="float:right;">{ShortDate(d[Milestone.StartReg])}&ndash;{ShortDate(d[Milestone.EndReg])}</b></div>
                  </td></tr>
                </table>
              </td>
            </tr>
            <tr>
              <td style="width:50%;padding:0 6px 0 0;vertical-align:top;">
                <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border:1px solid #ece9fb;">
                  <tr><td style="border-left:4px solid #7F77DD;padding:11px 14px;">
                    <div style="font-size:11px;font-weight:bold;color:#3C3489;letter-spacing:.05em;">DEPLOYMENT</div>
                    <div style="font-size:13px;color:#1f2421;padding-top:7px;line-height:1.8;">QED<b style="float:right;">{ShortDate(d[Milestone.QedDeploy])}</b></div>
                  </td></tr>
                </table>
              </td>
              <td style="width:50%;padding:0 0 0 6px;vertical-align:top;">
                <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border:1px solid #d7efe6;">
                  <tr><td style="border-left:4px solid #1D9E75;padding:11px 14px;">
                    <div style="font-size:11px;font-weight:bold;color:#0F6E56;letter-spacing:.05em;">RELEASE</div>
                    <div style="font-size:13px;color:#0F6E56;padding-top:7px;line-height:1.8;font-weight:bold;">AMER/UK<b style="float:right;">{ShortDate(d[Milestone.Release])}</b></div>
                  </td></tr>
                </table>
              </td>
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
            <tr><td style="background:#FA4616;text-align:center;">
              <a href="https://dashboard-delivery-plan.example" style="display:block;padding:15px 20px;font-size:15px;font-weight:bold;color:#ffffff;text-decoration:none;">Open the release dashboard &rarr;</a>
            </td></tr>
          </table>
        </td></tr>
        <tr><td style="padding:22px 30px 26px;">
          <div style="border-top:1px solid #eceef0;padding-top:14px;font-size:13px;color:#1f2421;">Thanks,<br><b>Mariana Moser</b></div>
          <div style="font-size:11px;color:#9aa0a6;padding-top:2px;">Thomson Reuters &middot; Scrum Master &middot; GoFileRoom</div>
        </td></tr>
        """;
    }
}