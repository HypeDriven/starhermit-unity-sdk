using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>The account's entitlements.</summary>
    /// <remarks>
    /// Granting and revoking are publisher operations and live on the publisher client, because they
    /// need publisher permissions rather than the player's own session.
    /// </remarks>
    public sealed class StarhermitEntitlementsClient : StarhermitServiceClient
    {
        internal StarhermitEntitlementsClient(StarhermitRestClient rest) : base(rest)
        {
        }

        /// <summary>Lists the caller's entitlements.</summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The entitlements.</returns>
        public async Task<IReadOnlyList<StarhermitEntitlement>> ListAsync(CancellationToken cancellationToken = default)
        {
            var json = await SendJsonAsync(Get("me/entitlements"), "entitlements.list", cancellationToken).ConfigureAwait(false);
            return json.AsList(StarhermitEntitlement.Read);
        }

        /// <summary>Tests whether the account is entitled to a title.</summary>
        /// <param name="softwareTitleId">The title to check.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>True when an unrevoked entitlement exists.</returns>
        public async Task<bool> HasEntitlementAsync(System.Guid softwareTitleId, CancellationToken cancellationToken = default)
        {
            var entitlements = await ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var entitlement in entitlements)
                if (entitlement.SoftwareTitleId == softwareTitleId && !entitlement.IsRevoked)
                    return true;
            return false;
        }
    }
}
