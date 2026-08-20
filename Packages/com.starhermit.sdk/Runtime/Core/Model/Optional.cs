using System;
using System.Collections.Generic;

namespace Starhermit
{
    /// <summary>
    /// A value that may be absent, present-and-null, or present-with-a-value.
    /// </summary>
    /// <typeparam name="T">The wrapped value type.</typeparam>
    /// <remarks>
    /// PATCH bodies need all three states: leaving a member out means "do not touch", sending
    /// <c>null</c> means "clear it", and sending a value means "set it". A plain nullable field cannot
    /// tell the first two apart, which is how a partial update quietly wipes a field it never meant to
    /// mention.
    /// </remarks>
    public readonly struct Optional<T> : IEquatable<Optional<T>>
    {
        private readonly T _value;

        private Optional(T value, bool isSet)
        {
            _value = value;
            IsSet = isSet;
        }

        /// <summary>The absent state: the member is omitted from the request entirely.</summary>
        public static Optional<T> Unset => default;

        /// <summary>True when a value was supplied, including an explicit null.</summary>
        public bool IsSet { get; }

        /// <summary>The supplied value.</summary>
        /// <exception cref="InvalidOperationException">No value was supplied.</exception>
        public T Value => IsSet ? _value : throw new InvalidOperationException("This optional has no value.");

        /// <summary>Wraps a value, marking it as supplied.</summary>
        /// <param name="value">The value to send, which may itself be null.</param>
        /// <returns>A set optional.</returns>
        public static Optional<T> Set(T value) => new Optional<T>(value, true);

        /// <summary>Returns the value, or <paramref name="fallback"/> when unset.</summary>
        /// <param name="fallback">Value to use when nothing was supplied.</param>
        /// <returns>The value or the fallback.</returns>
        public T GetValueOrDefault(T fallback) => IsSet ? _value : fallback;

        /// <summary>Implicitly wraps a value.</summary>
        /// <param name="value">The value to send.</param>
        public static implicit operator Optional<T>(T value) => Set(value);

        /// <inheritdoc />
        public bool Equals(Optional<T> other) =>
            IsSet == other.IsSet && EqualityComparer<T>.Default.Equals(_value, other._value);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() =>
            IsSet ? EqualityComparer<T>.Default.GetHashCode(_value!) * 31 + 1 : 0;

        /// <summary>Compares two optionals.</summary>
        /// <param name="left">First operand.</param>
        /// <param name="right">Second operand.</param>
        public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

        /// <summary>Compares two optionals.</summary>
        /// <param name="left">First operand.</param>
        /// <param name="right">Second operand.</param>
        public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => IsSet ? _value?.ToString() ?? "null" : "<unset>";
    }
}
