using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Starhermit.Publishing
{
    /// <summary>One asset to publish with a build.</summary>
    public sealed class StarhermitBuildAsset
    {
        /// <summary>Creates an asset description.</summary>
        /// <param name="type">Asset type, matching what the upload target asks for.</param>
        /// <param name="openContent">
        /// Opens the content. Called twice - once to checksum, once to upload - so it must return a
        /// fresh stream over the same bytes each time.
        /// </param>
        /// <param name="length">Byte length when known, which lets progress report a percentage.</param>
        /// <param name="contentType">Media type to send.</param>
        public StarhermitBuildAsset(
            string type,
            Func<Stream> openContent,
            long? length = null,
            string contentType = "application/octet-stream")
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            OpenContent = openContent ?? throw new ArgumentNullException(nameof(openContent));
            Length = length;
            ContentType = contentType ?? "application/octet-stream";
        }

        /// <summary>Creates an asset from a file on disk.</summary>
        /// <param name="type">Asset type.</param>
        /// <param name="path">Path to the file.</param>
        /// <param name="contentType">Media type to send.</param>
        /// <returns>The asset description.</returns>
        public static StarhermitBuildAsset FromFile(string type, string path, string contentType = "application/octet-stream")
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("The asset file does not exist.", path);
            return new StarhermitBuildAsset(type, () => File.OpenRead(path), info.Length, contentType);
        }

        /// <summary>Asset type.</summary>
        public string Type { get; }

        /// <summary>Opens a fresh stream over the content.</summary>
        public Func<Stream> OpenContent { get; }

        /// <summary>Byte length when known.</summary>
        public long? Length { get; }

        /// <summary>Media type.</summary>
        public string ContentType { get; }
    }

    /// <summary>What a publish did.</summary>
    public sealed class StarhermitPublishResult
    {
        internal StarhermitPublishResult(Guid titleId, string version, IReadOnlyList<string> uploadedTypes)
        {
            TitleId = titleId;
            Version = version;
            UploadedTypes = uploadedTypes;
        }

        /// <summary>The title that was published to.</summary>
        public Guid TitleId { get; }

        /// <summary>The build version that was finalised.</summary>
        public string Version { get; }

        /// <summary>Asset types that were uploaded, in the order the server asked for them.</summary>
        public IReadOnlyList<string> UploadedTypes { get; }
    }

    /// <summary>
    /// Runs the whole publish flow: request signed upload targets, upload each asset, and finalise the
    /// build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This lives in the optional <c>Starhermit.Publishing</c> assembly. A project that excludes it
    /// keeps every typed publisher operation on <c>client.Publishers</c> and loses only this
    /// multi-step convenience - which is the point of the split: a shipped game has no reason to carry
    /// publishing tooling.
    /// </para>
    /// <para>
    /// Nothing is finalised until every asset has uploaded. An interrupted publish leaves the previous
    /// build serving players, because a half-published build is worse than none.
    /// </para>
    /// </remarks>
    public sealed class StarhermitBuildPublisher
    {
        private readonly StarhermitClient _client;

        /// <summary>Creates the publisher.</summary>
        /// <param name="client">A signed-in client whose account holds publisher permissions.</param>
        public StarhermitBuildPublisher(StarhermitClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>Publishes a build: upload every asset, then finalise.</summary>
        /// <param name="titleId">The title to publish to.</param>
        /// <param name="version">Version string for the build.</param>
        /// <param name="releaseNotes">Release notes for the build.</param>
        /// <param name="assets">Assets to upload, keyed by the type the server asks for.</param>
        /// <param name="progress">Optional per-asset upload progress.</param>
        /// <param name="cancellationToken">Cancels the publish before it is finalised.</param>
        /// <returns>What was published.</returns>
        /// <exception cref="InvalidOperationException">The server asked for an asset that was not supplied.</exception>
        public async Task<StarhermitPublishResult> PublishAsync(
            Guid titleId,
            string version,
            string releaseNotes,
            IReadOnlyList<StarhermitBuildAsset> assets,
            IProgress<StarhermitTransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));

            var targets = await _client.Publishers.GenerateUploadTargetsAsync(titleId, cancellationToken)
                .ConfigureAwait(false);

            var descriptors = new List<StarhermitAssetDescriptor>(targets.Count);
            var uploaded = new List<string>(targets.Count);

            foreach (var target in targets)
            {
                var asset = Find(assets, target.Type);
                if (asset == null)
                {
                    throw new InvalidOperationException(
                        $"The server asked for an asset of type '{target.Type}', which was not supplied. " +
                        "Nothing has been finalised, so the previous build is still serving players.");
                }

                var checksum = ComputeSha256(asset);

                await _client.Pipeline
                    .UploadSignedAsync(
                        new Uri(target.UploadUrl, UriKind.Absolute),
                        StarhermitContent.Stream(asset.OpenContent, asset.Length, asset.ContentType),
                        "PUT",
                        progress,
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);

                descriptors.Add(new StarhermitAssetDescriptor(target.Type, checksum, target.FieldKey));
                uploaded.Add(target.Type);
            }

            await _client.Publishers
                .FinalizeBuildAsync(titleId, version, releaseNotes, descriptors, null, cancellationToken)
                .ConfigureAwait(false);

            return new StarhermitPublishResult(titleId, version, uploaded);
        }

        private static StarhermitBuildAsset? Find(IReadOnlyList<StarhermitBuildAsset> assets, string type)
        {
            foreach (var asset in assets)
                if (string.Equals(asset.Type, type, StringComparison.OrdinalIgnoreCase))
                    return asset;
            return null;
        }

        private static string ComputeSha256(StarhermitBuildAsset asset)
        {
            using var hasher = SHA256.Create();
            using var content = asset.OpenContent();
            var hash = hasher.ComputeHash(content);

            var characters = new char[hash.Length * 2];
            const string digits = "0123456789abcdef";
            for (var i = 0; i < hash.Length; i++)
            {
                characters[i * 2] = digits[hash[i] >> 4];
                characters[i * 2 + 1] = digits[hash[i] & 0xF];
            }

            return new string(characters);
        }
    }
}
