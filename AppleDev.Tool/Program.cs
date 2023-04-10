﻿using AppleDev.Tool.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
	if (!cts.IsCancellationRequested)
		cts.Cancel();
};

var data = new CommandContextData
{
	CancellationToken = cts.Token
};

var app = new CommandApp();
app.Configure(config =>
{
	config
	.AddBranch("simulator", sim =>
	{
		sim.AddCommand<ListSimulatorsCommand>("list")
			.WithData(data)
			.WithDescription("Lists Simulators")
			.WithExample(new[] { "simulator", "list" })
			.WithExample(new[] { "simulator", "list", "--available" })
			.WithExample(new[] { "simulator", "list", "--booted" });

		sim.AddCommand<BootSimulatorCommand>("boot")
			.WithData(data)
			.WithDescription("Boots a simulator")
			.WithExample(new[] { "simulator", "boot", "1234-1112-12242-12322-1111232" })
			.WithExample(new[] { "simulator", "boot", "--wait", "1234-1112-12242-12322-1111232" })
			.WithExample(new[] { "simulator", "boot", "--wait", "--timeout", "240", "1234-1112-12242-12322-1111232" });
		
		sim.AddCommand<ShutdownSimulatorCommand>("shutdown")
			.WithData(data)
			.WithDescription("Shuts down simulator(s)")
			.WithExample(new[] { "simulator", "shutdown", "1234-1112-12242-12322-1111232" })
			.WithExample(new[] { "simulator", "shutdown", "all" })
			.WithExample(new[] { "simulator", "shutdown", "Booted" });
		
		sim.AddCommand<EraseSimulatorCommand>("erase")
			.WithData(data)
			.WithDescription("Erases / resets simulator(s)")
			.WithExample(new[] { "simulator", "erase", "1234-1112-12242-12322-1111232" })
			.WithExample(new[] { "simulator", "erase", "all" })
			.WithExample(new[] { "simulator", "erase", "Booted" });

		sim.AddCommand<ScreenshotSimulatorCommand>("screenshot")
			.WithData(data)
			.WithDescription("Records a screenshot")
			.WithExample(new[] { "simulator", "screenshot", "1234-1112-12242-12322-1111232" })
			.WithExample(new[]
				{ "simulator", "screenshot", "--output", "~/screenshot.png", "1234-1112-12242-12322-1111232" });
	});

	config.AddBranch("device", sdkBranch =>
	{
		
	});

	config.AddBranch("keychain", sdkBranch =>
	{
		
	});
});


try
{
	app.Run(args);
}
catch (Exception ex)
{
	AnsiConsole.WriteException(ex);
}