using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NuDepot.Feed.Entities;

/// <summary>
/// Keeps ASP.NET Core data protection's keys in the feed's database, unless the application keeps them somewhere
/// else. Otherwise a service without a user profile, like the Linux service, gets new keys each time it starts,
/// which signs people out and makes generated signing certificates unreadable. On Windows, the keys are encrypted
/// for the machine, so a copy of the database can't decrypt them. New keys encrypt with AES-256-CBC and
/// authenticate with HMAC-SHA512, which protects generated certificates' private keys and sign-in cookies.
/// </summary>
/// <param name="loggerFactory">Creates the key storage's loggers.</param>
/// <param name="services">The application's services, which the key storage creates database scopes from.</param>
internal sealed class ConfigureDataProtectionKeys(ILoggerFactory loggerFactory, IServiceProvider services) : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options)
    {
        if (options.XmlRepository != null)
            return;

        options.XmlRepository = new EntityFrameworkCoreXmlRepository<NuDepotContext>(services, loggerFactory);
        if (options.XmlEncryptor == null && OperatingSystem.IsWindows())
            options.XmlEncryptor = new DpapiXmlEncryptor(protectToLocalMachine: true, loggerFactory);

        options.AuthenticatedEncryptorConfiguration = new AuthenticatedEncryptorConfiguration
        {
            EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
            ValidationAlgorithm = ValidationAlgorithm.HMACSHA512,
        };
    }
}
