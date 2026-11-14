using RazorSlices;

namespace NuDepot.Gallery.Slices;

/// <summary>
/// Creates the slice in <c>Slices/Account.cshtml</c>. Razor Slices would generate this, but only as a public class, which
/// can't take the gallery's internal models.
/// </summary>
internal sealed class Account : IRazorSliceProxy<AccountModel>
{
    /// <summary>
    /// Creates the slice with its model.
    /// </summary>
    public static RazorSlice<AccountModel> Create(AccountModel model)
        => RazorSliceFactory.Create(model, static model => new AspNetCoreGeneratedDocument.Slices_Account { Model = model });

    static RazorSlice<AccountModel> IRazorSliceProxy<AccountModel>.CreateSlice(AccountModel model) => Create(model);
}
