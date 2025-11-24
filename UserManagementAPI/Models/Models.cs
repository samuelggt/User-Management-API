// Models.cs
namespace UserManagementAPI.Models;

public record User(int Id, string Name, string Email);
public record UserCreateRequest(string Name, string Email);
public record UserUpdateRequest(string Name, string Email);

public static class UserValidator
{
    public static string? ValidateUser(string? name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "El nombre es obligatorio.";

        if (string.IsNullOrWhiteSpace(email))
            return "El email es obligatorio.";

        if (!email.Contains('@') || email.StartsWith("@") || email.EndsWith("@"))
            return "El email no tiene un formato válido.";

        return null;
    }
}
