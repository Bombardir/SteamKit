using System;

namespace SPS.Core;

/// <summary>    
/// The disposable action.    
/// </summary>    
public class DisposableAction : IDisposable
{
    private Action _dispose;

    /// <summary>    
    /// Initializes a new instance of the <see cref="DisposableAction"/> class.    
    /// </summary>    
    /// <param name="dispose">    
    /// The dispose.    
    /// </param>    
    public DisposableAction(Action dispose)
    {
        if (dispose == null) throw new ArgumentNullException("dispose");

        this._dispose = dispose;
    }

    /// <summary>    
    /// Initializes a new instance of the <see cref="DisposableAction"/> class.    
    /// </summary>    
    /// <param name="construct">    
    /// The construct.    
    /// </param>    
    /// <param name="dispose">    
    /// The dispose.    
    /// </param>    
    public DisposableAction(Action construct, Action dispose)
    {
        if (construct == null) throw new ArgumentNullException("construct");
        if (dispose == null) throw new ArgumentNullException("dispose");

        construct();

        this._dispose = dispose;
    }

    /// <summary>    
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.    
    /// </summary>    
    /// <filterpriority>2</filterpriority>    
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>    
    /// The dispose.    
    /// </summary>    
    /// <param name="disposing">    
    /// The disposing.    
    /// </param>    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (this._dispose == null)
            {
                return;
            }

            try
            {
                this._dispose();
            }
            catch (Exception ex)
            {
                // ToDo: Log error?    
            }

            this._dispose = null;
        }
    }
}
