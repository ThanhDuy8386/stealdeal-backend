namespace StealDeal.Services.Order.Application.Constants;

public static class OrderRoles
{
    public const string Seller = "Seller";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
    public const string SellerOrAdministrator = Seller + "," + Admin + "," + SuperAdmin;

    public static bool IsAdministrator(IEnumerable<string> roles) =>
        roles.Any(role =>
            string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, SuperAdmin, StringComparison.OrdinalIgnoreCase));
}
