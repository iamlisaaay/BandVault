using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using BandVault.Web.Services;

namespace BandVault.Web.Controllers
{
    // C16: вхід і реєстрація лише через Google OAuth2 (без входу за самим email без пароля)
    public class AuthController : Controller
    {
        private readonly IConfiguration _config;
        private readonly UserSignInService _signIn;

        public AuthController(IConfiguration config, UserSignInService signIn)
        {
            _config = config;
            _signIn = signIn;
        }

        public static bool GoogleConfigured(IConfiguration config) =>
            !string.IsNullOrWhiteSpace(config["Authentication:Google:ClientId"]) && !string.IsNullOrWhiteSpace(config["Authentication:Google:ClientSecret"]);

        [HttpGet]
        public IActionResult Login(string returnUrl = "/")
        {
            // Якщо користувач вже авторизований, не показуємо йому сторінку логіну
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
            return View();
        }

        // ============================================
        // GOOGLE OAUTH2 (C16)
        // ============================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ExternalLogin(string returnUrl = "/")
        {
            if (!GoogleConfigured(_config))
            {
                TempData["LoginError"] = "Вхід через Google ще не налаштовано: адміністратору потрібно додати Authentication:Google:ClientId та ClientSecret.";
                return RedirectToAction(nameof(Login));
            }
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Auth", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string returnUrl = "/")
        {
            var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!result.Succeeded || result.Principal == null)
            {
                TempData["LoginError"] = "Google не підтвердив вхід. Спробуйте ще раз.";
                return RedirectToAction(nameof(Login));
            }

            var claims = result.Principal.Claims;
            var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            var name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            var avatar = claims.FirstOrDefault(c => c.Type == "urn:google:picture")?.Value;

            if (string.IsNullOrEmpty(email))
            {
                TempData["LoginError"] = "Google не передав email акаунта.";
                return RedirectToAction(nameof(Login));
            }

            await _signIn.SignInAsync(HttpContext, email, name, avatar);

            return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // вихід лише з форми сайту (чужа сторінка не може розлогінити користувача)
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        // ============================================
        // ЕКШЕН ДЛЯ ДОСТУПУ ЗАБОРОНЕНО
        // ============================================
        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
