using System.Diagnostics;
using System.Text;
using CliWrap;
using CliWrap.Builders;

namespace AppleDev;

public class OpenSsl
{
    public Task<ProcessResult> ExtractPemFile(string p12filePath, string certificateparaphrase, string outputFilePath,
        CancellationToken cancellationToken = default)
        => WrapOpenSslAsync(args =>
        {
            args.Add("pkcs12");
            args.Add("-in");
            args.Add(p12filePath);
            args.Add("-clcerts");
            args.Add("-nokeys");
            args.Add("-out");
            args.Add(outputFilePath);
            args.Add("-passin");
            args.Add($"pass:{certificateparaphrase}");
        }, cancellationToken);


    async Task<ProcessResult> WrapOpenSslAsync(Action<ArgumentsBuilder> args,
        CancellationToken cancellationToken = default)
    {
        var success = false;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            var argBuilder = new ArgumentsBuilder();
            args(argBuilder);
            var argstr = argBuilder.Build();

            Debug.WriteLine("/usr/bin/openssl " + argstr);

            var r = await Cli.Wrap("/usr/bin/openssl")
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
            stderr.AppendLine(ex.ToString());
        }

        return new ProcessResult(success, stdout.ToString(), stderr.ToString());
    }
}