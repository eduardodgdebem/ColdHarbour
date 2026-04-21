using ColdHarbour.Domain.Identity;

namespace ColdHarbour.Application.Identity.Ports;

public interface ITokenService
{
    string GenerateAccessToken(User user, string deviceId);
    string GenerateRefreshTokenPlaintext();
}
