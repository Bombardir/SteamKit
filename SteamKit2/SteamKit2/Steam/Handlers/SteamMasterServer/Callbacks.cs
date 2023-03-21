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
                public uint MaxPlayers { get; }
                public uint? AuthPlayers { get; }
                public string? Name { get; }
                public string? Version { get; }
                public string? Map { get; }
                public string? GameDir { get; }
                public string? GameDesc { get; }
                public string? Tags { get; set; }

                public Server( IPAddress ip, uint queryPort, uint? authPlayers, uint maxPlayers, string? name, string? version, string? map, 
                    string? gameDir, string? gameDesc, string? tags )
                {
                    Ip = ip;
                    QueryPort = queryPort;
                    AuthPlayers = authPlayers;
                    MaxPlayers = maxPlayers;
                    Name = name;
                    Version = version;
                    Map = map;
                    GameDir = gameDir;
                    GameDesc = gameDesc;
                    Tags = tags;
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

                    string? version;
                    if ( !string.IsNullOrEmpty( serverResponse.version_str ) )
                        version = serverResponse.version_str;
                    else if ( serverResponse.ShouldSerializeversion_strindex() )
                        version = msg.server_strings[ ( int )serverResponse.version_strindex ];
                    else if ( serverResponse.ShouldSerializerevision() )
                        version = msg.default_server_data.version_str;
                    else
                        version = null;

                    string? map;
                    if ( !string.IsNullOrEmpty( serverResponse.map_str ) )
                        map = serverResponse.map_str;
                    else if ( serverResponse.ShouldSerializemap_strindex() )
                        map = msg.server_strings[ ( int )serverResponse.map_strindex ];
                    else if ( serverResponse.ShouldSerializerevision() )
                        map = msg.default_server_data.map_str;
                    else
                        map = null;

                    string? gamedir;
                    if ( !string.IsNullOrEmpty( serverResponse.gamedir_str ) )
                        gamedir = serverResponse.gamedir_str;
                    else if ( serverResponse.ShouldSerializegamedir_strindex() )
                        gamedir = msg.server_strings[ ( int )serverResponse.gamedir_strindex ];
                    else if ( serverResponse.ShouldSerializerevision() )
                        gamedir = msg.default_server_data.gamedir_str;
                    else
                        gamedir = null;

                    string? gamedesc;
                    if ( !string.IsNullOrEmpty( serverResponse.game_description_str ) )
                        gamedesc = serverResponse.game_description_str;
                    else if ( serverResponse.ShouldSerializegame_description_strindex() )
                        gamedesc = msg.server_strings[ ( int )serverResponse.game_description_strindex ];
                    else if ( serverResponse.ShouldSerializerevision() )
                        gamedesc = msg.default_server_data.game_description_str;
                    else
                        gamedesc = null;

                    string? tags;
                    if ( !string.IsNullOrEmpty( serverResponse.gametype_str ) )
                        tags = serverResponse.gametype_str;
                    else if ( serverResponse.ShouldSerializegametype_strindex() )
                        tags = msg.server_strings[ ( int )serverResponse.gametype_strindex ];
                    else if ( serverResponse.ShouldSerializerevision() )
                        tags = msg.default_server_data.gametype_str;
                    else
                        tags = null;

                    var ip = serverResponse.server_ip.GetIPAddress();
                    var queryPort = serverResponse.query_port;
                    uint? authPlayers = serverResponse.ShouldSerializeauth_players() ? serverResponse.auth_players : null;
                    var maxPlayers = serverResponse.max_players;

                    serverList[index] = new Server( ip, queryPort, authPlayers, maxPlayers, name, version, map, gamedir, gamedesc, tags );
                }

                this.Servers = serverList;
            }
        }
    }
}
