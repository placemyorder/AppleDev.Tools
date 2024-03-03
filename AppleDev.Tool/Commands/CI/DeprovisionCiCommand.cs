using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AppleDev.Tool.Commands;

public class DeprovisionCiCommand : AsyncCommand<DeprovisionCiCommandSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, DeprovisionCiCommandSettings settings)
	{
		var data = context.GetData();
		
		AnsiConsole.Write(new Rule("Deprovisioning..."));
		
		var keychainName = settings.Keychain ?? "build";
		
		var keychain = new Keychain();
		var keychainFile = keychain.Locate(keychainName);

		if (keychainFile.Exists && !keychainFile.Name.Equals(AppleDev.Keychain.DefaultKeychain))
		{
			AnsiConsole.Write($"Deleting Keychain {keychainFile.Name}...");
			var createResult = await keychain.DeleteKeychainAsync(keychainFile.FullName, data.CancellationToken).ConfigureAwait(false);

			if (!createResult.Success)
			{
				AnsiConsole.WriteLine();
				createResult.OutputFailure("Deleting Keychain Failed");
				return 1;
			}

			if (!string.IsNullOrEmpty(settings.DefaultKeychain))
			{
				var defaultKeychain = new Keychain();
				var defaultKeychainFile = defaultKeychain.Locate(settings.DefaultKeychain);
				AnsiConsole.Write($"Setting Default Keychain {defaultKeychainFile.FullName}...");
				var setDefResult = await keychain
					.SetDefaultKeychainAsync(defaultKeychainFile.FullName, data.CancellationToken)
					.ConfigureAwait(false);

				if (!setDefResult.Success)
				{
					AnsiConsole.WriteLine();
					setDefResult.OutputFailure("Setting Default Keychain Failed");
					return 1;
				}
			}

			AnsiConsole.WriteLine($" Done.");
		}

		return this.ExitCode();
	}
}
public class DeprovisionCiCommandSettings : CommandSettings
{
	[Description("Keychain name to remove")]
	[CommandOption("--keychain <keychain>")]
	public string Keychain { get; set; } = string.Empty;
	
	[Description("Keychain name to set as default")]
	[CommandOption("--defaultkeychain <keychain>")]
	public string DefaultKeychain { get; set; } = string.Empty;
}