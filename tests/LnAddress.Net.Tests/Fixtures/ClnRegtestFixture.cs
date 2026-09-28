using System.Diagnostics;

namespace LnAddress.Net.Tests.Fixtures;

/// <summary>
/// Starts a regtest bitcoind and two Core Lightning nodes using tests/Docker/cln/docker-compose.yml:
/// the node under test (with cln-grpc enabled) and a payer node with a channel to it. Mines enough
/// blocks for the nodes to sync and exposes the mTLS identity needed to talk to the gRPC plugin.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class ClnRegtestFixture : IDisposable
{
    private const string BitcoindContainer = "lnaddress-cln-bitcoind";
    private const string ClnContainer = "lnaddress-cln-node";
    private const string PayerContainer = "lnaddress-cln-payer";
    private const string BitcoinCli = "bitcoin-cli -regtest -rpcuser=lnaddress -rpcpassword=lnaddress";
    private const string LightningCli = "lightning-cli --regtest";
    private const int InitialBlocks = 101;

    private static readonly string ComposeDirectory =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Docker/cln"));

    public string RpcAddress { get; } = "https://127.0.0.1:19736";
    public string CaCertPem { get; private set; } = string.Empty;
    public string ClientCertPem { get; private set; } = string.Empty;
    public string ClientKeyPem { get; private set; } = string.Empty;
    public string NodePubKeyHex { get; private set; } = string.Empty;

    public ClnRegtestFixture()
    {
        SetupNetwork().Wait();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Run("docker", $"compose -f \"{Path.Combine(ComposeDirectory, "docker-compose.yml")}\" down -v", throwOnError: false).Wait();
    }

    private async Task SetupNetwork()
    {
        var composeFile = Path.Combine(ComposeDirectory, "docker-compose.yml");
        await Run("docker", $"compose -f \"{composeFile}\" down -v", throwOnError: false);
        await Run("docker", $"compose -f \"{composeFile}\" up -d --wait");

        // Give the nodes a fresh chain tip so bitcoind leaves initial block download
        await Run("docker", $"exec {BitcoindContainer} {BitcoinCli} createwallet miner", throwOnError: false);
        var minerAddress = (await Run("docker", $"exec {BitcoindContainer} {BitcoinCli} -rpcwallet=miner getnewaddress")).Trim();
        await MineBlocks(InitialBlocks, minerAddress);

        NodePubKeyHex = await WaitForNodeSync(ClnContainer, InitialBlocks);
        await WaitForNodeSync(PayerContainer, InitialBlocks);

        CaCertPem = await Run("docker", $"exec {ClnContainer} cat /root/.lightning/regtest/ca.pem");
        ClientCertPem = await Run("docker", $"exec {ClnContainer} cat /root/.lightning/regtest/client.pem");
        ClientKeyPem = await Run("docker", $"exec {ClnContainer} cat /root/.lightning/regtest/client-key.pem");

        await OpenChannelFromPayer(minerAddress);
    }

    /// <summary>
    /// Pays a BOLT11 invoice from the payer node and returns the lowercase hex preimage.
    /// </summary>
    public async Task<string> PayAsync(string bolt11)
    {
        var output = await Run("docker", $"exec {PayerContainer} {LightningCli} pay {bolt11}");
        return ExtractJsonString(output, "payment_preimage");
    }

    private async Task OpenChannelFromPayer(string minerAddress)
    {
        // Fund the payer's on-chain wallet
        var payerAddress = ExtractJsonString(await Run("docker", $"exec {PayerContainer} {LightningCli} newaddr"), "bech32");
        await Run("docker", $"exec {BitcoindContainer} {BitcoinCli} -rpcwallet=miner sendtoaddress {payerAddress} 1");
        await MineBlocks(1, minerAddress);
        await WaitFor("payer wallet funds",
            () => Run("docker", $"exec {PayerContainer} {LightningCli} listfunds", throwOnError: false),
            output => output.Contains("\"status\": \"confirmed\""));

        // Open a channel from the payer to the node under test
        await Run("docker", $"exec {PayerContainer} {LightningCli} connect {NodePubKeyHex}@cln:9735");
        await WaitFor("channel funding",
            () => Run("docker", $"exec {PayerContainer} {LightningCli} fundchannel {NodePubKeyHex} 5000000", throwOnError: false),
            output => output.Contains("\"txid\""));
        await MineBlocks(6, minerAddress);
        await WaitFor("channel to become usable",
            () => Run("docker", $"exec {PayerContainer} {LightningCli} listpeerchannels", throwOnError: false),
            output => output.Contains("\"state\": \"CHANNELD_NORMAL\""));
    }

    private static Task MineBlocks(int count, string address) =>
        Run("docker", $"exec {BitcoindContainer} {BitcoinCli} generatetoaddress {count} {address}");

    private static async Task<string> WaitForNodeSync(string container, int blockHeight)
    {
        var output = await WaitFor($"{container} to sync",
            () => Run("docker", $"exec {container} {LightningCli} getinfo", throwOnError: false),
            o => o.Contains($"\"blockheight\": {blockHeight}") && !o.Contains("warning_"));
        return ExtractJsonString(output, "id");
    }

    private static async Task<string> WaitFor(string what, Func<Task<string>> probe, Func<string, bool> isReady)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var output = await probe();
            if (isReady(output))
            {
                return output;
            }

            await Task.Delay(1_000);
        }

        throw new Exception($"Timed out waiting for {what}.");
    }

    private static string ExtractJsonString(string json, string key)
    {
        var marker = $"\"{key}\": \"";
        var start = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = json.IndexOf('"', start);
        return json[start..end];
    }

    private static async Task<string> Run(string fileName, string arguments, bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo) ?? throw new Exception($"Could not start {fileName}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (throwOnError && process.ExitCode != 0)
        {
            throw new Exception($"{fileName} {arguments} failed ({process.ExitCode}): {await stderr}");
        }

        return await stdout;
    }
}
