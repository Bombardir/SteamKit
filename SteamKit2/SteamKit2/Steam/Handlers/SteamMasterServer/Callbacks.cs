/*
 * This file is subject to the terms and conditions defined in
 * file 'license.txt', which is part of this source code package.
 */

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using SteamKit2.Internal;

namespace SteamKit2
{
    public sealed partial class SteamMasterServer
    {
        /// <summary>
        /// This callback is received in response to calling <see cref="ServerQuery"/>.
        /// </summary>
        public sealed class QueryCallback : CallbackMsg
        {
            /// <summary>
            /// Represents a single server.
            /// </summary>
            public sealed class Server
            {
                public IPAddress Ip { get; }
                public uint QueryPort { get; }
                public uint AuthPlayers { get; }
                public uint MaxPlayers { get; }
                public string? Name { get; }

                internal Server( IPAddress ip, uint queryPort, uint authPlayers, uint maxPlayers, string? name )
                {
                    Ip = ip;
                    QueryPort = queryPort;
                    AuthPlayers = authPlayers;
                    MaxPlayers = maxPlayers;
                    Name = name;
                }
            }

            /// <summary>
            /// Gets the list of servers.
            /// </summary>
            public IReadOnlyList<Server> Servers { get; private set; }


            internal QueryCallback( JobID jobID, CMsgGMSClientServerQueryResponse msg )
            {
                JobID = jobID;

                var serverList = new Server[msg.servers.Count];

                for ( var index = 0; index < msg.servers.Count; index++ )
                {
                    var serverResponse = msg.servers[ index ];
                    string? name;

                    if ( !string.IsNullOrEmpty(serverResponse.name_str) )
                        name = serverResponse.name_str;
                    else if ( serverResponse.ShouldSerializename_strindex() )
                        name = msg.server_strings[ ( int )serverResponse.name_strindex ];
                    else if ( serverResponse.ShouldSerializerevision() )
                        name = msg.default_server_data.name_str;
                    else
                        name = null;

                    var ip = serverResponse.server_ip.GetIPAddress();
                    var queryPort = serverResponse.query_port;
                    var authPlayers = serverResponse.auth_players;
                    var maxPlayers = serverResponse.max_players;

                    serverList[index] = new Server( ip, queryPort, authPlayers, maxPlayers, name );
                }

                this.Servers = serverList;
            }
        }
    }
}
