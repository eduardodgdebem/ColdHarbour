using ColdHarbour.Domain.Identity;

namespace ColdHarbour.Application.Identity.Ports;

public interface ITokenService
{
    string GenerateAccessToken(User user, string deviceId);
    // 8-hour JWT for HttpOnly media cookie — no deviceId, no bearer header required.
    // Browser img/audio elements send this cookie automatically; Angular HttpClient uses the bearer token.
    string GenerateMediaToken(User user);
    string GenerateRefreshTokenPlaintext();
}
