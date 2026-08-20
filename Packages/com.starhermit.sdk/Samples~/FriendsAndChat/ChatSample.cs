#if UNITY_2021_3_OR_NEWER
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Starhermit.Samples
{
    /// <summary>
    /// Friends, conversations and live chat, with the socket and REST kept consistent.
    /// </summary>
    /// <remarks>
    /// The pattern worth copying is the deduplicator: history comes from REST, live messages come from
    /// the socket, and the same message can arrive twice. It is matched by the server's id, because
    /// that is the only id both sides agree on.
    /// </remarks>
    public sealed class ChatSample : MonoBehaviour
    {
        private readonly StarhermitMessageDeduplicator _seen = new StarhermitMessageDeduplicator();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private StarhermitChatConnection? _connection;

        /// <summary>The signed-in client to use. Assign before enabling the component.</summary>
        public StarhermitClient? Client { get; set; }

        private void Start()
        {
            _ = RunAsync();
        }

        private async Task RunAsync()
        {
            var client = Client;
            if (client == null)
            {
                Debug.LogWarning("[Sample] Assign a signed-in StarhermitClient first.");
                return;
            }

            var friends = await client.Friends.GetFriendsAsync(_lifetime.Token);
            foreach (var friend in friends)
                Debug.Log($"[Sample] {friend.Username} is {(friend.IsOnline ? "online" : "offline")}.");

            var conversations = await client.Chat.GetConversationsAsync(_lifetime.Token);
            if (conversations.Count == 0)
            {
                Debug.Log("[Sample] No conversations yet.");
                return;
            }

            var conversation = conversations[0];

            // History first, so the deduplicator already knows these ids when the socket starts.
            var history = await client.Chat.GetMessagesAsync(conversation.Id, pageSize: 50, cancellationToken: _lifetime.Token);
            foreach (var message in history.Items)
                if (_seen.TryAdd(message))
                    Render(message);

            _connection = client.CreateChatConnection();
            _connection.MessageReceived += message =>
            {
                if (message.ConversationId == conversation.Id && _seen.TryAdd(message)) Render(message);
            };
            _connection.MessageUpdated += message => Debug.Log($"[Sample] edited: {message.Content}");
            _connection.MessageDeleted += message => Debug.Log($"[Sample] deleted message {message.Id}");
            _connection.StateChanged += state => Debug.Log($"[Sample] chat socket is {state}");

            await _connection.ConnectAsync(_lifetime.Token);
            await client.Chat.MarkReadAsync(conversation.Id, _lifetime.Token);
            await client.Chat.SendMessageAsync(conversation.Id, "Hello from the Unity SDK sample.", _lifetime.Token);
        }

        private static void Render(StarhermitMessage message)
        {
            // Player-authored text: a real game applies its own moderation and formatting policy
            // rather than trusting the string.
            var author = message.IsSystem ? "system" : message.SenderUsername;
            Debug.Log($"[Sample] {author}: {message.Content}");
        }

        private void OnDestroy()
        {
            _lifetime.Cancel();
            _connection?.Dispose();
        }
    }
}
#endif
