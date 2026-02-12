using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace Medipiel.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OtpOptions _otpOptions;

    public AuthController(IHttpClientFactory httpClientFactory, IOptions<OtpOptions> otpOptions)
    {
        _httpClientFactory = httpClientFactory;
        _otpOptions = otpOptions.Value;
    }

    public sealed record RequestOtpBody(string Email);

    [HttpPost("otp")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestOtpBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Email))
        {
            return BadRequest(new { message = "Email es requerido." });
        }

        if (string.IsNullOrWhiteSpace(_otpOptions.OtpUrl))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Otp:OtpUrl no esta configurado." });
        }

        var http = _httpClientFactory.CreateClient();

        HttpResponseMessage upstream;
        try
        {
            upstream = await http.PostAsJsonAsync(_otpOptions.OtpUrl, new { email = body.Email }, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "No se pudo contactar el servicio OTP.", detail = ex.Message });
        }

        var json = await upstream.Content.ReadAsStringAsync(ct);

        if (!upstream.IsSuccessStatusCode)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Error del servicio OTP.",
                upstreamStatus = (int)upstream.StatusCode,
                upstreamBody = SafeJsonOrText(json)
            });
        }

        return Content(string.IsNullOrWhiteSpace(json) ? "{}" : json, "application/json");
    }

    public sealed record LoginBody(string Email, string Code);

    /// <summary>
    /// OTP login. "Code" is the OTP received by email.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Code))
        {
            return BadRequest(new { message = "Email y code son requeridos." });
        }

        if (string.IsNullOrWhiteSpace(_otpOptions.LoginUrl))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Otp:LoginUrl no esta configurado." });
        }

        var http = _httpClientFactory.CreateClient();

        HttpResponseMessage upstream;
        try
        {
            // Upstream expects { email, password } (OTP is used as password).
            upstream = await http.PostAsJsonAsync(_otpOptions.LoginUrl, new { email = body.Email, password = body.Code }, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "No se pudo contactar el servicio de login OTP.", detail = ex.Message });
        }

        var json = await upstream.Content.ReadAsStringAsync(ct);

        if (!upstream.IsSuccessStatusCode)
        {
            // 401/403 should be treated as invalid code.
            if (upstream.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                return Unauthorized(new
                {
                    message = "OTP invalido o expirado.",
                    upstreamStatus = (int)upstream.StatusCode,
                    upstreamBody = SafeJsonOrText(json)
                });
            }

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Error del servicio de login OTP.",
                upstreamStatus = (int)upstream.StatusCode,
                upstreamBody = SafeJsonOrText(json)
            });
        }

        return Content(string.IsNullOrWhiteSpace(json) ? "{}" : json, "application/json");
    }

    // Compatibility with Fuse template defaults: /api/auth/sign-in
    public sealed record SignInBody(string Email, string Password);

    /// <summary>
    /// Compatibility alias: expects { email, password } where password is the OTP.
    /// </summary>
    [HttpPost("sign-in")]
    public Task<IActionResult> SignIn([FromBody] SignInBody body, CancellationToken ct)
        => Login(new LoginBody(body.Email, body.Password), ct);

    private static object SafeJsonOrText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            return raw!;
        }
    }
}

public sealed class OtpOptions
{
    public string LoginUrl { get; set; } = string.Empty;
    public string OtpUrl { get; set; } = string.Empty;
}

