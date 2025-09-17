using Microsoft.AspNetCore.SignalR;
using EcsFeMappingApi.Models;
using System.Threading.Tasks;

namespace EcsFeMappingApi.Services
{
    public class NotificationHub : Hub
    {
        // Group names for different data types
        public const string AllClientsGroup = "AllClients";
        public const string AdminsGroup = "Admins";

        // Called when a client connects
        public override async Task OnConnectedAsync()
        {
            // Add all clients to the general group
            await Groups.AddToGroupAsync(Context.ConnectionId, AllClientsGroup);
            await base.OnConnectedAsync();
        }

        // Allow clients to join admin group (you can add authentication checks here)
        public async Task JoinAdminGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }

        // Methods for sending updates from server to clients
        public async Task UpdateFieldEngineer(FieldEngineer engineer)
        {
            await Clients.All.SendAsync("ReceiveFieldEngineerUpdate", engineer);
        }

        public async Task UpdateServiceRequest(ServiceRequest request)
        {
            await Clients.All.SendAsync("ReceiveServiceRequestUpdate", request);
        }

        public async Task NewServiceRequest(ServiceRequest request)
        {
            await Clients.All.SendAsync("ReceiveNewServiceRequest", request);
        }

        public async Task UpdateBranch(Branch branch)
        {
            await Clients.All.SendAsync("ReceiveBranchUpdate", branch);
        }

        public async Task NewRoute(object routeData)
        {
            await Clients.All.SendAsync("ReceiveNewRoute", routeData);
        }

        public async Task UpdateRoute(object routeData)
        {
            await Clients.All.SendAsync("ReceiveRouteUpdate", routeData);
        }

        public async Task SendNewServiceRequest(ServiceRequest serviceRequest)
        {
            await Clients.All.SendAsync("NewServiceRequest", serviceRequest);
        }

        public async Task SendServiceRequestUpdate(ServiceRequest serviceRequest)
        {
            await Clients.All.SendAsync("ServiceRequestUpdated", serviceRequest);
        }

        public async Task BroadcastNewBranch(Branch branch)
        {
            await Clients.All.SendAsync("ReceiveNewBranch", branch);
        }
        public async Task BroadcastNewFieldEngineer(FieldEngineer fe)
        {
            await Clients.All.SendAsync("ReceiveNewFieldEngineer", fe);
        }

        
    }
}