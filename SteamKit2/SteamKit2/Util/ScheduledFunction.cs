using System;
using System.Threading;
using SteamKit2.Util;

namespace SteamKit2
{
    public class ScheduledFunction
    {
        private static GlobalScheduledFunction GlobalScheduledFunction = new();

        private long _delayTicks;

        public TimeSpan Delay
        {
            get
            {
                var ticks = Interlocked.Read( ref _delayTicks );
                return TimeSpan.FromTicks( ticks );
            }
            set
            {
                Interlocked.Exchange( ref _delayTicks, value.Ticks );
            }
        }

        readonly Action _func;

        public ScheduledFunction( Action func )
            : this( func, TimeSpan.FromMilliseconds( -1 ) )
        {
        }

        public ScheduledFunction( Action func, TimeSpan delay )
        {
            _func = func ?? throw new ArgumentException("Function can not be null", nameof(func) );
            Delay = delay;
        }

        ~ScheduledFunction()
        {
            Stop();
        }

        public void Start()
        {
            GlobalScheduledFunction.Start( this );
        }

        public void Stop()
        {
            GlobalScheduledFunction.Stop( this );
        }

        public void InvokeSafe()
        {
            try
            {
                _func.Invoke();
            }
            catch ( Exception ex )
            {
                Console.WriteLine( "Scheduled function error: {0}", ex );
            }
        }
    }
}
