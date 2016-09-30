﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IrcDotNet
{
    /// <summary>
    /// Provides information used by an <see cref="IrcClient"/> for registering the connection as a service.
    /// </summary>
    /// <threadsafety static="true" instance="false"/>
    public class IrcServiceRegistrationInfo : IrcRegistrationInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IrcServiceRegistrationInfo"/> class.
        /// </summary>
        public IrcServiceRegistrationInfo()
            : base()
        {
        }

        /// <summary>
        /// Gets or sets the distribution of the service, which determines its visibility to users on specific servers.
        /// </summary>
        /// <value>A wildcard expression for matching against the names of servers on which the service should be
        /// visible.</value>
        public string Distribution
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the description of the service to set upon registration.
        /// The description cannot later be changed.
        /// </summary>
        /// <value>A description of the service.</value>
        public string Description
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Provides information used by an <see cref="IrcClient"/> for registering the connection as a user.
    /// </summary>
    /// <threadsafety static="true" instance="false"/>
    public class IrcUserRegistrationInfo : IrcRegistrationInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IrcUserRegistrationInfo"/> class.
        /// </summary>
        public IrcUserRegistrationInfo()
            : base()
        {
        }

        /// <summary>
        /// Gets or sets the user name of the local user to set upon registration.
        /// The user name cannot later be changed.
        /// </summary>
        /// <value>The user name of the local user.</value>
        public string UserName
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the real name of the local user to set upon registration.
        /// The real name cannot later be changed.
        /// </summary>
        /// <value>The real name of the local user.</value>
        public string RealName
        {
            get;
            set;
        }

        /// <summary>
        /// Whether our given info should override the welcome message from the server.
        /// </summary>
        /// <value>TRUE if we should ignore the server's welcome message declaring our name, false otherwise.</value>
        public bool IgnoreServerWelcomeInfo
        {
            get;
            set;
        } = false;

        /// <summary>
        /// Gets or sets the modes of the local user to set initially.
        /// The collection should not contain any characters except 'w' or 'i'.
        /// The modes can be changed after registration.
        /// </summary>
        /// <value>A collection of modes to set on the local user.</value>
        public ICollection<char> UserModes
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Provides information used by an <see cref="IrcClient"/> for registering the connection with the server.
    /// </summary>
    /// <threadsafety static="true" instance="false"/>
    public abstract class IrcRegistrationInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IrcRegistrationInfo"/> class.
        /// </summary>
        public IrcRegistrationInfo()
        {
        }

        /// <summary>
        /// Gets or sets the password for registering with the server.
        /// </summary>
        /// <value>The password for registering with the server.</value>
        public string Password
        {
            get;
            set;
        }

        /// <summary>
        /// Whether the password needs to be sent after the nick, instead of before.
        /// </summary>
        /// <value>False to send PASS immediately on connect, True to send it after the nick.</value>
        public bool PassAfterNick
        {
            get;
            set;
        } = false;


        /// <summary>
        /// Gets or sets the nick name of the local user to set initially upon registration.
        /// The nick name can be changed after registration.
        /// </summary>
        /// <value>The initial nick name of the local user.</value>
        public string NickName
        {
            get;
            set;
        }
    }
}
