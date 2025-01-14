﻿using System;
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
        _functionExecutionTimes = new ConcurrentDictionary<ScheduledFunction, DateTime>( 2, 6100 );
        _scheduleTask = Task.Run( FunctionCycle );
    }

    public void Start( ScheduledFunction func )
    {
        var currentTime = DateTime.UtcNow;
        _functionExecutionTimes.AddOrUpdate( func, currentTime, ( _, _ ) => currentTime );
    }

    public void Stop( ScheduledFunction func )
    {
        _functionExecutionTimes.TryRemove( func, out _ );
    }

    internal bool IsEnabled( ScheduledFunction scheduledFunction ) => _functionExecutionTimes.ContainsKey(scheduledFunction);

    private async Task FunctionCycle()
    {
        while ( true )
        {
            var currentTime = DateTime.UtcNow;

            foreach ( (ScheduledFunction func, DateTime lastExecutionTime) in _functionExecutionTimes )
            {
                var timeSinceLastExecution = currentTime - lastExecutionTime;
                var delay = func.Delay;

                if ( delay <= TimeSpan.Zero || timeSinceLastExecution < delay )
                    continue;

                if (_functionExecutionTimes.TryUpdate( func, currentTime, lastExecutionTime ))
                    func.InvokeSafe();
            }

            await Task.Delay( FunctionCycleTime );
        }
    }
}
