namespace AtelieMarylu.Web.Services;

public record UserSummary(string Id, string Email, string Name, bool IsAdmin, bool IsEmployee, IReadOnlyList<string> Groups);
