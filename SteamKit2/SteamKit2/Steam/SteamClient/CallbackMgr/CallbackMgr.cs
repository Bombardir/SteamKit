/*
 * This file is subject to the terms and conditions defined in
 * file 'license.txt', which is part of this source code package.
 */

using System;
using System.Collections.Generic;
using System.Threading;
using SteamKit2.Internal;

namespace SteamKit2
{
    /// <summary>
    /// This class is a utility for routing callbacks to function calls.
    /// In order to bind callbacks to functions, an instance of this class must be created for the
    /// <see cref="SteamClient"/> instance that will be posting callbacks.
    /// </summary>
    public sealed class CallbackManager
    {
        private readonly struct CallbackQueueEntry
        {
            public CallbackManager Manager { get; }
            public CallbackMsg Callback { get; }

            public CallbackQueueEntry( CallbackManager manager, CallbackMsg callback )
            {
                Manager = manager;
                Callback = callback;
            }
        }

        private static List<CallbackQueueEntry> GlobalCallbackQueue = new List<CallbackQueueEntry>(100);
        List<CallbackBase> registeredCallbacks;

        /// <summary>
        /// Initializes a new instance of the <see cref="CallbackManager"/> class.
        /// </summary>
        /// <param name="client">The <see cref="SteamClient"/> instance to handle the callbacks of.</param>
        public CallbackManager( SteamClient client )
        {
            if ( client == null )
            {
                throw new ArgumentNullException( nameof(client) );
            }

            registeredCallbacks = new List<CallbackBase>(10);
            client.OnCallback += OnClientCallback;
        }

        public static void RunWaitAllCallbacks( TimeSpan timeout )
        {
            CallbackQueueEntry[] callbacksToRun;

            lock ( GlobalCallbackQueue )
            {
                if ( GlobalCallbackQueue.Count == 0 && !Monitor.Wait( GlobalCallbackQueue, timeout ) )
                    return;

                callbacksToRun = GlobalCallbackQueue.ToArray();
                GlobalCallbackQueue.Clear();
            }

            foreach ( var callback in callbacksToRun )
            {
                try
                {
                    callback.Manager.Handle( callback.Callback );
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine( "error", "Failed to handle callback: {0}", ex );
                }
            }
        }

        private void OnClientCallback( CallbackMsg msg )
        {
            lock ( GlobalCallbackQueue )
            {
                GlobalCallbackQueue.Add( new CallbackQueueEntry( this, msg ) );
                Monitor.Pulse( GlobalCallbackQueue );
            }
        }

        /// <summary>
        /// Registers the provided <see cref="Action{T}"/> to receive callbacks of type <typeparamref name="TCallback" />.
        /// </summary>
        /// <param name="jobID">The <see cref="JobID"/> of the callbacks that should be subscribed to.
        ///		If this is <see cref="JobID.Invalid"/>, all callbacks of type <typeparamref name="TCallback" /> will be recieved.</param>
        /// <param name="callbackFunc">The function to invoke with the callback.</param>
        /// <typeparam name="TCallback">The type of callback to subscribe to.</typeparam>
        /// <returns>An <see cref="IDisposable"/>. Disposing of the return value will unsubscribe the <paramref name="callbackFunc"/>.</returns>
        public IDisposable Subscribe<TCallback>( JobID jobID, Action<TCallback> callbackFunc )
            where TCallback : CallbackMsg
        {
            if ( jobID == null )
            {
                throw new ArgumentNullException( nameof(jobID) );
            }

            if ( callbackFunc == null )
            {
                throw new ArgumentNullException( nameof(callbackFunc) );
            }

            var callback = new Internal.Callback<TCallback>( callbackFunc, this, jobID );
            return new Subscription( callback, this );
        }

        /// <summary>
        /// Registers the provided <see cref="Action{T}"/> to receive callbacks of type <typeparam name="TCallback" />.
        /// </summary>
        /// <param name="callbackFunc">The function to invoke with the callback.</param>
        /// <returns>An <see cref="IDisposable"/>. Disposing of the return value will unsubscribe the <paramref name="callbackFunc"/>.</returns>
        public IDisposable Subscribe<TCallback>( Action<TCallback> callbackFunc )
            where TCallback : CallbackMsg
        {
            return Subscribe( JobID.Invalid, callbackFunc );
        }

        public void Register( CallbackBase call )
        {
            if ( registeredCallbacks.Contains( call ) )
                return;

            registeredCallbacks.Add( call );
        }

        void Handle( CallbackMsg call )
        {
            var type = call.GetType();

            foreach ( var callback in registeredCallbacks )
            {
                if (callback.CallbackType.IsAssignableFrom(type))
                    callback.Run( call );
            }
        }

        public void Unregister( CallbackBase call )
        {
            registeredCallbacks.Remove( call );
        }

        sealed class Subscription : IDisposable
        {
            public Subscription( CallbackBase call, CallbackManager manager )
            {
                this.manager = manager;
                this.call = call;
            }

            CallbackManager? manager;
            CallbackBase? call;

            void IDisposable.Dispose()
            {
                if ( call != null && manager != null )
                {
                    manager.Unregister( call );
                    call = null;
                    manager = null;
                }
            }
        }
    }
}
