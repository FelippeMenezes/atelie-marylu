namespace AtelieMarylu.Web.Services;

public record UserAccessData(bool IsAdmin, IReadOnlyList<string> Groups, IReadOnlyList<string> IndividualPermissions);
