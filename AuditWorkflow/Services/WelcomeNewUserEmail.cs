using System.Net;

namespace AuditWorkflow.Services;

/// <summary>Plantilla HTML para el correo de bienvenida con contraseña provisional.</summary>
public static class WelcomeNewUserEmail
{
    public static string BuildHtml(string recipientName, string plainPassword, string loginUrl, string firmDisplayName)
    {
        var safePassword = WebUtility.HtmlEncode(plainPassword ?? "");
        var safeUrl = WebUtility.HtmlEncode(loginUrl ?? "#");

        var intro = AmperaBrandedEmail.BodyParagraph(
            "Se ha creado su cuenta de acceso. Utilice la <strong>contraseña provisional</strong> indicada abajo para iniciar sesión. " +
            "Por seguridad, el sistema le solicitará definir una nueva contraseña al ingresar.");

        return AmperaBrandedEmail.BuildDocument(
            htmlPageTitle: "Bienvenido a AMPERA",
            firmDisplayName: firmDisplayName,
            recipientName: recipientName,
            introAfterGreetingHtml: intro,
            highlightLabel: "Contraseña provisional",
            highlightInnerHtml:
                $"<span style=\"font-size:18px;font-weight:700;font-family:Consolas,'Courier New',monospace;color:{AmperaEmailPalette.TealOscuro};\">{safePassword}</span>",
            primaryButtonHref: loginUrl ?? "#",
            primaryButtonText: "Iniciar sesión en AMPERA",
            fallbackLinkDisplayEncoded: safeUrl);
    }
}
