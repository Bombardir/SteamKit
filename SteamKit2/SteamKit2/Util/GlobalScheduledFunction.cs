using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SteamKit2.Util;

internal class GlobalScheduledFunction
{
    public const int FunctionCycleTime = 1000;

    private readonly ConcurrentDictionary<ScheduledFunction, DateTime> _functionExecutionTimes;
    private readonly Task _scheduleTask;

    public GlobalScheduledFunction()
    {
        _functionExecutionTimes = new ConcurrentDictionary<ScheduledFunction, DateTime>( 2, 2000 );
        _scheduleTask = Task.Run( FunctionCycle );
    }

    public void Start( ScheduledFunction func )
    {
        _functionExecutionTimes.AddOrUpdate( func, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
    }

    public void Stop( ScheduledFunction func )
    {
        _functionExecutionTimes.TryRemove( func, out _ );
    }

    private async Task FunctionCycle()
    {
        while ( true )
        {
            var currentTime = DateTime.UtcNow;

            foreach ( ( ScheduledFunction func, DateTime lastExecutionTime ) in _functionExecutionTimes )
            {
                var timeSinceLastExecution = currentTime - lastExecutionTime;

                if ( timeSinceLastExecution < func.Delay )
                    continue;

                func.InvokeSafe();

                _functionExecutionTimes.TryUpdate( func, currentTime, lastExecutionTime );
            }

            await Task.Delay( FunctionCycleTime );
        }
    }
}
