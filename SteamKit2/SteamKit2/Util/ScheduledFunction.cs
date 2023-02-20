/*
 * This file is subject to the terms and conditions defined in
 * file 'license.txt', which is part of this source code package.
 */

using System;
using SteamKit2.Util;

namespace SteamKit2
{
    class ScheduledFunction
    {
        private static GlobalScheduledFunction GlobalScheduling = new();

        public TimeSpan Delay { get; set; }

        Action func;
        bool bStarted;

        public ScheduledFunction( Action func )
            : this( func, TimeSpan.FromMilliseconds( -1 ) )
        {
        }

        public ScheduledFunction( Action func, TimeSpan delay )
        {
            this.func = func;
            this.Delay = delay;
        }

        ~ScheduledFunction()
        {
            Stop();
        }

        public void Start()
        {
            if ( bStarted )
                return;

            GlobalScheduling.Start( this );
            bStarted = true;
        }

        public void Stop()
        {
            if ( !bStarted )
                return;

            GlobalScheduling.Stop( this );
            bStarted = false;
        }

        public void InvokeSafe()
        {
            try
            {
                func?.Invoke();
            }
            catch ( Exception ex )
            {
                Console.WriteLine( "Scheduled function error: {0}", ex );
            }
        }
    }
}
