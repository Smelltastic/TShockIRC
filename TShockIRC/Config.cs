﻿using System.IO;
using Newtonsoft.Json;

namespace TShockIRC
{
	public class Config
	{
		public string AdminChannel = "#admin";
		public string AdminChannelKey = "";
		public string Channel = "#terraria";
		public string ChannelKey = "";
		public string[] ConnectCommands = new string[] { "PRIVMSG NickServ :IDENTIFY password" };
		public string Nick = "TShock";
		public short Port = 6667;
		public string RealName = "TShock";
		public string Server = "localhost";
		public bool SSL = false;
		public string UserName = "TShock";
        // Discrete password option added. ConnectCommands don't seem to be sufficient because they
        // don't appear to send until after you're authenticated, with PASS auth anyhow.
        // Users need to be informed this is for PASS auth though, NOT NickServ.
        public string Password = "";

		public string BotPrefix = ".";
		public string[] IgnoredCommands = new string[] { };
		public string[] IgnoredIRCChatRegexes = new string[] { };
		public string[] IgnoredIRCNicks = new string[] { };
		public string[] IgnoredServerChatRegexes = new string[] { };

		public string IRCActionMessageFormat = "(IRC) * {0} {1}";
		public string IRCChatMessageFormat = "(IRC) {0}<{1}> {2}";
		public string IRCChatModesRequired = "";
		public string IRCJoinMessageFormat = "(IRC) {0} has joined.";
		public string IRCKickMessageFormat = "(IRC) {0} was kicked ({1}).";
		public string IRCLeaveMessageFormat = "(IRC) {0} has left ({1}).";
		public string IRCQuitMessageFormat = "(IRC) {0} has quit ({1}).";
		public string ServerActionMessageFormat = "\u000302\u0002* {0}\u000f {1}";
        // Failed attempt at echoing server broadcasts to IRC.
        // Leaving this here potentially for the future.
        //public string ServerBroadcastMessageFormat = "\u000302(Server Broadcast) {0}";
		public string ServerCommandMessageFormat = "\u000302{0}<{1}>\u000f executed /{2}";
		public string ServerChatMessageFormat = "\u000302{0}<{1}>\u000f {2}{3}";
		public string ServerJoinMessageFormat = "\u000303{0} has joined.";
		public string ServerJoinAdminMessageFormat = "\u000303{0} has joined. IP: {1}";
		public string ServerLeaveMessageFormat = "\u000305{0} has left.";
		public string ServerLeaveAdminMessageFormat = "\u000305{0} has left. IP: {1}";
		public string ServerLoginAdminMessageFormat = "\u000305{0} has logged in as {1}. IP: {2}";

		public void Write(string path)
		{
			File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
		}

		public static Config Read(string path)
		{
			if (!File.Exists(path))
				return new Config();
			return JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
		}
	}
}
