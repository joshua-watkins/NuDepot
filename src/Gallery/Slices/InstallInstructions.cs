namespace NuDepot.Gallery.Slices;

/// <summary>
/// How to install a package with one tool.
/// </summary>
/// <param name="Name">The tool's name.</param>
/// <param name="Prefix">The prompt shown before the command.</param>
/// <param name="Content">The command's lines.</param>
internal record InstallInstructions(string Name, string Prefix, IReadOnlyList<string> Content);
