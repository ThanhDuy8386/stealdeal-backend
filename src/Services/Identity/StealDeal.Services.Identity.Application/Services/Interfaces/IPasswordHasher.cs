namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IPasswordHasher
    {
        string Hash(string password);
        bool Verify(string hashedPassword, string providedPassword);
    }
}