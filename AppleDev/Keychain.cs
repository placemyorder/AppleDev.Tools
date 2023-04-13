﻿using CliWrap;
using CliWrap.Builders;
using System.Text;

namespace AppleDev;

public class Keychain
{
	public const string DefaultKeychain = "login.keychain-db";

	public FileInfo Locate(string keychain)
	{
		if (Path.IsPathRooted(keychain))
			return new FileInfo(keychain);

		if (!keychain.EndsWith(".keychain-db"))
		{
			if (keychain.EndsWith(".keychain"))
				keychain += "-db";
			else
				keychain += ".keychain-db";
		}

		return new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Keychains", keychain));
	}

	public Task<ProcessResult> UpdateKeychainListAsync(string keychain = DefaultKeychain, CancellationToken cancellationToken = default)
	{
		var keychainPath = Locate(keychain);

		return WrapSecurityAsync(args =>
			{
				args.Add("list-keychains");
				args.Add("-d");
				args.Add("user");
				args.Add("-s");
				args.Add(keychainPath.FullName);

				if (!Path.GetFileName(keychainPath.FullName).Equals(DefaultKeychain))
					args.Add(Locate(DefaultKeychain).FullName);
			}, cancellationToken);
	}

	public Task<ProcessResult> DeleteKeychainAsync(string keychain = DefaultKeychain, CancellationToken cancellationToken = default)
		=> WrapSecurityAsync(new[] { "delete-keychain", Locate(keychain).FullName }, cancellationToken);

	public Task<ProcessResult> ImportPkcs12Async(string file, string passphrase, string keychain = DefaultKeychain, bool allowReadToAnyApp = false, CancellationToken cancellationToken = default)
		=> WrapSecurityAsync(args =>
			{
				args.Add("import");
				args.Add(file);
				args.Add("-k");
				args.Add(Locate(keychain).FullName);
				args.Add("-f");
				args.Add("pkcs12");

				// Allows any app to read the keys, not a good idea if keychain is retained or was not a throwaway VM
				if (allowReadToAnyApp)
					args.Add("-A");

				args.Add("-T");
				args.Add("/usr/bin/codesign");
				args.Add("-T");
				args.Add("/usr/bin/security");
				args.Add("-P");
				args.Add(passphrase);
			}, cancellationToken);

	public Task<ProcessResult> SetPartitionListAsync(string password, string keychain = DefaultKeychain, CancellationToken cancellationToken = default)
		=> WrapSecurityAsync(new[] { 
				"set-key-partition-list",
				"-S",
				"apple-tool:,apple:",
				"-k",
				password,
				Locate(keychain).FullName
			}, cancellationToken);

	public Task<ProcessResult> UnlockKeychainAsync(string password, string keychain = DefaultKeychain, CancellationToken cancellationToken = default)
		=> WrapSecurityAsync(new[] { 
				"unlock-keychain", 
				"-p", 
				password, 
				Locate(keychain).FullName 
			}, cancellationToken);

	public async Task<ProcessResult> CreateKeychainAsync(string keychain, string? password = null, CancellationToken cancellationToken = default)
	{
		var createResult = await WrapSecurityAsync(args =>
		{
			args.Add("create-keychain");

			if (!string.IsNullOrEmpty(password))
			{
				args.Add("-p");
				args.Add(password);
			}

			args.Add(Locate(keychain).FullName);
		}, cancellationToken).ConfigureAwait(false);

		if (!createResult.Success)
			return createResult;

		var setResult = await WrapSecurityAsync(new[] { 
				"set-keychain-settings", 
				"-lut", 
				"21600", 
				Locate(keychain).FullName 
			}, cancellationToken).ConfigureAwait(false);

		return new ProcessResult(setResult.Success, createResult.StdOut + Environment.NewLine + setResult.StdOut, createResult.StdErr + Environment.NewLine + setResult.StdErr);
	}

	Task<ProcessResult> WrapSecurityAsync(string[] args, CancellationToken cancellationToken = default)
		=> WrapSecurityAsync(b =>
		{
			foreach (var a in args)
				b.Add(a);
		}, cancellationToken);

	async Task<ProcessResult> WrapSecurityAsync(Action<ArgumentsBuilder> args, CancellationToken cancellationToken = default)
	{
		var success = false;
		var stdout = new StringBuilder();
		var stderr = new StringBuilder();
		try
		{
			var argBuilder = new ArgumentsBuilder();
			args(argBuilder);
			var argstr = argBuilder.Build();
			
			Debug.WriteLine("/usr/bin/security " + argstr);
			
			var r = await Cli.Wrap("/usr/bin/security")
				.WithArguments(argstr)
				.WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout))
				.WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
				.ExecuteAsync(cancellationToken)
				.ConfigureAwait(false);

			success = r.ExitCode == 0;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			stderr.AppendLine(ex.Message);
		}

		return new ProcessResult(success, stdout.ToString(), stderr.ToString());
	}
}
