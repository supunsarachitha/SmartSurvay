using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Sends the e-mail confirmation link for accounts created outside the register page (workspace sign-up
/// and join, UI or API). The link opens the UI page <c>/Account/ConfirmEmail</c>, like the register page's.
/// </summary>
public sealed class AccountEmails(UserManager<ApplicationUser> users, IEmailSender<ApplicationUser> sender)
{
    /// <summary>Generates a confirmation token for <paramref name="userId"/> and e-mails the link.</summary>
    /// <param name="userId">The new account.</param>
    /// <param name="baseUri">Absolute base URI of the site (ends with a slash).</param>
    /// <param name="returnUrl">Optional local page to open after confirming.</param>
    public async Task SendConfirmationAsync(Guid userId, string baseUri, string? returnUrl = null)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user?.Email is null)
        {
            return;
        }

        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        var query = new Dictionary<string, string?> { ["userId"] = user.Id.ToString(), ["code"] = code };
        if (!string.IsNullOrEmpty(returnUrl))
        {
            query["returnUrl"] = returnUrl;
        }

        var link = QueryHelpers.AddQueryString(new Uri(new Uri(baseUri), "Account/ConfirmEmail").AbsoluteUri, query);
        await sender.SendConfirmationLinkAsync(user, user.Email, HtmlEncoder.Default.Encode(link));
    }

    /// <summary>Base URI of the current request (scheme, host, path base, trailing slash).</summary>
    public static string BaseUri(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}/";
}
