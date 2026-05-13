import type { DexAdapter, SwapParams, SwapQuote, UnsignedTransaction } from "../core/types.js";
import type { Result } from "../core/result.js";
import { ok, err } from "../core/result.js";
import { SdkError, SdkErrorCode } from "../core/errors.js";

export interface JupiterConfig {
  /** Jupiter API base URL. Defaults to Ultra API. */
  apiUrl?: string;
}

const JUPITER_QUOTE_API = "https://quote-api.jup.ag/v6";

/**
 * Cross-platform base64 decode that avoids atob dependency.
 */
function base64ToBytes(b64: string): Uint8Array {
  const lookup = new Uint8Array(256).fill(255);
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  for (let i = 0; i < chars.length; i++) lookup[chars.charCodeAt(i)] = i;

  // Strip padding and whitespace
  const stripped = b64.replace(/[\s=]+/g, "");

  // Validate all characters before decoding
  for (let i = 0; i < stripped.length; i++) {
    if ((lookup[stripped.charCodeAt(i)] ?? 255) === 255) {
      throw new Error(`Invalid base64 character '${stripped[i]}' at position ${i}`);
    }
  }

  const outputLen = Math.floor((stripped.length * 3) / 4);
  const out = new Uint8Array(outputLen);

  let byteIndex = 0;
  for (let i = 0; i < stripped.length; i += 4) {
    const a = lookup[stripped.charCodeAt(i)]!;
    const b = lookup[stripped.charCodeAt(i + 1)]!;
    const c = i + 2 < stripped.length ? lookup[stripped.charCodeAt(i + 2)]! : 0;
    const d = i + 3 < stripped.length ? lookup[stripped.charCodeAt(i + 3)]! : 0;

    out[byteIndex++] = (a << 2) | (b >> 4);
    if (i + 2 < stripped.length) out[byteIndex++] = ((b & 0xf) << 4) | (c >> 2);
    if (i + 3 < stripped.length) out[byteIndex++] = ((c & 0x3) << 6) | d;
  }

  return out.slice(0, byteIndex);
}

interface UltraOrderResponse {
  inputMint: string;
  outputMint: string;
  inAmount: string;
  outAmount: string;
  priceImpactPct: string;
  /** Base64-encoded VersionedTransaction ready to sign and submit. */
  transaction: string;
  [key: string]: unknown;
}

export class JupiterAdapter implements DexAdapter {
  readonly chainId = "solana";
  readonly dexName = "jupiter";

  private readonly apiUrl: string;

  constructor(config?: JupiterConfig) {
    this.apiUrl = config?.apiUrl ?? JUPITER_QUOTE_API;
  }

  async getQuote(params: SwapParams): Promise<Result<SwapQuote, SdkError>> {
    try {
      const url = new URL(`${JUPITER_QUOTE_API}/quote`);
      url.searchParams.set("inputMint", params.tokenIn);
      url.searchParams.set("outputMint", params.tokenOut);
      url.searchParams.set("amount", params.amountIn);
      url.searchParams.set("slippageBps", params.slippageBps.toString());

      const resp = await fetch(url.toString());
      if (!resp.ok) {
        const body = await resp.text().catch(() => "");
        return err(
          new SdkError(
            SdkErrorCode.DEX_ERROR,
            `Jupiter quote failed: ${resp.status}${body ? ` — ${body}` : ""}`,
            { chain: "solana" }
          )
        );
      }

      interface QuoteResponse {
        inAmount: string;
        outAmount: string;
        priceImpactPct: string;
        routePlan: any[];
      }
      const data = await resp.json() as QuoteResponse;

      return ok({
        chain: "solana",
        tokenIn: params.tokenIn,
        tokenOut: params.tokenOut,
        amountIn: data.inAmount,
        expectedAmountOut: data.outAmount,
        priceImpact: parseFloat(data.priceImpactPct),
        fee: "0",
        raw: data,
        route: data.routePlan,
      });
    } catch (e) {
      return err(
        new SdkError(SdkErrorCode.DEX_ERROR, `Jupiter quote error: ${String(e)}`, {
          chain: "solana",
          cause: e instanceof Error ? e : undefined,
        })
      );
    }
  }

  async buildSwapTransaction(
    quote: SwapQuote,
    sender: string
  ): Promise<Result<UnsignedTransaction, SdkError>> {
    // Note: Full unsigned tx requires /swap endpoint with POST {quoteResponse, userPublicKey}
    // For demo/tests, return quote as-is (signing requires @solana/web3.js)
    return ok({
      chain: "solana",
      format: "quote" as const,
      quote,
      description: `Jupiter v6 quote: ${quote.amountIn} → ${quote.expectedAmountOut}`,
      requiresSigning: true,
    });
  }
}
