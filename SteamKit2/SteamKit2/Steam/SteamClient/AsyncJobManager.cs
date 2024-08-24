using System;
using System.Collections.Generic;

namespace SteamKit2
{
    class AsyncJobManager
    {
        internal Dictionary<JobID, AsyncJob> asyncJobs;
        internal ScheduledFunction jobTimeoutFunc;

        public AsyncJobManager()
        {
            asyncJobs = new Dictionary<JobID, AsyncJob>(8);
            jobTimeoutFunc = new ScheduledFunction( CancelTimedoutJobs, TimeSpan.FromSeconds( 1 ) );
        }


        /// <summary>
        /// Tracks a job with this manager.
        /// </summary>
        /// <param name="asyncJob">The asynchronous job to track.</param>
        public void StartJob( AsyncJob asyncJob )
        {
            lock ( asyncJobs )
                asyncJobs[ asyncJob ] = asyncJob;
        }

        /// <summary>
        /// Passes a callback to a pending async job.
        /// If the given callback completes the job, the job is removed from this manager.
        /// </summary>
        /// <param name="jobId">The JobID.</param>
        /// <param name="callback">The callback.</param>
        public void TryCompleteJob( JobID jobId, CallbackMsg callback )
        {
            var asyncJob = GetJob( jobId );

            if ( asyncJob == null )
            {
                // not a job we are tracking ourselves, can ignore it
                return;
            }

            // pass this callback into the job so it can determine if the job is finished (in the case of multiple responses to a job)
            bool jobFinished = asyncJob.AddResult( callback );

            if ( jobFinished )
            {
                // if the job is finished, we can stop tracking it

                lock ( asyncJobs )
                    asyncJobs.Remove( jobId );
            }
        }

        /// <summary>
        /// Extends the lifetime of a job.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        public void HeartbeatJob( JobID jobId )
        {
            var asyncJob = GetJob( jobId );

            if ( asyncJob == null )
            {
                // ignore heartbeats for jobs we're not tracking
                return;
            }

            asyncJob.Heartbeat();
        }
        /// <summary>
        /// Marks a certain job as remotely failed.
        /// </summary>
        /// <param name="jobId">The job identifier.</param>
        public void FailJob( JobID jobId )
        {
            var asyncJob = GetJob( jobId, andRemove: true );

            if ( asyncJob == null )
            {
                // ignore remote failures for jobs we're not tracking
                return;
            }

            TryFailAsyncJob( asyncJob, dueToRemoteFailure: true );
        }

        /// <summary>
        /// Cancels and clears all jobs being tracked.
        /// </summary>
        public void CancelPendingJobs()
        {
            lock ( asyncJobs )
            {
                foreach ( AsyncJob asyncJob in asyncJobs.Values )
                {
                    TryFailAsyncJob( asyncJob, dueToRemoteFailure: false );
                }

                asyncJobs.Clear();
            }
        }

        private static void TryFailAsyncJob(AsyncJob asyncJob, bool dueToRemoteFailure)
        {
            try
            {
                asyncJob.SetFailed( dueToRemoteFailure );
            }
            catch ( Exception e )
            {
                // Empty
            }
        }


        /// <summary>
        /// Enables or disables periodic checks for job timeouts.
        /// </summary>
        /// <param name="enable">Whether or not job timeout checks should be enabled.</param>
        public void SetTimeoutsEnabled( bool enable )
        {
            if ( enable )
            {
                jobTimeoutFunc.Start();
            }
            else
            {
                jobTimeoutFunc.Stop();
            }
        }


        /// <summary>
        /// This is called periodically to cancel and clear out any jobs that have timed out (no response from Steam).
        /// </summary>
        void CancelTimedoutJobs()
        {
            lock ( asyncJobs )
            {
                foreach ( AsyncJob job in asyncJobs.Values )
                {
                    if ( job.IsTimedout )
                    {
                        TryFailAsyncJob( job, dueToRemoteFailure: false );
                        asyncJobs.Remove( job );
                    }
                }
            }
        }


        /// <summary>
        /// Retrieves a job from this manager, and optionally removes it from tracking.
        /// </summary>
        /// <param name="jobId">The JobID.</param>
        /// <param name="andRemove">If set to <c>true</c>, this job is removed from tracking.</param>
        /// <returns></returns>
        AsyncJob? GetJob( JobID jobId, bool andRemove = false )
        {
            AsyncJob? asyncJob;
            bool foundJob;

            if ( andRemove )
            {
                lock ( asyncJobs )
                    foundJob = asyncJobs.Remove( jobId, out asyncJob );
            }
            else
            {
                lock ( asyncJobs )
                    foundJob = asyncJobs.TryGetValue( jobId, out asyncJob );
            }

            if ( !foundJob )
            {
                // requested a job we're not tracking
                return null;
            }

            return asyncJob;
        }
    }
}
