using System.Threading;
using System.Threading.Tasks;

namespace Starhermit
{
    /// <summary>
    /// Signs public-key authentication challenges.
    /// </summary>
    /// <remarks>
    /// The SDK never generates or stores a private key of its own. Whatever holds the key - a console
    /// secure element, an OS keychain, an HSM, a file the game manages - implements this and keeps the
    /// key material entirely outside the package.
    /// </remarks>
    public interface IStarhermitSigner
    {
        /// <summary>
        /// The key algorithm, exactly as the API names it: <c>Ed25519</c>, <c>ECDSA-P256</c> or
        /// <c>RSA-PSS</c>.
        /// </summary>
        string KeyType { get; }

        /// <summary>The public key in the base64 form the API registered it under.</summary>
        string PublicKeyData { get; }

        /// <summary>Signs the exact bytes the server will verify.</summary>
        /// <param name="data">Canonical challenge bytes.</param>
        /// <param name="cancellationToken">Cancels the signature.</param>
        /// <returns>The raw signature bytes.</returns>
        Task<byte[]> SignAsync(byte[] data, CancellationToken cancellationToken);
    }

    /// <summary>Key algorithms the API accepts for public-key authentication.</summary>
    public static class StarhermitKeyTypes
    {
        /// <summary>Ed25519, signed over the raw challenge bytes.</summary>
        public const string Ed25519 = "Ed25519";

        /// <summary>ECDSA over NIST P-256 with SHA-256.</summary>
        public const string EcdsaP256 = "ECDSA-P256";

        /// <summary>RSA-PSS with SHA-256.</summary>
        public const string RsaPss = "RSA-PSS";
    }
}
