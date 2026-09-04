namespace Core.Tests;

// Verifies the pure .eml MIME builder: headers, the X-Unsent compose flag, base64
// body round-trip and RFC 2047 subject encoding for non-ASCII characters.
public class EmlDraftWriterTests
{
    private static readonly DateTimeOffset FixedDate = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IncludesToAndSubjectHeaders()
    {
        var eml = EmlDraftWriter.BuildEml(from: null, to: "you@tr.com", subject: "Hello", htmlBody: "<p>hi</p>", date: FixedDate);

        Assert.Contains("To: you@tr.com\r\n", eml);
        Assert.Contains("Subject: Hello\r\n", eml);
    }

    [Fact]
    public void OmitsToHeader_WhenRecipientBlank()
    {
        var eml = EmlDraftWriter.BuildEml(from: null, to: null, subject: "Hello", htmlBody: "<p>hi</p>", date: FixedDate);

        Assert.DoesNotContain("\r\nTo:", eml);
        Assert.DoesNotContain("To: ", eml);
    }

    [Fact]
    public void SetsXUnsentSoOutlookOpensInComposeMode()
    {
        var eml = EmlDraftWriter.BuildEml(from: null, to: "you@tr.com", subject: "Hello", htmlBody: "<p>hi</p>", date: FixedDate);

        Assert.Contains("X-Unsent: 1\r\n", eml);
    }

    [Fact]
    public void DeclaresHtmlUtf8Base64()
    {
        var eml = EmlDraftWriter.BuildEml(from: null, to: null, subject: "Hello", htmlBody: "<p>hi</p>", date: FixedDate);

        Assert.Contains("MIME-Version: 1.0\r\n", eml);
        Assert.Contains("Content-Type: text/html; charset=utf-8\r\n", eml);
        Assert.Contains("Content-Transfer-Encoding: base64\r\n", eml);
    }

    [Fact]
    public void BodyIsBase64OfHtml_RoundTrips()
    {
        var html = "<html><body>Caña &amp; ¡olé!</body></html>";
        var eml = EmlDraftWriter.BuildEml(from: null, to: null, subject: "Hello", htmlBody: html, date: FixedDate);

        // Everything after the blank line is the base64 body (possibly wrapped).
        var idx = eml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var b64 = eml[(idx + 4)..].Replace("\r\n", "");
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));

        Assert.Equal(html, decoded);
    }

    [Fact]
    public void EncodesNonAsciiSubject_AsRfc2047()
    {
        // Em dash forces encoded-word so Outlook renders it correctly.
        var subject = "[GFR] Delivery Plan \u2014 September Release";
        var eml = EmlDraftWriter.BuildEml(from: null, to: null, subject: subject, htmlBody: "<p>hi</p>", date: FixedDate);

        Assert.Contains("Subject: =?utf-8?B?", eml);

        // The encoded word must decode back to the original subject.
        var line = eml.Split("\r\n").First(l => l.StartsWith("Subject:"));
        var token = line["Subject: =?utf-8?B?".Length..].TrimEnd('?', '=');
        // Re-add stripped padding by decoding the full encoded-word instead:
        var start = line.IndexOf("?B?", StringComparison.Ordinal) + 3;
        var end = line.LastIndexOf("?=", StringComparison.Ordinal);
        var payload = line[start..end];
        Assert.Equal(subject, Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }

    [Fact]
    public void PlainAsciiSubject_IsNotEncoded()
    {
        var eml = EmlDraftWriter.BuildEml(from: null, to: null, subject: "Plain ascii", htmlBody: "<p>hi</p>", date: FixedDate);

        Assert.Contains("Subject: Plain ascii\r\n", eml);
        Assert.DoesNotContain("=?utf-8?B?", eml);
    }
}
