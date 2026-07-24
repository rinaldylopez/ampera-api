using System.Net;
using System.Text;
using System.Linq;

namespace AuditWorkflow.Services;

/// <summary>Correo de segundo factor (código de 6 dígitos), mismo diseño corporativo que el resto de AMPERA.</summary>
public static class LoginOtpEmail
{
    /// <summary>
    /// Dígitos separados con espacio no rompible (Outlook no interpreta bien <c>rem</c> ni <c>letter-spacing</c> en el mismo span).
    /// </summary>
    private static string BuildOtpDigitsInnerHtml(string sixDigitCode)
    {
        var digits = string.Concat((sixDigitCode ?? "").Where(char.IsDigit));
        if (digits.Length == 0)
            return $"<span style=\"{OtpDigitSpanStyle}\">{WebUtility.HtmlEncode(sixDigitCode ?? "")}</span>";

        var sb = new StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0)
                sb.Append("&nbsp;&nbsp;");
            sb.Append("<span style=\"");
            sb.Append(OtpDigitSpanStyle);
            sb.Append("\">");
            sb.Append(WebUtility.HtmlEncode(digits[i].ToString()));
            sb.Append("</span>");
        }

        return sb.ToString();
    }

    /// <summary>Estilos solo en px y fuentes seguras para Outlook (Word HTML).</summary>
    private const string OtpDigitSpanStyle =
        $"font-size:26px;font-weight:700;line-height:1.2;color:{AmperaEmailPalette.TealOscuro};font-family:Consolas,'Courier New',monospace;";

    public static string BuildHtml(string recipientName, string sixDigitCode, int expiryMinutes, string loginUrl, string firmDisplayName)
    {
        var safeUrl = WebUtility.HtmlEncode(loginUrl ?? "#");

        var intro =
            AmperaBrandedEmail.BodyParagraph(
                "Ha iniciado sesión en AMPERA. Para completar el acceso, introduzca el siguiente <strong>código de verificación</strong> en la pantalla de inicio de sesión.") +
            AmperaBrandedEmail.BodyParagraphSecondary(
                $"El código caduca en <strong>{expiryMinutes} minutos</strong>. Si usted no solicitó este acceso, ignore este mensaje y considere cambiar su contraseña.");

        return AmperaBrandedEmail.BuildDocument(
            htmlPageTitle: "Código de acceso AMPERA",
            firmDisplayName: firmDisplayName,
            recipientName: recipientName,
            introAfterGreetingHtml: intro,
            highlightLabel: "Código de verificación",
            highlightInnerHtml: BuildOtpDigitsInnerHtml(sixDigitCode),
            primaryButtonHref: loginUrl ?? "#",
            primaryButtonText: "Continuar en AMPERA",
            fallbackLinkDisplayEncoded: safeUrl);
    }
}
