using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// Base class for every model the API returns.
    /// </summary>
    /// <remarks>
    /// Models keep the object they were read from. Nothing is lost when the deployment ships a field
    /// before the SDK maps it: read it through <see cref="RawJson"/> today and switch to the typed
    /// member when the next SDK release adds it, without forking the package.
    /// </remarks>
    public abstract class StarhermitModel
    {
        /// <summary>Creates the model from its source JSON.</summary>
        /// <param name="rawJson">The object the model was read from.</param>
        protected StarhermitModel(JsonValue rawJson)
        {
            RawJson = rawJson ?? JsonValue.EmptyObject;
        }

        /// <summary>
        /// The untouched JSON this model was read from, including members this SDK version does not
        /// map. Never null; an object built locally carries an empty object.
        /// </summary>
        public JsonValue RawJson { get; }
    }
}
