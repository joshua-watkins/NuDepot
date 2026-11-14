using RazorSlices;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// Creates the slice in <c>Slices/FeedError.cshtml</c>. Razor Slices would generate this, but only as a public class, which
/// can't take the gallery's internal models.
/// </summary>
internal sealed class FeedError : IRazorSliceProxy<FeedErrorModel>
{
    /// <summary>
    /// Creates the slice with its model.
    /// </summary>
    public static RazorSlice<FeedErrorModel> Create(FeedErrorModel model)
        => RazorSliceFactory.Create(model, static model => new AspNetCoreGeneratedDocument.Slices_FeedError { Model = model });

    static RazorSlice<FeedErrorModel> IRazorSliceProxy<FeedErrorModel>.CreateSlice(FeedErrorModel model) => Create(model);
}
