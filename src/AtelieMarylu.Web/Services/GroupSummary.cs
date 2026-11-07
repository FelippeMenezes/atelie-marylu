using AtelieMarylu.Domain.Security;

namespace AtelieMarylu.Web.Services;

public record GroupSummary(string Name, IReadOnlyList<string> Permissions, int Members);
