# LnAddress.Net

**LnAddress.Net** is a service that allows you to receive Lightning payments using any username at your domain. For
example: `username@your.domain`.

## Overview

- **Docker Image**: A pre-built Docker image is available
  at [ngoline/lnaddress.net](https://hub.docker.com/r/ngoline/lnaddress.net).
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

    - Choose the Lightning backend (`LIGHTNING__BACKEND`): `lnd` (default) or `cln` (Core Lightning).
    - Configure connection details to your LND or Core Lightning instance.
    - Adjust limits for payment amounts or comment fields.

3. **Set Up Nginx (Optional)**:

   For a production setup, use [example.nginx](example.nginx) as a guide to set up a reverse proxy with TLS termination.

## Connecting to LND

This is the default backend (`LIGHTNING__BACKEND=lnd`). To enable Lightning payments, you need to connect
LnAddress.Net to your LND instance. You will need:

- The **TLS certificate** (`tls.cert`)
- The **admin.macaroon** in base64 format
- The **LND RPC server endpoint**

**Steps to Obtain LND Credentials**:

1. **TLS Certificate**:

   Extract the certificate content between the `-----BEGIN CERTIFICATE-----` and `-----END CERTIFICATE-----` lines.

   ```bash
   cat /.lnd/tls.cert
   ```

   Copy only the certificate portion without the header and footer lines.

2. **Invoice Macaroon**:

   Convert the `invoice.macaroon` to a single-line base64 string:

   ```bash
   base64 /.lnd/data/chain/bitcoin/mainnet/invoice.macaroon | tr -d '\n'
   ```

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

Set `LIGHTNING__BACKEND=cln` to use Core Lightning instead of LND. LnAddress.Net talks to the
[cln-grpc](https://github.com/ElementsProject/lightning/tree/master/plugins/grpc-plugin) plugin, which authenticates
clients with mutual TLS. You will need:

- The **CA certificate** (`ca.pem`)
- A **client certificate and key** signed by that CA (`client.pem` and `client-key.pem`)
- The **cln-grpc endpoint**

**Steps to Obtain CLN Credentials**:

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

   Set your cln-grpc endpoint, for example:

   ```bash
   https://<cln-ip>:9736
   ```

   The server certificate is validated against `ca.pem`, so the hostname does not need to match the certificate.

**Environment variables**:

```bash
docker run -d \
  -p 80:80 \
  -e LIGHTNING__BACKEND=cln \
  -e CLN__RPCADDRESS="https://<cln-ip>:9736" \
  -e CLN__CACERT="<base64_ca_pem>" \
  -e CLN__CLIENTCERT="<base64_client_pem>" \
  -e CLN__CLIENTKEY="<base64_client_key_pem>" \
  ngoline/lnaddress.net:latest
```

## Supported LNURL Specs

- [LUD-06](https://github.com/lnurl/luds/blob/luds/06.md): `payRequest` base spec.
- [LUD-12](https://github.com/lnurl/luds/blob/luds/12.md): comments in `payRequest`, enabled by
  `INVOICE__MAXCOMMENTALLOWED`.
- [LUD-16](https://github.com/lnurl/luds/blob/luds/16.md): Lightning Address, `username@your.domain`.
- [LUD-21](https://github.com/lnurl/luds/blob/luds/21.md): `verify` base spec. The callback response carries a
  `verify` URL (`https://your.domain/lnurl/verify/<payment_hash>`) that anyone holding the invoice can poll to learn
  whether it was settled. Once paid, the response includes the preimage:

  ```json
  {"status": "OK", "settled": true, "preimage": "<hex>", "pr": "lnbc..."}
  ```

  Unknown payment hashes return `{"status": "ERROR", "reason": "Not found"}`. The endpoint needs no authentication.
  It looks up the hash on the backend node, so it answers for **any** invoice on that node, not only the ones
  LnAddress created. For invoices LnAddress issued, it only reveals data the payer already holds.

  > **Use a dedicated node.** If other apps (a shop, a wallet, ...) create invoices on the same node, anyone who
  > learns one of their payment hashes can read the full bolt11 (amount and description), and once it is paid, the
  > preimage, which serves as proof of payment. Run LnAddress against a node used only for it, or don't expose
  > `/lnurl/verify` publicly.

## Default Settings

- **MinSendable**: 1,000 millisatoshis (1 satoshi)
- **MaxSendable**: 100,000,000 millisatoshis (100,000 satoshis)
- **MaxCommentAllowed**: 0 (no comments accepted)

If these defaults don’t meet your needs, adjust them via environment variables as shown
in [docker-compose.yml](docker-compose.yml).

## Running the Service

Once you have your environment variables set and Docker is ready, you can run:

```bash
docker-compose up -d
```

or, if running standalone:

```bash
docker run -d \
  -p 80:80 \
  -e LND__CERT="<base64_tls_cert>" \
  -e LND__MACAROON="<base64_admin_macaroon>" \
  -e LND__RPCADDRESS="https://<lnd-ip>:10009" \
  ngoline/lnaddress.net:latest
```

Replace the environment variables with your actual values.

---

**You’re now set up to receive Lightning payments via username addresses on your domain!**