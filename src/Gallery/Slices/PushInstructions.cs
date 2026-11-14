namespace NuDepot.Gallery.Slices;

/// <summary>
/// How to push a package with one tool.
/// </summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Content">The command's lines.</param>
/// <param name="Documentation">The tool's documentation.</param>
internal record PushInstructions(string Name, IReadOnlyList<string> Content, string Documentation);
