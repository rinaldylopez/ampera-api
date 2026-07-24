using System.Net;
using System.Text;

namespace AuditWorkflow.Services;

/// <summary>Layout HTML corporativo AMPERA (cabecera, caja destacada, CTA, pie).</summary>
public static class AmperaBrandedEmail
{
    public static string BodyParagraph(string innerHtmlSafe) =>
        $"""<p style="margin:0 0 16px;font-size:15px;line-height:1.65;color:{AmperaEmailPalette.Negro};">{innerHtmlSafe}</p>""";

    public static string BodyParagraphSecondary(string innerHtmlSafe) =>
        $"""<p style="margin:0;font-size:14px;line-height:1.6;color:{AmperaEmailPalette.TextoSecundario};">{innerHtmlSafe}</p>""";

    /// <param name="introAfterGreetingHtml">HTML seguro tras el saludo.</param>
    /// <param name="highlightInnerHtml">Caja central (código, contraseña, etc.).</param>
    /// <param name="fallbackLinkDisplayEncoded">Enlace alternativo, ya <see cref="WebUtility.HtmlEncode"/>.</param>
    public static string BuildDocument(
        string htmlPageTitle,
        string firmDisplayName,
        string recipientName,
        string introAfterGreetingHtml,
        string? highlightLabel,
        string? highlightInnerHtml,
        string? primaryButtonHref,
        string? primaryButtonText,
        string? fallbackLinkDisplayEncoded)
    {
        var safeName = WebUtility.HtmlEncode(recipientName?.Trim() ?? "Usuario");
        var safeFirm = WebUtility.HtmlEncode(firmDisplayName?.Trim() ?? "AMPERA");

        var sb = new StringBuilder(5000);
        AppendOpen(sb, WebUtility.HtmlEncode(htmlPageTitle));
        AppendHeaderBlock(sb, safeFirm: safeFirm, headerLine1: "Plataforma", showAmpTitle: true);

        sb.Append(
            $"""
                        <tr>
                          <td style="padding:32px 32px 8px;">
                            <p style="margin:0 0 16px;font-size:16px;line-height:1.6;color:{AmperaEmailPalette.Negro};">Hola <strong>
            """);

        sb.Append(safeName);
        sb.Append($"</strong>,</p>");
        sb.Append(introAfterGreetingHtml);
        sb.Append("\n                        </td>\n                      </tr>\n");

        AppendHighlightIfAny(sb, highlightLabel, highlightInnerHtml);
        AppendCtaIfAny(sb, primaryButtonHref, primaryButtonText, fallbackLinkDisplayEncoded);
        AppendFooterAndClose(sb);

        return sb.ToString();
    }

    /// <summary>Correos de flujo (solicitudes PBC) sin saludo personal: título + cuerpo + CTA.</summary>
    public static string BuildNotificationDocument(
        string htmlPageTitle,
        string headerSubtitle,
        string cardHeadline,
        string bodyInnerHtml,
        string? primaryButtonHref,
        string? primaryButtonText,
        string? fallbackLinkDisplayEncoded)
    {
        var safeSub = WebUtility.HtmlEncode(headerSubtitle?.Trim() ?? "Notificación");
        var safeHeadline = WebUtility.HtmlEncode(cardHeadline?.Trim() ?? "");

        var sb = new StringBuilder(5000);
        AppendOpen(sb, WebUtility.HtmlEncode(htmlPageTitle));
        AppendHeaderBlock(sb, safeFirm: safeSub, headerLine1: "Notificación", showAmpTitle: false);

        sb.Append(
            $"""
                      <tr>
                        <td style="padding:28px 32px 8px;">
                          <h2 style="margin:0 0 18px;font-size:21px;font-weight:700;line-height:1.35;color:{AmperaEmailPalette.Negro};">
            """);

        sb.Append(safeHeadline);
        sb.Append(
            $"""
            </h2>
            {bodyInnerHtml}
                        </td>
                      </tr>
            """);

        AppendCtaIfAny(sb, primaryButtonHref, primaryButtonText, fallbackLinkDisplayEncoded);
        AppendFooterAndClose(sb);

        return sb.ToString();
    }

    private static void AppendOpen(StringBuilder sb, string encodedPageTitle)
    {
        sb.Append("<!DOCTYPE html>\n<html lang=\"es\">\n<head>\n  <meta charset=\"utf-8\" />\n  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n  <title>");
        sb.Append(encodedPageTitle);
        sb.Append(
            $"""
            </title>
            </head>
            <body style="margin:0;padding:0;background-color:{AmperaEmailPalette.FondoPagina};font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:{AmperaEmailPalette.FondoPagina};padding:32px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background-color:{AmperaEmailPalette.Blanco};border:1px solid {AmperaEmailPalette.TealClaro};border-radius:12px;">
            """);
    }

    /// <param name="showAmpTitle">True: líneas «Plataforma» / «AMPERA» / firma. False: «Notificación» / «AMPERA» / subtítulo de contexto.</param>
    private static void AppendHeaderBlock(StringBuilder sb, string safeFirm, string headerLine1, bool showAmpTitle)
    {
        sb.Append(
            $"""
                      <tr>
                        <td style="background-color:{AmperaEmailPalette.DarkTeal};padding:28px 32px;">
                          <div style="font-size:13px;letter-spacing:2px;text-transform:uppercase;color:#E6F5F5;margin-bottom:8px;">
            """);

        sb.Append(WebUtility.HtmlEncode(headerLine1));
        sb.Append("</div>\n");

        if (showAmpTitle)
        {
            sb.Append(
                $"""
                          <div style="font-size:22px;font-weight:700;color:{AmperaEmailPalette.Blanco};line-height:1.3;">AMPERA</div>
                          <div style="font-size:14px;color:{AmperaEmailPalette.TealClaro};margin-top:8px;">
                """);
            sb.Append(safeFirm);
            sb.Append("</div>\n");
        }
        else
        {
            sb.Append(
                $"""
                          <div style="font-size:22px;font-weight:700;color:{AmperaEmailPalette.Blanco};line-height:1.25;">AMPERA</div>
                          <div style="font-size:14px;color:{AmperaEmailPalette.TealClaro};margin-top:8px;">
                """);
            sb.Append(safeFirm);
            sb.Append("</div>\n");
        }

        sb.Append(
            """
                        </td>
                      </tr>
            """);
    }

    private static void AppendHighlightIfAny(StringBuilder sb, string? highlightLabel, string? highlightInnerHtml)
    {
        if (string.IsNullOrEmpty(highlightLabel) || string.IsNullOrEmpty(highlightInnerHtml))
            return;

        sb.Append(
            $"""
                      <tr>
                        <td style="padding:0 32px 24px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:{AmperaEmailPalette.FondoTarjetaSutil};border:1px solid {AmperaEmailPalette.TealClaro};border-radius:8px;">
                            <tr>
                              <td style="padding:20px 24px;text-align:center;">
                """);

        sb.Append(
            $"<div style=\"font-size:12px;font-weight:600;letter-spacing:1px;text-transform:uppercase;color:{AmperaEmailPalette.TealOscuro};margin:0 0 12px 0;text-align:center;\">");
        sb.Append(WebUtility.HtmlEncode(highlightLabel));
        sb.Append($"</div>\n");
        sb.Append(highlightInnerHtml);
        sb.Append(
            """
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
            """);
    }

    private static void AppendCtaIfAny(
        StringBuilder sb,
        string? primaryButtonHref,
        string? primaryButtonText,
        string? fallbackLinkDisplayEncoded)
    {
        if (string.IsNullOrWhiteSpace(primaryButtonHref) || string.IsNullOrWhiteSpace(primaryButtonText))
            return;

        sb.Append(
            """
                      <tr>
                        <td style="padding:0 32px 32px;" align="center">
            """);

        // Botón en tabla: Outlook (Word) ignora a menudo padding en <a> suelto; VML innecesario con este patrón.
        sb.Append(
            $"""
                          <table role="presentation" border="0" cellspacing="0" cellpadding="0" align="center" style="margin:0 auto;">
                            <tr>
                              <td align="center" bgcolor="{AmperaEmailPalette.Teal}" style="border-radius:8px;background-color:{AmperaEmailPalette.Teal};mso-padding-alt:14px 32px;border:1px solid {AmperaEmailPalette.TealOscuro};">
            """);

        sb.Append("<a href=\"");
        sb.Append(primaryButtonHref);
        sb.Append(
            $"\" style=\"display:inline-block;background-color:{AmperaEmailPalette.Teal};color:{AmperaEmailPalette.Blanco};text-decoration:none;font-size:15px;font-weight:600;line-height:20px;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;padding:14px 32px;border-radius:8px;border:1px solid {AmperaEmailPalette.TealOscuro};\">");
        sb.Append(WebUtility.HtmlEncode(primaryButtonText));
        sb.Append("</a></td></tr></table>");

        if (!string.IsNullOrEmpty(fallbackLinkDisplayEncoded))
        {
            sb.Append(
                $"""
                                  <p style="margin:20px 0 0;font-size:12px;line-height:1.5;color:{AmperaEmailPalette.TextoMeta};">
                                    Si el botón no funciona, copie y pegue este enlace en su navegador:<br />
                                    <span style="color:{AmperaEmailPalette.TealOscuro};word-break:break-all;">
                    """);

            sb.Append(fallbackLinkDisplayEncoded);
            sb.Append("</span>\n                          </p>");
        }

        sb.Append("\n                        </td>\n                      </tr>\n");
    }

    /// <summary>
    /// Tabla de listado para correos masivos de solicitudes (estilos inline). Los encabezados son texto llano y se codifican.
    /// Cada celda puede incluir HTML interno ya seguro (&lt;a&gt;, etc.).
    /// </summary>
    public static string BuildBulkRequestDataTableHtml(IReadOnlyList<string> columnHeadersPlain,
        IReadOnlyList<string[]> rowsCellsInnerHtml)
    {
        if (columnHeadersPlain.Count == 0 || rowsCellsInnerHtml.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(rowsCellsInnerHtml.Count * 240 + 1100);

        sb.Append("<div style=\"margin:20px 0 6px;font-size:12px;font-weight:700;letter-spacing:0.06em;text-transform:uppercase;color:");
        sb.Append(AmperaEmailPalette.TealOscuro);
        sb.Append(";\">Detalle de solicitudes</div>");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;border-radius:10px;border:1px solid ");
        sb.Append(AmperaEmailPalette.TealClaro);
        sb.Append(";overflow:hidden;background-color:");
        sb.Append(AmperaEmailPalette.Blanco);
        sb.Append(";mso-table-lspace:0pt;mso-table-rspace:0pt;\"><tr><td style=\"padding:0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;\"><tr>");

        var thBgTail = ";background-color:" + AmperaEmailPalette.DarkTeal +
                       ";border-bottom:2px solid " + AmperaEmailPalette.TealOscuro + ';';

        foreach (var h in columnHeadersPlain)
        {
            sb.Append("<th scope=\"col\" align=\"left\" valign=\"middle\" bgcolor=\"");
            sb.Append(AmperaEmailPalette.DarkTeal);
            sb.Append(
                $"\" style=\"padding:11px 12px;text-align:left;font-size:11px;font-weight:700;letter-spacing:0.04em;text-transform:uppercase;line-height:1.35;color:{AmperaEmailPalette.Blanco}");
            sb.Append(thBgTail);
            sb.Append("\">");
            sb.Append(WebUtility.HtmlEncode(h));
            sb.Append("</th>");
        }

        sb.Append("</tr>");
        var zebra = false;
        foreach (var row in rowsCellsInnerHtml)
        {
            var bg = zebra ? AmperaEmailPalette.FondoTarjetaSutil : AmperaEmailPalette.Blanco;
            zebra = !zebra;
            sb.Append("<tr>");
            foreach (var cell in row)
            {
                var inner = cell ?? "";
                sb.Append("<td valign=\"middle\" bgcolor=\"");
                sb.Append(bg);
                sb.Append("\" style=\"padding:11px 12px;font-size:14px;line-height:1.55;color:");
                sb.Append(AmperaEmailPalette.Negro);
                sb.Append(";background-color:");
                sb.Append(bg);
                sb.Append(";border-bottom:1px solid ");
                sb.Append(AmperaEmailPalette.TealClaro);
                sb.Append(";\">");
                sb.Append(inner);
                sb.Append("</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</table></td></tr></table>");
        return sb.ToString();
    }

    private static void AppendFooterAndClose(StringBuilder sb)
    {
        sb.Append(
            $"""
                      <tr>
                        <td style="padding:20px 32px 28px;border-top:1px solid {AmperaEmailPalette.TealClaro};background:{AmperaEmailPalette.FondoPie};">
                          <p style="margin:0;font-size:12px;line-height:1.55;color:{AmperaEmailPalette.TextoSecundario};">
                            Este correo ha sido enviado de forma automatizada desde AMPERA. Por favor no responda por este medio: las respuestas no serán atendidas. Para consultas, utilice los canales habituales de su empresa o el contacto asignado a su proyecto.
                          </p>
                        </td>
                      </tr>
                    </table>
                    <p style="margin:24px 0 0;font-size:11px;color:{AmperaEmailPalette.TextoSecundario};">© AMPERA · Gestión de auditoría</p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """);
    }
}
