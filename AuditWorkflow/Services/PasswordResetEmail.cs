using System.Net;

namespace AuditWorkflow.Services;

/// <summary>Correo con enlace de restablecimiento (sin contraseña en claro).</summary>
public static class PasswordResetEmail
{
    public static string BuildHtml(
        string recipientName,
        string resetUrl,
        int expiresMinutes,
        string firmDisplayName)
    {
        var safeUrl = WebUtility.HtmlEncode(resetUrl ?? "#");
        var intro = AmperaBrandedEmail.BodyParagraph(
            "Se solicitó un enlace para definir o restablecer su contraseña de AMPERA. " +
            $"El enlace es de <strong>un solo uso</strong> y caduca en <strong>{expiresMinutes} minutos</strong>. " +
            "Si usted no lo pidió, ignore este mensaje; su contraseña actual no cambia.");

        return AmperaBrandedEmail.BuildDocument(
            htmlPageTitle: "Restablecer acceso — AMPERA",
            firmDisplayName: firmDisplayName,
            recipientName: recipientName,
            introAfterGreetingHtml: intro,
            highlightLabel: "Acción requerida",
            highlightInnerHtml:
                $"<span style=\"font-size:15px;font-weight:600;color:{AmperaEmailPalette.TealOscuro};\">Defina su nueva contraseña con el botón siguiente</span>",
            primaryButtonHref: resetUrl ?? "#",
            primaryButtonText: "Definir contraseña",
            fallbackLinkDisplayEncoded: safeUrl);
    }
}
