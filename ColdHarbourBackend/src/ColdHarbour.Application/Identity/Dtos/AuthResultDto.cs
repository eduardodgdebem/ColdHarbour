namespace ColdHarbour.Application.Identity.Dtos;

public sealed record AuthResultDto(string AccessToken, Guid UserId, string Email);

public sealed record AuthenticateResult(AuthResultDto Dto, string RefreshTokenPlaintext);
