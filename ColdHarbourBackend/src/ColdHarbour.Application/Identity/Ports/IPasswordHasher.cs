namespace ColdHarbour.Application.Identity.Ports;

public interface IPasswordHasher
{
    string Hash(string plaintext);
    bool Verify(string plaintext, string hash);
}
