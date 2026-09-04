namespace Adapters;

// Builds a self-contained RFC 822 .eml message for a delivery-plan e-mail.
//
// Why .eml instead of Microsoft Graph: creating a draft via Graph needs the
// Mail.ReadWrite scope, which in some tenants is gated behind admin approval.
// A .eml file needs NO Graph, NO permissions and NO admin: the user double-clicks
// it and Outlook opens it as a ready-to-send message in their own mailbox.
//
// The 'X-Unsent: 1' header is the key: it tells Outlook to open the file in
// COMPOSE mode (an editable, sendable draft) rather than as a received message.
public static class EmlDraftWriter
{
    // Produces the full MIME text. Pure (no I/O) so it can be unit-tested.
    // The HTML body is base64-encoded to survive arbitrary content (the render
    // embeds a base64 PNG and long lines) without quoted-printable line-length traps.
    public static string BuildEml(string? from, string? to, string subject, string htmlBody, DateTimeOffset date)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(from)) sb.Append("From: ").Append(from).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(to)) sb.Append("To: ").Append(to).Append("\r\n");

        sb.Append("Subject: ").Append(EncodeHeader(subject)).Append("\r\n");
        sb.Append("Date: ").Append(date.ToString("r")).Append("\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("X-Unsent: 1\r\n");
        sb.Append("Content-Type: text/html; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append("\r\n");

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(htmlBody));
        for (var i = 0; i < b64.Length; i += 76)
            sb.Append(b64, i, Math.Min(76, b64.Length - i)).Append("\r\n");

        return sb.ToString();
    }

    // RFC 2047 encoded-word for headers that contain non-ASCII (e.g. an em dash),
    // so the subject shows correctly in Outlook. Plain ASCII is left as-is.
    private static string EncodeHeader(string value) =>
        value.All(c => c < 128)
            ? value
            : "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
}
