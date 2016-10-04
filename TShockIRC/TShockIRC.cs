using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IrcDotNet;
using IrcDotNet.Ctcp;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using PlayerDB;

using Group = TShockAPI.Group;

namespace TShockIRC
{
    [ApiVersion(1, 25)]
    public class TShockIRC : TerrariaPlugin
    {
        #region TerrariaPlugin implementation
        public override string Author
        {
            get { return "Nyx Studios"; }
        }
        public override string Description
        {
            get { return "Provides an IRC interface."; }
        }
        public override string Name
        {
            get { return "TShockIRC"; }
        }
        public override Version Version
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version; }
        }
        #endregion

        public static Config Config = new Config();
        public DB db = new DB();
        public static CtcpClient CtcpClient;
        public static IrcClient IrcClient = new IrcClient();
        public static Dictionary<IrcUser, Group> IrcUsers = new Dictionary<IrcUser, Group>();
        private string LastOutgoing = "";

        public TShockIRC(Main game)
            : base(game)
        {
            Order = Int32.MaxValue;
        }

        bool Connecting = false;
        void Connect()
        {
            if (Connecting)
                return;

            Connecting = true;
            IrcUsers.Clear();
            IrcClient = new IrcClient();
            //IrcClient.Connect((String)Config.Server, Config.Port, Config.SSL,
            IrcClient.Connect((String)Config.Server, Config.Port, false,
                new IrcUserRegistrationInfo()
                {
                    NickName = Config.Nick,
                    RealName = Config.RealName,
                    UserName = Config.UserName,
                    Password = Config.Password,
                    PassAfterNick = Config.PassAfterNick,
                    IgnoreServerWelcomeInfo = Config.IgnoreServerWelcomeInfo,
                    UserModes = new List<char> { 'i', 'w' }
                });
            IrcClient.Disconnected += OnIRCDisconnected;
            IrcClient.Registered += OnIRCRegistered;
            CtcpClient = new CtcpClient(IrcClient) { ClientVersion = "TShockIRC v" + Version };
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInitialize);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
                ServerApi.Hooks.ServerChat.Deregister(this, OnChat);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                //ServerApi.Hooks.ServerBroadcast.Deregister(this, OnBroadcast);
                PlayerHooks.PlayerCommand -= OnPlayerCommand;
                PlayerHooks.PlayerPostLogin -= OnPostLogin;

                IrcClient.Dispose();
            }
        }
        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInitialize);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
            ServerApi.Hooks.ServerChat.Register(this, OnChat);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            //ServerApi.Hooks.ServerBroadcast.Register(this, OnBroadcast);
            PlayerHooks.PlayerCommand += OnPlayerCommand;
            PlayerHooks.PlayerPostLogin += OnPostLogin;
        }

        // An attempt to handle server broadcasts, except it actually just picks up player chats
        // and not server broadcasts at all.
        /*
        void OnBroadcast(ServerBroadcastEventArgs e)
        {
            //Debug.Assert(true == false);
            if (!IrcClient.IsConnected)
                Connect();
            else if (e.Message != null && !String.IsNullOrEmpty(Config.ServerBroadcastMessageFormat))
            {
                SendMessage(Config.Channel, String.Format(Config.ServerBroadcastMessageFormat, e.Message));
            }
        }
        */

        void OnChat(ServerChatEventArgs e)
        {
            TSPlayer tsPlr = TShock.Players[e.Who];
            if (!IrcClient.IsConnected)
                Connect();
            else if (e.Text != null && !e.Text.StartsWith(Commands.Specifier) && !e.Text.StartsWith(Commands.SilentSpecifier) && tsPlr != null &&
                !tsPlr.mute && tsPlr.Group.HasPermission(Permissions.canchat) && !String.IsNullOrEmpty(Config.ServerChatMessageFormat) &&
                !Config.IgnoredServerChatRegexes.Any(s => Regex.IsMatch(e.Text, s)))
            {
                //SendMessage(Config.Channel, String.Format(Config.ServerChatMessageFormat, tsPlr.Group.Prefix, tsPlr.Name, e.Text, tsPlr.Group.Suffix, Main.worldName));
                 SendToListeningChannels(tsPlr, (IsSending(tsPlr, "transparently")?e.Text:String.Format(Config.ServerChatMessageFormat, tsPlr.Group.Prefix, tsPlr.Name, e.Text, tsPlr.Group.Suffix, Main.worldName) ));
            }
        }
        void OnGreetPlayer(GreetPlayerEventArgs e)
        {
            if (!IrcClient.IsConnected)
                Connect();
            else
            {
                TSPlayer tsplr = TShock.Players[e.Who];
                if (!String.IsNullOrEmpty(Config.ServerJoinMessageFormat) && !IsSending(tsplr, "transparently"))
                    SendToListeningChannels(tsplr, String.Format(Config.ServerJoinMessageFormat, tsplr.Name, "", "", "", Main.worldName));
                if (!String.IsNullOrEmpty(Config.ServerJoinAdminMessageFormat))
                    SendMessage(Config.AdminChannel, String.Format(Config.ServerJoinAdminMessageFormat, tsplr.Name, tsplr.IP, "", "", Main.worldName));
                SendChanInfo(tsplr, "");
            }
        }

        public bool GetChannelProperty(TSPlayer player, string channel, string property)
        {
            string chans = db.GetUserData(player, property);
            if( chans == null )
            {
                chans = String.Join(" ", Config.Channels);
                db.SetUserData(player, new String[] { chans, "" }); // Initialize listening to everything but not sending
            }
            return chans.Split(' ').Contains(channel, StringComparer.OrdinalIgnoreCase);
        }

        public void SetChannelProperty(TSPlayer player, string channel, string property, bool set_to)
        {
            if (GetChannelProperty(player, channel, property) != set_to)
            {
                if (set_to)
                {
                    if (db.GetUserData(player, property).Length < 2)
                    {
                        db.SetUserData(player, property, channel);
                    }
                    else
                    {
                        db.SetUserData(player, property, db.GetUserData(player, property) + " " + channel);
                    }
                }
                else
                {
                    if (db.GetUserData(player, property) == channel)
                    {
                        db.SetUserData(player, property, "");
                    }
                    else
                    {
                        string chans = db.GetUserData(player, property);
                        if (chans == null)
                        {
                            chans = String.Join(" ", Config.Channels);
                            db.SetUserData(player, new List<string> { chans, chans });
                        }
                        //return chans.Split(' ').Contains(channel, StringComparer.OrdinalIgnoreCase);
                        db.SetUserData(player, property, chans.Replace(channel, "").Replace("  ", " ").Trim());
                        if( property == "listening" && !set_to )
                        {
                            SetSending(player, channel, false);
                        }
                        else if( property == "sending" && set_to )
                        {
                            SetListening(player, channel, true);
                        }
                    }
                }
            }
        }

        public bool ToggleChannelProperty(TSPlayer player, string channel, string property)
        {
            bool to = (!GetChannelProperty(player, channel, property));
            SetChannelProperty(player, channel, property, to);
            return to;
        }

        public bool ToggleListening(TSPlayer player, string channel)
        {
            return ToggleChannelProperty(player, channel, "listening");
        }

        public bool ToggleSending(TSPlayer player, string channel)
        {
            return ToggleChannelProperty(player, channel, "sending");
        }

        public bool IsListening(TSPlayer player, string channel)
        {
            return GetChannelProperty(player, channel, "listening");
        }

        public void SetListening(TSPlayer player, string channel, bool set_to)
        {
            SetChannelProperty(player, channel, "listening", set_to);
        }

        public bool IsSending(TSPlayer player, string channel)
        {
            return GetChannelProperty(player, channel, "sending");
        }

        public void SetSending(TSPlayer player, string channel, bool set_to)
        {
            SetChannelProperty(player, channel, "sending", set_to);
        }

        public void SendToListeningChannels(TSPlayer player, string msg)
        {
            LastOutgoing = msg;
            foreach ( string chan in Config.Channels )
            {
                if (IsSending(player, chan))
                {
                    SendMessage(chan, msg);
                    BroadcastToListeners(chan, msg, Color.White, player.User.ID);
                }
            }
        }

        void OnInitialize(EventArgs e)
		{
			IRCCommands.Initialize();
            Commands.ChatCommands.Add(new Command("tshockirc.listen", IRC, "irc"));
			//Commands.ChatCommands.Add(new Command("tshockirc.manage", IRCReload, "ircreload"));
			//Commands.ChatCommands.Add(new Command("tshockirc.manage", IRCRestart, "ircrestart"));

			string configPath = Path.Combine(TShock.SavePath, "tshockircconfig.json");
			(Config = Config.Read(configPath)).Write(configPath);

            db.Connect("tshockircplayers", new string[]{ "listening", "sending" } );
		}

		void OnPostInitialize(EventArgs e)
		{
			Connect();
		}
		void OnLeave(LeaveEventArgs e)
		{
			TSPlayer tsplr = TShock.Players[e.Who];
			if (!IrcClient.IsConnected)
				Connect();
			else if (tsplr != null && tsplr.ReceivedInfo && tsplr.State >= 3 && !tsplr.SilentKickInProgress)
			{
				if (!String.IsNullOrEmpty(Config.ServerLeaveMessageFormat) && ! IsSending( tsplr, "transparently" ) )
                    SendToListeningChannels(tsplr, String.Format(Config.ServerLeaveMessageFormat, tsplr.Name, "", "", "", Main.worldName));
				if (!String.IsNullOrEmpty(Config.ServerLeaveAdminMessageFormat))
					SendMessage(Config.AdminChannel, String.Format(Config.ServerLeaveAdminMessageFormat, tsplr.Name, tsplr.IP, "", "", Main.worldName));
			}
		}
		void OnPlayerCommand(PlayerCommandEventArgs e)
		{
			if (!IrcClient.IsConnected)
				Connect();
			else if (e.Player.RealPlayer)
			{
				if (String.Equals(e.CommandName, "me", StringComparison.CurrentCultureIgnoreCase) && e.CommandText.Length > 2)
				{
					if (!e.Player.mute && e.Player.Group.HasPermission(Permissions.cantalkinthird) && !String.IsNullOrEmpty(Config.ServerActionMessageFormat) && !IsListening(e.Player, "transparently"))
                        SendToListeningChannels(e.Player, String.Format(Config.ServerActionMessageFormat, e.Player.Name, e.CommandText.Substring(3), "", "", Main.worldName));
				}
				else if (e.CommandList.Count() == 0 || e.CommandList.First().DoLog)
				{
					if (!String.IsNullOrEmpty(Config.ServerCommandMessageFormat))
						SendMessage(Config.AdminChannel, String.Format(Config.ServerCommandMessageFormat, e.Player.Group.Prefix, e.Player.Name, e.CommandText, e.Player.Group.Suffix, Main.worldName));
				}
			}
		}
		void OnPostLogin(PlayerPostLoginEventArgs e)
		{
			if (!IrcClient.IsConnected)
				Connect();
			else if (!String.IsNullOrEmpty(Config.ServerLoginAdminMessageFormat))
				SendMessage(Config.AdminChannel, String.Format(Config.ServerLoginAdminMessageFormat, e.Player.Name, e.Player.User.Name, e.Player.IP, "", Main.worldName));
		}

		#region Commands
		void IRCReload(CommandArgs e)
		{
            string configPath = Path.Combine(TShock.SavePath, "tshockircconfig.json");
			(Config = Config.Read(configPath)).Write(configPath);
			e.Player.SendSuccessMessage("Reloaded IRC config!");
		}
		void IRCRestart(CommandArgs e)
		{
            IrcClient.Quit("Restarting...");
			IrcUsers.Clear();

            IrcClient = new IrcClient();
			//IrcClient.Connect( (String)Config.Server, Config.Port, Config.SSL,
            IrcClient.Connect((String)Config.Server, Config.Port, false,
                new IrcUserRegistrationInfo()
				{
					NickName = Config.Nick,
					RealName = Config.RealName,
					UserName = Config.UserName,
                    Password = Config.Password,
					UserModes = new List<char> { 'i', 'w' }
				});
			IrcClient.Registered += OnIRCRegistered;
			CtcpClient = new CtcpClient(IrcClient) { ClientVersion = "TShockIRC v" + Version };

			e.Player.SendInfoMessage("Restarted the IRC bot.");
		}

        public bool HasAnyPermission(TSPlayer p, string[] perms)
        {
            foreach (string s in perms)
            {
                if (p.HasPermission(s))
                    return true;
            }
            return false;
        }

        public bool HasAllPermissions(TSPlayer p, string[] perms)
        {
            foreach (string s in perms)
            {
                if (!p.HasPermission(s))
                    return false;
            }
            return true;
        }

        void SendIfAnyPerm( TSPlayer p, string[] perms, string msg )
        {
            if( !String.IsNullOrWhiteSpace(msg) && HasAnyPermission(p, perms) )
            {
                p.SendInfoMessage(msg);
            }
        }

        void SendIfPerm(TSPlayer p, string perm, string msg)
        {
            if (!String.IsNullOrWhiteSpace(msg) && p.HasPermission(perm))
            {
                p.SendInfoMessage(msg);
            }
        }

        void SendChanInfo(TSPlayer p, string filter)
        {
            string msg = "";

            foreach (string chan in Config.Channels)
            {
                if (filter == "" || chan.IndexOf(filter) >= 0)
                {
                    if (IsSending(p, chan))
                    {
                        msg += ", " + chan + " (chatting)";
                    }
                    else if (IsListening(p, chan))
                    {
                        msg += ", " + chan + " (listening)";
                    }
                    else
                    {
                        msg += ", " + chan + " (ignoring)";
                    }
                }
            }
            if (msg == "")
            {
                p.SendInfoMessage("No " + (filter != "" ? "matching " : "") + "channels found.");
            }
            else
            {
                p.SendInfoMessage("Channels: " + msg.Substring(2));
            }
        }

        void IRC(CommandArgs e)
        {
            List<string> param = e.Parameters;
            if (param.Count() < 1)
            {
                SendIfPerm(e.Player, "tshockirc.manage", "/irc reload: Reload the IRC config.");
                SendIfPerm(e.Player, "tshockirc.manage", "/irc restart: Restart the IRC bot.");
                SendIfAnyPerm(e.Player, new string[] { "tshockirc.listen", "tshockirc.send"}, "/irc channels: List the available IRC channels.");
                SendIfPerm(e.Player, "tshockirc.listen", "/irc listen #channel: Toggle listing to a channel.");
                SendIfPerm(e.Player, "tshockirc.listen", "/irc ignore #channel: Stop listening to a channel.");
                SendIfPerm(e.Player, "tshockirc.send", "/irc chat #channel: Toggle echoing your chatter to a channel.");
                SendIfPerm(e.Player, "tshockirc.send", "/irc send #channel <msg>: Send a single message to a channel.");
                SendIfPerm(e.Player, "tshockirc.transparent", "/irc trans: Toggle transparent message sending (no world/name).");
                return;
            }

            switch( param[0].ToLowerInvariant() )
            {
                case "reload":
                    if( e.Player.HasPermission("tshockirc.manage") )
                    {
                        IRCReload(e);
                    }
                    break;
                case "restart":
                    if( e.Player.HasPermission("tshockirc.manage") )
                    {
                        IRCRestart(e);
                    }
                    break;
                case "list":
                case "channels":
                    if( HasAnyPermission(e.Player, new string[] { "tshockirc.listen", "tshockirc.send" } ) )
                    {
                        SendChanInfo(e.Player, (param.Count>=2?param[2]:"") );
                    }
                    break;
                case "listen":
                    if( e.Player.HasPermission("tshockirc.listen") ) {
                        if( param.Count != 2 )
                        {
                            e.Player.SendInfoMessage("/irc listen #channel: Toggle listening to a given channel.");
                        }
                        else if( ! Config.Channels.Contains(param[1], StringComparer.InvariantCultureIgnoreCase ) )
                        {
                            e.Player.SendInfoMessage("Channel not found: " + param[1]);
                        }
                        else
                        {
                            e.Player.SendInfoMessage("You are " + (ToggleListening(e.Player, param[1])?"now":"no longer") + " listening to " + param[1] + ".");
                        }
                    }
                    break;
                case "ignore":
                    if (e.Player.HasPermission("tshockirc.listen") ) {
                        if ( param.Count != 2 )
                        {
                            e.Player.SendInfoMessage("/irc ignore #channel: Stop listening to a given channel.");
                        }
                        else if (!Config.Channels.Contains(param[1], StringComparer.InvariantCultureIgnoreCase))
                        {
                            e.Player.SendInfoMessage("Channel not found: " + param[1]);
                        }
                        else
                        {
                            SetListening(e.Player, param[1], false);
                            e.Player.SendInfoMessage("You are no longer listening to " + param[1] + ".");
                        }
                    }
                    break;
                case "chat":
                    if (e.Player.HasPermission("tshockirc.send") ) {
                        if ( param.Count != 2 )
                        {
                            e.Player.SendInfoMessage("/irc chat #channel: Toggle sending your chats to a given channel.");
                        }
                        else if (!Config.Channels.Contains(param[1], StringComparer.InvariantCultureIgnoreCase))
                        {
                            e.Player.SendInfoMessage("Channel not found: " + param[1]);
                        }
                        else
                        {
                            e.Player.SendInfoMessage("Your chats are " + (ToggleSending(e.Player, param[1])?"now":"no longer") + " echoed on " + param[1] + ".");
                        }
                    }
                    break;
                case "send":
                    if (e.Player.HasPermission("tshockirc.send"))
                    {
                        if ( param.Count < 3 )
                        {
                            e.Player.SendInfoMessage("/irc send #channel <msg>: Send a message to a particular channel.");
                        }
                        else if (!Config.Channels.Contains(param[1], StringComparer.InvariantCultureIgnoreCase))
                        {
                            e.Player.SendInfoMessage("Channel not found: " + param[1]);
                        }
                        else
                        {
                            string msg = "";
                            foreach( string s in param.Skip(2) )
                            {
                                msg += " " + s;
                            }
                            SendMessage( param[1], String.Format(Config.ServerChatMessageFormat, e.Player.Group.Prefix, e.Player.Name, msg.Substring(1), e.Player.Group.Suffix, Main.worldName));
                        }

                    }
                    break;
                case "trans":
                    if (e.Player.HasPermission("tshockirc.transparent"))
                    {
                        e.Player.SendInfoMessage("You are " + (ToggleSending(e.Player, "transparently")? "now" : "no longer") + " chatting transparently.");
                    }
                    break;
            }

        }
        #endregion

        #region IRC client events
        void BroadcastToListeners(string channel, string msg, Color col, int skipuid = -1)
        {
            foreach (TSPlayer player in TShock.Players)
            {
                if (player != null && player.User != null)
                {
                    int uid = player.User.ID;
                    if (uid != skipuid && IsListening(player, channel))
                    {
                        player.SendMessage(msg.Replace("#channel",channel), col);
                    }
                }
            }
        }

        void BroadcastToListeners(string channel, string msg, byte red, byte green, byte blue, int skipuid = -1)
        {
            foreach (TSPlayer player in TShock.Players)
            {
                int uid = player.User.ID;
                if (uid != skipuid && IsListening(player, channel))
                {
                    player.SendMessage(msg.Replace("#channel",channel), red, green, blue);
                }
            }
        }

        void OnIRCDisconnected(object sender, EventArgs e)
		{
            Connect();
		}
		void OnIRCRegistered(object sender, EventArgs e)
		{
            Connecting = false;
			foreach (string command in Config.ConnectCommands)
				IrcClient.SendRawMessage(command);
            /*
			IrcClient.Channels.Join(new List<Tuple<string, string>>
			{
				Tuple.Create(Config.Channel, Config.ChannelKey),
				Tuple.Create(Config.AdminChannel, Config.AdminChannelKey)
			});
            */
            List<Tuple<string, string>> cj = new List<Tuple<string, string>> { Tuple.Create(Config.AdminChannel, Config.AdminChannelKey) };
            foreach( string chan in Config.Channels )
            {
                cj.Add(Tuple.Create(chan, Config.ChannelKey));
            }
            IrcClient.Channels.Join(cj);

            IrcClient.LocalUser.JoinedChannel += OnIRCJoinedChannel;
			IrcClient.LocalUser.MessageReceived += OnIRCMessageReceived;
		}
		void OnIRCJoinedChannel(object sender, IrcChannelEventArgs e)
		{
            // Avoid calling events multiple times by attempting to remove any existing hooks first.
            // This is important if the client tries to join the channel when the server already has it joined, as can happen in ZNC.
            e.Channel.MessageReceived -= OnChannelMessage;
            e.Channel.UserJoined -= OnChannelJoined;
            e.Channel.UserKicked -= OnChannelKicked;
            e.Channel.UserLeft -= OnChannelLeft;
            e.Channel.UsersListReceived -= OnChannelUsersList;
            e.Channel.MessageReceived += OnChannelMessage;
			e.Channel.UserJoined += OnChannelJoined;
			e.Channel.UserKicked += OnChannelKicked;
			e.Channel.UserLeft += OnChannelLeft;
			e.Channel.UsersListReceived += OnChannelUsersList;
		}
		void OnIRCMessageReceived(object sender, IrcMessageEventArgs e)
		{
            // Add an exception for any messages starting with "\u00035" to avoid infinite loopback when sending commands from the bot's own account.
            // Yes, I added it in two places. I really hate loops.
            if (!e.Text.StartsWith("\u00035"))
            {
                IRCCommands.Execute(e.Text, (IrcUser)e.Source, (IIrcMessageTarget)e.Source);
            }
		}
		
		void OnChannelJoined(object sender, IrcChannelUserEventArgs e)
		{
            if (Config.IgnoredIRCNicks.Contains(e.ChannelUser.User.NickName))
				return;

			//if (String.Equals(e.ChannelUser.Channel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
            if( Config.Channels.Contains( e.ChannelUser.Channel.Name, StringComparer.OrdinalIgnoreCase ) )
			{
				if (!IrcUsers.ContainsKey(e.ChannelUser.User))
					IrcUsers.Add(e.ChannelUser.User, TShock.Groups.GetGroupByName(TShock.Config.DefaultGuestGroupName));
				e.ChannelUser.User.Quit += OnUserQuit;

                if (!String.IsNullOrEmpty(Config.IRCJoinMessageFormat))
                    BroadcastToListeners(e.ChannelUser.Channel.Name, String.Format(Config.IRCJoinMessageFormat, e.ChannelUser.User.NickName, e.ChannelUser.Channel.Name), Color.Yellow);
                    //TShock.Utils.Broadcast(String.Format(Config.IRCJoinMessageFormat, e.ChannelUser.User.NickName), Color.Yellow);
			}
		}
		void OnChannelKicked(object sender, IrcChannelUserEventArgs e)
		{
            if (Config.IgnoredIRCNicks.Contains(e.ChannelUser.User.NickName))
				return;

			//if (String.Equals(e.ChannelUser.Channel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
            if( Config.Channels.Contains( e.ChannelUser.Channel.Name, StringComparer.OrdinalIgnoreCase ))
			{
				IrcUsers.Remove(e.ChannelUser.User);
                if (!String.IsNullOrEmpty(Config.IRCKickMessageFormat))
                    BroadcastToListeners(e.ChannelUser.Channel.Name, String.Format(Config.IRCKickMessageFormat, e.ChannelUser.User.NickName, e.Comment, e.ChannelUser.Channel.Name), Color.Green);
					//TShock.Utils.Broadcast(String.Format(Config.IRCKickMessageFormat, e.ChannelUser.User.NickName, e.Comment), Color.Green);
			}
		}
		void OnChannelLeft(object sender, IrcChannelUserEventArgs e)
		{
            if (Config.IgnoredIRCNicks.Contains(e.ChannelUser.User.NickName))
				return;

			//if (String.Equals(e.ChannelUser.Channel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
            if( Config.Channels.Contains(e.ChannelUser.Channel.Name, StringComparer.OrdinalIgnoreCase) )
			{
				IrcUsers.Remove(e.ChannelUser.User);
                if (!String.IsNullOrEmpty(Config.IRCLeaveMessageFormat))
                    BroadcastToListeners(e.ChannelUser.Channel.Name, String.Format(Config.IRCLeaveMessageFormat, e.ChannelUser.User.NickName, e.Comment, e.ChannelUser.Channel.Name), Color.Yellow);
					//TShock.Utils.Broadcast(String.Format(Config.IRCLeaveMessageFormat, e.ChannelUser.User.NickName, e.Comment), Color.Yellow);
			}
		}
		void OnChannelMessage(object sender, IrcMessageEventArgs e)
		{
            if (Config.IgnoredIRCNicks.Contains(((IrcUser)e.Source).NickName) ||
				Config.IgnoredIRCChatRegexes.Any(s => Regex.IsMatch(e.Text, s)))
				return;

            if (String.Equals(e.Text, LastOutgoing)) // This was our last outgoing message bounced back to us.
                return;

			var ircChannel = ((IrcChannel)e.Targets[0]);
			var ircUser = (IrcUser)e.Source;

            // It was responding to bot commands on all joined channels, which also was problematic with multiple
            // logins to the same account using ZNC connecting to Twitch. Rearranged so it only listens to the chat
            // and admin channels, period.
            if (String.Equals(ircChannel.Name, Config.AdminChannel, StringComparison.OrdinalIgnoreCase))
            {
                IRCCommands.Execute(e.Text, ircUser, (IIrcMessageTarget)sender);
            }
            else if (Config.Channels.Contains(ircChannel.Name, StringComparer.OrdinalIgnoreCase))
			{
                if (e.Text.StartsWith(Config.BotPrefix))
                {
                    IRCCommands.Execute(e.Text.Substring(Config.BotPrefix.Length), ircUser, (IIrcMessageTarget)sender);
                    return;
                }
                IrcChannelUser ircChannelUser = ircChannel.GetChannelUser(ircUser);
				if (!String.IsNullOrEmpty(Config.IRCChatModesRequired) && ircChannelUser != null &&
					!ircChannelUser.Modes.Intersect(Config.IRCChatModesRequired).Any())
				{
					return;
				}

				string text = e.Text;
				text = Regex.Replace(text, "\u0003[0-9]{1,2}(,[0-9]{1,2})?", "");
				text = text.Replace("\u0002", "");
				text = text.Replace("\u000f", "");
				text = text.Replace("\u001d", "");
				text = text.Replace("\u001f", "");

				if (text.StartsWith("\u0001ACTION") && text.EndsWith("\u0001"))
				{
                    if (!String.IsNullOrEmpty(Config.IRCActionMessageFormat))
                        BroadcastToListeners(ircChannel.Name, String.Format(Config.IRCActionMessageFormat, e.Source.Name, text.Substring(8, text.Length - 9)), 205, 133, 63);
                        //TShock.Utils.Broadcast(String.Format(Config.IRCActionMessageFormat, e.Source.Name, text.Substring(8, text.Length - 9)), 205, 133, 63);
                }
                else
				{
					if (!String.IsNullOrEmpty(Config.IRCChatMessageFormat))
					{

                        if (Config.BotIRCNicks.Contains(e.Source.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            // Get rid of formatting digits
                            if( text.Substring(0,2).All( Char.IsDigit ) )
                            {
                                text = text.Substring(2);
                            }
                            BroadcastToListeners(ircChannel.Name, String.Format(Config.IRCChatFromBotFormat, "", e.Source.Name, text), Color.White);
                            //TShock.Utils.Broadcast(String.Format(Config.IRCChatFromBotFormat, "", e.Source.Name, text), Color.White );
                        }
                        // IrcUsers may or may not actually know the user and have the key.
                        // Presumably this used to be fine returning null, but now it crashes.
                        else if (IrcUsers.ContainsKey(ircUser))
                        {
                            Group group = IrcUsers[ircUser];
                            BroadcastToListeners(ircChannel.Name, String.Format(Config.IRCChatMessageFormat, group.Prefix, e.Source.Name, text, group.Suffix), group.R, group.G, group.B);
                            //TShock.Utils.Broadcast(String.Format(Config.IRCChatMessageFormat, group.Prefix, e.Source.Name, text, group.Suffix), group.R, group.G, group.B);
                        }
                        else
                        {
                            BroadcastToListeners(ircChannel.Name, String.Format(Config.IRCChatMessageFormat, "", e.Source.Name, text), Color.White);
                            //TShock.Utils.Broadcast(String.Format(Config.IRCChatMessageFormat, "", e.Source.Name, text), Color.White);
                        }
                    }
				}
			}
		}
		void OnChannelUsersList(object sender, EventArgs e)
		{
            var ircChannel = (IrcChannel)sender;
            if( Config.Channels.Contains( ircChannel.Name, StringComparer.InvariantCultureIgnoreCase ) )
			//if (String.Equals(ircChannel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
			{
				foreach (IrcChannelUser ircChannelUser in ircChannel.Users.Where(icu => !Config.IgnoredIRCNicks.Contains(icu.User.NickName)))
				{
					if (!IrcUsers.ContainsKey(ircChannelUser.User))
						IrcUsers.Add(ircChannelUser.User, TShock.Groups.GetGroupByName(TShock.Config.DefaultGuestGroupName));
					ircChannelUser.User.Quit += OnUserQuit;
				}
			}
		}

		void OnUserQuit(object sender, IrcCommentEventArgs e)
		{
            var ircUser = (IrcUser)sender;
			IrcUsers.Remove(ircUser);

            if (!String.IsNullOrEmpty(Config.IRCQuitMessageFormat))
				TShock.Utils.Broadcast(String.Format(Config.IRCQuitMessageFormat, ircUser.NickName, e.Comment), Color.Yellow);
                //Appears to be no way to get what channel(s) they were in...?
		}
		#endregion

		public static void SendMessage(IIrcMessageTarget target, string msg)
		{
            msg = msg.Replace("\0", "");
			msg = msg.Replace("\r", "");
			msg = msg.Replace("\n", "");

			var sb = new StringBuilder();
			foreach (string word in msg.Split(' '))
			{
				if (sb.Length + word.Length + 1 > 400)
				{
					IrcClient.LocalUser.SendMessage(target, sb.ToString());
					sb.Clear();
				}
				else
					sb.Append(word).Append(" ");
			}
			IrcClient.LocalUser.SendMessage(target, sb.ToString());
		}
		public static void SendMessage(string target, string msg)
		{
			msg = msg.Replace("\0", "");
			msg = msg.Replace("\r", "");
			msg = msg.Replace("\n", "");

			var sb = new StringBuilder();
			foreach (string word in msg.Split(' '))
			{
				if (sb.Length + word.Length + 1 > 400)
				{
					IrcClient.LocalUser.SendMessage(target, sb.ToString());
					sb.Clear();
				}
				else
					sb.Append(word).Append(" ");
			}
			IrcClient.LocalUser.SendMessage(target, sb.ToString());
		}
	}
}
