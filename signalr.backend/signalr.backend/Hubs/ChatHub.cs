using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using signalr.backend.Data;
using signalr.backend.Models;

namespace signalr.backend.Hubs
{
    // On garde en mémoire les connexions actives (clé: email, valeur: userId)
    // Note: Ce n'est pas nécessaire dans le TP
    public static class UserHandler
    {
        public static Dictionary<string, string> UserConnections { get; set; } = new Dictionary<string, string>();
    }

    // L'annotation Authorize fonctionne de la même façon avec SignalR qu'avec Web API
    [Authorize]
    // Le Hub est le type de base des "contrôleurs" de SignalR
    public class ChatHub : Hub
    {
        public ApplicationDbContext _context;

        public IdentityUser CurentUser
        {
            get
            {
                // On récupère le userid à partir du Cookie qui devrait être envoyé automatiquement
                string userid = Context.UserIdentifier!;
                return _context.Users.Single(u => u.Id == userid);
            }
        }

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public async override Task OnConnectedAsync()
        {
            UserHandler.UserConnections.Add(CurentUser.Email!, Context.UserIdentifier);

            await Clients.All.SendAsync("UsersList", UserHandler.UserConnections.ToList());
            await SendMessage(CurentUser.UserName + " s'est connecté", 0, null);
            await Clients.User(CurentUser.Id).SendAsync("ChannelsList", await _context.Channel.ToListAsync());
        }

        public async override Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);

            // Lors de la fermeture de la connexion, on met à jour notre dictionnary d'utilisateurs connectés
            KeyValuePair<string, string> entrie = UserHandler.UserConnections.SingleOrDefault(uc => uc.Value == Context.UserIdentifier);
            UserHandler.UserConnections.Remove(entrie.Key);

            await Clients.All.SendAsync("UsersList", UserHandler.UserConnections.ToList());
            await SendMessage(CurentUser.UserName + " s'est déconnecté", 0, null); 
        }

        public async Task CreateChannel(string title)
        {
            _context.Channel.Add(new Channel { Title = title });
            await _context.SaveChangesAsync();

            await Clients.All.SendAsync("ChannelsList", await _context.Channel.ToListAsync());
        }

        public async Task DeleteChannel(int channelId)
        {
            Channel channel = _context.Channel.Find(channelId);

            if(channel != null)
            {
                _context.Channel.Remove(channel);
                await _context.SaveChangesAsync();
            }
            string groupName = CreateChannelGroupName(channelId);
            // Envoyer les messages nécessaires aux clients
            await Clients.User(CurentUser.Id).SendAsync("ChannelsList", await _context.Channel.ToListAsync());
        }

        public async Task JoinChannel(int oldChannelId, int newChannelId)
        {
            string userTag = "[" + CurentUser.Email! + "]";

            var OldChannelName = _context.Channel.Where(c => c.Id == oldChannelId).Select(c => c.Title).Single();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, OldChannelName);

            var NewChannelName = _context.Channel.Where(c => c.Id == newChannelId).Select(c => c.Title).Single();
            await Groups.AddToGroupAsync(Context.ConnectionId, NewChannelName);
        }

        public async Task SendMessage(string message, int channelId, string userId)
        {
            if (userId != null)
            {
                string Username = _context.Users.Where(u => u.Id == userId).Select(u => u.UserName).Single();
                await Clients.User(userId).SendAsync("NewMessage", "[" + Username + "] " + message);
            }
            else if (channelId != 0)
            {
                string NomChannel = _context.Channel.Where(c => c.Id == channelId).Select(c => c.Title).Single();
                await Clients.Group(NomChannel).SendAsync("NewMessage", "[" + NomChannel + "] " + message);
            }
            else
            {
                await Clients.All.SendAsync("NewMessage", "[Tous] " + message);
            }
        }

        private static string CreateChannelGroupName(int channelId)
        {
            
            return "Channel" + channelId;
        }
    }
}