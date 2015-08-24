using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IrcDotNet;
using Terraria;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace TShockIRC
{
	public static class IRCCommands
	{
		static List<IRCCommand> Commands = new List<IRCCommand>();

		public static void Execute(string str, IrcUser sender, IIrcMessageTarget target)
		{
			var args = new IRCCommandEventArgs(str, sender, target);

			string commandName = args[-1].ToLowerInvariant();
			if (TShockIRC.Config.IgnoredCommands.Contains(commandName))
				return;

			var ircCommand = Commands.Find(c => c.Names.Contains(commandName));
			User user = TShockIRC.IrcUsers[sender];
			Group senderGroup = user == null
				? TShock.Groups.GetGroupByName(TShock.Config.DefaultGuestGroupName)
				: TShock.Groups.GetGroupByName(user.Group);
			if (ircCommand != null)
			{
				if (String.IsNullOrEmpty(ircCommand.Permission) || senderGroup.HasPermission(ircCommand.Permission))
				{
					if (ircCommand.DoLog)
						TShock.Log.Info("{0} executed: /{1}.", sender.NickName, str);
					ircCommand.Execute(args);
				}
				else
				{
					TShock.Log.Warn("{0} tried to execute /{1}.", sender.NickName, str);
					TShockIRC.SendMessage(target, "\u00035You do not have access to this command.");
				}
			}
			else if (senderGroup.HasPermission("tshockirc.command"))
			{
				var tsIrcPlayer = new TSIrcPlayer(sender.NickName, senderGroup, target);
				var commands = TShockAPI.Commands.ChatCommands.Where(c => c.HasAlias(commandName));

				if (commands.Count() != 0)
				{
					Main.rand = new Random();
					WorldGen.genRand = new Random();
					foreach (Command command in commands)
					{
						if (!command.CanRun(tsIrcPlayer))
						{
							TShock.Log.Warn("{0} tried to execute /{1}.", sender.NickName, str);
							TShockIRC.SendMessage(target, "\u00035You do not have access to this command.");
						}
						else if (!command.AllowServer)
							TShockIRC.SendMessage(target, "\u00035You must use this command in-game.");
						else
						{
							var parms = args.ParameterRange(0, args.Length);
							if (PlayerHooks.OnPlayerCommand(tsIrcPlayer, command.Name, str, parms, ref commands, TShockAPI.Commands.Specifier))
								return;
							if (command.DoLog)
								TShock.Log.Info("{0} executed: /{1}.", sender.NickName, str);
							command.Run(str, tsIrcPlayer, parms);
						}
					}
				}
				else
					TShockIRC.SendMessage(target, "\u00035Invalid command.");
			}
			else
			{
				TShock.Log.Warn("{0} tried to execute /{1}.", sender.NickName, str);
				TShockIRC.SendMessage(target, "\u00035You do not have access to this command.");
			}
		}
		public static void Initialize()
		{
			Commands.Add(new IRCCommand("", Login, "login") { DoLog = false });
			Commands.Add(new IRCCommand("", Logout, "logout"));
			Commands.Add(new IRCCommand("", Players, "online", "players", "who"));
		}
		public static List<string> ParseParameters(string text)
		{
			var parameters = new List<string>();
			var sb = new StringBuilder();

			bool quote = false;
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];

				if (c == '\\' && ++i < text.Length)
				{
					if (text[i] != '"' && text[i] != ' ' && text[i] != '\\')
						sb.Append('\\');
					sb.Append(text[i]);
				}
				else if (c == '"')
				{
					quote = !quote;
					if (!quote || sb.Length > 0)
					{
						parameters.Add(sb.ToString());
						sb.Clear();
					}
				}
				else if (Char.IsWhiteSpace(c) && !quote)
				{
					if (sb.Length > 0)
					{
						parameters.Add(sb.ToString());
						sb.Clear();
					}
				}
				else
					sb.Append(c);
			}
			if (sb.Length > 0 || parameters.Count == 0)
				parameters.Add(sb.ToString());
			return parameters;
		}

		static void Login(object sender, IRCCommandEventArgs e)
		{
			if (e.Length != 2)
			{
				TShockIRC.SendMessage(e.Target, "\u00035Invalid syntax! Proper syntax: " + TShockIRC.Config.BotPrefix + e[-1] + " <user> <password>");
				return;
			}

			User user = TShock.Users.GetUserByName(e[0]);
			if (user == null || e[0] == "")
				TShockIRC.SendMessage(e.Target, "\u00035Invalid user.");
			else
			{
				if (user.VerifyPassword(e[1]))
				{
					TShockIRC.SendMessage(e.Target, "\u00033You have logged in as " + user.Name + ".");
					TShockIRC.IrcUsers[e.Sender] = user;
				}
				else
					TShockIRC.SendMessage(e.Target, "\u00035Incorrect password!");
			}
		}
		static void Logout(object sender, IRCCommandEventArgs e)
		{
			TShockIRC.IrcUsers[e.Sender] = null;
			TShockIRC.SendMessage(e.Target, "\u00033You have logged out.");
		}
		static void Players(object sender, IRCCommandEventArgs e)
		{
			int numPlayers = TShock.Players.Where(p => p != null && p.Active).Count();
			string players = String.Join(", ", TShock.Players.Where(p => p != null && p.Active).Select(p => p.Name));
			if (numPlayers == 0)
				TShockIRC.SendMessage(e.Target, "0 players currently on.");
			else
			{
				TShockIRC.SendMessage(e.Target, numPlayers + " player(s) currently on:");
				TShockIRC.SendMessage(e.Target, players + ".");
			}
		}
	}
}
