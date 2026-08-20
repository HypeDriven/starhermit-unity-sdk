namespace Starhermit.Json
{
    /// <summary>The JSON type of a <see cref="JsonValue"/>.</summary>
    public enum JsonKind
    {
        /// <summary>A member that was not present in the payload at all.</summary>
        Missing = 0,

        /// <summary>An explicit <c>null</c> literal.</summary>
        Null = 1,

        /// <summary>A <c>true</c> or <c>false</c> literal.</summary>
        Boolean = 2,

        /// <summary>A number. The exact source text is preserved so large integers survive intact.</summary>
        Number = 3,

        /// <summary>A string.</summary>
        String = 4,

        /// <summary>An array.</summary>
        Array = 5,

        /// <summary>An object.</summary>
        Object = 6
    }
}
