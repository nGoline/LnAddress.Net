# LnAddress.Net

**LnAddress.Net** is a service that allows you to receive Lightning payments using any username at your domain. For
example: `username@your.domain`.

## Overview

- **Docker Image**: A pre-built multi-arch image (`amd64`, `arm64`, `arm/v7`) is available
  at [ngoline/lnaddress.net](https://hub.docker.com/r/ngoline/lnaddress.net).
- **Binaries**: Self-contained builds for Linux, macOS and Windows are attached to each
  [GitHub release](https://github.com/nGoline/LnAddress.Net/releases); no .NET runtime required.
- **Configuration Reference**: Review the [docker-compose.yml](docker-compose.yml) file for a complete list of
  environment variables and configuration options.
- **Reverse Proxy Setup**: An example Nginx configuration is provided in [example.nginx](example.nginx).

## Getting Started

1. **Pull the Docker Image**:

   ```bash
   docker pull ngoline/lnaddress.net:latest
   ```

2. **Review Configuration Variables**:

   Check the [docker-compose.yml](docker-compose.yml) file for environment variables. These variables allow you to:

    - Choose the Lightning backend (`LIGHTNING__BACKEND`): `lnd` (default), `clnrest` (Core Lightning, recommended)
      or `cln` (Core Lightning over gRPC, legacy).
    - Configure connection details to your LND or Core Lightning node.
    - Adjust limits for payment amounts or comment fields.

3. **Set Up Nginx (Optional)**:

   For a production setup, use [example.nginx](example.nginx) as a guide to set up a reverse proxy. LNURL requires
   HTTPS, and LnAddress.Net builds its callback and verify URLs as `https://<Host header>/...`, so the proxy must
   terminate TLS (for example with `certbot --nginx`) and forward the original `Host` header.

## Connecting to LND

This is the default backend (`LIGHTNING__BACKEND=lnd`). To enable Lightning payments, you need to connect
LnAddress.Net to your LND instance. You will need:

- The **TLS certificate** (`tls.cert`)
- A **macaroon** baked with the permissions the service needs, in base64 format
- The **LND RPC server endpoint**

**Steps to Obtain LND Credentials**:

1. **TLS Certificate**:

   Take the base64 body of `tls.cert`, without the `-----BEGIN/END CERTIFICATE-----` lines, as a single line:

   ```bash
   grep -v CERTIFICATE ~/.lnd/tls.cert | tr -d '\n'
   ```

   Unlike the CLN settings, `LND__CERT` does not accept raw PEM text.

2. **Macaroon**:

   Bake a macaroon that grants only what the service needs: `invoices:write` to create invoices,
   `invoices:read` to look them up for the verify endpoint, and `info:read` for `/health`:

   ```bash
   lncli bakemacaroon --save_to=lnaddress.macaroon invoices:read invoices:write info:read
   ```

   Then convert it to a single-line base64 string:

   ```bash
   base64 lnaddress.macaroon | tr -d '\n'
   ```

   > Don't use the stock `invoice.macaroon`: it lacks `info:read`, so invoices work but `/health` fails and the
   > Docker healthcheck marks the container unhealthy. Avoid `admin.macaroon` too, since it grants full control of
   > the node, including spending funds.

3. **RPC Server URL**:

   Set your LND RPC endpoint, for example:

   ```bash
   https://<lnd-ip>:10009 
   ```

   Ensure your `lnd.conf` includes:

   ```ini
   rpclisten=0.0.0.0:10009
   ```

   This makes LND’s RPC interface accessible to LnAddress.Net.

## Connecting to Core Lightning (CLN)

LnAddress.Net can reach Core Lightning in two ways:

- **clnrest with a rune (recommended)**: `LIGHTNING__BACKEND=clnrest`. The rune can be restricted to exactly what
  LnAddress.Net needs, so a leaked rune can't move funds.
- **cln-grpc with mutual TLS**: `LIGHTNING__BACKEND=cln`. cln-grpc can't restrict a client: its certificate grants full
  control of the node. Kept for existing setups; see [Legacy: cln-grpc](#legacy-cln-grpc).

### clnrest with a restricted rune

You will need:

- The **clnrest endpoint**
- A **rune** restricted to the calls LnAddress.Net makes
- The node's **CA certificate** (`ca.pem`) to validate clnrest's TLS certificate

1. **Enable clnrest**:

   Add the following to your CLN config (or pass them as command line flags) and restart `lightningd`:

   ```ini
   clnrest-port=3010
   clnrest-host=0.0.0.0
   ```

   Leave `clnrest-host` unset if LnAddress.Net runs on the same host as CLN (clnrest listens on `127.0.0.1` by
   default); otherwise it must be an IP address. clnrest serves HTTPS with a certificate issued by the node's CA:
   `ca.pem` in the network directory (for example `~/.lightning/bitcoin/`), the same CA cln-grpc uses.

2. **Create the rune**:

   ```bash
   lightning-cli createrune restrictions='[["method=getinfo","method=invoice","method=listinvoices"],["method/invoice","pnamelabel^lnaddress-"],["method/listinvoices","pnamepayment_hash/"]]'
   ```

   Each inner list is a set of alternatives, and all of them must hold. The rune can only:

   - call `getinfo`, `invoice` and `listinvoices`;
   - create invoices whose label starts with `lnaddress-`, the prefix LnAddress.Net uses;
   - look up invoices by `payment_hash`, one at a time, so it can't list your other invoices.

   Everything else, such as `pay`, `withdraw`, `listfunds` or `createrune`, is rejected. Copy the `rune` value from
   the output. To revoke it later, find its `unique_id` with `lightning-cli showrunes` and run
   `lightning-cli blacklistrune <unique_id>`.

3. **CA certificate**:

   ```bash
   base64 ~/.lightning/bitcoin/ca.pem | tr -d '\n'
   ```

   Raw PEM text is accepted as well. The server certificate is validated against this CA, so the hostname doesn't
   need to match the certificate. Leave `CLNREST__CACERT` unset only if clnrest sits behind a proxy with a publicly
   trusted certificate.

**Environment variables**:

```bash
docker run -d \
  -p 8080:8080 \
  -e LIGHTNING__BACKEND=clnrest \
  -e CLNREST__URL="https://<cln-ip>:3010" \
  -e CLNREST__RUNE="<rune>" \
  -e CLNREST__CACERT="<base64_ca_pem>" \
  ngoline/lnaddress.net:latest
```

> [!WARNING]
> **Don't expose clnrest or cln-grpc to the public internet.** The rune above can't spend funds, but anyone holding
> it can still create invoices on your node, and the clnrest port accepts any other rune you have issued, including
> unrestricted ones.
>
> - Run LnAddress.Net next to `lightningd` on a private Docker network and don't publish the clnrest or gRPC port at
>   all (`CLNREST__URL=https://lightningd:3010`).
> - Only proxy `/.well-known/lnurlp/` and `/lnurl/` to LnAddress.Net, as [example.nginx](example.nginx) does.
> - Treat the rune like a password.

### Legacy: cln-grpc

`LIGHTNING__BACKEND=cln` talks to the
[cln-grpc](https://github.com/ElementsProject/lightning/tree/master/plugins/grpc-plugin) plugin, which authenticates
clients with mutual TLS. You will need:

- The **CA certificate** (`ca.pem`)
- A **client certificate and key** signed by that CA (`client.pem` and `client-key.pem`)
- The **cln-grpc endpoint**

> [!CAUTION]
> **The cln-grpc client certificate grants full control of the node, funds included.** cln-grpc accepts any
> certificate signed by the node's CA and can't restrict it to certain methods, unlike LND macaroons or clnrest
> runes. LnAddress.Net only calls `invoice`, `listinvoices` and `getinfo`, but anyone holding `client.pem` and
> `client-key.pem` can do anything, including spending funds. Prefer [clnrest](#clnrest-with-a-restricted-rune); if you
> stay on cln-grpc, follow the warning above and treat the `CLN__*` environment variables like a hot wallet's keys.

1. **Enable the gRPC plugin**:

   Add the following to your CLN config (or pass them as command line flags) and restart `lightningd`:

   ```ini
   grpc-port=9736
   grpc-host=0.0.0.0
   ```

   Leave `grpc-host` unset if LnAddress.Net runs on the same host as CLN (the plugin listens on localhost by
   default). On first start the plugin generates `ca.pem`, `client.pem` and `client-key.pem` in the network
   directory, for example `~/.lightning/bitcoin/`.

2. **Certificates**:

   Convert each PEM file to a single-line base64 string:

   ```bash
   base64 ~/.lightning/bitcoin/ca.pem | tr -d '\n'
   base64 ~/.lightning/bitcoin/client.pem | tr -d '\n'
   base64 ~/.lightning/bitcoin/client-key.pem | tr -d '\n'
   ```

   Raw PEM text (including the `-----BEGIN ...-----` lines) is accepted as well.

3. **RPC Server URL**:

   Set your cln-grpc endpoint, for example `https://<cln-ip>:9736`. The server certificate is validated against
   `ca.pem`, so the hostname does not need to match the certificate.

**Environment variables**:

```bash
docker run -d \
  -p 8080:8080 \
  -e LIGHTNING__BACKEND=cln \
  -e CLN__RPCADDRESS="https://<cln-ip>:9736" \
  -e CLN__CACERT="<base64_ca_pem>" \
  -e CLN__CLIENTCERT="<base64_client_pem>" \
  -e CLN__CLIENTKEY="<base64_client_key_pem>" \
  ngoline/lnaddress.net:latest
```

**Moving from cln-grpc to clnrest**: enable clnrest and create the rune as above, then replace
`LIGHTNING__BACKEND=cln` and the `CLN__*` variables with `LIGHTNING__BACKEND=clnrest` and the `CLNREST__*` ones.
`CLNREST__CACERT` takes the same `ca.pem` as `CLN__CACERT`. Invoices created over either transport carry the same
labels, so `/lnurl/verify` keeps working for invoices issued before the switch. Once nothing else uses cln-grpc,
disable it and delete `client.pem` and `client-key.pem`.

## Supported LNURL Specs

- [LUD-06](https://github.com/lnurl/luds/blob/luds/06.md): `payRequest` base spec. Invoices commit to the metadata
  through their description hash, so wallets that check it accept them.
- [LUD-12](https://github.com/lnurl/luds/blob/luds/12.md): comments in `payRequest`, enabled by
  `INVOICE__MAXCOMMENTALLOWED`. The comment can't go in the invoice without breaking the description hash. On Core
  Lightning it is kept in the invoice label (`lnaddress-<username>-<id>: <comment>`); LND has no such field, so it is
  dropped.
- [LUD-16](https://github.com/lnurl/luds/blob/luds/16.md): Lightning Address, `username@your.domain`. Usernames may use
  `a-z`, `0-9`, `-`, `_` and `.` (case-insensitive, served lowercase); anything else returns `404`.
- [LUD-21](https://github.com/lnurl/luds/blob/luds/21.md): `verify` base spec. The callback response carries a
  `verify` URL (`https://your.domain/lnurl/verify/<payment_hash>`) that anyone holding the invoice can poll to learn
  whether it was settled. Once paid, the response includes the preimage:

  ```json
  {"status": "OK", "settled": true, "preimage": "<hex>", "pr": "lnbc..."}
  ```

  Unknown payment hashes return `{"status": "ERROR", "reason": "Not found"}`. The endpoint needs no authentication.
  For invoices LnAddress issued, it only reveals data the payer already holds. On Core Lightning it only answers for
  invoices LnAddress created (their label starts with `lnaddress-`). On LND it can't tell them apart, so it answers
  for **any** invoice on the node.

  > **LND: use a dedicated node.** If other apps (a shop, a wallet, ...) create invoices on the same LND node, anyone
  > who learns one of their payment hashes can read the full bolt11 (amount and description), and once it is paid,
  > the preimage, which serves as proof of payment. Run LnAddress against a node used only for it, or don't expose
  > `/lnurl/verify` publicly.

## Endpoints

| Endpoint                                | Description                                                            |
|-----------------------------------------|------------------------------------------------------------------------|
| `GET /.well-known/lnurlp/{username}`    | LUD-16 Lightning Address lookup. Any username is accepted.             |
| `GET /lnurl/callback/{username}`        | Creates an invoice. Query: `amount` (millisatoshis), optional `comment`. |
| `GET /lnurl/verify/{payment_hash}`      | LUD-21 verify, see above.                                              |
| `GET /health`                           | `200` when the Lightning node answers `getinfo`, `503`/`500` otherwise.  |

## Docker Image

- Runs as a non-root user and listens on port `8080`. To change the port, set `ASPNETCORE_HTTP_PORTS`, which the
  healthcheck also uses, rather than `ASPNETCORE_URLS`.
- Has a built-in healthcheck that polls `/health` every 30 seconds. The container turns unhealthy when the Lightning
  node can't be reached or isn't synced.
- Logs requests at `Warning` and above only, so the healthcheck doesn't flood the logs. Set
  `Logging__LogLevel__Microsoft.AspNetCore=Information` to see every request.

## Default Settings

- **MinSendable**: 1,000 millisatoshis (1 satoshi)
- **MaxSendable**: 100,000,000 millisatoshis (100,000 satoshis)
- **MaxCommentAllowed**: 0 (no comments accepted)

If these defaults don’t meet your needs, adjust them via environment variables as shown
in [docker-compose.yml](docker-compose.yml).

## Running the Service

Once you have your environment variables set and Docker is ready, you can run:

```bash
docker compose up -d
```

or, if running standalone (the container listens on port `8080`):

```bash
docker run -d \
  -p 8080:8080 \
  -e LND__CERT="<base64_tls_cert>" \
  -e LND__MACAROON="<base64_macaroon>" \
  -e LND__RPCADDRESS="https://<lnd-ip>:10009" \
  ngoline/lnaddress.net:latest
```

Replace the environment variables with your actual values.

### Running the binaries

Download the archive for your platform from the [releases page](https://github.com/nGoline/LnAddress.Net/releases),
check it against `SHA256SUMS`, then set the same environment variables and run it:

```bash
tar xzf lnaddress.net-<version>-linux-x64.tar.gz
ASPNETCORE_URLS=http://127.0.0.1:8080 \
LND__CERT="<base64_tls_cert>" \
LND__MACAROON="<base64_macaroon>" \
LND__RPCADDRESS="https://<lnd-ip>:10009" \
./LnAddress.Net
```

Without `ASPNETCORE_URLS`, the binary listens on `http://localhost:5000`.

## Development

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build
# Unit tests
dotnet test --project tests/LnAddress.Net.Tests --filter-not-namespace "*IntegrationTests*"
# Integration tests: spin up regtest bitcoind + LND / Core Lightning nodes, Docker required
dotnet test --project tests/LnAddress.Net.Tests --filter-namespace "*IntegrationTests*"
```

---

**You’re now set up to receive Lightning payments via username addresses on your domain!**