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

using Group = TShockAPI.Group;

namespace TShockIRC
{
	[ApiVersion(1, 24)]
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
		public static CtcpClient CtcpClient;
		public static IrcClient IrcClient = new IrcClient();
		public static Dictionary<IrcUser, Group> IrcUsers = new Dictionary<IrcUser, Group>();

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
            IrcClient.Connect( (String)Config.Server, Config.Port, Config.SSL,
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
            else if (e.Message != null && !String.IsNullOrEmpty(Config.ServerChatMessageFormat))
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
				SendMessage(Config.Channel, String.Format(Config.ServerChatMessageFormat, tsPlr.Group.Prefix, tsPlr.Name, e.Text, tsPlr.Group.Suffix, Main.worldName));
			}
		}
		void OnGreetPlayer(GreetPlayerEventArgs e)
		{
			if (!IrcClient.IsConnected)
				Connect();
			else
			{
				TSPlayer tsplr = TShock.Players[e.Who];
				if (!String.IsNullOrEmpty(Config.ServerJoinMessageFormat))
					SendMessage(Config.Channel, String.Format(Config.ServerJoinMessageFormat, tsplr.Name, "", "", "", Main.worldName));
				if (!String.IsNullOrEmpty(Config.ServerJoinAdminMessageFormat))
					SendMessage(Config.AdminChannel, String.Format(Config.ServerJoinAdminMessageFormat, tsplr.Name, tsplr.IP, "", "", Main.worldName));
			}
		}
		void OnInitialize(EventArgs e)
		{
			IRCCommands.Initialize();
			Commands.ChatCommands.Add(new Command("tshockirc.manage", IRCReload, "ircreload"));
			Commands.ChatCommands.Add(new Command("tshockirc.manage", IRCRestart, "ircrestart"));

			string configPath = Path.Combine(TShock.SavePath, "tshockircconfig.json");
			(Config = Config.Read(configPath)).Write(configPath);
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
				if (!String.IsNullOrEmpty(Config.ServerLeaveMessageFormat))
					SendMessage(Config.Channel, String.Format(Config.ServerLeaveMessageFormat, tsplr.Name, "", "", "", Main.worldName));
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
					if (!e.Player.mute && e.Player.Group.HasPermission(Permissions.cantalkinthird) && !String.IsNullOrEmpty(Config.ServerActionMessageFormat))
						SendMessage(Config.Channel, String.Format(Config.ServerActionMessageFormat, e.Player.Name, e.CommandText.Substring(3), "", "", Main.worldName));
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
			IrcClient.Connect( (String)Config.Server, Config.Port, Config.SSL,
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
        #endregion

        #region IRC client events
        void OnIRCConnected(object sender, EventArgs e)
        {
            
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
			IrcClient.Channels.Join(new List<Tuple<string, string>>
			{
				Tuple.Create(Config.Channel, Config.ChannelKey),
				Tuple.Create(Config.AdminChannel, Config.AdminChannelKey)
			});
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

			if (String.Equals(e.ChannelUser.Channel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
			{
				if (!IrcUsers.ContainsKey(e.ChannelUser.User))
					IrcUsers.Add(e.ChannelUser.User, TShock.Groups.GetGroupByName(TShock.Config.DefaultGuestGroupName));
				e.ChannelUser.User.Quit += OnUserQuit;

				if (!String.IsNullOrEmpty(Config.IRCJoinMessageFormat))
					TShock.Utils.Broadcast(String.Format(Config.IRCJoinMessageFormat, e.ChannelUser.User.NickName), Color.Yellow);
			}
		}
		void OnChannelKicked(object sender, IrcChannelUserEventArgs e)
		{
            if (Config.IgnoredIRCNicks.Contains(e.ChannelUser.User.NickName))
				return;

			if (String.Equals(e.ChannelUser.Channel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
			{
				IrcUsers.Remove(e.ChannelUser.User);
				if (!String.IsNullOrEmpty(Config.IRCKickMessageFormat))
					TShock.Utils.Broadcast(String.Format(Config.IRCKickMessageFormat, e.ChannelUser.User.NickName, e.Comment), Color.Green);
			}
		}
		void OnChannelLeft(object sender, IrcChannelUserEventArgs e)
		{
            if (Config.IgnoredIRCNicks.Contains(e.ChannelUser.User.NickName))
				return;

			if (String.Equals(e.ChannelUser.Channel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
			{
				IrcUsers.Remove(e.ChannelUser.User);
				if (!String.IsNullOrEmpty(Config.IRCLeaveMessageFormat))
					TShock.Utils.Broadcast(String.Format(Config.IRCLeaveMessageFormat, e.ChannelUser.User.NickName, e.Comment), Color.Yellow);
			}
		}
		void OnChannelMessage(object sender, IrcMessageEventArgs e)
		{
            if (Config.IgnoredIRCNicks.Contains(((IrcUser)e.Source).NickName) ||
				Config.IgnoredIRCChatRegexes.Any(s => Regex.IsMatch(e.Text, s)))
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
            else if (String.Equals(ircChannel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
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
						TShock.Utils.Broadcast(String.Format(Config.IRCActionMessageFormat, e.Source.Name, text.Substring(8, text.Length - 9)), 205, 133, 63);
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
                            TShock.Utils.Broadcast(String.Format(Config.IRCChatFromBotFormat, "", e.Source.Name, text), Color.White );
                        }
                        // IrcUsers may or may not actually know the user and have the key.
                        // Presumably this used to be fine returning null, but now it crashes.
                        else if (IrcUsers.ContainsKey(ircUser))
                        {
                            Group group = IrcUsers[ircUser];
                            TShock.Utils.Broadcast(String.Format(Config.IRCChatMessageFormat, group.Prefix, e.Source.Name, text, group.Suffix), group.R, group.G, group.B);
                        }
                        else
                        {
                            TShock.Utils.Broadcast(String.Format(Config.IRCChatMessageFormat, "", e.Source.Name, text), Color.White);
                        }
                    }
				}
			}
		}
		void OnChannelUsersList(object sender, EventArgs e)
		{
            var ircChannel = (IrcChannel)sender;
			if (String.Equals(ircChannel.Name, Config.Channel, StringComparison.OrdinalIgnoreCase))
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
