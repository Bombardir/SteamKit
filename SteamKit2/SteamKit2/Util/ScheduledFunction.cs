using System;
using System.Threading;

namespace SteamKit2
{
    public class ScheduledFunction
    {
        public TimeSpan Delay { get; set; }
        readonly Action _func;
        readonly Timer _timer;

        public ScheduledFunction( Action func )
            : this( func, TimeSpan.FromMilliseconds( -1 ) )
        {
        }

        public ScheduledFunction( Action func, TimeSpan delay )
        {
            _func = func ?? throw new ArgumentException("Function can not be null", nameof(func) );
            Delay = delay;
            _timer = new Timer( Tick, null, TimeSpan.FromMilliseconds( -1 ), delay );
        }

        ~ScheduledFunction()
        {
            Stop();
        }

        public void Start()
        {
            _timer.Change( TimeSpan.Zero, Delay );
        }

        public void Stop()
        {
            _timer.Change( TimeSpan.FromMilliseconds( -1 ), Delay );
        }

        private void Tick( object state )
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
