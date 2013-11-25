﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IrcDotNet;
using TShockAPI;

namespace TShockIRC
{
	public class IRCCommandEventArgs : EventArgs
	{
		private string[] parameters;
		public int Length { get { return parameters.Length; } }
		public string RawText { get; private set; }
		public Group SenderGroup { get; private set; }
		public IIrcMessageSource Sender { get; private set; }
		public IIrcMessageTarget SendTo { get; private set; }

		public string this[int index] { get { return parameters[index]; } }

		public IRCCommandEventArgs(string text, IIrcMessageSource sender, Group senderGroup, IIrcMessageTarget sendTo)
		{
			parameters = IRCCommand.Parse(text).ToArray();
			RawText = text;
			Sender = sender;
			SenderGroup = senderGroup;
			SendTo = sendTo;
		}

		public string Eol(int index)
		{
			return String.Join(" ", parameters, index, parameters.Length - index);
		}
		public string[] ParameterRange(int index, int count)
		{
			return parameters.ToList().GetRange(index, count).ToArray();
		}
	}
}
