#if UNITY_2021_3_OR_NEWER
using System;
using UnityEngine;

namespace Starhermit.Platform
{
    /// <summary>
    /// Turns the image bytes the API returns - avatars, cover art, icons - into Unity textures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ownership is explicit because Unity's is not: <see cref="Texture2D"/> holds native memory that
    /// garbage collection does not reclaim on any schedule you can rely on. Every texture created here
    /// belongs to the caller, who must <c>Destroy</c> it - typically when the sprite showing it goes
    /// away, not when the C# reference does.
    /// </para>
    /// <para>
    /// These must run on the main thread. The SDK dispatches its events there by default, so a handler
    /// can call straight into this; a background task cannot.
    /// </para>
    /// </remarks>
    public static class StarhermitTextures
    {
        /// <summary>Decodes image bytes into a texture the caller owns and must destroy.</summary>
        /// <param name="bytes">Encoded image, in a format Unity's loader accepts (PNG or JPEG).</param>
        /// <param name="markNonReadable">
        /// True to upload the pixels and drop the CPU copy, halving memory for a texture that is only
        /// ever displayed. Leave false if the game needs to read pixels back.
        /// </param>
        /// <returns>The texture, or null when the bytes are not a decodable image.</returns>
        public static Texture2D? Decode(byte[] bytes, bool markNonReadable = true)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length == 0) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (texture.LoadImage(bytes, markNonReadable)) return texture;

            // Not an image this platform can decode. Release the native allocation rather than
            // handing back a texture of the wrong two-by-two placeholder size.
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        /// <summary>Decodes an avatar into a texture the caller owns and must destroy.</summary>
        /// <param name="avatar">Avatar bytes from the API.</param>
        /// <returns>The texture, or null when the bytes are not decodable.</returns>
        public static Texture2D? Decode(StarhermitAvatar avatar) => Decode(avatar.Bytes);

        /// <summary>Decodes binary API content, such as cover art, into a texture the caller owns.</summary>
        /// <param name="binary">Binary content from the API.</param>
        /// <returns>The texture, or null when the bytes are not decodable.</returns>
        public static Texture2D? Decode(StarhermitBinary binary) => Decode(binary.Bytes);

        /// <summary>Creates a sprite from image bytes. The caller owns and must destroy both objects.</summary>
        /// <param name="bytes">Encoded image bytes.</param>
        /// <param name="pixelsPerUnit">Sprite scale.</param>
        /// <returns>The sprite, or null when the bytes are not decodable.</returns>
        public static Sprite? DecodeSprite(byte[] bytes, float pixelsPerUnit = 100f)
        {
            var texture = Decode(bytes);
            if (texture == null) return null;

            return Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
        }
    }
}
#endif
