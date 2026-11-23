using System.Text.Json.Serialization;

namespace Notcho.Services;

/// <summary>
/// The types settings keep as JSON in Shiny's store. Its serializer only knows the types it's given a
/// source-generated context for - without this, saving the tray or the weather places threw.
/// </summary>
[JsonSerializable(typeof(string[]))]
sealed partial class SettingsJson : JsonSerializerContext
{
    /// <summary>
    /// Hands the context to Shiny's serializer, before any setting is read or written: from
    /// <c>UseNotcho</c>, and earlier still from a head that touches the store before that. Safe to call
    /// more than once.
    /// </summary>
    public static void Register() => Shiny.Json.AddContext(Default);
}
