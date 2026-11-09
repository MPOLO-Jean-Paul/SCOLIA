import { handleMailbox } from "../../src/mailbox.mjs";

/**
 * Health-only rollout. This Netlify deployment intentionally exposes no
 * mailbox transport until the Windows/Android adapter and its security
 * audit are complete. No external npm dependencies are needed to deploy.
 *
 * Do not enable experimental mail transport via environment variables.
 */
export default async function handler(request) {
  try {
    return await handleMailbox(request, { enabled: false, store: null });
  } catch {
    return Response.json(
      { error: "Service de secours indisponible." },
      { status: 503, headers: { "Cache-Control": "no-store" } },
    );
  }
}

export const config = { path: "/api/scolia-backup/*" };
