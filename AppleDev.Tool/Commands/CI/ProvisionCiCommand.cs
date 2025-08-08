using System.ComponentModel;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography.X509Certificates;
using AppleAppStoreConnect;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AppleDev.Tool.Commands;

public class ProvisionCiCommand : AsyncCommand<ProvisionCiCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ProvisionCiCommandSettings settings)
    {
        var data = context.GetData();

        AnsiConsole.Write(new Rule("Provisioning..."));

        var keychainName = settings.Keychain ?? "build";
        var keychainPassword = settings.KeychainPassword ?? keychainName;

        var certificateData = settings.GetBytesFromFileOrEnvironmentOrBase64String(settings.Certificate);
        var rootCaCertificateData = settings.GetBytesFromFileOrEnvironmentOrBase64String(settings.RootCACertificate);
        var intermediateCertificateData =
            settings.GetBytesFromFileOrEnvironmentOrBase64String(settings.IntermediateCertificate);
        
        if (!settings.ImportCert())
        {
            AnsiConsole.Write($"Certificate not specified or found, skipping...");
        }
        else
        {
            var keychain = new Keychain();

            var keychainFile = keychain.Locate(keychainName);

            this.SetOutputVariable("Keychain", keychainFile.FullName);
            this.SetOutputVariable("KeychainPassword", keychainPassword, true);

            if (keychainFile.Exists)
            {
                AnsiConsole.WriteLine($"Keychain already exists: {keychainFile.FullName}");
            }
            else
            {
                AnsiConsole.Write($"Creating Keychain {keychainFile.FullName}...");
                var createResult = await keychain
                    .CreateKeychainAsync(keychainFile.FullName, keychainPassword, data.CancellationToken)
                    .ConfigureAwait(false);

                if (!createResult.Success)
                {
                    AnsiConsole.WriteLine();
                    createResult.OutputFailure("Creating Keychain Failed");
                    return 1;
                }

                AnsiConsole.WriteLine($" Done.");
            }

            AnsiConsole.Write($"Setting Default Keychain {keychainFile.FullName}...");
            var setDefResult = await keychain
                .SetDefaultKeychainAsync(keychainFile.FullName, data.CancellationToken)
                .ConfigureAwait(false);

            if (!setDefResult.Success)
            {
                AnsiConsole.WriteLine();
                setDefResult.OutputFailure("Setting Default Keychain Failed");
                return 1;
            }

            AnsiConsole.WriteLine($" Done.");

            AnsiConsole.Write($"Unlocking Keychain {keychainFile.FullName}...");
            try
            {
                var unlockResult = await keychain.UnlockKeychainAsync(keychainPassword, keychainFile.FullName)
                    .ConfigureAwait(false);

                if (!unlockResult.Success)
                    AnsiConsole.WriteLine("[yellow]Warning: Failed to unlock keychain[/]");
            }
            catch
            {
            }

            AnsiConsole.WriteLine(" Done.");


            bool allowAny = !settings.DisallowAllowAnyAppRead;

            AnsiConsole.Write($"Importing Certificate into {keychainFile.FullName} (AllowAnyAppRead: {allowAny})...");

            var tmpP12File = Path.GetTempFileName();
            if (certificateData is not null)
            {
                await File.WriteAllBytesAsync(tmpP12File, certificateData);

                // Use passphrase if specified
                var x509 = !string.IsNullOrWhiteSpace(settings.CertificatePassphrase)
                    ? new X509Certificate2(certificateData, settings.CertificatePassphrase)
                    : new X509Certificate2(certificateData, (string)null!);
                var certificateFriendlyName = x509.FriendlyName;

                if (!string.IsNullOrEmpty(certificateFriendlyName))
                {
                    this.SetOutputVariable("AppleCertificateFriendlyName", certificateFriendlyName);
                    this.SetOutputVariable("AppleCertificateFile", tmpP12File);
                }
            }

            var importResult = await keychain.ImportPkcs12Async(tmpP12File, settings.CertificatePassphrase,
                keychainFile.FullName, allowAny, data.CancellationToken).ConfigureAwait(false);

            if (!importResult.Success)
            {
                AnsiConsole.WriteLine();
                importResult.OutputFailure("Import Certificate Failed");
                return 1;
            }

            AnsiConsole.WriteLine($" Done.");

            if (settings.CreateKeychain())
            {
                AnsiConsole.Write($"Setting Partition List for {keychainFile.FullName}...");

                var partitionResult = await keychain.SetPartitionListAsync(keychainPassword, keychainFile.FullName,
                        data.CancellationToken)
                    .ConfigureAwait(false);

                if (!partitionResult.Success)
                {
                    AnsiConsole.WriteLine();
                    partitionResult.OutputFailure("Set Partition List Failed");
                    return 1;
                }

                //Verify Certificate
                if (rootCaCertificateData is not null && intermediateCertificateData is not null)
                {
                    AnsiConsole.WriteLine($" Begin Certificate Verification.");
                    //1. Import both Root certificates
                    var tmpRootCaFile = Path.GetTempPath() + Guid.NewGuid() + ".cer";
                    await File.WriteAllBytesAsync(tmpRootCaFile, rootCaCertificateData);
                    await keychain.ImportCertificateAsync(tmpRootCaFile, keychainFile.FullName, data.CancellationToken)
                        .ConfigureAwait(false);
                    var tmpIntermediateFile = Path.GetTempPath() + Guid.NewGuid() + ".cer";
                    await File.WriteAllBytesAsync(tmpIntermediateFile, intermediateCertificateData);
                    await keychain.ImportCertificateAsync(tmpIntermediateFile, keychainFile.FullName,
                            data.CancellationToken)
                        .ConfigureAwait(false);
                    AnsiConsole.WriteLine($" Root & Intermediate Certificates Extracted and imported.");

                    //2. Extract Pem from P12
                    var openSsl = new OpenSsl();
                    var tempPemfile = Path.GetTempPath() + Guid.NewGuid() + ".pem";
                    await openSsl.ExtractPemFile(tmpP12File, settings.CertificatePassphrase, tempPemfile,
                            data.CancellationToken)
                        .ConfigureAwait(false);
                    AnsiConsole.WriteLine($" PEM Certificate Extracted from P12.");

                    //3. Verify Pem
                    var result = await keychain.VerifyCertificate(tempPemfile, keychainFile.FullName,
                        data.CancellationToken);
                    if (!result.Success)
                    {
                        result.OutputFailure();
                        return this.ExitCode(result.Success, -1);
                    }

                    AnsiConsole.WriteLine($" Certificate Verified.");
                }
                
                AnsiConsole.WriteLine($" Done.");
            }
        }

        if (settings.InstallApiPrivateKey)
        {
            var apiPrivateKey = settings.GetStringFromFileOrEnvironmentOrString(settings.ApiPrivateKey);

            var xcrun = new XCRun();
            xcrun.PrivateKeysDirectory = settings.ApiPrivateKeyDirectory;

            var keyPath = await xcrun.InstallPrivateKey(settings.ApiKeyId, apiPrivateKey!);
            AnsiConsole.WriteLine($"Saved API Key to: {keyPath.FullName}");
        }

        if (settings.InstallProfiles())
        {
            this.SetOutputVariable("BundleIdentifiers", string.Join(",", settings.BundleIdentifiers));
            for (int i = 0; i < settings.BundleIdentifiers.Length; i++)
                this.SetOutputVariable($"BundleIdentifier{i}", settings.BundleIdentifiers[i]);


            if (settings.ProfileTypes.Length > 0)
            {
                this.SetOutputVariable("ProfileTypes", string.Join(",", settings.ProfileTypes));
                for (int i = 0; i < settings.ProfileTypes.Length; i++)
                    this.SetOutputVariable($"ProfileType{i}", settings.ProfileTypes[i].ToString());
            }

            AnsiConsole.WriteLine(
                $"Installing Provisioning Profiles for: {string.Join(", ", settings.BundleIdentifiers)}...");

            var profileResults = new List<ProvisioningProfile>();

            var apiPrivateKey = settings.GetStringFromFileOrEnvironmentOrString(settings.ApiPrivateKey);

            var appStoreConnect =
                new AppStoreConnectClient(new AppStoreConnectConfiguration(settings.ApiKeyId, settings.ApiIssuerId,
                    apiPrivateKey!));

            var profiles = await appStoreConnect.ListProfilesAsync(
                    filterProfileState: new[] { ProfileState.ACTIVE },
                    filterProfileType: settings.ProfileTypes.Length > 0 ? settings.ProfileTypes : null,
                    include: "bundleId",
                    limit: 200)
                .ConfigureAwait(false);

            foreach (var profile in profiles.Data)
            {
                var bundleId = await appStoreConnect.GetRelatedBundleIdAsync(profile.Id).ConfigureAwait(false);
                // Get the Bundle ID for the profile
                var profileBundleId = bundleId.Data.Attributes;

                if (settings.BundleIdentifiers.Length > 0)
                {
                    if (settings.BundleIdentifiers.Any(b => profileBundleId?.IdentifierMatches(b) ?? false))
                        profileResults.Add(new ProvisioningProfile(profile.Attributes, profileBundleId));
                }
                else
                {
                    profileResults.Add(new ProvisioningProfile(profile.Attributes, profileBundleId));
                }
            }


            foreach (var p in profileResults)
            {
                AnsiConsole.WriteLine(
                    $"  - Installing profile: {p.Profile.Name} ({p.Profile.Uuid}) - {p.Profile.ProfileTypeValue} -  {p.Profile.PlatformValue} - BundleID: {p.BundleId?.Identifier}");
            }


            if (profileResults.Count > 0)
            {
                foreach (var p in profileResults)
                {
                    var profileData = Convert.FromBase64String(p.Profile.ProfileContent);
                    await ProvisioningProfiles.InstallProfileAsync(profileData, settings.ProfilePath)
                        .ConfigureAwait(false);
                }
            }

            AnsiConsole.WriteLine($"Done - {profileResults.Count} Provisioning Profiles Installed.");
        }

        return this.ExitCode();
    }
}

public class ProvisionCiCommandSettings : CommandSettings
{
    [Description(
        "Base64 encoded certificate data, certificate filename, or environment variable name with base 64 encoded certificate data")]
    [CommandOption("--certificate")]
    public string Certificate { get; set; } = string.Empty;

    [Description(
        "Base64 encoded root ca certificate data, certificate filename, or environment variable name with base 64 encoded certificate data")]
    [CommandOption("--rootcacertificate")]
    public string RootCACertificate { get; set; } = string.Empty;

    [Description(
        "Base64 encoded root ww authority data, certificate filename, or environment variable name with base 64 encoded certificate data")]
    [CommandOption("--intermediatecertificate")]
    public string IntermediateCertificate { get; set; } = string.Empty;

    [Description("Certificate's passphrase")]
    [CommandOption("--certificate-passphrase <passphrase>")]
    public string CertificatePassphrase { get; set; } = string.Empty;

    internal bool ImportCert()
        => !string.IsNullOrWhiteSpace(Certificate) &&
           this.GetBytesFromFileOrEnvironmentOrBase64String(Certificate) is not null;

    [Description("Keychain name to import into")]
    [CommandOption("--keychain <keychain>")]
    public string? Keychain { get; set; }

    [Description("Keychain password")]
    [CommandOption("--keychain-password <password>")]
    public string? KeychainPassword { get; set; } = string.Empty;

    internal bool CreateKeychain()
        => Keychain != AppleDev.Keychain.DefaultKeychain;

    [Description("Allows any app read permission")]
    [CommandOption("--keychain-allow-any-app-read")]
    public bool AllowAnyAppRead { get; set; } = true;

    [Description("Allows any app read permission")]
    [CommandOption("--keychain-disallow-any-app-read")]
    public bool DisallowAllowAnyAppRead { get; set; }


    [Description("App bundle identifier(s) to match provisioning profiles for")]
    [CommandOption("--bundle-identifier <BUNDLE_IDENTIFIER>")]
    public string[] BundleIdentifiers { get; set; } = new string[0];

    internal bool InstallProfiles()
        => BundleIdentifiers?.Any() ?? false;

    [Description("Provisioning profile type(s) to match / download")]
    [CommandOption("--profile-type <PROFILE_TYPE>")]
    [DefaultValue(ProfileType.Unknown)]
    [TypeConverter(typeof(StringEnumTypeConverter<ProfileType>))]
    public ProfileType[] ProfileTypes { get; set; } = new ProfileType[0];


    [Description("Override default directory to save downloaded profiles to")]
    [CommandOption("--profile-path <directory>")]
    [TypeConverter(typeof(DirectoryInfoTypeConverter))]
    public DirectoryInfo? ProfilePath { get; set; }


    [Description(
        "AppStoreConnect Key ID value or environment variable name (Can also be supplied via the 'APP_STORE_CONNECT_KEY_ID' environment variable)")]
    [CommandOption("--api-key-id|--app-store-connect-key-id <key_id>")]
    public string ApiKeyId { get; set; }
        = Environment.GetEnvironmentVariable("APP_STORE_CONNECT_KEY_ID") ?? string.Empty;

    [Description(
        "AppStoreConnect Issuer ID value or environment variable name (Can also be supplied via the 'APP_STORE_CONNECT_ISSUER_ID' environment variable)")]
    [CommandOption("--api-issuer-id|--app-store-connect-issuer-id <issuer_id>")]
    public string ApiIssuerId { get; set; }
        = Environment.GetEnvironmentVariable("APP_STORE_CONNECT_ISSUER_ID") ?? string.Empty;

    [Description(
        "AppStoreConnect Private Key (.p8) filename or environment variable name with (.p8) file contents (Can also be supplied via the 'APP_STORE_CONNECT_PRIVATE_KEY' environment variable)")]
    [CommandOption("--api-private-key|--app-store-connect-private-key <private_key>")]
    public string ApiPrivateKey { get; set; }
        = Environment.GetEnvironmentVariable("APP_STORE_CONNECT_PRIVATE_KEY") ?? string.Empty;

    [Description(
        "If true, installs the --api-private-key AppStoreConnect Private Key (.p8) (if specified) to the --api-private-key-dir location.  You are responsible for removing this key if you do not want it to persist.")]
    [CommandOption("--install-api-private-key")]
    public bool InstallApiPrivateKey { get; set; }
        = false;

    [Description(
        "Specifies a path to save the AppStoreConnect Private Key (.p8) file to if --install-api-private-key is true.  Default directory path is \"~/private_keys/\".")]
    [CommandOption("--api-private-key-dir <directory_path>")]
    public DirectoryInfo ApiPrivateKeyDirectory { get; set; }
        = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "private_keys"));

    public override ValidationResult Validate()
    {
        if (this.InstallProfiles())
        {
            if (string.IsNullOrEmpty(ApiKeyId))
                return ValidationResult.Error("--api-key-id is required");

            if (string.IsNullOrEmpty(ApiIssuerId))
                return ValidationResult.Error("--api-issuer-id is required");

            if (string.IsNullOrEmpty(ApiPrivateKey))
                return ValidationResult.Error("--api-private-key is required");
        }

        if (this.InstallApiPrivateKey)
        {
            if (string.IsNullOrEmpty(ApiPrivateKey))
                return ValidationResult.Error("--api-private-key is required");

            if (string.IsNullOrEmpty(ApiKeyId))
                return ValidationResult.Error("--api-key-id is required");
        }

        return base.Validate();
    }
}