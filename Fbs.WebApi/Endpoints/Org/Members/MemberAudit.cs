using Fbs.WebApi.Data.Entities;

namespace Fbs.WebApi.Endpoints.Org.Members;

/// <summary>What is written down of an admin changing somebody, in words with no name in them.</summary>
public static class MemberAudit
{
    /// <summary>What was done, or null if it changed nothing. The most important of what was done is the action.</summary>
    public static (string Action, string Summary)? Describe(TenantMember before, MemberStatus status, string displayName, string? phone, Guid? unitId, MemberRole role, NotificationScope scope)
    {
        string? action = null;
        var sentences = new List<string>();

        // Whether they are in, which is how somebody waiting is let in or turned away
        if (before.Status == MemberStatus.Pending)
        {
            (action, var said) = status == MemberStatus.Removed ? ("member.turned_away", "Turned a person away.") : ("member.let_in", "Let a person in.");
            sentences.Add(said);
        }
        else if (before.Status == MemberStatus.Removed && status != MemberStatus.Removed)
        {
            action = "member.let_back_in";
            sentences.Add("Let a person back in.");
        }
        else if (before.Status != MemberStatus.Removed && status == MemberStatus.Removed)
        {
            action = "member.removed";
            sentences.Add("Removed a person.");
        }

        if (role != before.Role)
        {
            action ??= role == MemberRole.Admin ? "member.made_admin" : "member.made_member";
            sentences.Add(role == MemberRole.Admin ? "Made them an admin." : "Made them a member.");
        }

        var details = new List<string>();
        if (displayName != before.DisplayName)
        {
            details.Add("name");
        }

        if (phone != before.Phone)
        {
            details.Add("phone number");
        }

        if (unitId != before.UnitId)
        {
            details.Add("unit");
        }

        if (scope != before.NotificationScope)
        {
            details.Add("whose bookings they are told about");
        }

        if (details.Count > 0)
        {
            action ??= "member.changed";
            sentences.Add($"Changed their {string.Join(", ", details)}.");
        }

        return action is null ? null : (action, string.Join(' ', sentences));
    }
}
