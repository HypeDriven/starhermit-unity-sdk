using System;
using System.Threading;

namespace Starhermit
{
    /// <summary>
    /// Decides which thread the SDK's events and progress callbacks run on.
    /// </summary>
    /// <remarks>
    /// Unity objects may only be touched from the main thread, so by default every event the SDK
    /// raises is posted back to the synchronization context that created the client. A dedicated
    /// server with no main-thread requirement can swap in <see cref="ImmediateCallbackDispatcher"/>
    /// and skip the hop.
    /// </remarks>
    public interface IStarhermitCallbackDispatcher
    {
        /// <summary>Runs a callback on the dispatcher's thread.</summary>
        /// <param name="callback">The work to run.</param>
        void Post(Action callback);
    }

    /// <summary>Runs callbacks inline, on whichever thread produced them.</summary>
    public sealed class ImmediateCallbackDispatcher : IStarhermitCallbackDispatcher
    {
        /// <summary>The shared instance.</summary>
        public static readonly ImmediateCallbackDispatcher Instance = new ImmediateCallbackDispatcher();

        private ImmediateCallbackDispatcher()
        {
        }

        /// <inheritdoc />
        public void Post(Action callback) => callback?.Invoke();
    }

    /// <summary>
    /// Posts callbacks to a captured <see cref="SynchronizationContext"/> - Unity's main thread when
    /// the client is created there.
    /// </summary>
    public sealed class SynchronizationContextDispatcher : IStarhermitCallbackDispatcher
    {
        private readonly SynchronizationContext? _context;

        /// <summary>Captures the current synchronization context.</summary>
        public SynchronizationContextDispatcher()
            : this(SynchronizationContext.Current)
        {
        }

        /// <summary>Uses an explicit synchronization context.</summary>
        /// <param name="context">Context to post to; null runs callbacks inline.</param>
        public SynchronizationContextDispatcher(SynchronizationContext? context)
        {
            _context = context;
        }

        /// <summary>True when a context was captured and callbacks will be marshalled.</summary>
        public bool HasContext => _context != null;

        /// <inheritdoc />
        public void Post(Action callback)
        {
            if (callback == null) return;
            if (_context == null)
            {
                callback();
                return;
            }

            _context.Post(state => ((Action)state!)(), callback);
        }
    }
}
