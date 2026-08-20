using System;
using System.Collections.Generic;

namespace Starhermit
{
    /// <summary>
    /// Remembers which messages have already been shown, so a message that arrives both on the socket
    /// and in a REST refresh is rendered once.
    /// </summary>
    /// <remarks>
    /// De-duplication is by the server's own message id. The SDK never invents an id or an optimistic
    /// timestamp for a message the server has not accepted, so there is nothing to reconcile later and
    /// no window where a local id and a server id describe the same message.
    /// </remarks>
    public sealed class StarhermitMessageDeduplicator
    {
        private readonly int _capacity;
        private readonly HashSet<Guid> _seen = new HashSet<Guid>();
        private readonly Queue<Guid> _order = new Queue<Guid>();
        private readonly object _gate = new object();

        /// <summary>Creates a deduplicator.</summary>
        /// <param name="capacity">
        /// How many ids to remember. Old ids are forgotten first, which bounds memory on a long
        /// session at the cost of re-showing a message older than the window - which cannot arrive
        /// twice in practice.
        /// </param>
        public StarhermitMessageDeduplicator(int capacity = 2048)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        /// <summary>How many ids are currently remembered.</summary>
        public int Count
        {
            get { lock (_gate) return _seen.Count; }
        }

        /// <summary>Records a message and reports whether it is new.</summary>
        /// <param name="messageId">The server's message id.</param>
        /// <returns>True the first time an id is seen, false afterwards.</returns>
        public bool TryAdd(Guid messageId)
        {
            lock (_gate)
            {
                if (!_seen.Add(messageId)) return false;
                _order.Enqueue(messageId);
                while (_order.Count > _capacity) _seen.Remove(_order.Dequeue());
                return true;
            }
        }

        /// <summary>Records a message and reports whether it is new.</summary>
        /// <param name="message">The message.</param>
        /// <returns>True the first time this message is seen.</returns>
        public bool TryAdd(StarhermitMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return TryAdd(message.Id);
        }

        /// <summary>Forgets every id.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _seen.Clear();
                _order.Clear();
            }
        }
    }
}
