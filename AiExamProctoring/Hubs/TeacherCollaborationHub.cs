using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
namespace AiExamProctoring.Hubs;
[Authorize(Roles = "Teacher")]
public sealed class TeacherCollaborationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier is not null) await Groups.AddToGroupAsync(Context.ConnectionId, "collaboration:" + Context.UserIdentifier);
        await base.OnConnectedAsync();
    }
}
